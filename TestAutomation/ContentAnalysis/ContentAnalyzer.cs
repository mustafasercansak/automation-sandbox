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
        /// <summary>Counts the text passages <see cref="RunHeuristics(WebElementInfo)" />/<see cref="AnalyzeAsync" /> would
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
            return RunHeuristics(dom, null);
        }

        /// <summary>Runs only the zero-dependency heuristic checks. A repeated word that appears in
        /// <paramref name="allowedWords" /> (compared ignoring case) is not reported: some languages repeat a
        /// word on purpose, for example Turkish <c>adım adım</c> ("step by step"), and the heuristic cannot know
        /// a language.</summary>
        public static IReadOnlyList<ContentIssue> RunHeuristics(WebElementInfo dom, IEnumerable<string>? allowedWords)
        {
            if (dom == null)
            {
                throw new ArgumentNullException(nameof(dom));
            }

            var allowed = allowedWords == null
                ? null
                : new HashSet<string>(allowedWords, StringComparer.OrdinalIgnoreCase);
            var issues = new List<ContentIssue>();
            foreach (var passage in ExtractPassages(dom))
            {
                issues.AddRange(HeuristicContentChecks.Check(passage, allowed));
            }

            return issues;
        }

        /// <summary>Runs only <paramref name="spellingChecker" /> over the same passages the other checks use
        /// (so <c>&lt;pre&gt;</c>/<c>&lt;code&gt;</c> subtrees are skipped and repeated text is checked once).
        /// Kept separate from <see cref="AnalyzeAsync" /> so that existing callers and results are unchanged;
        /// concatenate the results when both are wanted.</summary>
        /// <param name="dom">The captured page.</param>
        /// <param name="spellingChecker">The checker to run.</param>
        /// <param name="capturedTextLimit">The <c>WebDiscoveryOptions.MaxTextLength</c> the page was captured
        /// with, when it is known. A passage as long as the limit was cut off, so its final, probably partial,
        /// word is not checked.</param>
        public static IReadOnlyList<ContentIssue> RunSpellCheck(
            WebElementInfo dom,
            SpellingChecker spellingChecker,
            int? capturedTextLimit = null)
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
                var cutOff = capturedTextLimit.HasValue && passage.Text.Length >= capturedTextLimit.Value;
                issues.AddRange(spellingChecker.Check(passage, cutOff));
            }

            return CollapseInheritedIssues(issues);
        }

        /// <summary>Drops an issue that an ancestor element merely inherited from a descendant. Captured text
        /// includes descendant text, so one typo in a paragraph is also found in the paragraph's parent, its
        /// parent's parent, and so on up to <c>body</c>. An issue is dropped when another issue has the same
        /// type and message and was found in a passage whose text lies inside this one's (even when captured
        /// text was cut off partway through it), leaving the most specific element. Two separate occurrences in unrelated elements are both kept.</summary>
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
                    StartsInside(issue.Text, other.Text));
                if (!inherited)
                {
                    kept.Add(issue);
                }
            }

            return kept;
        }

        // Captured text is cut at a fixed length, so a long ancestor's text may stop partway through a
        // descendant's. Matching the descendant's opening characters, not its whole text, still recognises it.
        private static bool StartsInside(string ancestorText, string descendantText)
        {
            var probe = descendantText.Substring(0, Math.Min(descendantText.Length, 20));
            return ancestorText.IndexOf(probe, StringComparison.Ordinal) >= 0;
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
                if (IsNonProseElement(node.TagName))
                {
                    return;
                }

                var text = node.Text;
                var duplicatesAChild = node.Children.Exists(child => child.Text == text);
                if (!node.IsHidden && !HasUnreliableOwnText(node.TagName) && !string.IsNullOrWhiteSpace(text) && !duplicatesAChild && seenText.Add(text))
                {
                    passages.Add(new ContentPassage(node.CssSelector, text, FlowTextOf(node)));
                }

                foreach (var child in node.Children)
                {
                    Visit(child);
                }
            }
        }

        // Source code and markup that is not shown as text. style/script/template have no innerText, so the
        // capture falls back to their raw source (CSS properties, colours, font names), which reads as
        // hundreds of misspellings. kbd and samp hold key names and program output.
        private static readonly HashSet<string> NonProseTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pre", "code", "style", "script", "noscript", "template", "kbd", "samp",
        };

        // SVG elements have no innerText, so the capture uses textContent, which for the root <svg> includes
        // the source of any <style> inside it (a mermaid diagram embeds a few thousand characters of CSS). The
        // root's own text is therefore not prose; its children (the <text> labels) are still visited.
        // Elements that start a new block or a new widget. The capture collapses all whitespace to single
        // spaces, so the boundary between two table cells, two list items or a heading and the paragraph
        // after it is invisible in the text; the tree is the only place it is still known. Inline elements
        // (a, span, b, em ...) are deliberately absent: <p>Click <b>the</b> the button</p> is one sentence
        // and its repeat is a real typo.
        private static readonly HashSet<string> BlockTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "address", "article", "aside", "blockquote", "body", "button", "dd", "details", "div", "dl", "dt",
            "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header",
            "label", "li", "main", "nav", "ol", "option", "p", "section", "summary", "table", "tbody", "td",
            "tfoot", "th", "thead", "tr", "ul",
        };

        // The node's text with each visible block-level child's text replaced by a line break. A child whose
        // text cannot be found in the parent's (the capture cut the parent shorter) is left in place.
        internal static string FlowTextOf(WebElementInfo node)
        {
            var text = node.Text;
            if (string.IsNullOrEmpty(text) || node.Children.Count == 0)
            {
                return text;
            }

            var flow = new System.Text.StringBuilder();
            var position = 0;
            foreach (var child in node.Children)
            {
                if (child.IsHidden || string.IsNullOrEmpty(child.Text) || !BlockTags.Contains(child.TagName))
                {
                    continue;
                }

                var index = text.IndexOf(child.Text, position, StringComparison.Ordinal);
                var length = child.Text.Length;
                if (index < 0)
                {
                    // The capture cuts a long page at a character limit, so the parent can end partway into this
                    // child while the child's own text (it starts later, so it fits longer) runs on. Then the
                    // end of the parent is the beginning of the child.
                    index = FindTruncatedChild(text, position, child.Text);
                    if (index < 0)
                    {
                        continue;
                    }

                    length = text.Length - index;
                }

                flow.Append(text, position, index - position).Append('\n');
                position = index + length;
            }

            flow.Append(text, position, text.Length - position);
            return flow.ToString();
        }

        // The earliest index at which the rest of the parent's text is the beginning of the child's text, or -1.
        // A tail shorter than four characters is too weak a match to trust.
        private static int FindTruncatedChild(string text, int position, string childText)
        {
            if (text.Length - position > childText.Length)
            {
                return -1;
            }

            for (var index = position; text.Length - index >= 4; index++)
            {
                if (childText.StartsWith(text.Substring(index), StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool HasUnreliableOwnText(string tagName)
        {
            return string.Equals(tagName, "svg", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNonProseElement(string tagName)
        {
            return NonProseTags.Contains(tagName);
        }
    }
}
