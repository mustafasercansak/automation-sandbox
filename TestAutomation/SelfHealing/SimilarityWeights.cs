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

        // Per-component name gate (#370): when the stale locator HAD a name, the winning
        // candidate's NameScore must be at least this before the heuristic match can be
        // IsConfident - independently of the weighted total. The 0.20-weighted name signal
        // is blended away by the weighted average, so a deleted tab healing onto an adjacent
        // one (Name 'Summary' -> 'Dimensions', NameScore ~0.10) still clears MinimumConfidence
        // on structure alone. Measured through TreeCalibrator on HandBrake: a 0.30 floor
        // drops the Balanced false-heal rate 9.3% -> 7.6% (precision 90.7% -> 92.4%) with
        // zero auto-heal recall cost; ShareX moves the same direction. Default 0.0 (disabled)
        // keeps the shipped permissive behaviour; the Balanced and Conservative profiles set
        // 0.30. Not applied when the stale locator had no name, or when the candidate's
        // NameScore is null (missing on one side - neither penalised nor rewarded, matching
        // the evidence gate).
        public double MinimumNameScoreWhenNamed { get; set; } = 0.0;

        // Per-component descendant gate (#375): when the stale snapshot recorded a non-empty
        // child-ControlType signature (it was a container), the winning candidate's live
        // children must match it with at least this multiset-Jaccard similarity before the
        // heuristic match can be IsConfident - independently of the weighted total. The five
        // scoring components never look at an element's own contents, so a deleted container
        // heals with a perfect structural score onto a structurally identical sibling holding
        // something else entirely (ShareX 'pHotkeys', child {DataGrid:1}, healing onto a
        // sibling Pane with child {Pane:1}). Measured across 131 genuine-drift heals on
        // HandBrake and ShareX: every one scores 1.0, so a 0.5 floor costs zero auto-heal
        // recall and removes the sole uncontested residual left after the name gate and
        // repository reconciliation. Default 0.0 (disabled); Balanced and Conservative set
        // 0.50. Not applied when the snapshot's signature is null (legacy data) or empty
        // (the stale locator was a leaf).
        public double MinimumChildSignatureSimilarity { get; set; } = 0.0;

        // Minimum gap between the best and runner-up candidate scores before a heuristic
        // match can be IsConfident (issue #4). 0.880 vs 0.879 means "I don't know" - the
        // resolver falls back to LLM/manual review instead of guessing. Default 0.05: the
        // issue's examples (0.88/0.79 confident, 0.88/0.879 not) imply a threshold below
        // 0.09, and the calibrated WinForms demo scenario sits at a ~0.057 margin. Estimate,
        // like the other defaults - recalibrated against the #15 benchmark dataset, not
        // reopened here.

        public double MinimumCandidateMargin { get; set; } = 0.05;

        // Consensus acceptance (#10, decided in #19): an LLM pick is accepted only when at
        // least this many providers independently name the same candidateId. Below 2 there is
        // no consensus to speak of - a single provider's uncalibrated confidence would be
        // deciding again, which is exactly what #19 ruled out.

        public int MinimumConsensusVotes { get; set; } = 2;

        // Candidate pruning: candidates below MinCandidateScore are dropped before ranking,
        // and at most MaxCandidatesForLlm survive into the LLM fallback's shortlist prompt -
        // bounds both heuristic scoring cost and LLM prompt size/token cost on large trees.

        public int MaxCandidatesForLlm { get; set; } = 20;
        public double MinCandidateScore { get; set; } = 0.05;
        public static SimilarityWeights Default => new();

        /// <summary>
        /// Creates a <see cref="SimilarityWeights"/> instance configured according to the specified preset <see cref="ThresholdProfile"/>.
        /// </summary>
        public static SimilarityWeights FromProfile(ThresholdProfile profile)
        {
            switch (profile)
            {
                case ThresholdProfile.Conservative:
                    return new SimilarityWeights
                    {
                        MinimumConfidence = 0.90,
                        MinimumCandidateMargin = 0.08,
                        MinimumEvidenceWeight = 0.50,
                        MinimumNameScoreWhenNamed = 0.30,
                        MinimumChildSignatureSimilarity = 0.50
                    };
                case ThresholdProfile.Aggressive:
                    return new SimilarityWeights
                    {
                        MinimumConfidence = 0.50,
                        MinimumCandidateMargin = 0.03,
                        MinimumEvidenceWeight = 0.30
                    };
                case ThresholdProfile.Balanced:
                default:
                    return new SimilarityWeights
                    {
                        MinimumConfidence = 0.75,
                        MinimumCandidateMargin = 0.05,
                        MinimumEvidenceWeight = 0.40,
                        MinimumNameScoreWhenNamed = 0.30,
                        MinimumChildSignatureSimilarity = 0.50
                    };
            }
        }

        /// <summary>
        /// Balanced profile (~0.75 confidence, 0.05 margin, 0.40 evidence). Recommended baseline balancing recall and false-heal suppression.
        /// </summary>
        public static SimilarityWeights Balanced => FromProfile(ThresholdProfile.Balanced);

        /// <summary>
        /// Conservative profile (~0.90 confidence, 0.08 margin, 0.50 evidence). Minimizes false heals, prioritizing safety over autonomous recall.
        /// </summary>
        public static SimilarityWeights Conservative => FromProfile(ThresholdProfile.Conservative);

        /// <summary>
        /// Aggressive profile (~0.50 confidence, 0.03 margin, 0.30 evidence). Maximizes autonomous recall across compound refactors.
        /// </summary>
        public static SimilarityWeights Aggressive => FromProfile(ThresholdProfile.Aggressive);

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
            ValidateRange(MinimumNameScoreWhenNamed, nameof(MinimumNameScoreWhenNamed), 0.0, 1.0);
            ValidateRange(MinimumChildSignatureSimilarity, nameof(MinimumChildSignatureSimilarity), 0.0, 1.0);
            ValidateRange(MinimumCandidateMargin, nameof(MinimumCandidateMargin), 0.0, 1.0);
            ValidateRange(MinCandidateScore, nameof(MinCandidateScore), 0.0, 1.0);

            if (MaxCandidatesForLlm < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxCandidatesForLlm), "MaxCandidatesForLlm must be at least one.");
            }

            if (MinimumConsensusVotes < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(MinimumConsensusVotes), "MinimumConsensusVotes must be at least two - one provider agreeing with itself is not consensus.");
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
