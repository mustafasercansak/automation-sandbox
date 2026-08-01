using System;

namespace LlmHealing
{
    public sealed class LlmHealingResult
    {
        public string ProviderName { get; set; } = "";
        public bool Success { get; set; }
        public string? MatchedAutomationId { get; set; }
        public double Confidence { get; set; }
        public string Reasoning { get; set; } = "";
        public string? ErrorMessage { get; set; }
        public TimeSpan Elapsed { get; set; }
    }
}
