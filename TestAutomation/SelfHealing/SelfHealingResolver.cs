using Discovery;

namespace SelfHealing
{
    public static class SelfHealingResolver
    {
        public const double MinimumConfidence = 0.5;

        // expected: the last known snapshot state of the locator that just broke.
        // currentTreeRoot: a freshly captured UI tree from the application right now.
        public static HealResult Resolve(UiElementInfo expected, UiElementInfo currentTreeRoot, Action<string>? log = null)
        {
            log ??= Console.WriteLine;

            var scoredCandidates = Flatten(currentTreeRoot)
                .Select(candidate => (Candidate: candidate, Score: SimilarityScorer.Score(expected, candidate)))
                .Where(x => x.Score > 0.0)
                .OrderByDescending(x => x.Score)
                .ToList();

            if (scoredCandidates.Count == 0)
            {
                log($"[SelfHealing] No candidate with the same ControlType was found for '{expected.AutomationId}' ({expected.ControlType}).");
                return new HealResult { Matched = null, Score = 0.0, CandidateCount = 0 };
            }

            var best = scoredCandidates[0];
            var confidenceLabel = best.Score >= MinimumConfidence ? "CONFIDENT" : "LOW CONFIDENCE";

            log($"[SelfHealing] '{expected.AutomationId}' ({expected.ControlType}) not found. " +
                $"Best candidate: Name='{best.Candidate.Name}', AutomationId='{best.Candidate.AutomationId}', " +
                $"Score={best.Score:F2} ({confidenceLabel}), chosen among {scoredCandidates.Count} candidate(s).");

            return new HealResult
            {
                Matched = best.Candidate,
                Score = best.Score,
                CandidateCount = scoredCandidates.Count,
            };
        }

        private static IEnumerable<UiElementInfo> Flatten(UiElementInfo node)
        {
            yield return node;

            foreach (var child in node.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
