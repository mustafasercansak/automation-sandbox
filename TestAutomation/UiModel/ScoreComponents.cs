namespace UiModel
{
    // Per-signal breakdown of a CandidateScore's TotalScore, so callers/logs can see WHY a
    // candidate won (or lost) instead of only a single opaque number. Lives in UiModel (not
    // SelfHealing) so both SelfHealing and LlmHealing can reference it without a circular
    // project dependency between the two.

    public sealed class ScoreComponents
    {
        // All signals are nullable: "missing == missing" (e.g. both elements have an empty
        // Name, an empty ParentControlType, or no sibling metadata) is reported as null -
        // the signal is excluded from the weighted average entirely, never treated as a
        // perfect 1.0 match. A null means "no evidence", not "full match" and not "failed".

        public double? ControlTypeScore { get; set; }
        public double? ParentControlTypeScore { get; set; }
        public double? SiblingPositionScore { get; set; }
        public double? NameScore { get; set; }

        // Null when the bounding rectangle wasn't usable (e.g. zero width/height) - excluded
        // from the weighted average entirely rather than penalized.

        public double? PositionScore { get; set; }
    }
}
