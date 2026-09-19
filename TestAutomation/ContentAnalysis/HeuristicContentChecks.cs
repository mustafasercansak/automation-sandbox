using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AutomationSandbox.ContentAnalysis
{
    // Zero-dependency, always-on checks: cheap enough to run on every captured passage, and
    // narrow enough (word-boundary anchored, 4+ repeated characters) to stay low-false-positive
    // on real UI copy rather than flagging legitimate text that merely looks similar.
    internal static class HeuristicContentChecks
    {
        private static readonly Regex DuplicateWord = new(@"\b(\w+)\s+\1\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PlaceholderMarker = new(
            @"\b(TODO|TBD|XXX|NaN|undefined)\b|lorem ipsum|\[object Object\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RepeatedPunctuation = new(@"([!?.\-])\1{3,}", RegexOptions.Compiled);

        public static IEnumerable<ContentIssue> Check(ContentPassage passage)
        {
            var text = passage.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                yield break;
            }

            var duplicateMatch = DuplicateWord.Match(text);
            if (duplicateMatch.Success)
            {
                yield return new ContentIssue(
                    passage.CssSelector, text, "DuplicateWord",
                    $"Repeated word \"{duplicateMatch.Groups[1].Value}\".", ContentIssueSource.Heuristic);
            }

            var placeholderMatch = PlaceholderMarker.Match(text);
            if (placeholderMatch.Success)
            {
                yield return new ContentIssue(
                    passage.CssSelector, text, "Placeholder",
                    $"Looks like leftover placeholder text (\"{placeholderMatch.Value}\").", ContentIssueSource.Heuristic);
            }

            var punctuationMatch = RepeatedPunctuation.Match(text);
            if (punctuationMatch.Success)
            {
                yield return new ContentIssue(
                    passage.CssSelector, text, "RepeatedPunctuation",
                    $"Repeated punctuation (\"{punctuationMatch.Value}\").", ContentIssueSource.Heuristic);
            }
        }
    }
}
