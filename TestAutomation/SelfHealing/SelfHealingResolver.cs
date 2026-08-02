using LlmHealing;
using UiModel;
namespace SelfHealing
{
    public static class SelfHealingResolver
    {
        // expected: the last known snapshot state of the locator that just broke.
        // currentTreeRoot: a freshly captured UI tree from the application right now.

        public static HealResult Resolve(
            UiElementInfo expected,
            UiElementInfo currentTreeRoot,
            SimilarityWeights? weights = null,
            Action<string>? log = null)
        {
            log ??= Console.WriteLine;
            var w = weights ?? SimilarityWeights.Default;
            w.Validate();
            var scoredCandidates = ScoreCandidates(expected, currentTreeRoot, w);
            if (scoredCandidates.Count == 0)
            {
                log($"[SelfHealing] No structurally similar candidate was found for '{expected.AutomationId}' ({expected.ControlType}).");
                return new HealResult { Matched = null, Score = 0.0, CandidateCount = 0, ConfidenceThreshold = w.MinimumConfidence };
            }

            var best = scoredCandidates[0];
            var confidenceLabel = best.TotalScore >= w.MinimumConfidence ? "CONFIDENT" : "LOW CONFIDENCE";
            log($"[SelfHealing] '{expected.AutomationId}' ({expected.ControlType}) not found. " +
                $"Best candidate: Name='{best.Candidate.Name}', AutomationId='{best.Candidate.AutomationId}', " +
                $"Score={best.TotalScore:F2} ({confidenceLabel}), chosen among {scoredCandidates.Count} candidate(s).");
            return new HealResult
            {
                Matched = best.Candidate,
                Score = best.TotalScore,
                CandidateCount = scoredCandidates.Count,
                Source = HealSource.Heuristic,
                ConfidenceThreshold = w.MinimumConfidence,
                ScoreBreakdown = best.Components,
            };
        }

        // Same as Resolve, but falls back to an LLM provider when the heuristic result is not
        // confident. Never requires an LLM provider to be configured: with no providers, none
        // available, or all of them failing, this returns exactly the heuristic Resolve() result -
        // a consumer that never passes llmProviders is unaffected by this method existing.

        public static async Task<HealResult> ResolveAsync(
            UiElementInfo expected,
            UiElementInfo currentTreeRoot,
            IEnumerable<ILlmHealingProvider>? llmProviders = null,
            SimilarityWeights? weights = null,
            Action<string>? log = null,
            CancellationToken cancellationToken = default)
        {
            log ??= Console.WriteLine;
            var w = weights ?? SimilarityWeights.Default;
            w.Validate();
            var heuristicResult = Resolve(expected, currentTreeRoot, w, log);
            if (heuristicResult.IsConfident)
            {
                return heuristicResult;
            }

            var available = (llmProviders ?? Enumerable.Empty<ILlmHealingProvider>()).Where(p => p.IsAvailable).ToList();
            if (available.Count == 0)
            {
                log("[SelfHealing] Low-confidence heuristic match and no LLM provider available - returning heuristic result.");
                return heuristicResult;
            }

            // Bound what the LLM sees to the top-N scored candidates rather than the whole tree -
            // keeps prompt/token cost bounded on large trees (see SimilarityWeights.MaxCandidatesForLlm).
            var shortlist = ScoreCandidates(expected, currentTreeRoot, w)
                .Take(w.MaxCandidatesForLlm)
                .ToList();
            for (var i = 0; i < shortlist.Count; i++)
            {
                shortlist[i].CandidateId = "c" + i;
            }

            IReadOnlyList<LlmHealingResult> llmResults;
            try
            {
                llmResults = await LlmHealingEvaluator.EvaluateAsync(available, expected, shortlist, cancellationToken).ConfigureAwait(false);
            }

            catch (Exception ex)
            {
                log($"[SelfHealing] LLM fallback threw ({ex.Message}) - returning heuristic result.");
                return heuristicResult;
            }

            var best = llmResults
                .Where(r => r.Success && !string.IsNullOrEmpty(r.MatchedCandidateId))
                .OrderByDescending(r => r.Confidence)
                .FirstOrDefault();
            if (best is null)
            {
                log("[SelfHealing] No LLM provider returned a usable match - returning heuristic result.");
                return heuristicResult;
            }

            if (best.Confidence < w.MinimumLlmConfidence)
            {
                log($"[SelfHealing] {best.ProviderName}'s pick had confidence {best.Confidence:F2}, below MinimumLlmConfidence ({w.MinimumLlmConfidence:F2}) - returning heuristic result rather than a low-confidence override.");
                return heuristicResult;
            }

            // Look the pick up by CandidateId in the exact shortlist we sent - not by
            // AutomationId in the whole tree. AutomationId can be empty/duplicated (the exact
            // scenario this framework exists to heal), so it's not a safe lookup key; CandidateId
            // is unique within the shortlist and this also doubles as the hallucination guard,
            // now an exact membership check against a bounded, explicit list.
            var matchedCandidate = shortlist.FirstOrDefault(c => c.CandidateId == best.MatchedCandidateId);
            if (matchedCandidate is null)
            {
                log($"[SelfHealing] {best.ProviderName} returned candidateId '{best.MatchedCandidateId}', which is not in the shortlist it was sent - returning heuristic result.");
                return heuristicResult;
            }

            log($"[SelfHealing] '{expected.AutomationId}' resolved via LLM fallback: {best.ProviderName} matched '{matchedCandidate.Candidate.AutomationId}' (confidence={best.Confidence:F2}, reasoning=\"{best.Reasoning}\").");
            return new HealResult
            {
                Matched = matchedCandidate.Candidate,
                Score = heuristicResult.Score,
                CandidateCount = heuristicResult.CandidateCount,
                Source = HealSource.Llm,
                ConfidenceThreshold = w.MinimumLlmConfidence,
                ScoreBreakdown = heuristicResult.ScoreBreakdown,
                LlmProviderName = best.ProviderName,
                LlmConfidence = best.Confidence,
                LlmReasoning = best.Reasoning,
            };
        }

        // Backs both the heuristic winner pick (Resolve) and the LLM shortlist (ResolveAsync).
        // Public so callers that want to build their own LLM shortlist (or just inspect
        // per-candidate scores) can do so without duplicating this logic - flattens the tree,
        // scores every node, drops anything below MinCandidateScore, sorts descending. Pruning
        // here only bounds cost/noise - it never changes which candidate would have won, since
        // the winner is always the highest-scoring one regardless of how many lower-scoring
        // candidates get dropped below MinCandidateScore.

        public static List<CandidateScore> ScoreCandidates(UiElementInfo expected, UiElementInfo currentTreeRoot, SimilarityWeights? weights = null)
        {
            weights ??= SimilarityWeights.Default;
            weights.Validate();
            return Flatten(currentTreeRoot)
                .Select(candidate => SimilarityScorer.ScoreCandidate(expected, candidate, weights))
                .Where(c => c.TotalScore >= weights.MinCandidateScore)
                .OrderByDescending(c => c.TotalScore)
                .ThenBy(c => c.Candidate.ControlType, StringComparer.Ordinal)
                .ThenBy(c => c.Candidate.AutomationId, StringComparer.Ordinal)
                .ThenBy(c => c.Candidate.Name, StringComparer.Ordinal)
                .ThenBy(c => c.Candidate.ClassName, StringComparer.Ordinal)
                .ThenBy(c => c.Candidate.SiblingIndex)
                .ThenBy(c => c.Candidate.BoundingRectangle.X)
                .ThenBy(c => c.Candidate.BoundingRectangle.Y)
                .ThenBy(c => c.Candidate.BoundingRectangle.Width)
                .ThenBy(c => c.Candidate.BoundingRectangle.Height)
                .ToList();
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
