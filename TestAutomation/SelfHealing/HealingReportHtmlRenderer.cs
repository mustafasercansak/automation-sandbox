using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using UiModel;

namespace SelfHealing
{
    public static class HealingReportHtmlRenderer
    {
        public static string Render(HealingReportDocument document)
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
            html.AppendLine("  <title>Self-Healing Report</title>");
            html.AppendLine("  <style>");
            html.AppendLine("    :root { color-scheme: light; --ink: #17202a; --muted: #5b6777; --line: #d9e1ea; --panel: #f7f9fb; --ok: #176f45; --llm: #235a97; --review: #9a6700; }");
            html.AppendLine("    body { margin: 0; font-family: Segoe UI, Arial, sans-serif; color: var(--ink); background: #fff; }");
            html.AppendLine("    header { padding: 28px 32px 18px; border-bottom: 1px solid var(--line); background: var(--panel); }");
            html.AppendLine("    h1 { margin: 0 0 8px; font-size: 28px; font-weight: 650; }");
            html.AppendLine("    .meta { color: var(--muted); font-size: 14px; }");
            html.AppendLine("    main { padding: 24px 32px 36px; }");
            html.AppendLine("    table { width: 100%; border-collapse: collapse; table-layout: fixed; }");
            html.AppendLine("    th, td { border-bottom: 1px solid var(--line); padding: 10px 12px; text-align: left; vertical-align: top; font-size: 13px; overflow-wrap: anywhere; }");
            html.AppendLine("    th { background: #eef3f8; font-weight: 650; color: #293747; }");
            html.AppendLine("    .badge { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 12px; font-weight: 650; }");
            html.AppendLine("    .accepted { color: var(--ok); background: #e8f5ee; }");
            html.AppendLine("    .accepted-unverified { color: var(--llm); background: #e8f1fb; }");
            html.AppendLine("    .accepted-with-llm { color: var(--llm); background: #e8f1fb; }");
            html.AppendLine("    .manual-review, .observed, .fail-closed, .retry-failed, .ambiguous, .ownership-conflict, .low-evidence, .low-confidence, .no-candidates, .no-consensus, .provider-error, .unspecified { color: var(--review); background: #fff4d6; }");
            html.AppendLine("    .empty { padding: 24px; border: 1px solid var(--line); background: var(--panel); color: var(--muted); }");
            html.AppendLine("    code { font-family: Consolas, monospace; font-size: 12px; }");
            html.AppendLine("  </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("  <header>");
            html.AppendLine("    <h1>Self-Healing Report</h1>");
            html.Append("    <div class=\"meta\">Generated ");
            html.Append(E(document.GeneratedAt.ToString("u", CultureInfo.InvariantCulture)));
            html.Append(" · ");
            html.Append(document.Events.Count.ToString(CultureInfo.InvariantCulture));
            html.AppendLine(document.Events.Count == 1 ? " event</div>" : " events</div>");
            html.AppendLine("  </header>");
            html.AppendLine("  <main>");

            if (document.Events.Count == 0)
            {
                html.AppendLine("    <div class=\"empty\">No self-healing events were recorded.</div>");
            }
            else
            {
                html.AppendLine("    <table>");
                html.AppendLine("      <thead>");
                html.AppendLine("        <tr>");
                html.AppendLine("          <th>Locator Key</th>");
                html.AppendLine("          <th>Source</th>");
                html.AppendLine("          <th>Platform</th>");
                html.AppendLine("          <th>Outcome</th>");
                html.AppendLine("          <th>Review</th>");
                html.AppendLine("          <th>Score</th>");
                html.AppendLine("          <th>Previous</th>");
                html.AppendLine("          <th>Accepted</th>");
                html.AppendLine("          <th>Proposed</th>");
                html.AppendLine("          <th>Reasoning</th>");
                html.AppendLine("        </tr>");
                html.AppendLine("      </thead>");
                html.AppendLine("      <tbody>");

                foreach (var entry in document.Events)
                {
                    html.AppendLine("        <tr>");
                    html.Append("          <td><code>").Append(E(entry.LocatorKey)).AppendLine("</code></td>");
                    html.Append("          <td>").Append(E(entry.Source)).AppendLine("</td>");
                    html.Append("          <td>").Append(E(entry.Platform ?? "unknown")).AppendLine("</td>");
                    var outcome = entry.Outcome ?? entry.ReviewStatus;
                    html.Append("          <td><span class=\"badge ").Append(E(outcome)).Append("\">").Append(E(outcome)).AppendLine("</span></td>");
                    html.Append("          <td><span class=\"badge ").Append(E(entry.ReviewStatus)).Append("\">").Append(E(entry.ReviewStatus)).AppendLine("</span></td>");
                    html.Append("          <td>").Append(E(FormatScore(entry))).AppendLine("</td>");
                    html.Append("          <td>").Append(FormatSnapshot(entry.PreviousSnapshot)).AppendLine("</td>");
                    html.Append("          <td>").Append(FormatSnapshot(entry.AcceptedSnapshot)).AppendLine("</td>");
                    html.Append("          <td>").Append(FormatSnapshot(entry.ProposedSnapshot)).AppendLine("</td>");
                    var reasoning = entry.LlmReasoning ?? "";
                    // Who agreed is the evidence behind an LLM acceptance (#10), so it belongs
                    // in the human-readable report and not only in the JSON. Null means the
                    // entry predates v5, which is not the same as nobody agreeing.
                    if (entry.AgreedProviders != null && entry.AgreedProviders.Count > 0)
                    {
                        var consensusNote = $"[Consensus: {string.Join(" + ", entry.AgreedProviders)}]";
                        reasoning = string.IsNullOrEmpty(reasoning) ? consensusNote : consensusNote + " " + reasoning;
                    }

                    if (entry.DivergedFromHeuristic)
                    {
                        var heuristicId = entry.HeuristicSnapshot?.AutomationId ?? "unknown";
                        var divergenceNote = $"[Diverged from heuristic '{heuristicId}']";
                        reasoning = string.IsNullOrEmpty(reasoning) ? divergenceNote : divergenceNote + " " + reasoning;
                    }
                    if (entry.ProviderErrors != null && entry.ProviderErrors.Count > 0)
                    {
                        var providerErrorNote = "[Provider errors: " + string.Join("; ", entry.ProviderErrors
                            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                            .Select(kvp => kvp.Key + ": " + kvp.Value)) + "]";
                        reasoning = string.IsNullOrEmpty(reasoning) ? providerErrorNote : providerErrorNote + " " + reasoning;
                    }
                    if (!string.IsNullOrEmpty(entry.ReconciliationDisposition))
                    {
                        var reconciliationNote = "[Reconciliation: " + entry.ReconciliationDisposition +
                            (string.IsNullOrEmpty(entry.CandidateIdentity) ? "" : " · candidate " + entry.CandidateIdentity) + "]";
                        reasoning = string.IsNullOrEmpty(reasoning) ? reconciliationNote : reconciliationNote + " " + reasoning;
                    }
                    html.Append("          <td>").Append(E(reasoning)).AppendLine("</td>");
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

        private static string FormatScore(HealingReportEntry entry)
        {
            var confidence = entry.LlmConfidence ?? entry.Score;
            // Evidence coverage next to the score: a manual-review badge alone doesn't tell
            // the reviewer whether the problem was a low score or thin evidence. Entries
            // upgraded from v1 reports have no coverage recorded - "unknown", not 0.
            var evidence = entry.EvidenceCoverage.HasValue
                ? " · evidence " + entry.EvidenceCoverage.Value.ToString("0.00", CultureInfo.InvariantCulture)
                : " · evidence unknown";
            return confidence.ToString("0.00", CultureInfo.InvariantCulture) +
                " / " +
                entry.ConfidenceThreshold.ToString("0.00", CultureInfo.InvariantCulture) +
                evidence;
        }

        private static string FormatSnapshot(UiElementInfo? snapshot)
        {
            if (snapshot == null)
            {
                return "";
            }

            var id = string.IsNullOrWhiteSpace(snapshot.AutomationId) ? "(no AutomationId)" : snapshot.AutomationId;
            var name = string.IsNullOrWhiteSpace(snapshot.Name) ? "" : " · " + snapshot.Name;
            return "<code>" + E(id) + "</code>" + E(name);
        }

        private static string E(string value)
        {
            return WebUtility.HtmlEncode(value);
        }
    }
}
