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

            // Telemetry: record attempt counts across all evaluated providers (#11, #47).
            // Populated on heuristicResult too so failed consensus / single-provider runs still carry telemetry.
            var providerAttempts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in llmResults)
            {
                if (!string.IsNullOrEmpty(r.ProviderName))
                {
                    providerAttempts[r.ProviderName] = Math.Max(
                        providerAttempts.TryGetValue(r.ProviderName, out var existing) ? existing : 0,
                        r.AttemptCount);
                }
            }

            heuristicResult.ProviderAttempts = providerAttempts;

            // Consensus acceptance (#10, decided in #19). Self-reported confidences are not
            // calibrated across model architectures - Claude's 0.72 and Gemini's 0.95 do not
            // live on one scale - so they are never compared or thresholded here. What is
            // accepted instead is agreement: independent providers naming the same candidate.
            //
            // Step 1: the hallucination guard runs BEFORE the vote is counted, not after the
            // winner is chosen. A provider naming a candidateId that was never in the shortlist
            // it was sent must not be able to cast a vote, win with it, and take every other
            // provider's valid vote down with it.
            var shortlistIds = new HashSet<string>(shortlist.Select(c => c.CandidateId), StringComparer.Ordinal);
            var answered = llmResults.Where(r => r.Success && !string.IsNullOrEmpty(r.MatchedCandidateId)).ToList();
            var validVotes = new List<LlmHealingResult>();
            foreach (var vote in answered)
            {
                if (shortlistIds.Contains(vote.MatchedCandidateId!))
                {
                    validVotes.Add(vote);
                }
                else
                {
                    log($"[SelfHealing] {vote.ProviderName} returned candidateId '{vote.MatchedCandidateId}', which is not in the shortlist it was sent - its vote is discarded.");
                }
            }

            // Step 2: quorum. Providers that failed, timed out or hallucinated are simply not
            // here, so this covers "not enough providers configured" and "not enough survived"
            // with one check.
            if (validVotes.Count < w.MinimumConsensusVotes)
            {
                log($"[SelfHealing] {validVotes.Count} usable LLM vote(s), consensus requires {w.MinimumConsensusVotes} - returning heuristic result.");
                return heuristicResult;
            }

            var voteGroups = validVotes
                .GroupBy(r => r.MatchedCandidateId!, StringComparer.Ordinal)
                .Select(g => new { CandidateId = g.Key, Votes = g.ToList() })
                .OrderByDescending(g => g.Votes.Count)
                .ThenBy(g => g.CandidateId, StringComparer.Ordinal)
                .ToList();
            var tally = string.Join(", ", voteGroups.Select(g => $"{g.CandidateId}={g.Votes.Count}"));
            var topGroup = voteGroups[0];

            // Step 3: no candidate reached quorum - e.g. three providers naming three different
            // candidates. That is the resolver being told "we do not know", not a close call to
            // be settled by whoever sounded most sure.
            if (topGroup.Votes.Count < w.MinimumConsensusVotes)
            {
                log($"[SelfHealing] LLM providers did not converge (votes: {tally}); no candidate reached {w.MinimumConsensusVotes} - returning heuristic result.");
                return heuristicResult;
            }

            // Step 4: a tie for the lead is disagreement too. Breaking it by confidence would
            // quietly reinstate the cross-provider confidence comparison #19 removed.
            if (voteGroups.Count > 1 && voteGroups[1].Votes.Count == topGroup.Votes.Count)
            {
                log($"[SelfHealing] LLM vote tied (votes: {tally}) - a tie is disagreement, not consensus - returning heuristic result.");
                return heuristicResult;
            }

            // CandidateId, not AutomationId, is the lookup key throughout: AutomationId can be
            // empty or duplicated (the exact scenario this framework exists to heal), while
            // CandidateId is unique within the shortlist we sent.
            var matchedCandidate = shortlist.First(c => c.CandidateId == topGroup.CandidateId);
            var agreedProviders = topGroup.Votes
                .Select(r => r.ProviderName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            // The report keeps one provider's reasoning verbatim; ordinal-first makes that
            // choice deterministic rather than dependent on which provider answered first.
            // AgreedProviders carries the full record of who agreed.
            var best = topGroup.Votes.OrderBy(r => r.ProviderName, StringComparer.Ordinal).First();
            var consensusConfidence = topGroup.Votes.Average(r => r.Confidence);

            // Determine if the LLM choice diverged from the heuristic winner (issue #6).
            // ReferenceEquals is used because AutomationId may be empty or duplicated,
            // while shortlist and Resolve operate over nodes from the exact same tree instance.
            var isDivergent = heuristicResult.Matched != null && !ReferenceEquals(heuristicResult.Matched, matchedCandidate.Candidate);
            var agreedProvidersSummary = string.Join(", ", agreedProviders.Select(p => providerAttempts.TryGetValue(p, out var attempts) && attempts > 1 ? $"{p} [{attempts} attempts]" : p));
            var consensusSummary = $"consensus of {topGroup.Votes.Count}/{validVotes.Count} providers ({agreedProvidersSummary}) matched '{matchedCandidate.Candidate.AutomationId}' (mean self-reported confidence={consensusConfidence:F2}, reasoning=\"{best.Reasoning}\"";
            if (isDivergent)
            {
                log($"[SelfHealing] '{expected.AutomationId}' resolved via LLM fallback: {consensusSummary}, diverged from heuristic winner '{heuristicResult.Matched!.AutomationId}' with score {heuristicResult.Score:F2}).");
            }
            else
            {
                log($"[SelfHealing] '{expected.AutomationId}' resolved via LLM fallback: {consensusSummary}).");
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
                // Recorded for report continuity only - since #10 the acceptance rule is
                // AgreedProviders.Count, not this threshold (see SimilarityWeights).
                ConfidenceThreshold = w.MinimumLlmConfidence,
                EvidenceCoverage = matchedCandidate.EvidenceCoverage,
                EvidenceThreshold = heuristicResult.EvidenceThreshold,
                // RunnerUpScore preserves the heuristic competition telemetry (c0 vs c1) that triggered
                // fallback. The margin gate itself is not applied to LLM picks (which use MinimumLlmConfidence).
                RunnerUpScore = heuristicResult.RunnerUpScore,
                MarginThreshold = heuristicResult.MarginThreshold,
                Candidates = heuristicResult.Candidates,
                LlmProviderName = best.ProviderName,
                LlmConfidence = consensusConfidence,
                LlmReasoning = best.Reasoning,
                AgreedProviders = agreedProviders,
                ProviderAttempts = providerAttempts,
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
