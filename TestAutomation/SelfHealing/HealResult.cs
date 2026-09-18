using AutomationSandbox.UiModel;
namespace AutomationSandbox.SelfHealing
{
    /// <summary>Mechanism that proposed a healed locator; source-specific acceptance rules still apply.</summary>
    public enum HealSource
    {
        /// <summary>Deterministic structural similarity proposed the match.</summary>
        Heuristic,
        /// <summary>A quorum of independent providers proposed the match.</summary>
        Llm,
    }

    /// <summary>Classification of a resolution attempt, distinct from the recorded engine execution outcome.</summary>
    public enum HealResolutionStatus
    {
        /// <summary>The decision path did not classify itself; this is not evidence of low confidence.</summary>
        Unspecified,
        /// <summary>The resolver accepted the candidate under its configured gates.</summary>
        Confident,
        /// <summary>No candidate survived the minimum candidate-score filter.</summary>
        NoCandidates,
        /// <summary>The candidate did not meet the structural score threshold.</summary>
        LowConfidence,
        /// <summary>Too little signal weight was supported by observed evidence.</summary>
        LowEvidence,
        /// <summary>Competing candidates did not provide the required separation.</summary>
        Ambiguous,
        /// <summary>Available provider votes did not establish an unambiguous independent-agreement quorum.</summary>
        NoConsensus,
        /// <summary>Provider failures prevented a usable fallback decision.</summary>
        ProviderError,
        /// <summary>Batch reconciliation rejected competing claims to the same live node.</summary>
        OwnershipConflict,
    }

    /// <summary>Editable resolution evidence used by resolver fallback, batch reconciliation, and report construction. Check IsConfident before accepting Matched.</summary>
    public sealed class HealResult
    {
        /// <summary>Proposed live node; it may remain present even when acceptance gates reject the proposal.</summary>
        public UiElementInfo? Matched { get; set; }
        /// <summary>Structural similarity score; it is not a calibrated correctness probability.</summary>
        public double Score { get; set; }
        /// <summary>Number of candidates considered by this result&apos;s resolution or matching stage.</summary>
        public int CandidateCount { get; set; }
        /// <summary>Resolution mechanism that proposed the candidate.</summary>
        public HealSource Source { get; set; } = HealSource.Heuristic;
        /// <summary>Classification assigned by the resolver decision path.</summary>
        public HealResolutionStatus ResolutionStatus { get; set; }
        /// <summary>Minimum structural score required for heuristic acceptance.</summary>
        public double ConfidenceThreshold { get; set; } = SimilarityWeights.Default.MinimumConfidence;

        // Fraction of the total signal weight backed by non-null evidence (see
        // CandidateScore.EvidenceCoverage). Defaults to 1.0 so hand-constructed results
        // keep their previous confidence semantics.

        /// <summary>Fraction of configured signal weight backed by non-null evidence, from zero to one.</summary>
        public double EvidenceCoverage { get; set; } = 1.0;
        /// <summary>Minimum evidence coverage required for acceptance, including LLM proposals.</summary>
        public double EvidenceThreshold { get; set; } = SimilarityWeights.Default.MinimumEvidenceWeight;

        // Runner-up margin (issue #4): a top candidate that barely beats the second-best is
        // ambiguous, not confident. Null when there is no runner-up (single candidate = no
        // competition). The margin gate applies to heuristic results only - LLM picks have
        // their own acceptance rule (consensus agreement).

        /// <summary>Runner-up margin (issue #4): a top candidate that barely beats the second-best is ambiguous, not confident. Null when there is no runner-up (single candidate = no competition). The margin gate applies to heuristic results only - LLM picks have their own acceptance rule (consensus agreement).</summary>
        public double? RunnerUpScore { get; set; }
        /// <summary>Required structural-score separation from the runner-up for heuristic acceptance.</summary>
        public double MarginThreshold { get; set; } = SimilarityWeights.Default.MinimumCandidateMargin;

        // Every scored candidate, UNPRUNED (below-MinCandidateScore nodes included) -
        // persisted into the healing report so thresholds can be re-tuned offline against
        // recorded data (#15).

        /// <summary>Every scored candidate, UNPRUNED (below-MinCandidateScore nodes included) - persisted into the healing report so thresholds can be re-tuned offline against recorded data (#15).</summary>
        public IReadOnlyList<CandidateScore>? Candidates { get; set; }

        /// <summary>Per-signal explanation of the proposed candidate&apos;s structural score.</summary>
        public ScoreComponents? ScoreBreakdown { get; set; }
        /// <summary>Provider name or names recorded for an LLM proposal; absent for heuristic-only resolution.</summary>
        public string? LlmProviderName { get; set; }

        // Mean of the agreeing providers' self-reported confidences. Informational only:
        // it is recorded and reported, never thresholded, because those numbers are not
        // calibrated against each other (#19). Consensus is what accepts a pick.
        /// <summary>Provider-reported confidence retained for diagnostics only; it never controls acceptance.</summary>
        public double? LlmConfidence { get; set; }
        /// <summary>Provider explanation retained for diagnostics; not independently verified evidence.</summary>
        public string? LlmReasoning { get; set; }

        // Consensus acceptance (#10): the providers that independently named the accepted
        // candidate, sorted ordinally so a report is stable across runs regardless of which
        // provider answered first. Empty on heuristic results.
        /// <summary>Consensus acceptance (#10): the providers that independently named the accepted candidate, sorted ordinally so a report is stable across runs regardless of which provider answered first. Empty on heuristic results.</summary>
        public IReadOnlyList<string> AgreedProviders { get; set; } = Array.Empty<string>();
        /// <summary>Minimum number of independent agreeing provider votes required for LLM acceptance.</summary>
        public int ConsensusThreshold { get; set; } = SimilarityWeights.Default.MinimumConsensusVotes;

        // Provider attempt telemetry (#11): records how many attempts each evaluated provider made.
        /// <summary>Provider attempt telemetry (#11): records how many attempts each evaluated provider made.</summary>
        public IReadOnlyDictionary<string, int>? ProviderAttempts { get; set; }

        // Provider failures observed during fallback. Null means this build did not record
        // provider errors; an empty dictionary means providers were evaluated and none failed.
        /// <summary>Provider failures observed during fallback. Null means this build did not record provider errors; an empty dictionary means providers were evaluated and none failed.</summary>
        public IReadOnlyDictionary<string, string>? ProviderErrors { get; set; }

        // Heuristic winner metadata and divergence tracking (issue #6):
        // When Source == HealSource.Llm, HeuristicMatched and HeuristicScore preserve the
        // baseline winner before fallback. DivergedFromHeuristic indicates whether the LLM
        // picked a different candidate than the heuristic scorer.
        /// <summary>Candidate proposed by the heuristic before LLM fallback, if retained.</summary>
        public UiElementInfo? HeuristicMatched { get; set; }
        /// <summary>Structural score of the heuristic winner before fallback, if retained.</summary>
        public double? HeuristicScore { get; set; }
        /// <summary>Whether the LLM proposed a different candidate from the heuristic winner.</summary>
        public bool DivergedFromHeuristic { get; set; }

        // Populated only by the opt-in batch resolver. CandidateIdentity is an opaque,
        // snapshot-local tree path and deliberately does not depend on AutomationId.
        // RejectedByReconciliation keeps the independently proposed match available for
        // diagnostics while preventing callers from accepting it through IsConfident.
        /// <summary>Opaque pre-order tree path identifying the candidate within this captured tree only.</summary>
        public string? CandidateIdentity { get; set; }
        /// <summary>Ownership decision applied to the independently proposed candidate.</summary>
        public BatchReconciliationDisposition? ReconciliationDisposition { get; set; }
        /// <summary>Whether ownership reconciliation vetoed acceptance while retaining the proposed match for diagnostics.</summary>
        public bool RejectedByReconciliation { get; set; }

        // Per-component name gate (#370). MatchedNameScore is the winning candidate's
        // NameScore, populated only when the stale locator HAD a name and the component is
        // non-null; NameGateFloor is the SimilarityWeights.MinimumNameScoreWhenNamed in
        // effect. When both are set and MatchedNameScore < NameGateFloor the match is not
        // IsConfident regardless of the weighted total - the name signal contradicts it.
        /// <summary>Per-component name gate (#370). MatchedNameScore is the winning candidate&apos;s NameScore, populated only when the stale locator HAD a name and the component is non-null; NameGateFloor is the SimilarityWeights.MinimumNameScoreWhenNamed in effect. When both are set and MatchedNameScore &lt; NameGateFloor the match is not IsConfident regardless of the weighted total - the name signal contradicts it.</summary>
        public double? MatchedNameScore { get; set; }
        /// <summary>Minimum acceptable name similarity when name evidence is available; zero disables this gate.</summary>
        public double NameGateFloor { get; set; }

        // Per-component descendant gate (#375). MatchedChildSignatureSimilarity is the
        // multiset-Jaccard between the stale snapshot's recorded child-ControlType signature
        // and the winning candidate's live children, populated only when the snapshot
        // recorded a non-empty signature (the stale locator was a container);
        // ChildSignatureFloor is the SimilarityWeights.MinimumChildSignatureSimilarity in
        // effect. When both are set and the similarity is below the floor the match is not
        // IsConfident regardless of the weighted total - the element's contents contradict it.
        /// <summary>Per-component descendant gate (#375). MatchedChildSignatureSimilarity is the multiset-Jaccard between the stale snapshot&apos;s recorded child-ControlType signature and the winning candidate&apos;s live children, populated only when the snapshot recorded a non-empty signature (the stale locator was a container); ChildSignatureFloor is the SimilarityWeights.MinimumChildSignatureSimilarity in effect. When both are set and the similarity is below the floor the match is not IsConfident regardless of the weighted total - the element&apos;s contents contradict it.</summary>
        public double? MatchedChildSignatureSimilarity { get; set; }
        /// <summary>Minimum direct-child type-signature similarity for recorded containers; zero disables this gate.</summary>
        public double ChildSignatureFloor { get; set; }

        /// <summary>Evaluates reconciliation, evidence, component gates, and source-specific acceptance. Provider-reported confidence does not participate.</summary>
        public bool IsConfident =>
            !RejectedByReconciliation &&
            Matched is not null &&
            (NameGateFloor <= 0.0 || MatchedNameScore is null || MatchedNameScore >= NameGateFloor) &&
            (ChildSignatureFloor <= 0.0 || MatchedChildSignatureSimilarity is null || MatchedChildSignatureSimilarity >= ChildSignatureFloor) &&
            // The evidence gate applies to LLM picks too: otherwise a candidate the
            // heuristic rejected as thin-evidence (e.g. ControlType-only, coverage 0.20)
            // would re-enter through the LLM fallback and be reported as a confident
            // match - the exact false-positive channel issue #3 closes.
            EvidenceCoverage >= EvidenceThreshold &&
            (Source == HealSource.Heuristic
                ? Score >= ConfidenceThreshold && CandidateMargin.HasSufficientMargin(Score, RunnerUpScore, MarginThreshold)
                // Consensus, not confidence (#10/#19): an LLM pick is confident when
                // independent providers agreed on it. A single provider - however sure it
                // says it is - is one uncalibrated opinion, which is what this replaces.
                : AgreedProviders.Count >= ConsensusThreshold);
    }
}
