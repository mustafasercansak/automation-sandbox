using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>Turns an <see cref="AgentRunReport" /> into the text of an issue comment. Everything that came from a model or
    /// from a provider is untrusted and is going to be shown on a public page, so it is escaped here: a mention cannot
    /// notify anybody, a link or an HTML tag is not live, markdown from the model cannot open or close formatting, a
    /// provider's response body (which can carry account identifiers) is reduced to a status code, and the length is
    /// capped.</summary>
    public static class AgentReportComment
    {
        /// <summary>Hidden marker at the top of every comment the agent posts, so a person or a later step can find them.</summary>
        public const string Marker = "<!-- automation-sandbox-agent -->";

        /// <summary>Longest comment, in characters; GitHub's own limit is far higher.</summary>
        public const int MaxLength = 4000;

        private const int MaxFieldLength = 400;

        private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

        private static readonly Regex HttpStatus = new Regex(@"HTTP (?<code>\d{3})\b[^\r\n]*", RegexOptions.CultureInvariant, MatchTimeout);
        private static readonly Regex Braces = new Regex(@"\{[^{}]*\}", RegexOptions.CultureInvariant, MatchTimeout);
        private static readonly Regex Autolink = new Regex(@"(?<scheme>(?:https?|ftp)):(?<slashes>//)|(?<www>www\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant, MatchTimeout);

        /// <summary>Reduces an error that may hold a provider's response body to a short, public-safe reason: the status code
        /// with a plain description, or the text with any JSON object removed. The response body is never kept.</summary>
        public static string ErrorSummary(string? error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return "";
            }

            var text = error!;
            var match = HttpStatus.Match(text);
            if (match.Success)
            {
                var code = match.Groups["code"].Value;
                text = text.Substring(0, match.Index) + "HTTP " + code + " (" + Describe(code) + ")";
            }
            else
            {
                while (Braces.IsMatch(text))
                {
                    text = Braces.Replace(text, "");
                }
            }

            text = Spaces.Replace(new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()), " ").Trim();
            return text.Length <= 240 ? text : text.Substring(0, 240) + "...";
        }

        /// <summary>Builds the comment for <paramref name="report" />.</summary>
        public static string Build(AgentRunReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var lines = new List<string>
            {
                Marker,
                "**Issue-triage agent report.** Read-only: this run changed no label and no card. A language model wrote any proposal below; a person must read it before acting on it.",
                "",
                "- Stage: " + Md(report.Stage.ToString()),
                "- Gate: " + Md(report.Gate?.Reason ?? "unknown") + (report.TaskKind == null ? "" : " (task: " + Md(report.TaskKind) + ")"),
            };

            if (!string.IsNullOrEmpty(report.ReadError))
            {
                lines.Add("- Note: " + Md(ErrorSummary(report.ReadError)));
            }

            foreach (var problem in report.RoutingProblems)
            {
                lines.Add("- Routing: " + Md(ErrorSummary(problem)));
            }

            var triage = report.Triage;
            if (triage != null)
            {
                lines.Add("- Drafted by: " + Md(triage.DraftedBy ?? "nobody"));
                lines.Add("- Reviewed by: " + (triage.ReviewedBy == null ? "nobody (not reviewed)" : Md(triage.ReviewedBy)));
                lines.Add("- Result: " + Md(triage.Status.ToString()) + (string.IsNullOrEmpty(triage.Error) ? "" : " - " + Md(ErrorSummary(triage.Error))));
                foreach (var finding in triage.Findings)
                {
                    lines.Add("- Reviewer finding: " + Md(finding));
                }

                var proposal = triage.Proposal;
                if (proposal != null)
                {
                    lines.Add("");
                    lines.Add("Proposal" + (triage.Status == TriageStatus.Blocked ? " (blocked by the reviewer, shown only for reading)" : "") + ":");
                    lines.Add("- Category: " + Md(proposal.Category));
                    lines.Add("- Summary: " + Md(proposal.Summary));
                    lines.Add("- Missing information: " + (proposal.MissingInformation.Count == 0 ? "none" : string.Join(" / ", proposal.MissingInformation.Select(Md))));
                    lines.Add("- Suggested labels: " + (proposal.SuggestedLabels.Count == 0 ? "none" : string.Join(", ", proposal.SuggestedLabels.Select(Md))));
                }
            }

            var ci = report.CiFailure;
            if (ci != null)
            {
                lines.Add("- Failure category: " + Md(ci.Classification.Category) + (ci.Classification.Retryable ? " (a plain re-run may fix it)" : ""));
                lines.Add("- Drafted by: " + Md(ci.DraftedBy ?? "nobody"));
                lines.Add("- Reviewed by: " + (ci.ReviewedBy == null ? "nobody (not reviewed)" : Md(ci.ReviewedBy)));
                lines.Add("- Result: " + Md(ci.Status.ToString()) + (string.IsNullOrEmpty(ci.Error) ? "" : " - " + Md(ErrorSummary(ci.Error))));
                foreach (var finding in ci.Findings)
                {
                    lines.Add("- Reviewer finding: " + Md(finding));
                }

                foreach (var line in ci.Classification.Evidence)
                {
                    lines.Add("- Evidence: " + Md(line));
                }

                if (ci.Proposal != null)
                {
                    lines.Add("");
                    lines.Add("Proposed issue" + (ci.Status == TriageStatus.Blocked ? " (blocked by the reviewer, shown only for reading)" : "") + ":");
                    lines.Add("- Title: " + Md(ci.Proposal.Title));
                    lines.Add("- Body: " + Md(ci.Proposal.Body));
                    lines.Add("- Suggested labels: " + (ci.Proposal.SuggestedLabels.Count == 0 ? "none" : string.Join(", ", ci.Proposal.SuggestedLabels.Select(Md))));
                }
            }

            var comment = string.Join("\n", lines);
            return comment.Length <= MaxLength ? comment : comment.Substring(0, MaxLength - 20) + "\n[comment cut]";
        }

        /// <summary>One line of untrusted text, safe to show on a public page: control characters and line breaks become spaces,
        /// markdown, HTML and table characters are escaped, a mention is broken, an issue reference is escaped, a link is made
        /// inert, and the length is capped.</summary>
        public static string EscapeInline(string? text) => Md(text);

        private static string Md(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            var cut = text!.Length > MaxFieldLength ? text.Substring(0, MaxFieldLength) + "..." : text;
            var builder = new StringBuilder(cut.Length + 8);
            foreach (var c in cut)
            {
                if (char.IsControl(c))
                {
                    builder.Append(' ');
                    continue;
                }

                switch (c)
                {
                    case '\\':
                    case '`':
                    case '*':
                    case '_':
                    case '[':
                    case ']':
                    case '<':
                    case '>':
                    case '|':
                    case '~':
                    case '#':
                    case '!':
                        builder.Append('\\').Append(c);
                        break;
                    case '@':
                        builder.Append("@​");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return Autolink.Replace(builder.ToString(), m => m.Groups["www"].Success ? "www​." : m.Groups["scheme"].Value + ":​//");
        }

        private static string Describe(string code)
        {
            switch (code)
            {
                case "400": return "bad request";
                case "401": return "not authorised";
                case "403": return "forbidden";
                case "404": return "not found; the model or endpoint may not be available for this account";
                case "408":
                case "504": return "timed out";
                case "413": return "request too large";
                case "429": return "rate limited";
                default: return code.StartsWith("5", StringComparison.Ordinal) ? "provider error" : "request failed";
            }
        }
    }
}
