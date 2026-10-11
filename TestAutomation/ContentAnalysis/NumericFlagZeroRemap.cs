using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutomationSandbox.ContentAnalysis
{
    // Hunspell dictionaries that declare "FLAG num" name affix rules with numbers, and the Turkish one uses
    // flag 0 for the very common "-de" locative suffix (evde, listede, sekilde: 4660 entries). The Hunspell
    // library reads flag 0 as "no flag", so every such form was reported as a misspelling. Renaming flag 0 to
    // an unused number in memory fixes that without touching the dictionary file on disk.
    //
    // Works on text decoded as Latin-1 so a round trip preserves every byte whatever encoding the dictionary
    // declares; only ASCII markers are matched or written.
    internal static class NumericFlagZeroRemap
    {
        private static readonly Regex NumericFlagDirective = new Regex(@"^\s*FLAG\s+num\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex AliasLine = new Regex(@"^\s*AF\s", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex RuleHeader = new Regex(@"^(SFX|PFX)(\s+)(\d+)(\s)", RegexOptions.Multiline | RegexOptions.Compiled);

        public static bool TryRemap(string affix, string dictionary, out string newAffix, out string newDictionary)
        {
            newAffix = affix;
            newDictionary = dictionary;

            // With AF aliases the numbers in the dictionary are alias indexes, not flags: leave those alone.
            if (!NumericFlagDirective.IsMatch(affix) || AliasLine.IsMatch(affix))
            {
                return false;
            }

            var usesZero = false;
            var highest = 0;
            foreach (Match header in RuleHeader.Matches(affix))
            {
                var number = int.Parse(header.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                usesZero |= number == 0;
                highest = Math.Max(highest, number);
            }

            if (!usesZero)
            {
                return false;
            }

            var lines = dictionary.Split('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                foreach (var flag in FlagsOf(lines[i]))
                {
                    int number;
                    if (int.TryParse(flag, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number))
                    {
                        highest = Math.Max(highest, number);
                    }
                }
            }

            var replacement = (highest + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            newAffix = RuleHeader.Replace(affix, match =>
                match.Groups[3].Value == "0"
                    ? match.Groups[1].Value + match.Groups[2].Value + replacement + match.Groups[4].Value
                    : match.Value);

            for (var i = 1; i < lines.Length; i++)
            {
                lines[i] = ReplaceZeroFlag(lines[i], replacement);
            }

            newDictionary = string.Join("\n", lines);
            return true;
        }

        // A dictionary line looks like "word/12,34,0 po:noun": the flags run from the first unescaped slash to
        // the next whitespace.
        private static IEnumerable<string> FlagsOf(string line)
        {
            int start, end;
            return FlagSpan(line, out start, out end)
                ? line.Substring(start, end - start).Split(',')
                : Enumerable.Empty<string>();
        }

        private static string ReplaceZeroFlag(string line, string replacement)
        {
            int start, end;
            if (!FlagSpan(line, out start, out end))
            {
                return line;
            }

            var flags = line.Substring(start, end - start).Split(',');
            if (!flags.Contains("0"))
            {
                return line;
            }

            var changed = string.Join(",", flags.Select(flag => flag == "0" ? replacement : flag));
            return line.Substring(0, start) + changed + line.Substring(end);
        }

        private static bool FlagSpan(string line, out int start, out int end)
        {
            start = -1;
            end = -1;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == '/' && (i == 0 || line[i - 1] != '\\'))
                {
                    start = i + 1;
                    break;
                }
            }

            if (start < 0)
            {
                return false;
            }

            end = start;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
            {
                end++;
            }

            return end > start;
        }
    }
}
