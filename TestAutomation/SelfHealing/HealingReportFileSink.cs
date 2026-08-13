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

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        public HealingReportFileSink(string filePath)
            : this(filePath, Path.ChangeExtension(filePath, ".html"))
        {
        }

        public HealingReportFileSink(string filePath, string? htmlFilePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath must not be null or empty.", nameof(filePath));
            }

            FilePath = filePath;
            HtmlFilePath = htmlFilePath;
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

            // v2 only added fields (EvidenceCoverage, Candidates), so a v1 report
            // deserializes cleanly with the new fields at their defaults and is upgraded
            // in place on the next save. Only a NEWER schema is rejected - its semantics
            // are unknown to this build.
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
            File.WriteAllText(tempPath, JsonSerializer.Serialize(document, Options));

            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            File.Move(tempPath, FilePath);

            if (!string.IsNullOrWhiteSpace(HtmlFilePath))
            {
                var htmlDirectory = Path.GetDirectoryName(HtmlFilePath);
                if (!string.IsNullOrEmpty(htmlDirectory))
                {
                    Directory.CreateDirectory(htmlDirectory);
                }

                File.WriteAllText(HtmlFilePath, HealingReportHtmlRenderer.Render(document));
            }
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
