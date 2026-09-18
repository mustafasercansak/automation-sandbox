namespace AutomationSandbox.UiModel
{
    // Lives in UiModel (not SelfHealing) so both SelfHealing (which produces these) and
    // LlmHealing (which consumes a shortlist of these as an LLM prompt) can reference it
    // without a circular project dependency between the two.

    /// <summary>A structural score and its evidence for one tree node. CandidateId is assigned when a provider shortlist is materialized.</summary>
    public sealed class CandidateScore
    {
        /// <summary>Live tree node evaluated by the scorer.</summary>
        public UiElementInfo Candidate { get; set; } = null!;
        /// <summary>Weighted structural similarity after excluding signals with no evidence; a score is not a probability of correctness.</summary>
        public double TotalScore { get; set; }
        /// <summary>Per-signal structural similarities used to explain the weighted score.</summary>
        public ScoreComponents Components { get; set; } = null!;

        // Fraction of the total possible signal weight backed by non-null evidence
        // (0..1). A candidate that matches on ControlType alone has coverage 0.20 with the
        // default weights - high score, thin evidence. Confidence gating lives in
        // SelfHealingResolver/HealResult; this is just the measurement.

        /// <summary>Fraction of configured signal weight backed by non-null evidence, from zero to one.</summary>
        public double EvidenceCoverage { get; set; }

        // Opaque, per-call id assigned only when a shortlist is materialized for a single LLM
        // round-trip (see SelfHealingResolver.ResolveAsync) - not persisted, not stable across
        // calls. Empty outside that context.

        /// <summary>Opaque, per-call id assigned only when a shortlist is materialized for a single LLM round-trip (see SelfHealingResolver.ResolveAsync) - not persisted, not stable across calls. Empty outside that context.</summary>
        public string CandidateId { get; set; } = "";
    }
}
