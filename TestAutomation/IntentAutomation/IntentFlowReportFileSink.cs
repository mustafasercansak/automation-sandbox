using System;
using System.IO;
using System.Text.Json;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Writes intent flow reports as JSON and optional HTML.</summary>
    public sealed class IntentFlowReportFileSink
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        /// <summary>Configures JSON flow-report storage and optional HTML output paths.</summary>
        public IntentFlowReportFileSink(string filePath)
            : this(filePath, Path.ChangeExtension(filePath, ".html"))
        {
        }

        /// <summary>Configures JSON flow-report storage and optional HTML output paths.</summary>
        public IntentFlowReportFileSink(string filePath, string? htmlFilePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath must not be null or empty.", nameof(filePath));
            }

            FilePath = filePath;
            HtmlFilePath = htmlFilePath;
        }

        /// <summary>Path of the JSON document read or written by this instance.</summary>
        public string FilePath { get; }
        /// <summary>Optional destination of the HTML report rendered alongside JSON.</summary>
        public string? HtmlFilePath { get; }

        /// <summary>Writes the supplied flow report and refreshes the optional HTML output. Each file is written to a temporary file next to it and moved into place, so a crash or a concurrent reader never sees a half-written report, like the other report sinks (#310, #640). The document is written as it is: it already carries the time it was generated, and saving it does not change it.</summary>
        public void Write(IntentFlowReportDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            WriteAtomically(FilePath, JsonSerializer.Serialize(document, Options));
            if (!string.IsNullOrWhiteSpace(HtmlFilePath))
            {
                WriteAtomically(HtmlFilePath!, IntentFlowReportHtmlRenderer.Render(document));
            }
        }

        private static void WriteAtomically(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // The temporary file sits next to the destination, so the move stays on one volume and commits as one filesystem operation.
            var tempPath = Path.Combine(
                string.IsNullOrEmpty(directory) ? "." : directory,
                $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(tempPath, content);
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
