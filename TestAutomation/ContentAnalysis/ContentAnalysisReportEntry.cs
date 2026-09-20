using System;
using System.Collections.Generic;
using AutomationSandbox.WebDiscovery;

namespace AutomationSandbox.ContentAnalysis
{
    /// <summary>One page's content-analysis pass: the URL captured, when it ran, how many text passages were
    /// considered, and every issue found.</summary>
    public sealed class ContentAnalysisReportEntry
    {
        /// <summary>URL of the page this entry analyzed.</summary>
        public string Url { get; set; } = "";
        /// <summary>Timestamp when this page was captured and analyzed.</summary>
        public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Number of text passages <see cref="ContentAnalyzer.CountPassages" /> found on this page.</summary>
        public int PassageCount { get; set; }
        /// <summary>Every content-quality issue found on this page, heuristic and LLM-sourced alike.</summary>
        public List<ContentIssue> Issues { get; set; } = new List<ContentIssue>();

        /// <summary>Builds a report entry from one page's captured DOM and the issues already found on it -
        /// the usual shape after <c>await ContentAnalyzer.AnalyzeAsync(dom, llmProvider)</c>.</summary>
        public static ContentAnalysisReportEntry FromAnalysis(string url, WebElementInfo dom, IReadOnlyList<ContentIssue> issues)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (dom == null)
            {
                throw new ArgumentNullException(nameof(dom));
            }

            if (issues == null)
            {
                throw new ArgumentNullException(nameof(issues));
            }

            return new ContentAnalysisReportEntry
            {
                Url = url,
                CapturedAtUtc = DateTimeOffset.UtcNow,
                PassageCount = ContentAnalyzer.CountPassages(dom),
                Issues = new List<ContentIssue>(issues),
            };
        }
    }
}
