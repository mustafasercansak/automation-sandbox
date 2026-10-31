using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AutomationSandbox.ContentAnalysis
{
    // Zero-dependency, always-on checks: cheap enough to run on every captured passage, and
    // narrow enough (word-boundary anchored, 4+ repeated characters) to stay low-false-positive
    // on real UI copy rather than flagging legitimate text that merely looks similar.
    internal static class HeuristicContentChecks
    {
        // A typo is a repeat inside one run of prose, so the two words must be separated by plain
        // spaces only. The check runs on ContentPassage.FlowText, where the text of block-level children
        // (table cells, list items, headings) has been replaced by a line break: "No<NL>No" is two cells
        // that happen to hold the same word, not a mistake. The captured Text cannot show that boundary
        // because the capture collapses all whitespace. The word must start with a letter (a repeated number is never a typo) and must
        // not run on into more word characters or a file extension ("expected expected.json").
        private static readonly Regex DuplicateWord = new(
            @"(?<![\p{L}\p{N}_.])(?<first>\p{L}[\p{L}\p{N}_'\u2019]*)[ \u00A0\u202F]+(?<second>\k<first>)(?![\p{L}\p{N}_'\u2019]|\.[\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PlaceholderMarker = new(
            @"\b(TODO|TBD|XXX|NaN|undefined)\b|lorem ipsum|\[object Object\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RepeatedPunctuation = new(@"([!?.\-])\1{3,}", RegexOptions.Compiled);

        public static IEnumerable<ContentIssue> Check(ContentPassage passage, ISet<string>? allowedRepeats = null)
        {
            var text = passage.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                yield break;
            }

            var duplicate = FindDuplicateWord(passage.FlowText, allowedRepeats);
            if (duplicate != null)
            {
                yield return new ContentIssue(
                    passage.CssSelector, text, "DuplicateWord",
                    $"Repeated word \"{duplicate}\".", ContentIssueSource.Heuristic);
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

        // The first repeat that is not an identifier. A name with a capital inside it ("HealResult
        // healResult", "MaxRetries maxRetries") is code written in prose, and the two spellings differ
        // only by case, so the case-insensitive match would otherwise call it a typo.
        private static string? FindDuplicateWord(string text, ISet<string>? allowedRepeats)
        {
            foreach (Match match in DuplicateWord.Matches(text))
            {
                var first = match.Groups["first"].Value;
                var second = match.Groups["second"].Value;
                if (!HasInternalCapital(first) && !HasInternalCapital(second) &&
                    (allowedRepeats == null || !allowedRepeats.Contains(first)))
                {
                    return first;
                }
            }

            return null;
        }

        private static bool HasInternalCapital(string word)
        {
            for (var i = 1; i < word.Length; i++)
            {
                if (char.IsUpper(word[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
