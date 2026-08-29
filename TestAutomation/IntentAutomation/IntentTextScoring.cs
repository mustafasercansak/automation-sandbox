using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IntentAutomation
{
    // Shared text-scoring helpers for IntentExplorationBridge (web) and IntentDesktopExplorationBridge
    // (desktop) (issue #175, #237). Single source of truth to avoid the two bridges' semantic scoring
    // silently diverging as one is edited without the other.

    public static class IntentTextScoring
    {
        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "to", "in", "on", "at", "by", "for", "from", "of",
            "with", "into", "onto", "and", "or", "is", "are", "be", "as", "this",
            "that", "your", "their", "our", "my", "please", "user", "then", "step", "action"
        };

        internal static string BuildMatchingText(IntentStep step)
        {
            // TestIntent explains why the step exists and ExpectedOutcome describes the state
            // after it runs. Neither identifies the element to act on, so letting them influence
            // candidate ranking makes contradictory narrative metadata override the target.
            return step.TargetDescription ?? "";
        }

        public static double TokenOverlap(string targetText, string elementText)
        {
            var targetTokens = SignificantTokens(targetText).ToList();
            if (targetTokens.Count == 0)
            {
                return 0.0;
            }

            var elementTokens = new HashSet<string>(Tokens(elementText), StringComparer.OrdinalIgnoreCase);
            if (elementTokens.Count == 0)
            {
                return 0.0;
            }

            var matches = targetTokens.Count(elementTokens.Contains);
            return (double)matches / targetTokens.Count;
        }

        public static IEnumerable<string> SignificantTokens(string value)
        {
            var tokens = Tokens(value).Where(token => !StopWords.Contains(token)).ToList();
            return tokens.Count > 0 ? tokens : Tokens(value);
        }

        public static IEnumerable<string> Tokens(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Enumerable.Empty<string>();
            }

            var expanded = SplitCamelCase(value);
            return NormalizeText(expanded)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 1);
        }

        public static bool ContainsNormalized(string haystack, string needle)
        {
            var normalizedNeedle = NormalizeText(needle).Replace(" ", "");
            if (normalizedNeedle.Length <= 1)
            {
                return false;
            }

            var normalizedHaystack = NormalizeText(haystack).Replace(" ", "");
            if (normalizedHaystack.Length <= 1)
            {
                return false;
            }

            if (normalizedHaystack.Contains(normalizedNeedle))
            {
                return true;
            }

            var haystackTokens = new HashSet<string>(Tokens(haystack), StringComparer.OrdinalIgnoreCase);
            var needleTokens = SignificantTokens(needle).ToList();
            if (needleTokens.Count > 0 && needleTokens.All(haystackTokens.Contains))
            {
                return true;
            }

            var significantHaystackTokens = SignificantTokens(haystack).ToList();
            if (significantHaystackTokens.Count > 0)
            {
                var needleTokenSet = new HashSet<string>(Tokens(needle), StringComparer.OrdinalIgnoreCase);
                if (significantHaystackTokens.All(needleTokenSet.Contains))
                {
                    return true;
                }
            }

            return false;
        }

        public static string NormalizeText(string value)
        {
            var chars = (value ?? "").Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
            return new string(chars.ToArray());
        }

        public static string Join(params string[] values)
        {
            return string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string SplitCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            var sb = new StringBuilder(value.Length * 2);
            for (int i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (i > 0 && char.IsUpper(ch) && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1])))
                {
                    sb.Append(' ');
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
