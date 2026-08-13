namespace SelfHealing
{
    // Injectable tunables for SimilarityScorer/SelfHealingResolver. The default values are
    // the same weights this project shipped with when they were hardcoded consts - validated
    // against exactly two scenarios (WinForms panel1, WPF CompanyPanel), not a broad dataset.

    public sealed class SimilarityWeights
    {
        public double ControlTypeWeight { get; set; } = 0.20;
        public double ParentControlTypeWeight { get; set; } = 0.20;
        public double SiblingPositionWeight { get; set; } = 0.15;
        public double NameWeight { get; set; } = 0.20;
        public double PositionWeight { get; set; } = 0.25;
        public double PositionToleranceRadius { get; set; } = 300.0;
        public double MinimumConfidence { get; set; } = 0.5;

        // Minimum fraction of the total signal weight that must be backed by non-null
        // evidence before a heuristic match can be IsConfident. With the default weights a
        // ControlType-only match has coverage 0.20, so 0.40 demands at least one more real
        // signal. Estimate - the mechanism is final, the value is recalibrated against the
        // real-world benchmark dataset (see issue #15).

        public double MinimumEvidenceWeight { get; set; } = 0.4;

        // Minimum gap between the best and runner-up candidate scores before a heuristic
        // match can be IsConfident (issue #4). 0.880 vs 0.879 means "I don't know" - the
        // resolver falls back to LLM/manual review instead of guessing. Default 0.05: the
        // issue's examples (0.88/0.79 confident, 0.88/0.879 not) imply a threshold below
        // 0.09, and the calibrated WinForms demo scenario sits at a ~0.057 margin. Estimate,
        // like the other defaults - recalibrated against the #15 benchmark dataset, not
        // reopened here.

        public double MinimumCandidateMargin { get; set; } = 0.05;

        // Separate from MinimumConfidence: an LLM's self-reported confidence isn't calibrated
        // the same way as the heuristic's structural score, so a low-confidence LLM pick
        // shouldn't silently replace a heuristic result just for having *a* pick.

        public double MinimumLlmConfidence { get; set; } = 0.5;

        // Candidate pruning: candidates below MinCandidateScore are dropped before ranking,
        // and at most MaxCandidatesForLlm survive into the LLM fallback's shortlist prompt -
        // bounds both heuristic scoring cost and LLM prompt size/token cost on large trees.

        public int MaxCandidatesForLlm { get; set; } = 20;
        public double MinCandidateScore { get; set; } = 0.05;
        public static SimilarityWeights Default => new();

        public void Validate()
        {
            ValidateNonNegative(ControlTypeWeight, nameof(ControlTypeWeight));
            ValidateNonNegative(ParentControlTypeWeight, nameof(ParentControlTypeWeight));
            ValidateNonNegative(SiblingPositionWeight, nameof(SiblingPositionWeight));
            ValidateNonNegative(NameWeight, nameof(NameWeight));
            ValidateNonNegative(PositionWeight, nameof(PositionWeight));
            ValidateNonNegative(PositionToleranceRadius, nameof(PositionToleranceRadius));
            ValidateRange(MinimumConfidence, nameof(MinimumConfidence), 0.0, 1.0);
            ValidateRange(MinimumEvidenceWeight, nameof(MinimumEvidenceWeight), 0.0, 1.0);
            ValidateRange(MinimumCandidateMargin, nameof(MinimumCandidateMargin), 0.0, 1.0);
            ValidateRange(MinimumLlmConfidence, nameof(MinimumLlmConfidence), 0.0, 1.0);
            ValidateRange(MinCandidateScore, nameof(MinCandidateScore), 0.0, 1.0);

            if (MaxCandidatesForLlm < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxCandidatesForLlm), "MaxCandidatesForLlm must be at least one.");
            }

            var totalWeight = ControlTypeWeight
                            + ParentControlTypeWeight
                            + SiblingPositionWeight
                            + NameWeight
                            + PositionWeight;
            if (totalWeight <= 0.0)
            {
                throw new InvalidOperationException("At least one similarity weight must be greater than zero.");
            }
        }

        private static void ValidateNonNegative(double value, string propertyName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            {
                throw new ArgumentOutOfRangeException(propertyName, $"{propertyName} must be a finite non-negative value.");
            }
        }

        private static void ValidateRange(double value, string propertyName, double minimum, double maximum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(propertyName, $"{propertyName} must be between {minimum} and {maximum}.");
            }
        }
    }
}
