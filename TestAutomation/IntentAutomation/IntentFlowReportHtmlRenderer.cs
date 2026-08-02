using System;
using System.Globalization;
using System.Net;
using System.Text;

namespace IntentAutomation
{
    public static class IntentFlowReportHtmlRenderer
    {
        public static string Render(IntentFlowReportDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var html = new StringBuilder();
            html.AppendLine("<!doctype html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("  <meta charset=\"utf-8\">");
            html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            html.AppendLine("  <title>Intent Flow Report</title>");
            html.AppendLine("  <style>");
            html.AppendLine("    :root { color-scheme: light; --ink: #17202a; --muted: #5b6777; --line: #d9e1ea; --panel: #f7f9fb; --ok: #176f45; --review: #9a6700; --code: #f1f4f8; }");
            html.AppendLine("    body { margin: 0; font-family: Segoe UI, Arial, sans-serif; color: var(--ink); background: #fff; }");
            html.AppendLine("    header { padding: 28px 32px 18px; border-bottom: 1px solid var(--line); background: var(--panel); }");
            html.AppendLine("    h1 { margin: 0 0 8px; font-size: 28px; font-weight: 650; }");
            html.AppendLine("    h2 { margin: 28px 0 12px; font-size: 20px; }");
            html.AppendLine("    .meta { color: var(--muted); font-size: 14px; }");
            html.AppendLine("    main { padding: 24px 32px 36px; }");
            html.AppendLine("    table { width: 100%; border-collapse: collapse; table-layout: fixed; }");
            html.AppendLine("    th, td { border-bottom: 1px solid var(--line); padding: 10px 12px; text-align: left; vertical-align: top; font-size: 13px; overflow-wrap: anywhere; }");
            html.AppendLine("    th { background: #eef3f8; font-weight: 650; color: #293747; }");
            html.AppendLine("    .badge { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 12px; font-weight: 650; }");
            html.AppendLine("    .ok { color: var(--ok); background: #e8f5ee; }");
            html.AppendLine("    .review { color: var(--review); background: #fff4d6; }");
            html.AppendLine("    pre { padding: 14px; overflow: auto; background: var(--code); border: 1px solid var(--line); font-size: 12px; }");
            html.AppendLine("    code { font-family: Consolas, monospace; font-size: 12px; }");
            html.AppendLine("  </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("  <header>");
            html.AppendLine("    <h1>Intent Flow Report</h1>");
            html.Append("    <div class=\"meta\">").Append(E(document.ScenarioName)).Append(" · ");
            html.Append(E(document.GeneratedAt.ToString("u", CultureInfo.InvariantCulture))).AppendLine("</div>");
            html.AppendLine("  </header>");
            html.AppendLine("  <main>");
            html.Append("    <p><strong>Goal:</strong> ").Append(E(document.Goal)).AppendLine("</p>");
            html.Append("    <p><strong>Target:</strong> <code>").Append(E(document.TargetUrl)).AppendLine("</code></p>");
            html.AppendLine("    <h2>Steps</h2>");
            html.AppendLine("    <table>");
            html.AppendLine("      <thead><tr><th>#</th><th>Action</th><th>Locator</th><th>Intent</th><th>Candidates</th><th>Best</th><th>Status</th><th>Diagnostic</th></tr></thead>");
            html.AppendLine("      <tbody>");
            foreach (var step in document.Steps)
            {
                var statusClass = step.Recorded && !step.RequiresReview ? "ok" : "review";
                var status = step.Recorded ? "recorded" : step.RequiresReview ? "review" : "not-recorded";
                html.AppendLine("        <tr>");
                html.Append("          <td>").Append(step.Order.ToString(CultureInfo.InvariantCulture)).AppendLine("</td>");
                html.Append("          <td>").Append(E(step.ActionType)).AppendLine("</td>");
                html.Append("          <td><code>").Append(E(step.LocatorKey)).AppendLine("</code></td>");
                html.Append("          <td>").Append(E(step.TestIntent)).AppendLine("</td>");
                html.Append("          <td>").Append(step.CandidateCount.ToString(CultureInfo.InvariantCulture)).AppendLine("</td>");
                html.Append("          <td>").Append(E(FormatBest(step))).AppendLine("</td>");
                html.Append("          <td><span class=\"badge ").Append(statusClass).Append("\">").Append(status).AppendLine("</span></td>");
                html.Append("          <td>").Append(E(FirstNonEmpty(step.RecordingDiagnostic, step.ExplorationDiagnostic))).AppendLine("</td>");
                html.AppendLine("        </tr>");
            }

            html.AppendLine("      </tbody>");
            html.AppendLine("    </table>");
            html.AppendLine("    <h2>Playwright C#</h2>");
            html.Append("    <pre><code>").Append(E(document.PlaywrightCSharpTestCode)).AppendLine("</code></pre>");
            html.AppendLine("    <h2>Playwright TypeScript</h2>");
            html.Append("    <pre><code>").Append(E(document.PlaywrightTypeScriptTestCode)).AppendLine("</code></pre>");
            html.AppendLine("  </main>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            return html.ToString();
        }

        private static string FormatBest(IntentFlowReportStep step)
        {
            if (!step.BestCandidateScore.HasValue)
            {
                return "";
            }

            return step.BestCandidateScore.Value.ToString("0.00", CultureInfo.InvariantCulture) + " " + step.BestCandidateLocator;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }

        private static string E(string value)
        {
            return WebUtility.HtmlEncode(value ?? "");
        }
    }
}
