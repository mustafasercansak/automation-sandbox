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



        // Separate from MinimumConfidence: an LLM's self-reported confidence isn't calibrated

        // the same way as the heuristic's structural score, so a low-confidence LLM pick

        // shouldn't silently replace a heuristic result just for having *a* pick.

        public double MinimumLlmConfidence { get; set; } = 0.5;



        // Candidate pruning: candidates below MinCandidateScore are dropped before ranking,

        // and at most MaxCandidatesForLlm survive into the LLM fallback's shortlist prompt -

        // bounds both heuristic scoring cost and LLM prompt size/token cost on large trees.

        public int MaxCandidatesForLlm { get; set; } = 20;

        public double MinCandidateScore { get; set; } = 0.05;



        public static SimilarityWeights Default { get; } = new();

    }

}
