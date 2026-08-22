namespace UiModel
{
    // Owns read-modify-write access to a single .locator.json file on disk. The load-modify-save
    // cycle in Upsert is guarded by an exclusive lock on a sidecar ".lock" file so concurrent
    // callers - e.g. parallel xUnit test collections healing against the same repository file -
    // serialize instead of racing and silently dropping each other's updates.
    // In-memory document and O(1) lookup dictionary are cached across repeated Find/Load calls
    // and invalidated whenever the underlying file's mtime or length changes (dirty-check).
    public sealed class LocatorRepository
    {
        private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);

        private readonly object _syncRoot = new object();
        private DateTime _lastWriteTimeUtc = DateTime.MinValue;
        private long _lastLength = -1;
        private bool _cachedFileExists;
        private LocatorRepositoryDocument? _cachedDocument;
        private Dictionary<string, LocatorRecord>? _cachedLookup;

        public string FilePath { get; }

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
        public LocatorRepositoryDocument Load()
        {
            lock (_syncRoot)
            {
                RefreshCacheIfStaleLocked();
                return _cachedDocument ?? new LocatorRepositoryDocument();
            }
        }

        public void Save(LocatorRepositoryDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var json = LocatorRepositorySerializer.ToJson(document);
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a temp file then atomically replace/move it into place, so a crash or a
            // concurrent reader mid-write never observes a half-written repository file.
            var tempPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, json);
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
                _cachedDocument = document;
                _cachedLookup = BuildLookupIndex(document);
            }
        }

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
                    _cachedDocument = new LocatorRepositoryDocument();
                    _cachedLookup = new Dictionary<string, LocatorRecord>(StringComparer.Ordinal);
                }

                return;
            }

            var lastWrite = fileInfo.LastWriteTimeUtc;
            var length = fileInfo.Length;

            if (_cachedFileExists && _cachedDocument != null && _lastWriteTimeUtc == lastWrite && _lastLength == length)
            {
                return;
            }

            var json = File.ReadAllText(FilePath);
            var doc = LocatorRepositorySerializer.FromJson(json);
            var lookup = BuildLookupIndex(doc);

            _cachedFileExists = true;
            _lastWriteTimeUtc = lastWrite;
            _lastLength = length;
            _cachedDocument = doc;
            _cachedLookup = lookup;
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
