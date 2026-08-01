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

        public ScoreComponents? ScoreBreakdown { get; set; }

        public string? LlmProviderName { get; set; }

        public double? LlmConfidence { get; set; }

        public string? LlmReasoning { get; set; }



        public bool IsConfident =>

            Matched is not null &&

            (Source == HealSource.Heuristic ? Score : LlmConfidence ?? 0.0) >= ConfidenceThreshold;

    }

}
