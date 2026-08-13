namespace UiModel
{
    // Shared runner-up margin rule (issue #4): a top candidate that barely beats the
    // runner-up is ambiguous, not confident. Lives in UiModel so both SelfHealing
    // (SelfHealingResolver) and IntentAutomation (intent matchers, issue #5) use the same
    // implementation - two copies would drift over time.
    //
    // Margin = bestScore - runnerUpScore. With a single candidate there is no competition,
    // so the margin is treated as sufficient by definition.

    public static class CandidateMargin
    {
        public static bool HasSufficientMargin(double bestScore, double? runnerUpScore, double minimumMargin)
        {
            if (!runnerUpScore.HasValue)
            {
                return true;
            }

            return bestScore - runnerUpScore.Value >= minimumMargin;
        }
    }
}
