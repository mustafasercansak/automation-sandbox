using System;
using UiModel;

namespace IntentAutomation
{
    // Shared evaluation logic for IntentExplorationBridge and IntentDesktopExplorationBridge (issue #5).
    // Determines if a top candidate meets the review threshold, semantic score gate, and runner-up margin.
    // Single source of truth to avoid behavioral drift between Web and Desktop exploration matching.

    public static class IntentCandidateReviewEvaluator
    {
        public static (bool RequiresReview, string Diagnostic) Evaluate(
            double bestScore,
            double bestSemanticScore,
            double? runnerUpScore,
            double reviewThreshold,
            double minimumSemanticScore,
            double minimumCandidateMargin)
        {
            if (bestScore < reviewThreshold)
            {
                return (true, $"Best candidate score {bestScore:F2} is below review threshold {reviewThreshold:F2}.");
            }

            if (bestSemanticScore < minimumSemanticScore)
            {
                return (true, $"Best candidate semantic score {bestSemanticScore:F2} is below semantic gate {minimumSemanticScore:F2}.");
            }

            if (!CandidateMargin.HasSufficientMargin(bestScore, runnerUpScore, minimumCandidateMargin))
            {
                var runnerUp = runnerUpScore ?? 0.0;
                return (true, $"Best candidate score {bestScore:F2} is too close to runner-up {runnerUp:F2} (margin {bestScore - runnerUp:F3} < {minimumCandidateMargin:F2}).");
            }

            return (false, "");
        }
    }
}
