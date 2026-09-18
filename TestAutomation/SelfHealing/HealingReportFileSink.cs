using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AutomationSandbox.SelfHealing
{
    /// <summary>Persistence boundary for engine healing-attempt telemetry.</summary>
    public interface IHealingReportSink
    {
        /// <summary>Records one classified healing attempt.</summary>
        void Record(HealingReportEntry entry);
    }

    /// <summary>
    /// Writes an append-only JSON Lines audit trail (one <see cref="HealingReportEntry"/> object per
    /// line) and optional HTML while synchronizing access to the report file.
    /// </summary>
    public sealed class HealingReportFileSink : IHealingReportSink
    {
        /// <summary>Environment variable used to opt into file-backed healing reports.</summary>
        public const string EnvironmentVariableName = "SELF_HEALING_REPORT_PATH";
        private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private readonly Action<string, string> _replaceExistingFile;
        private readonly Action<string, string> _appendLine;

        // Deliberately not indented: WriteIndented would put literal newlines *inside* one
        // entry's JSON and break the one-line-per-record contract that makes Record() an O(1)
        // append (#424). String property values are still escaped normally regardless of this
        // setting, so a multi-line LlmReasoning value round-trips safely without corrupting the
        // line structure.
        private static readonly JsonSerializerOptions EntryOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
        };

        /// <summary>Configures JSON report storage and optional HTML rendering for the supplied output paths.</summary>
        public HealingReportFileSink(string filePath)
            : this(filePath, Path.ChangeExtension(filePath, ".html"))
        {
        }

        /// <summary>Configures JSON report storage and optional HTML rendering for the supplied output paths.</summary>
        public HealingReportFileSink(string filePath, string? htmlFilePath)
            : this(filePath, htmlFilePath, ReplaceExistingFile, AppendLineToFile)
        {
        }

        internal HealingReportFileSink(
            string filePath,
            string? htmlFilePath,
            Action<string, string> replaceExistingFile)
            : this(filePath, htmlFilePath, replaceExistingFile, AppendLineToFile)
        {
        }

        internal HealingReportFileSink(
            string filePath,
            string? htmlFilePath,
            Action<string, string> replaceExistingFile,
            Action<string, string> appendLine)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath must not be null or empty.", nameof(filePath));
            }

            if (replaceExistingFile == null)
            {
                throw new ArgumentNullException(nameof(replaceExistingFile));
            }

            if (appendLine == null)
            {
                throw new ArgumentNullException(nameof(appendLine));
            }

            FilePath = filePath;
            HtmlFilePath = htmlFilePath;
            _replaceExistingFile = replaceExistingFile;
            _appendLine = appendLine;
        }

        /// <summary>Path of the JSON Lines audit trail appended to by this instance.</summary>
        public string FilePath { get; }
        /// <summary>Optional destination of the HTML report rendered alongside JSON.</summary>
        public string? HtmlFilePath { get; }

        /// <summary>Creates the optional report sink configured by the environment, or returns null when reporting is not configured.</summary>
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

        /// <summary>
        /// Appends the attempt to the persisted report and refreshes the optional HTML view.
        /// The JSON write never reads or re-serializes previously recorded entries - it is a
        /// real filesystem append of exactly the one new line, guarded by the same cross-process
        /// lock as before, so its cost does not grow with how many events are already on disk
        /// (#424). A crash or thrown exception mid-write can at most leave a truncated trailing
        /// line; every entry committed before it stays intact and readable. When
        /// <see cref="HtmlFilePath"/> is configured, the dashboard is still re-rendered from the
        /// full history after the append - that cost is unchanged from before and is inherent to
        /// showing a complete, up-to-date dashboard rather than to recording the event itself.
        /// </summary>
        public void Record(HealingReportEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            using (AcquireLock())
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = JsonSerializer.Serialize(entry, EntryOptions);
                _appendLine(FilePath, line);

                // Pattern-match to a non-null local: on netstandard2.0 the compiler does not get
                // the nullable-flow attribute for string.IsNullOrWhiteSpace, so a bare
                // `HtmlFilePath` use below still warns (CS8604).
                if (HtmlFilePath is { } htmlFilePath && !string.IsNullOrWhiteSpace(htmlFilePath))
                {
                    SaveHtml(htmlFilePath, LoadDocumentUnlocked());
                }
            }
        }

        /// <summary>
        /// Reads every entry recorded so far into a fresh <see cref="HealingReportDocument"/>, or an
        /// empty document when nothing has been recorded yet. Unlike <see cref="Record"/>, this is
        /// an O(n) read over the whole file by design - use it for offline analysis, or to feed
        /// <see cref="HealingReportHtmlRenderer"/> yourself, rather than on a per-heal hot path.
        /// </summary>
        public HealingReportDocument LoadReport()
        {
            using (AcquireLock())
            {
                return LoadDocumentUnlocked();
            }
        }

        private HealingReportDocument LoadDocumentUnlocked()
        {
            var document = new HealingReportDocument
            {
                SchemaVersion = HealingReportDocument.CurrentSchemaVersion,
                GeneratedAt = DateTimeOffset.UtcNow,
            };

            if (!File.Exists(FilePath))
            {
                return document;
            }

            foreach (var line in File.ReadLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // Unknown properties on an entry written by a newer build are ignored by
                // System.Text.Json rather than rejected, and properties this build added that an
                // older entry lacks simply deserialize as null - the same nullable-field
                // tolerance HealingReportEntry has always relied on for evolution. Because every
                // line is an independently valid, self-describing entry, there is no longer a
                // single document-wide schema version to gate on the way the old single-JSON-array
                // format required.
                var entry = JsonSerializer.Deserialize<HealingReportEntry>(line, EntryOptions)
                    ?? throw new JsonException("Failed to deserialize a healing report entry.");
                document.Events.Add(entry);
            }

            return document;
        }

        private void SaveHtml(string htmlFilePath, HealingReportDocument document)
        {
            var htmlDirectory = Path.GetDirectoryName(htmlFilePath);
            if (!string.IsNullOrEmpty(htmlDirectory))
            {
                Directory.CreateDirectory(htmlDirectory);
            }

            var htmlTempPath = Path.Combine(
                string.IsNullOrEmpty(htmlDirectory) ? "." : htmlDirectory,
                $"{Path.GetFileName(htmlFilePath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                File.WriteAllText(htmlTempPath, HealingReportHtmlRenderer.Render(document));

                // The temp file is adjacent to the destination, so File.Replace/File.Move stays
                // on one volume and commits as one filesystem operation.
                if (File.Exists(htmlFilePath))
                {
                    _replaceExistingFile(htmlTempPath, htmlFilePath);
                }
                else
                {
                    File.Move(htmlTempPath, htmlFilePath);
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

        private static void ReplaceExistingFile(string tempPath, string destinationPath)
        {
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null);
        }

        private static void AppendLineToFile(string path, string line)
        {
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.NewLine = "\n";
                writer.WriteLine(line);
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
