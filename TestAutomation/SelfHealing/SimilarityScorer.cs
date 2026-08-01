using UiModel;

namespace SelfHealing
{
    // Pure heuristic, no LLM involved: ControlType match is a mandatory pre-filter,
    // then parent context + sibling position + name similarity + screen position
    // are combined with weights into a single 0..1 score. See TestAutomation/LlmHealing
    // for the separate LLM-based comparison harness.
    internal static class SimilarityScorer
    {
        private const double ParentControlTypeWeight = 0.25;
        private const double SiblingPositionWeight = 0.20;
        private const double NameWeight = 0.25;
        private const double PositionWeight = 0.30;

        private const double PositionToleranceRadius = 300.0;

        public static double Score(UiElementInfo expected, UiElementInfo candidate)
        {
            if (!string.Equals(expected.ControlType, candidate.ControlType, StringComparison.Ordinal))
            {
                return 0.0;
            }

            var parentScore = string.Equals(expected.ParentControlType, candidate.ParentControlType, StringComparison.Ordinal)
                ? 1.0
                : 0.0;

            var siblingScore = SiblingPositionScore(expected, candidate);
            var nameScore = NameSimilarity(expected.Name, candidate.Name);
            var positionScore = PositionSimilarity(expected.BoundingRectangle, candidate.BoundingRectangle);

            return parentScore * ParentControlTypeWeight
                 + siblingScore * SiblingPositionWeight
                 + nameScore * NameWeight
                 + positionScore * PositionWeight;
        }

        private static double SiblingPositionScore(UiElementInfo expected, UiElementInfo candidate)
        {
            var maxCount = Math.Max(expected.SiblingCount, candidate.SiblingCount);
            if (maxCount <= 0)
            {
                return 1.0;
            }

            var diff = Math.Abs(expected.SiblingIndex - candidate.SiblingIndex);
            return Math.Max(0.0, 1.0 - (double)diff / maxCount);
        }

        private static double NameSimilarity(string expected, string candidate)
        {
            if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(candidate))
            {
                return 1.0;
            }

            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(candidate))
            {
                return 0.0;
            }

            if (string.Equals(expected, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            var distance = LevenshteinDistance(expected, candidate);
            var maxLength = Math.Max(expected.Length, candidate.Length);
            return Math.Max(0.0, 1.0 - (double)distance / maxLength);
        }

        private static double PositionSimilarity(BoundingRectangle expected, BoundingRectangle candidate)
        {
            var expectedCenterX = expected.X + expected.Width / 2;
            var expectedCenterY = expected.Y + expected.Height / 2;
            var candidateCenterX = candidate.X + candidate.Width / 2;
            var candidateCenterY = candidate.Y + candidate.Height / 2;

            var distance = Math.Sqrt(
                Math.Pow(expectedCenterX - candidateCenterX, 2) +
                Math.Pow(expectedCenterY - candidateCenterY, 2));

            return Math.Max(0.0, 1.0 - distance / PositionToleranceRadius);
        }

        private static int LevenshteinDistance(string a, string b)
        {
            var dp = new int[a.Length + 1, b.Length + 1];

            for (var i = 0; i <= a.Length; i++)
            {
                dp[i, 0] = i;
            }

            for (var j = 0; j <= b.Length; j++)
            {
                dp[0, j] = j;
            }

            for (var i = 1; i <= a.Length; i++)
            {
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
                }
            }

            return dp[a.Length, b.Length];
        }
    }
}
