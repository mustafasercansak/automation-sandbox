using System;
using System.Globalization;
using System.Net;
using System.Text;

namespace AutomationSandbox.ContentAnalysis
{
    /// <summary>Renders a content-analysis report document as HTML for human review.</summary>
    public static class ContentAnalysisReportHtmlRenderer
    {
        /// <summary>Renders the supplied report document as HTML with encoded dynamic content.</summary>
        public static string Render(ContentAnalysisReportDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var totalIssues = 0;
            foreach (var entry in document.Entries)
            {
                totalIssues += entry.Issues.Count;
            }

            var html = new StringBuilder();
            html.AppendLine("<!doctype html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("  <meta charset=\"utf-8\">");
            html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            html.AppendLine("  <title>Content Analysis Report</title>");
            html.AppendLine("  <style>");
            html.AppendLine("    :root { color-scheme: light; --ink: #17202a; --muted: #5b6777; --line: #d9e1ea; --panel: #f7f9fb; --ok: #176f45; }");
            html.AppendLine("    body { margin: 0; font-family: Segoe UI, Arial, sans-serif; color: var(--ink); background: #fff; }");
            html.AppendLine("    header { padding: 28px 32px 18px; border-bottom: 1px solid var(--line); background: var(--panel); }");
            html.AppendLine("    h1 { margin: 0 0 8px; font-size: 28px; font-weight: 650; }");
            html.AppendLine("    h2 { margin: 28px 0 12px; font-size: 18px; overflow-wrap: anywhere; }");
            html.AppendLine("    .meta { color: var(--muted); font-size: 14px; }");
            html.AppendLine("    main { padding: 24px 32px 36px; }");
            html.AppendLine("    table { width: 100%; border-collapse: collapse; table-layout: fixed; margin-bottom: 28px; }");
            html.AppendLine("    th, td { border-bottom: 1px solid var(--line); padding: 10px 12px; text-align: left; vertical-align: top; font-size: 13px; overflow-wrap: anywhere; }");
            html.AppendLine("    th { background: #eef3f8; font-weight: 650; color: #293747; }");
            html.AppendLine("    .badge { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 12px; font-weight: 650; white-space: nowrap; }");
            html.AppendLine("    .heuristic-badge { color: var(--muted); background: var(--panel); }");
            html.AppendLine("    .llm-badge { color: #1d4ed8; background: #dbeafe; }");
            html.AppendLine("    .empty { color: var(--ok); font-style: italic; }");
            html.AppendLine("  </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("  <header>");
            html.AppendLine("    <h1>Content Analysis Report</h1>");
            html.Append("    <div class=\"meta\">")
                .Append(document.Entries.Count.ToString(CultureInfo.InvariantCulture)).Append(" page(s) scanned · ")
                .Append(totalIssues.ToString(CultureInfo.InvariantCulture)).Append(" issue(s) found · ")
                .Append(E(document.GeneratedAt.ToString("u", CultureInfo.InvariantCulture)))
                .AppendLine("</div>");
            html.AppendLine("  </header>");
            html.AppendLine("  <main>");
            foreach (var entry in document.Entries)
            {
                html.Append("    <h2>").Append(E(entry.Url)).Append(" <span class=\"meta\">(")
                    .Append(E(entry.CapturedAtUtc.ToString("u", CultureInfo.InvariantCulture))).Append(", ")
                    .Append(entry.PassageCount.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(" passage(s))</span></h2>");

                if (entry.Issues.Count == 0)
                {
                    html.AppendLine("    <p class=\"empty\">No issues found.</p>");
                    continue;
                }

                html.AppendLine("    <table>");
                html.AppendLine("      <thead><tr><th>Type</th><th>Selector</th><th>Text</th><th>Message</th><th>Source</th></tr></thead>");
                html.AppendLine("      <tbody>");
                foreach (var issue in entry.Issues)
                {
                    var sourceClass = issue.Source == ContentIssueSource.Llm ? "llm-badge" : "heuristic-badge";
                    html.AppendLine("        <tr>");
                    html.Append("          <td>").Append(E(issue.IssueType)).AppendLine("</td>");
                    html.Append("          <td><code>").Append(E(issue.CssSelector)).AppendLine("</code></td>");
                    html.Append("          <td>").Append(E(issue.Text)).AppendLine("</td>");
                    html.Append("          <td>").Append(E(issue.Message)).AppendLine("</td>");
                    html.Append("          <td><span class=\"badge ").Append(sourceClass).Append("\">")
                        .Append(E(issue.Source.ToString())).AppendLine("</span></td>");
                    html.AppendLine("        </tr>");
                }

                html.AppendLine("      </tbody>");
                html.AppendLine("    </table>");
            }

            html.AppendLine("  </main>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            return html.ToString();
        }

        private static string E(string value)
        {
            return WebUtility.HtmlEncode(value ?? "");
        }
    }
}
