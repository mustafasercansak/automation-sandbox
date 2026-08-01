using Discovery;

namespace SelfHealing
{
    public static class SelfHealingResolver
    {
        public const double MinimumConfidence = 0.5;

        // expected: kırılan locator'ın son bilinen snapshot'taki hali.
        // currentTreeRoot: şu anki uygulamadan yeni çekilmiş UI ağacı.
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
                log($"[SelfHealing] '{expected.AutomationId}' ({expected.ControlType}) için aynı ControlType'a sahip hiçbir aday bulunamadı.");
                return new HealResult { Matched = null, Score = 0.0, CandidateCount = 0 };
            }

            var best = scoredCandidates[0];
            var confidenceLabel = best.Score >= MinimumConfidence ? "GÜVENİLİR" : "DÜŞÜK GÜVEN";

            log($"[SelfHealing] '{expected.AutomationId}' ({expected.ControlType}) bulunamadı. " +
                $"En iyi aday: Name='{best.Candidate.Name}', AutomationId='{best.Candidate.AutomationId}', " +
                $"Score={best.Score:F2} ({confidenceLabel}), {scoredCandidates.Count} aday arasından seçildi.");

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
