using System;
using System.IO;
using System.Text.Json;

namespace IntentAutomation
{
    public sealed class IntentFlowReportFileSink
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        public IntentFlowReportFileSink(string filePath)
            : this(filePath, Path.ChangeExtension(filePath, ".html"))
        {
        }

        public IntentFlowReportFileSink(string filePath, string? htmlFilePath)
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
