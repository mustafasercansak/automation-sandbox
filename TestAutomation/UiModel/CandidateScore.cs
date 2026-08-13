namespace UiModel
{
    // Lives in UiModel (not SelfHealing) so both SelfHealing (which produces these) and
    // LlmHealing (which consumes a shortlist of these as an LLM prompt) can reference it
    // without a circular project dependency between the two.

    public sealed class CandidateScore
    {
        public UiElementInfo Candidate { get; set; } = null!;
        public double TotalScore { get; set; }
        public ScoreComponents Components { get; set; } = null!;

        // Fraction of the total possible signal weight backed by non-null evidence
        // (0..1). A candidate that matches on ControlType alone has coverage 0.20 with the
        // default weights - high score, thin evidence. Confidence gating lives in
        // SelfHealingResolver/HealResult; this is just the measurement.

        public double EvidenceCoverage { get; set; }

        // Opaque, per-call id assigned only when a shortlist is materialized for a single LLM
        // round-trip (see SelfHealingResolver.ResolveAsync) - not persisted, not stable across
        // calls. Empty outside that context.

        public string CandidateId { get; set; } = "";
    }
}
