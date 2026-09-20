using System;
using System.Collections.Generic;
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
    }
}
