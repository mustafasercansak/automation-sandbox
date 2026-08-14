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
            // The report-worthy candidate list is the UNPRUNED one: an offline threshold
            // sweep (#15) must be able to re-score below today's MinCandidateScore, so the
            // prune has to happen after the evidence is captured, not before.
            var allScored = ScoreAllCandidates(expected, currentTreeRoot, w);
            var scoredCandidates = allScored.Where(c => c.TotalScore >= w.MinCandidateScore).ToList();
            if (scoredCandidates.Count == 0)
            {
                log($"[SelfHealing] No structurally similar candidate was found for '{expected.AutomationId}' ({expected.ControlType}).");
                return new HealResult { Matched = null, Score = 0.0, CandidateCount = 0, ConfidenceThreshold = w.MinimumConfidence, EvidenceCoverage = 0.0, EvidenceThreshold = w.MinimumEvidenceWeight, Candidates = allScored };
            }

            var best = scoredCandidates[0];
            var runnerUpScore = scoredCandidates.Count > 1 ? scoredCandidates[1].TotalScore : (double?)null;
            var marginSufficient = CandidateMargin.HasSufficientMargin(best.TotalScore, runnerUpScore, w.MinimumCandidateMargin);
            var isConfident = best.TotalScore >= w.MinimumConfidence
                && best.EvidenceCoverage >= w.MinimumEvidenceWeight
                && marginSufficient;
            var confidenceLabel = isConfident
                ? "CONFIDENT"
                : best.EvidenceCoverage < w.MinimumEvidenceWeight
                    ? $"LOW EVIDENCE (coverage {best.EvidenceCoverage:F2} < {w.MinimumEvidenceWeight:F2})"
                    : !marginSufficient
                        ? $"AMBIGUOUS (runner-up margin {best.TotalScore - (runnerUpScore ?? 0.0):F3} < {w.MinimumCandidateMargin:F2})"
                        : "LOW CONFIDENCE";
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
                EvidenceCoverage = best.EvidenceCoverage,
                EvidenceThreshold = w.MinimumEvidenceWeight,
                RunnerUpScore = runnerUpScore,
                MarginThreshold = w.MinimumCandidateMargin,
                Candidates = allScored,
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
            string? platform = null,
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
                llmResults = await LlmHealingEvaluator.EvaluateAsync(available, expected, shortlist, platform, cancellationToken).ConfigureAwait(false);
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

            // Determine if the LLM choice diverged from the heuristic winner (issue #6).
            // ReferenceEquals is used because AutomationId may be empty or duplicated,
            // while shortlist and Resolve operate over nodes from the exact same tree instance.
            var isDivergent = heuristicResult.Matched != null && !ReferenceEquals(heuristicResult.Matched, matchedCandidate.Candidate);
            if (isDivergent)
            {
                log($"[SelfHealing] '{expected.AutomationId}' resolved via LLM fallback: {best.ProviderName} matched '{matchedCandidate.Candidate.AutomationId}' (confidence={best.Confidence:F2}, reasoning=\"{best.Reasoning}\", diverged from heuristic winner '{heuristicResult.Matched!.AutomationId}' with score {heuristicResult.Score:F2}).");
            }
            else
            {
                log($"[SelfHealing] '{expected.AutomationId}' resolved via LLM fallback: {best.ProviderName} matched '{matchedCandidate.Candidate.AutomationId}' (confidence={best.Confidence:F2}, reasoning=\"{best.Reasoning}\").");
            }

            return new HealResult
            {
                Matched = matchedCandidate.Candidate,
                // Score and ScoreBreakdown belong to the matched candidate (issue #6) so the report
                // and locator history explain the actual chosen element's metrics.
                Score = matchedCandidate.TotalScore,
                ScoreBreakdown = matchedCandidate.Components,
                CandidateCount = heuristicResult.CandidateCount,
                Source = HealSource.Llm,
                ConfidenceThreshold = w.MinimumLlmConfidence,
                EvidenceCoverage = matchedCandidate.EvidenceCoverage,
                EvidenceThreshold = heuristicResult.EvidenceThreshold,
                // RunnerUpScore preserves the heuristic competition telemetry (c0 vs c1) that triggered
                // fallback. The margin gate itself is not applied to LLM picks (which use MinimumLlmConfidence).
                RunnerUpScore = heuristicResult.RunnerUpScore,
                MarginThreshold = heuristicResult.MarginThreshold,
                Candidates = heuristicResult.Candidates,
                LlmProviderName = best.ProviderName,
                LlmConfidence = best.Confidence,
                LlmReasoning = best.Reasoning,
                HeuristicMatched = heuristicResult.Matched,
                HeuristicScore = heuristicResult.Score,
                DivergedFromHeuristic = isDivergent,
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
            return ScoreAllCandidates(expected, currentTreeRoot, weights)
                .Where(c => c.TotalScore >= weights.MinCandidateScore)
                .ToList();
        }

        // Same pipeline without the MinCandidateScore prune. The public ScoreCandidates
        // keeps pruning (its contract), while Resolve() reports the full list so recorded
        // evidence survives future threshold changes.
        private static List<CandidateScore> ScoreAllCandidates(UiElementInfo expected, UiElementInfo currentTreeRoot, SimilarityWeights weights)
        {
            return Flatten(currentTreeRoot)
                .Select(candidate => SimilarityScorer.ScoreCandidate(expected, candidate, weights))
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
