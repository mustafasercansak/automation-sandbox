namespace UiModel
{
    // Owns read-modify-write access to a single .locator.json file on disk. The load-modify-save
    // cycle in Upsert is guarded by an exclusive lock on a sidecar ".lock" file so concurrent
    // callers - e.g. parallel xUnit test collections healing against the same repository file -
    // serialize instead of racing and silently dropping each other's updates.
    public sealed class LocatorRepository
    {
        private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);

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
            if (!File.Exists(FilePath))
            {
                return new LocatorRepositoryDocument();
            }

            var json = File.ReadAllText(FilePath);
            return LocatorRepositorySerializer.FromJson(json);
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

            // Write to a temp file then swap it into place, so a crash or a concurrent reader
            // mid-write never observes (or is left with) a half-written, corrupt repository file.
            var tempPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            File.Move(tempPath, FilePath);
        }

        public LocatorRecord? Find(string locatorKey)
        {
            return Load().Locators.Find(r => r.LocatorKey == locatorKey);
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

            record.Snapshot = snapshot;
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
