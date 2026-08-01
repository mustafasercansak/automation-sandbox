namespace UiModel
{
    // Per-signal breakdown of a CandidateScore's TotalScore, so callers/logs can see WHY a
    // candidate won (or lost) instead of only a single opaque number. Lives in UiModel (not
    // SelfHealing) so both SelfHealing and LlmHealing can reference it without a circular
    // project dependency between the two.

    public sealed class ScoreComponents
    {
        public double ControlTypeScore { get; set; }
        public double ParentControlTypeScore { get; set; }
        public double SiblingPositionScore { get; set; }
        public double NameScore { get; set; }

        // Null when the bounding rectangle wasn't usable (e.g. zero width/height) - excluded
        // from the weighted average entirely rather than penalized.

        public double? PositionScore { get; set; }
    }
}
