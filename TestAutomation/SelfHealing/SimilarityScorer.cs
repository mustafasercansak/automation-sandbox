using UiModel;
namespace SelfHealing
{
    // Pure heuristic, no LLM involved: ControlType, parent context, sibling
    // position, name similarity, and screen position are combined with weights
    // into a single 0..1 score. See TestAutomation/LlmHealing for the separate
    // LLM-based comparison harness.

    internal static class SimilarityScorer
    {
        public static CandidateScore ScoreCandidate(UiElementInfo expected, UiElementInfo candidate, SimilarityWeights? weights = null)
        {
            var w = weights ?? SimilarityWeights.Default;
            var controlTypeScore = ExactMatchScore(expected.ControlType, candidate.ControlType);
            var parentScore = ExactMatchScore(expected.ParentControlType, candidate.ParentControlType);
            var siblingScore = SiblingPositionScore(expected, candidate);
            var nameScore = NameSimilarity(expected.Name, candidate.Name);
            var positionScore = PositionSimilarity(expected.BoundingRectangle, candidate.BoundingRectangle, w.PositionToleranceRadius);

            // Only non-null signals participate: a missing signal neither rewards nor
            // penalizes, its weight simply drops out of the weighted average. EvidenceCoverage
            // keeps track of how much of the total weight actually fired, so the resolver can
            // refuse to call a thin-evidence 1.0 "confident" (see MinimumEvidenceWeight).
            var totalWeight = w.ControlTypeWeight
                            + w.ParentControlTypeWeight
                            + w.SiblingPositionWeight
                            + w.NameWeight
                            + w.PositionWeight;
            var weightedScore = 0.0;
            var activeWeight = 0.0;
            Accumulate(controlTypeScore, w.ControlTypeWeight, ref weightedScore, ref activeWeight);
            Accumulate(parentScore, w.ParentControlTypeWeight, ref weightedScore, ref activeWeight);
            Accumulate(siblingScore, w.SiblingPositionWeight, ref weightedScore, ref activeWeight);
            Accumulate(nameScore, w.NameWeight, ref weightedScore, ref activeWeight);
            Accumulate(positionScore, w.PositionWeight, ref weightedScore, ref activeWeight);

            var totalScore = activeWeight <= 0.0 ? 0.0 : weightedScore / activeWeight;
            return new CandidateScore
            {
                Candidate = candidate,
                TotalScore = totalScore,
                EvidenceCoverage = totalWeight <= 0.0 ? 0.0 : activeWeight / totalWeight,
                Components = new ScoreComponents
                {
                    ControlTypeScore = controlTypeScore,
                    ParentControlTypeScore = parentScore,
                    SiblingPositionScore = siblingScore,
                    NameScore = nameScore,
                    PositionScore = positionScore,
                },
            };
        }

        private static void Accumulate(double? score, double weight, ref double weightedScore, ref double activeWeight)
        {
            if (score.HasValue)
            {
                weightedScore += score.Value * weight;
                activeWeight += weight;
            }
        }

        // "Missing == missing" is not a match: when both sides lack the signal there is no
        // evidence either way, so the signal reports null and drops out of the average.

        private static double? ExactMatchScore(string expected, string candidate)
        {
            if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(candidate))
            {
                return null;
            }

            return string.Equals(expected, candidate, StringComparison.Ordinal) ? 1.0 : 0.0;
        }

        private static double? SiblingPositionScore(UiElementInfo expected, UiElementInfo candidate)
        {
            var maxCount = Math.Max(expected.SiblingCount, candidate.SiblingCount);
            if (maxCount <= 0)
            {
                return null;
            }

            var diff = Math.Abs(expected.SiblingIndex - candidate.SiblingIndex);
            return Math.Max(0.0, 1.0 - (double)diff / maxCount);
        }

        private static double? NameSimilarity(string expected, string candidate)
        {
            if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(candidate))
            {
                return null;
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

        private static double? PositionSimilarity(BoundingRectangle expected, BoundingRectangle candidate, double positionToleranceRadius)
        {
            if (positionToleranceRadius <= 0.0 || !expected.IsUsable || !candidate.IsUsable)
            {
                return null;
            }

            var expectedCenterX = expected.X + expected.Width / 2;
            var expectedCenterY = expected.Y + expected.Height / 2;
            var candidateCenterX = candidate.X + candidate.Width / 2;
            var candidateCenterY = candidate.Y + candidate.Height / 2;
            var distance = Math.Sqrt(
                Math.Pow(expectedCenterX - candidateCenterX, 2) +
                Math.Pow(expectedCenterY - candidateCenterY, 2));
            return Math.Max(0.0, 1.0 - distance / positionToleranceRadius);
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
