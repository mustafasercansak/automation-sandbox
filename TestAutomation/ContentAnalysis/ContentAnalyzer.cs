using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.WebDiscovery;

namespace AutomationSandbox.ContentAnalysis
{
    /// <summary>Entry point for content-quality checks over a captured <see cref="WebElementInfo" /> tree:
    /// zero-dependency heuristics always run, and an <see cref="IContentAnalysisProvider" /> adds a language
    /// review layer when one is supplied and available.</summary>
    public static class ContentAnalyzer
    {
        /// <summary>Counts the text passages <see cref="RunHeuristics" />/<see cref="AnalyzeAsync" /> would
        /// consider for <paramref name="dom" />, without running any check. A page-scan report can record
        /// this alongside its issue count: zero passages on a page that should have visible text usually
        /// means the DOM was captured before dynamic content rendered, not that the page is clean.</summary>
        public static int CountPassages(WebElementInfo dom)
        {
            if (dom == null)
            {
                throw new ArgumentNullException(nameof(dom));
            }

            return ExtractPassages(dom).Count;
        }

        /// <summary>Runs only the zero-dependency heuristic checks.</summary>
        public static IReadOnlyList<ContentIssue> RunHeuristics(WebElementInfo dom)
        {
            if (dom == null)
            {
                throw new ArgumentNullException(nameof(dom));
            }

            var issues = new List<ContentIssue>();
            foreach (var passage in ExtractPassages(dom))
            {
                issues.AddRange(HeuristicContentChecks.Check(passage));
            }

            return issues;
        }

        /// <summary>Runs only <paramref name="spellingChecker" /> over the same passages the other checks use
        /// (so <c>&lt;pre&gt;</c>/<c>&lt;code&gt;</c> subtrees are skipped and repeated text is checked once).
        /// Kept separate from <see cref="AnalyzeAsync" /> so that existing callers and results are unchanged;
        /// concatenate the results when both are wanted.</summary>
        public static IReadOnlyList<ContentIssue> RunSpellCheck(WebElementInfo dom, SpellingChecker spellingChecker)
        {
            if (dom == null)
            {
                throw new ArgumentNullException(nameof(dom));
            }

            if (spellingChecker == null)
            {
                throw new ArgumentNullException(nameof(spellingChecker));
            }

            var issues = new List<ContentIssue>();
            foreach (var passage in ExtractPassages(dom))
            {
                issues.AddRange(spellingChecker.Check(passage));
            }

            return CollapseInheritedIssues(issues);
        }

        /// <summary>Drops an issue that an ancestor element merely inherited from a descendant. Captured text
        /// includes descendant text, so one typo in a paragraph is also found in the paragraph's parent, its
        /// parent's parent, and so on up to <c>body</c>. An issue is dropped when another issue has the same
        /// type and message and was found in a passage whose text lies inside this one's, leaving the most
        /// specific element. Two separate occurrences in unrelated elements are both kept.</summary>
        public static IReadOnlyList<ContentIssue> CollapseInheritedIssues(IReadOnlyList<ContentIssue> issues)
        {
            if (issues == null)
            {
                throw new ArgumentNullException(nameof(issues));
            }

            var kept = new List<ContentIssue>();
            foreach (var issue in issues)
            {
                var inherited = issues.Any(other =>
                    !ReferenceEquals(other, issue) &&
                    other.IssueType == issue.IssueType &&
                    other.Message == issue.Message &&
                    other.Text.Length < issue.Text.Length &&
                    issue.Text.IndexOf(other.Text, StringComparison.Ordinal) >= 0);
                if (!inherited)
                {
                    kept.Add(issue);
                }
            }

            return kept;
        }

        /// <summary>Runs the heuristic checks, then <paramref name="llmProvider" /> when supplied and
        /// available, merging both into one result.</summary>
        public static async Task<IReadOnlyList<ContentIssue>> AnalyzeAsync(
            WebElementInfo dom,
            IContentAnalysisProvider? llmProvider = null,
            CancellationToken cancellationToken = default)
        {
            if (dom == null)
            {
                throw new ArgumentNullException(nameof(dom));
            }

            var passages = ExtractPassages(dom);
            var issues = new List<ContentIssue>();
            foreach (var passage in passages)
            {
                issues.AddRange(HeuristicContentChecks.Check(passage));
            }

            if (llmProvider != null && llmProvider.IsAvailable && passages.Count > 0)
            {
                var llmIssues = await llmProvider.AnalyzeAsync(passages, cancellationToken).ConfigureAwait(false);
                issues.AddRange(llmIssues);
            }

            return issues;
        }

        // Captured .Text is innerText/textContent (see PlaywrightDomCaptureScript.textOf), which includes
        // descendant text - so a wrapper element's Text is often identical to its one meaningful child's
        // Text (e.g. <button><span>Save</span></button>). Skipping a node whose Text exactly matches a
        // child's, and de-duplicating exact repeats across the whole tree, keeps each real string from
        // being flagged and re-sent to an LLM once per ancestor level.
        private static IReadOnlyList<ContentPassage> ExtractPassages(WebElementInfo root)
        {
            var passages = new List<ContentPassage>();
            var seenText = new HashSet<string>(StringComparer.Ordinal);
            Visit(root);
            return passages;

            void Visit(WebElementInfo node)
            {
                // <pre>/<code> holds source code, not prose: identifiers, symbols, and deliberate markers
                // like "// TODO" read as spelling/duplicate-word/placeholder defects to these heuristics
                // (and would read the same way to an LLM reviewer) but are not content-quality issues.
                // Skipping the whole subtree - not just the pre/code node itself - keeps a syntax-highlighted
                // child <span> from being extracted as its own passage too.
                if (IsCodeElement(node.TagName))
                {
                    return;
                }

                var text = node.Text;
                var duplicatesAChild = node.Children.Exists(child => child.Text == text);
                if (!node.IsHidden && !string.IsNullOrWhiteSpace(text) && !duplicatesAChild && seenText.Add(text))
                {
                    passages.Add(new ContentPassage(node.CssSelector, text));
                }

                foreach (var child in node.Children)
                {
                    Visit(child);
                }
            }
        }

        private static bool IsCodeElement(string tagName)
        {
            return string.Equals(tagName, "pre", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tagName, "code", StringComparison.OrdinalIgnoreCase);
        }
    }
}
