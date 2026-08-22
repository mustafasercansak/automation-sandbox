using System;
using System.Collections.Generic;
using System.Linq;

namespace IntentAutomation
{
    // Shared text-scoring helpers for IntentExplorationBridge (web) and IntentDesktopExplorationBridge
    // (desktop) (issue #175). Single source of truth to avoid the two bridges' semantic scoring
    // silently diverging as one is edited without the other.

    public static class IntentTextScoring
    {
        internal static string BuildMatchingText(IntentStep step)
        {
            // TestIntent explains why the step exists and ExpectedOutcome describes the state
            // after it runs. Neither identifies the element to act on, so letting them influence
            // candidate ranking makes contradictory narrative metadata override the target.
            return Join(step.TargetDescription, step.LocatorKey);
        }

        public static double TokenOverlap(string targetText, string elementText)
        {
            var targetTokens = Tokens(targetText).ToList();
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

        public static IEnumerable<string> Tokens(string value)
        {
            return NormalizeText(value)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 1);
        }

        public static bool ContainsNormalized(string haystack, string needle)
        {
            var normalizedNeedle = NormalizeText(needle).Replace(" ", "");
            return normalizedNeedle.Length > 1
                && NormalizeText(haystack).Replace(" ", "").Contains(normalizedNeedle);
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
    }
}
