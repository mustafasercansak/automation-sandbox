namespace AutomationSandbox.UiModel
{
    // Per-signal breakdown of a CandidateScore's TotalScore, so callers/logs can see WHY a
    // candidate won (or lost) instead of only a single opaque number. Lives in UiModel (not
    // SelfHealing) so both SelfHealing and LlmHealing can reference it without a circular
    // project dependency between the two.

    /// <summary>Read-only per-signal structural similarities. Null means missing evidence, not a perfect match or a failed match.</summary>
    public sealed class ScoreComponents
    {
        // All signals are nullable: "missing == missing" (e.g. both elements have an empty
        // Name, an empty ParentControlType, or no sibling metadata) is reported as null -
        // the signal is excluded from the weighted average entirely, never treated as a
        // perfect 1.0 match. A null means "no evidence", not "full match" and not "failed".

        /// <summary>Control-type similarity from zero to one, or null when the signal is unavailable.</summary>
        public double? ControlTypeScore { get; }
        /// <summary>Parent-type similarity from zero to one, or null when the signal is unavailable.</summary>
        public double? ParentControlTypeScore { get; }
        /// <summary>Relative sibling-position similarity from zero to one, or null when sibling metadata is unavailable.</summary>
        public double? SiblingPositionScore { get; }
        /// <summary>Normalized name similarity from zero to one, or null when both names are absent.</summary>
        public double? NameScore { get; }

        // Null when the bounding rectangle wasn't usable (e.g. zero width/height) - excluded
        // from the weighted average entirely rather than penalized.

        /// <summary>Position similarity from zero to one, or null when usable geometry is unavailable.</summary>
        public double? PositionScore { get; }

        /// <summary>Captures fixed per-signal evidence; omitted signals stay null and are excluded from weighted scoring.</summary>
        public ScoreComponents(
            double? controlTypeScore = null,
            double? parentControlTypeScore = null,
            double? siblingPositionScore = null,
            double? nameScore = null,
            double? positionScore = null)
        {
            ControlTypeScore = controlTypeScore;
            ParentControlTypeScore = parentControlTypeScore;
            SiblingPositionScore = siblingPositionScore;
            NameScore = nameScore;
            PositionScore = positionScore;
        }
    }
}
