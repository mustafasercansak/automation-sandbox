using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AutomationSandbox.PlaywrightLiveExploration
{
    /// <summary>The parts of a <c>robots.txt</c> file a crawler needs, following RFC 9309: <c>User-agent</c>
    /// groups, <c>Allow</c> and <c>Disallow</c> rules with <c>*</c> and a trailing <c>$</c>, <c>Crawl-delay</c>
    /// and <c>Sitemap</c> lines. Pure parsing, no network access. Percent-encoded paths are compared as
    /// written, not normalised, so a rule written with <c>%7E</c> does not match a request path using
    /// <c>~</c>.</summary>
    public sealed class RobotsTxt
    {
        // A pattern from a third-party file is untrusted input; a match that runs this long is treated as
        // "this rule does not apply" rather than allowed to stall the crawl.
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

        private readonly List<Group> _groups;

        private RobotsTxt(List<Group> groups, List<string> sitemaps)
        {
            _groups = groups;
            Sitemaps = sitemaps;
        }

        /// <summary>A file that allows everything: what a missing or unreadable <c>robots.txt</c> means.</summary>
        public static RobotsTxt AllowAll { get; } = new RobotsTxt(new List<Group>(), new List<string>());

        /// <summary>Absolute <c>Sitemap:</c> URLs declared anywhere in the file, in order.</summary>
        public IReadOnlyList<string> Sitemaps { get; }

        /// <summary>Parses <paramref name="content" />. Null or blank content is <see cref="AllowAll" />;
        /// unknown fields and malformed lines are ignored, as the standard requires.</summary>
        public static RobotsTxt Parse(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return AllowAll;
            }

            var groups = new List<Group>();
            var sitemaps = new List<string>();
            Group? current = null;
            var lastLineWasAgent = false;

            foreach (var rawLine in content!.Split('\n'))
            {
                var line = rawLine.Trim('\r', '﻿', ' ', '\t');
                var hash = line.IndexOf('#');
                if (hash >= 0)
                {
                    line = line.Substring(0, hash).TrimEnd();
                }

                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var field = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();

                switch (field)
                {
                    case "user-agent":
                        if (current == null || !lastLineWasAgent)
                        {
                            current = new Group();
                            groups.Add(current);
                        }

                        current.Agents.Add(value.ToLowerInvariant());
                        lastLineWasAgent = true;
                        break;
                    case "allow":
                    case "disallow":
                        if (current != null)
                        {
                            current.Rules.Add(new Rule(field == "allow", value));
                            lastLineWasAgent = false;
                        }

                        break;
                    case "crawl-delay":
                        if (current != null)
                        {
                            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay) &&
                                delay >= 0 && !double.IsInfinity(delay) && !double.IsNaN(delay))
                            {
                                current.CrawlDelaySeconds = delay;
                            }

                            lastLineWasAgent = false;
                        }

                        break;
                    case "sitemap":
                        if (Uri.TryCreate(value, UriKind.Absolute, out var sitemap) &&
                            (sitemap.Scheme == Uri.UriSchemeHttp || sitemap.Scheme == Uri.UriSchemeHttps))
                        {
                            sitemaps.Add(value);
                        }

                        break;
                }
            }

            return new RobotsTxt(groups, sitemaps);
        }

        /// <summary>True when <paramref name="userAgent" /> may fetch <paramref name="pathAndQuery" />. The
        /// longest matching rule wins and <c>Allow</c> wins a tie; no matching rule means allowed. The file
        /// <c>/robots.txt</c> itself is always allowed.</summary>
        public bool IsAllowed(string userAgent, string pathAndQuery)
        {
            if (string.IsNullOrEmpty(pathAndQuery))
            {
                pathAndQuery = "/";
            }

            if (string.Equals(pathAndQuery, "/robots.txt", StringComparison.Ordinal))
            {
                return true;
            }

            Rule? best = null;
            foreach (var rule in RulesFor(userAgent))
            {
                if (rule.Pattern.Length == 0 || !rule.Matches(pathAndQuery))
                {
                    continue;
                }

                if (best == null || rule.Pattern.Length > best.Pattern.Length ||
                    (rule.Pattern.Length == best.Pattern.Length && rule.Allow && !best.Allow))
                {
                    best = rule;
                }
            }

            return best == null || best.Allow;
        }

        /// <summary>The <c>Crawl-delay</c> in seconds that applies to <paramref name="userAgent" />, or null.</summary>
        public double? GetCrawlDelaySeconds(string userAgent)
        {
            double? delay = null;
            foreach (var group in MatchingGroups(userAgent))
            {
                if (group.CrawlDelaySeconds.HasValue)
                {
                    delay = group.CrawlDelaySeconds;
                }
            }

            return delay;
        }

        private IEnumerable<Rule> RulesFor(string userAgent)
        {
            foreach (var group in MatchingGroups(userAgent))
            {
                foreach (var rule in group.Rules)
                {
                    yield return rule;
                }
            }
        }

        // The most specific group wins: the longest agent token that appears in our own name. Groups that
        // name the same agent are merged. Only when no group names us does the "*" group apply.
        private List<Group> MatchingGroups(string userAgent)
        {
            var name = (userAgent ?? string.Empty).Trim().ToLowerInvariant();
            var slash = name.IndexOf('/');
            if (slash >= 0)
            {
                name = name.Substring(0, slash);
            }

            var bestLength = 0;
            foreach (var group in _groups)
            {
                foreach (var agent in group.Agents)
                {
                    if (agent.Length > 0 && agent != "*" && name.Contains(agent) && agent.Length > bestLength)
                    {
                        bestLength = agent.Length;
                    }
                }
            }

            var matching = new List<Group>();
            foreach (var group in _groups)
            {
                foreach (var agent in group.Agents)
                {
                    var hit = bestLength > 0 ? agent.Length == bestLength && name.Contains(agent) : agent == "*";
                    if (hit)
                    {
                        matching.Add(group);
                        break;
                    }
                }
            }

            return matching;
        }

        private sealed class Group
        {
            public List<string> Agents { get; } = new List<string>();
            public List<Rule> Rules { get; } = new List<Rule>();
            public double? CrawlDelaySeconds { get; set; }
        }

        private sealed class Rule
        {
            private readonly Regex _regex;

            public Rule(bool allow, string pattern)
            {
                Allow = allow;
                Pattern = pattern;
                var builder = new StringBuilder("^");
                var anchored = pattern.EndsWith("$", StringComparison.Ordinal);
                var body = anchored ? pattern.Substring(0, pattern.Length - 1) : pattern;
                foreach (var part in body.Split('*'))
                {
                    builder.Append(Regex.Escape(part)).Append(".*");
                }

                // The loop appends ".*" after the last segment too; that is the implicit prefix match.
                builder.Length -= 2;
                if (anchored)
                {
                    builder.Append('$');
                }
                else
                {
                    builder.Append(".*");
                }

                _regex = new Regex(builder.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline, MatchTimeout);
            }

            public bool Allow { get; }

            public string Pattern { get; }

            public bool Matches(string pathAndQuery)
            {
                try
                {
                    return _regex.IsMatch(pathAndQuery);
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }
            }
        }
    }
}
