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



            var controlTypeScore = string.Equals(expected.ControlType, candidate.ControlType, StringComparison.Ordinal)

                ? 1.0

                : 0.0;



            var parentScore = string.Equals(expected.ParentControlType, candidate.ParentControlType, StringComparison.Ordinal)

                ? 1.0

                : 0.0;



            var siblingScore = SiblingPositionScore(expected, candidate);

            var nameScore = NameSimilarity(expected.Name, candidate.Name);

            var positionScore = PositionSimilarity(expected.BoundingRectangle, candidate.BoundingRectangle, w.PositionToleranceRadius);



            var weightedScore = controlTypeScore * w.ControlTypeWeight

                              + parentScore * w.ParentControlTypeWeight

                              + siblingScore * w.SiblingPositionWeight

                              + nameScore * w.NameWeight;

            var activeWeight = w.ControlTypeWeight

                             + w.ParentControlTypeWeight

                             + w.SiblingPositionWeight

                             + w.NameWeight;



            if (positionScore.HasValue)

            {

                weightedScore += positionScore.Value * w.PositionWeight;

                activeWeight += w.PositionWeight;

            }



            var totalScore = activeWeight <= 0.0 ? 0.0 : weightedScore / activeWeight;



            return new CandidateScore

            {

                Candidate = candidate,

                TotalScore = totalScore,

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



        private static double? PositionSimilarity(BoundingRectangle expected, BoundingRectangle candidate, double positionToleranceRadius)

        {

            if (positionToleranceRadius <= 0.0 || !IsUsableRectangle(expected) || !IsUsableRectangle(candidate))

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



        private static bool IsUsableRectangle(BoundingRectangle rectangle)

        {

            return rectangle.Width > 0.0 || rectangle.Height > 0.0;

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
