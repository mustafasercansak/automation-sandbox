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

        /// <summary>Writes the supplied flow report and refreshes the optional HTML output.</summary>
        public void Write(IntentFlowReportDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            document.GeneratedAt = DateTimeOffset.UtcNow;
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(document, Options));
            if (!string.IsNullOrWhiteSpace(HtmlFilePath))
            {
                var htmlDirectory = Path.GetDirectoryName(HtmlFilePath);
                if (!string.IsNullOrEmpty(htmlDirectory))
                {
                    Directory.CreateDirectory(htmlDirectory);
                }

                File.WriteAllText(HtmlFilePath, IntentFlowReportHtmlRenderer.Render(document));
            }
        }
    }
}
