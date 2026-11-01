using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>What <see cref="CiLogClassifier.Classify" /> found in a CI log. Plain data computed without a model.</summary>
    public sealed class CiLogClassification
    {
        internal CiLogClassification(string category, IReadOnlyList<string> matchedCategories, bool retryable, IReadOnlyList<string> evidence, string excerpt, bool truncated)
        {
            Category = category;
            MatchedCategories = matchedCategories;
            Retryable = retryable;
            Evidence = evidence;
            Excerpt = excerpt;
            Truncated = truncated;
        }

        /// <summary>The first matching category in rule order, or <see cref="CiLogClassifier.Unknown" />.</summary>
        public string Category { get; }

        /// <summary>Every category whose rule matched, in rule order; empty for <see cref="CiLogClassifier.Unknown" />.</summary>
        public IReadOnlyList<string> MatchedCategories { get; }

        /// <summary>True when the winning category is one a plain re-run may fix (timeout, infrastructure).</summary>
        public bool Retryable { get; }

        /// <summary>At most <see cref="CiLogClassifier.MaxEvidenceLines" /> log lines that matched the winning rule, cleaned and masked.</summary>
        public IReadOnlyList<string> Evidence { get; }

        /// <summary>The end of the cleaned log, at most <see cref="CiLogClassifier.MaxExcerptLength" /> characters, cut on a line boundary.</summary>
        public string Excerpt { get; }

        /// <summary>True when the log was longer than <see cref="CiLogClassifier.MaxLogLength" /> and only its end was read.</summary>
        public bool Truncated { get; }
    }

    /// <summary>Sorts a failed CI log into a category with ordered regular-expression rules. A log holds text a pull-request
    /// author controls, so it is treated as hostile: it is size-limited, every rule has a match timeout, terminal escape codes
    /// and timestamps are removed and common credential shapes are masked before anything leaves this class.</summary>
    public static class CiLogClassifier
    {
        /// <summary>The category of a log no rule matched.</summary>
        public const string Unknown = "unknown";

        /// <summary>Most evidence lines kept.</summary>
        public const int MaxEvidenceLines = 5;

        /// <summary>Longest evidence line, in characters.</summary>
        public const int MaxEvidenceLength = 200;

        /// <summary>Largest log read, in characters. A longer log is cut from the front: a failure is usually at the end.</summary>
        public const int MaxLogLength = 200000;

        /// <summary>Longest excerpt returned, in characters.</summary>
        public const int MaxExcerptLength = 4000;

        private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

        private const RegexOptions Flags = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private sealed class Rule
        {
            public Rule(string category, bool retryable, string pattern)
            {
                Category = category;
                Retryable = retryable;
                Pattern = new Regex(pattern, Flags, MatchTimeout);
            }

            public string Category { get; }

            public bool Retryable { get; }

            public Regex Pattern { get; }
        }

        // Order is priority: the first rule that matches decides the category, because a permission or dependency problem
        // usually causes the build and test lines that follow it, not the other way round.
        private static readonly Rule[] Rules =
        {
            new Rule("permissions", false, @"permission denied|resource not accessible by integration|\b403 forbidden|http(?: status)? 403\b|bad credentials|authentication failed|insufficient permissions|not authorized"),
            new Rule("dependency", false, @"\bNU1(?:101|102|103|301|903|904)\b|unable to find package|could not resolve dependenc|npm err! code (?:E404|ERESOLVE)|package [^\r\n]{1,80} not found|failed to restore"),
            new Rule("build", false, @"\berror (?:CS|MSB|NETSDK|TS)\d+|build failed|compilation failed|\bfailed to compile"),
            new Rule("test", false, @"failed!\s+-\s+failed:|\[fail\]|test run failed|\bassert\.\w+\(\) failure|\d+ (?:tests? )?failing|\d+ failed, \d+ passed"),
            new Rule("timeout", true, @"timed out|\btimeout\b|exceeded the maximum execution time|the operation was canceled"),
            new Rule("infrastructure", true, @"no space left on device|econnreset|etimedout|503 service unavailable|the runner has received a shutdown signal|lost communication with the server|unable to connect to|connection reset by peer"),
        };

        private static readonly Regex AnsiCodes = new Regex(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.CultureInvariant, MatchTimeout);

        private static readonly Regex LeadingTimestamp = new Regex(@"^\s*\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?Z?\s?", RegexOptions.CultureInvariant, MatchTimeout);

        private static readonly Regex[] CredentialShapes =
        {
            new Regex(@"gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}", RegexOptions.CultureInvariant, MatchTimeout),
            new Regex(@"\bAKIA[0-9A-Z]{16}\b|\bAIza[0-9A-Za-z_\-]{30,}|\bsk-[A-Za-z0-9_\-]{20,}|\bxox[baprs]-[A-Za-z0-9\-]{10,}", RegexOptions.CultureInvariant, MatchTimeout),
            new Regex(@"\bBearer\s+[A-Za-z0-9._~+/=\-]{16,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout),
        };

        private static readonly Regex KeyedSecret = new Regex(@"(?<key>\b[\w\-]*(?:api[_\-]?key|token|secret|password|passwd)[\w\-]*\s*[:=]\s*)(?<value>[^\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

        /// <summary>Classifies <paramref name="log" />. A null or empty log is <see cref="Unknown" />. Never throws; a rule whose
        /// match times out counts as no match.</summary>
        public static CiLogClassification Classify(string? log)
        {
            var text = log ?? "";
            var truncated = text.Length > MaxLogLength;
            if (truncated)
            {
                text = text.Substring(text.Length - MaxLogLength);
            }

            var lines = text.Split('\n').Select(CleanLine).Where(l => l.Length > 0).ToList();

            var matched = new List<Rule>();
            foreach (var rule in Rules)
            {
                if (lines.Any(line => IsMatch(rule.Pattern, line)))
                {
                    matched.Add(rule);
                }
            }

            var winner = matched.FirstOrDefault();
            var evidence = winner == null
                ? new List<string>()
                : lines.Where(line => IsMatch(winner.Pattern, line)).Take(MaxEvidenceLines).Select(Shorten).ToList();

            return new CiLogClassification(
                winner?.Category ?? Unknown,
                matched.Select(r => r.Category).ToList(),
                winner != null && winner.Retryable,
                evidence,
                Excerpt(lines),
                truncated);
        }

        private static bool IsMatch(Regex pattern, string line)
        {
            try
            {
                return pattern.IsMatch(line);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        // ANSI codes and a leading timestamp out, control characters to spaces, credential shapes masked. A regex that
        // times out leaves the line as it was only if nothing sensitive could remain, so the whole line is dropped instead.
        private static string CleanLine(string raw)
        {
            try
            {
                var line = AnsiCodes.Replace(raw, "");
                line = LeadingTimestamp.Replace(line, "");
                var builder = new StringBuilder(line.Length);
                foreach (var c in line)
                {
                    builder.Append(char.IsControl(c) ? ' ' : c);
                }

                line = builder.ToString();
                foreach (var shape in CredentialShapes)
                {
                    line = shape.Replace(line, "[masked]");
                }

                line = KeyedSecret.Replace(line, m => m.Groups["key"].Value + "[masked]");
                return line.Trim();
            }
            catch (RegexMatchTimeoutException)
            {
                return "";
            }
        }

        private static string Excerpt(List<string> lines)
        {
            var kept = new List<string>();
            var length = 0;
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (line.Length > MaxEvidenceLength * 2)
                {
                    line = line.Substring(0, MaxEvidenceLength * 2);
                }

                if (length + line.Length + 1 > MaxExcerptLength)
                {
                    break;
                }

                kept.Add(line);
                length += line.Length + 1;
            }

            kept.Reverse();
            return string.Join("\n", kept);
        }

        private static string Shorten(string line) => line.Length <= MaxEvidenceLength ? line : line.Substring(0, MaxEvidenceLength) + "...";
    }
}
