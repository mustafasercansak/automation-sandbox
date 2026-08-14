using UiModel;
namespace SelfHealing
{
    public enum HealSource
    {
        Heuristic,
        Llm,
    }

    public sealed class HealResult
    {
        public UiElementInfo? Matched { get; set; }
        public double Score { get; set; }
        public int CandidateCount { get; set; }
        public HealSource Source { get; set; } = HealSource.Heuristic;
        public double ConfidenceThreshold { get; set; } = SimilarityWeights.Default.MinimumConfidence;

        // Fraction of the total signal weight backed by non-null evidence (see
        // CandidateScore.EvidenceCoverage). Defaults to 1.0 so hand-constructed results
        // keep their previous confidence semantics.

        public double EvidenceCoverage { get; set; } = 1.0;
        public double EvidenceThreshold { get; set; } = SimilarityWeights.Default.MinimumEvidenceWeight;

        // Runner-up margin (issue #4): a top candidate that barely beats the second-best is
        // ambiguous, not confident. Null when there is no runner-up (single candidate = no
        // competition). The margin gate applies to heuristic results only - LLM picks have
        // their own acceptance rule (MinimumLlmConfidence).

        public double? RunnerUpScore { get; set; }
        public double MarginThreshold { get; set; } = SimilarityWeights.Default.MinimumCandidateMargin;

        // Every scored candidate, UNPRUNED (below-MinCandidateScore nodes included) -
        // persisted into the healing report so thresholds can be re-tuned offline against
        // recorded data (#15).

        public IReadOnlyList<CandidateScore>? Candidates { get; set; }

        public ScoreComponents? ScoreBreakdown { get; set; }
        public string? LlmProviderName { get; set; }

        // Mean of the agreeing providers' self-reported confidences. Informational only:
        // it is recorded and reported, never thresholded, because those numbers are not
        // calibrated against each other (#19). Consensus is what accepts a pick.
        public double? LlmConfidence { get; set; }
        public string? LlmReasoning { get; set; }

        // Consensus acceptance (#10): the providers that independently named the accepted
        // candidate, sorted ordinally so a report is stable across runs regardless of which
        // provider answered first. Empty on heuristic results.
        public IReadOnlyList<string> AgreedProviders { get; set; } = Array.Empty<string>();

        // Heuristic winner metadata and divergence tracking (issue #6):
        // When Source == HealSource.Llm, HeuristicMatched and HeuristicScore preserve the
        // baseline winner before fallback. DivergedFromHeuristic indicates whether the LLM
        // picked a different candidate than the heuristic scorer.
        public UiElementInfo? HeuristicMatched { get; set; }
        public double? HeuristicScore { get; set; }
        public bool DivergedFromHeuristic { get; set; }
        public bool IsConfident =>
            Matched is not null &&
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
                : AgreedProviders.Count >= 2);
    }
}
