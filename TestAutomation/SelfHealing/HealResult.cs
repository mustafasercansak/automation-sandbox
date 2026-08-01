using Discovery;

namespace SelfHealing
{
    public sealed class HealResult
    {
        public UiElementInfo? Matched { get; set; }
        public double Score { get; set; }
        public int CandidateCount { get; set; }
        public bool IsConfident => Matched is not null && Score >= SelfHealingResolver.MinimumConfidence;
    }
}
