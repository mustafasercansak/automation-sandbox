using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AutomationSandbox.ContentAnalysis
{
    /// <summary>
    /// Writes an append-only JSON Lines log (one <see cref="ContentAnalysisReportEntry" /> per line) and an
    /// optional HTML dashboard, so a multi-page scan can record findings incrementally without re-reading or
    /// re-serializing prior pages on every call - the same append-only design
    /// <c>AutomationSandbox.SelfHealing.HealingReportFileSink</c> uses for the healing engine (#424).
    /// </summary>
    public sealed class ContentAnalysisReportFileSink
    {
        private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // Deliberately not indented, for the same reason as HealingReportFileSink: WriteIndented would put
        // literal newlines inside one entry's JSON and break the one-line-per-record contract that keeps
        // Record() an O(1) append regardless of how many pages are already logged.
        private static readonly JsonSerializerOptions EntryOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
        };

        /// <summary>Configures JSON report storage with an HTML dashboard next to it (same name, .html extension).</summary>
        public ContentAnalysisReportFileSink(string filePath)
            : this(filePath, Path.ChangeExtension(filePath, ".html"))
        {
        }

        /// <summary>Configures JSON report storage and optional HTML rendering for the supplied output paths.</summary>
        public ContentAnalysisReportFileSink(string filePath, string? htmlFilePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath must not be null or empty.", nameof(filePath));
            }

            FilePath = filePath;
            HtmlFilePath = htmlFilePath;
        }

        /// <summary>Path of the JSON Lines log appended to by this instance.</summary>
        public string FilePath { get; }
        /// <summary>Optional destination of the HTML dashboard rendered alongside JSON.</summary>
        public string? HtmlFilePath { get; }

        /// <summary>
        /// Appends one page's analysis to the log and refreshes the optional HTML dashboard. The JSON write
        /// is a real filesystem append of exactly the new line, guarded by a cross-process lock file, so its
        /// cost does not grow with how many pages are already recorded. When <see cref="HtmlFilePath" /> is
        /// configured, the dashboard is still re-rendered from the full history after the append - showing a
        /// complete, up-to-date dashboard inherently requires reading it all back, the same trade-off
        /// HealingReportFileSink accepts.
        /// </summary>
        public void Record(ContentAnalysisReportEntry entry)
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
                AppendLineToFile(FilePath, line);

                // Pattern-match to a non-null local: on netstandard2.0 the compiler does not get the
                // nullable-flow attribute for string.IsNullOrWhiteSpace, so a bare HtmlFilePath use below
                // still warns (CS8604).
                if (HtmlFilePath is { } htmlFilePath && !string.IsNullOrWhiteSpace(htmlFilePath))
                {
                    SaveHtml(htmlFilePath, LoadDocumentUnlocked());
                }
            }
        }

        /// <summary>
        /// Reads every entry recorded so far into a fresh <see cref="ContentAnalysisReportDocument" />, or an
        /// empty document when nothing has been recorded yet. This is an O(n) read over the whole file by
        /// design - use it for offline review, or to feed <see cref="ContentAnalysisReportHtmlRenderer" />
        /// yourself, rather than on a per-page hot path.
        /// </summary>
        public ContentAnalysisReportDocument LoadReport()
        {
            using (AcquireLock())
            {
                return LoadDocumentUnlocked();
            }
        }

        private ContentAnalysisReportDocument LoadDocumentUnlocked()
        {
            var document = new ContentAnalysisReportDocument
            {
                SchemaVersion = ContentAnalysisReportDocument.CurrentSchemaVersion,
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

                var entry = JsonSerializer.Deserialize<ContentAnalysisReportEntry>(line, EntryOptions)
                    ?? throw new JsonException("Failed to deserialize a content analysis report entry.");
                document.Entries.Add(entry);
            }

            return document;
        }

        private static void SaveHtml(string htmlFilePath, ContentAnalysisReportDocument document)
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
                File.WriteAllText(htmlTempPath, ContentAnalysisReportHtmlRenderer.Render(document));

                // The temp file is adjacent to the destination, so File.Replace/File.Move stays on one
                // volume and commits as one filesystem operation.
                if (File.Exists(htmlFilePath))
                {
                    File.Replace(htmlTempPath, htmlFilePath, destinationBackupFileName: null);
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
