using System.Security.Cryptography;
using System.Text;

namespace AutomationSandbox.UiModel
{
    // Owns read-modify-write access to a single .locator.json file on disk. The load-modify-save
    // cycle in Upsert is guarded by an exclusive lock on a sidecar ".lock" file so concurrent
    // callers - e.g. parallel xUnit test collections healing against the same repository file -
    // serialize instead of racing and silently dropping each other's updates.
    // In-memory document and O(1) lookup dictionary are cached across repeated Find/Load calls
    // and invalidated whenever the underlying file's content changes (verified via content hash, dirty-check).
    /// <summary>File-backed locator storage with synchronized load, save, and upsert operations.</summary>
    public sealed class LocatorRepository
    {
        private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);

        private readonly object _syncRoot = new object();
        private DateTime _lastWriteTimeUtc = DateTime.MinValue;
        private long _lastLength = -1;
        private byte[]? _cachedContentHash;
        private bool _cachedFileExists;
        private LocatorRepositoryDocument? _cachedDocument;
        private Dictionary<string, LocatorRecord>? _cachedLookup;

        /// <summary>Path of the JSON document read or written by this instance.</summary>
        public string FilePath { get; }

        /// <summary>Creates a repository bound to the supplied JSON file path; load and save operations synchronize on that path.</summary>
        public LocatorRepository(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath must not be null or empty.", nameof(filePath));
            }

            FilePath = filePath;
        }

        // A missing file is an empty, not-yet-populated repository, not an error - the first
        // heal in a fresh environment is exactly when this file doesn't exist yet.
        /// <summary>Loads the repository document, or creates an empty document when the file does not exist.</summary>
        public LocatorRepositoryDocument Load()
        {
            lock (_syncRoot)
            {
                RefreshCacheIfStaleLocked();
                return _cachedDocument ?? new LocatorRepositoryDocument();
            }
        }

        /// <summary>Validates and writes the supplied repository document while holding the file lock.</summary>
        public void Save(LocatorRepositoryDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var json = LocatorRepositorySerializer.ToJson(document);
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a temp file then atomically replace/move it into place, so a crash or a
            // concurrent reader mid-write never observes a half-written repository file.
            var tempPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tempPath, jsonBytes);
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Replace(tempPath, FilePath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tempPath, FilePath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            lock (_syncRoot)
            {
                var fileInfo = new FileInfo(FilePath);
                _cachedFileExists = fileInfo.Exists;
                _lastWriteTimeUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue;
                _lastLength = fileInfo.Exists ? fileInfo.Length : -1;
                _cachedContentHash = fileInfo.Exists ? ComputeContentHash(jsonBytes) : null;
                _cachedDocument = document;
                _cachedLookup = BuildLookupIndex(document);
            }
        }

        /// <summary>Loads and returns the record for a logical locator key, or null if it is not stored.</summary>
        public LocatorRecord? Find(string locatorKey)
        {
            if (locatorKey == null)
            {
                return null;
            }

            lock (_syncRoot)
            {
                RefreshCacheIfStaleLocked();
                if (_cachedLookup != null && _cachedLookup.TryGetValue(locatorKey, out var record))
                {
                    return record;
                }

                return null;
            }
        }

        private void RefreshCacheIfStaleLocked()
        {
            var fileInfo = new FileInfo(FilePath);
            if (!fileInfo.Exists)
            {
                if (!_cachedFileExists || _cachedDocument == null)
                {
                    _cachedFileExists = false;
                    _lastWriteTimeUtc = DateTime.MinValue;
                    _lastLength = -1;
                    _cachedContentHash = null;
                    _cachedDocument = new LocatorRepositoryDocument();
                    _cachedLookup = new Dictionary<string, LocatorRecord>(StringComparer.Ordinal);
                }

                return;
            }

            var lastWrite = fileInfo.LastWriteTimeUtc;
            var length = fileInfo.Length;

            var bytes = File.ReadAllBytes(FilePath);
            var currentHash = ComputeContentHash(bytes);

            if (_cachedFileExists && _cachedDocument != null && HashesMatch(_cachedContentHash, currentHash))
            {
                _lastWriteTimeUtc = lastWrite;
                _lastLength = length;
                return;
            }

            var json = Encoding.UTF8.GetString(bytes);
            var doc = LocatorRepositorySerializer.FromJson(json);
            var lookup = BuildLookupIndex(doc);

            _cachedFileExists = true;
            _lastWriteTimeUtc = lastWrite;
            _lastLength = length;
            _cachedContentHash = currentHash;
            _cachedDocument = doc;
            _cachedLookup = lookup;
        }

        private static byte[] ComputeContentHash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(bytes);
        }

        private static bool HashesMatch(byte[]? a, byte[]? b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, LocatorRecord> BuildLookupIndex(LocatorRepositoryDocument document)
        {
            var lookup = new Dictionary<string, LocatorRecord>(StringComparer.Ordinal);
            if (document?.Locators != null)
            {
                foreach (var record in document.Locators)
                {
                    if (record?.LocatorKey != null && !lookup.ContainsKey(record.LocatorKey))
                    {
                        lookup[record.LocatorKey] = record;
                    }
                }
            }

            return lookup;
        }

        // Adds or updates the record for locatorKey under an exclusive lock spanning the whole
        // load-modify-save cycle, and returns the updated record. Safe for multiple callers -
        // in-process or cross-process - to call concurrently against the same file.
        /// <summary>Captures the supplied element and inserts or updates its logical locator record, optionally appending healing history.</summary>
        public LocatorRecord Upsert(
            string locatorKey,
            UiElementInfo snapshot,
            LocatorHealingHistoryEntry? healingEntry = null,
            string? applicationName = null,
            string? platform = null)
        {
            if (string.IsNullOrWhiteSpace(locatorKey))
            {
                throw new ArgumentException("locatorKey must not be null or empty.", nameof(locatorKey));
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            using var fileLock = AcquireLock();
            var document = Load();
            if (!string.IsNullOrEmpty(applicationName))
            {
                document.ApplicationName = applicationName!;
            }

            if (!string.IsNullOrEmpty(platform))
            {
                document.Platform = platform!;
            }

            var record = document.Locators.Find(r => r.LocatorKey == locatorKey);
            var now = DateTimeOffset.UtcNow;
            if (record == null)
            {
                record = new LocatorRecord { LocatorKey = locatorKey, CreatedAt = now };
                document.Locators.Add(record);
            }

            record.Snapshot = UiElementSnapshot.Capture(snapshot);
            if (!string.IsNullOrWhiteSpace(snapshot.TestIntent))
            {
                record.TestIntent = snapshot.TestIntent;
            }

            record.UpdatedAt = now;
            if (healingEntry != null)
            {
                record.HealingHistory.Add(healingEntry);
            }

            Save(document);
            return record;
        }

        // A plain FileStream opened with FileShare.None works as a cross-platform mutex
        // (Windows and Linux alike) without needing a named OS mutex - only whether another
        // process/thread currently holds the handle open matters, not the lock file's contents.
        private FileStream AcquireLock()
        {
            var lockPath = FilePath + ".lock";
            var directory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var deadline = DateTime.UtcNow + DefaultLockTimeout;
            while (true)
            {
                try
                {
                    return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(LockRetryDelay);
                }
            }
        }
    }
}
