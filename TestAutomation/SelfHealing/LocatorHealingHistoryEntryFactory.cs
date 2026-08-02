using UiModel;
namespace SelfHealing
{
    // Bridges a HealResult (this project's output) into the LocatorHealingHistoryEntry shape a
    // LocatorRepository persists. Lives here rather than in UiModel so UiModel stays consumer-
    // agnostic - the dependency direction is UiModel <- SelfHealing, never the reverse.
    public static class LocatorHealingHistoryEntryFactory
    {
        public static LocatorHealingHistoryEntry FromHealResult(HealResult result, UiElementInfo? previousSnapshot)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (result.Matched == null)
            {
                throw new InvalidOperationException("Cannot record healing history for a HealResult with no Matched element.");
            }

            return new LocatorHealingHistoryEntry
            {
                Source = result.Source == HealSource.Llm ? result.LlmProviderName ?? "llm" : "heuristic",
                Score = result.Score,
                ConfidenceThreshold = result.ConfidenceThreshold,
                LlmConfidence = result.LlmConfidence,
                LlmProviderName = result.LlmProviderName,
                PreviousSnapshot = previousSnapshot,
                AcceptedSnapshot = result.Matched,
                ScoreBreakdown = result.ScoreBreakdown,
            };
        }
    }
}
