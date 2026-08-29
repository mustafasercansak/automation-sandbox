using System;
using System.IO;
using System.Text.Json;

namespace SelfHealing
{
    public interface IHealingReportSink
    {
        void Record(HealingReportEntry entry);
    }

    public sealed class HealingReportFileSink : IHealingReportSink
    {
        public const string EnvironmentVariableName = "SELF_HEALING_REPORT_PATH";
        private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);
        private readonly Action<string, string> _replaceExistingFile;

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        public HealingReportFileSink(string filePath)
            : this(filePath, Path.ChangeExtension(filePath, ".html"))
        {
        }

        public HealingReportFileSink(string filePath, string? htmlFilePath)
            : this(filePath, htmlFilePath, ReplaceExistingFile)
        {
        }

        internal HealingReportFileSink(
            string filePath,
            string? htmlFilePath,
            Action<string, string> replaceExistingFile)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath must not be null or empty.", nameof(filePath));
            }

            if (replaceExistingFile == null)
            {
                throw new ArgumentNullException(nameof(replaceExistingFile));
            }

            FilePath = filePath;
            HtmlFilePath = htmlFilePath;
            _replaceExistingFile = replaceExistingFile;
        }

        public string FilePath { get; }
        public string? HtmlFilePath { get; }

        public static HealingReportFileSink? FromEnvironment()
        {
            var filePath = Environment.GetEnvironmentVariable(EnvironmentVariableName);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var htmlFilePath = Environment.GetEnvironmentVariable("SELF_HEALING_REPORT_HTML_PATH");
            return new HealingReportFileSink(filePath!, string.IsNullOrWhiteSpace(htmlFilePath) ? Path.ChangeExtension(filePath, ".html") : htmlFilePath);
        }

        public void Record(HealingReportEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            using (AcquireLock())
            {
                var document = Load();
                document.Events.Add(entry);
                Save(document);
            }
        }

        private HealingReportDocument Load()
        {
            if (!File.Exists(FilePath))
            {
                return new HealingReportDocument();
            }

            var document = JsonSerializer.Deserialize<HealingReportDocument>(File.ReadAllText(FilePath), Options)
                ?? throw new JsonException("Failed to deserialize healing report JSON.");

            // Every schema revision through v7 only added nullable/defaulted fields, so an
            // older report deserializes cleanly and upgrades in place on the next save. Only
            // a NEWER schema is rejected because its semantics are unknown to this build.
            if (document.SchemaVersion > HealingReportDocument.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Healing report schema version {document.SchemaVersion} is newer than this build supports ({HealingReportDocument.CurrentSchemaVersion}).");
            }

            document.SchemaVersion = HealingReportDocument.CurrentSchemaVersion;

            return document;
        }

        private void Save(HealingReportDocument document)
        {
            document.GeneratedAt = DateTimeOffset.UtcNow;

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = FilePath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, JsonSerializer.Serialize(document, Options));

                // The temp file is adjacent to the destination, so File.Replace/File.Move stays
                // on one volume and commits as one filesystem operation. If the process stops
                // before that operation, the previous report remains intact; there is no
                // delete-then-move window in which all recorded history is absent.
                if (File.Exists(FilePath))
                {
                    _replaceExistingFile(tempPath, FilePath);
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

            if (!string.IsNullOrWhiteSpace(HtmlFilePath))
            {
                var htmlDirectory = Path.GetDirectoryName(HtmlFilePath);
                if (!string.IsNullOrEmpty(htmlDirectory))
                {
                    Directory.CreateDirectory(htmlDirectory);
                }

                var htmlTempPath = Path.Combine(
                    string.IsNullOrEmpty(htmlDirectory) ? "." : htmlDirectory,
                    $"{Path.GetFileName(HtmlFilePath)}.{Guid.NewGuid():N}.tmp");

                try
                {
                    File.WriteAllText(htmlTempPath, HealingReportHtmlRenderer.Render(document));
                    if (File.Exists(HtmlFilePath))
                    {
                        _replaceExistingFile(htmlTempPath, HtmlFilePath);
                    }
                    else
                    {
                        File.Move(htmlTempPath, HtmlFilePath);
                    }
                }
                finally
                {
                    if (File.Exists(htmlTempPath))
                    {
                        File.Delete(htmlTempPath);
                    }
                }
            }
        }

        private static void ReplaceExistingFile(string tempPath, string destinationPath)
        {
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null);
        }

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
                    System.Threading.Thread.Sleep(25);
                }
            }
        }
    }
}
