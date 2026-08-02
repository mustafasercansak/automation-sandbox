using System;
using System.Collections.Generic;
using UiModel;

namespace SelfHealing
{
    public sealed class HealingReportDocument
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<HealingReportEntry> Events { get; set; } = new List<HealingReportEntry>();
    }

    public sealed class HealingReportEntry
    {
        public DateTimeOffset HealedAt { get; set; } = DateTimeOffset.UtcNow;
        public string LocatorKey { get; set; } = "";
        public string Source { get; set; } = "";
        public string ReviewStatus { get; set; } = "";
        public double Score { get; set; }
        public double ConfidenceThreshold { get; set; }
        public int CandidateCount { get; set; }
        public double? LlmConfidence { get; set; }
        public string? LlmProviderName { get; set; }
        public string? LlmReasoning { get; set; }
        public UiElementInfo? PreviousSnapshot { get; set; }
        public UiElementInfo? AcceptedSnapshot { get; set; }
        public ScoreComponents? ScoreBreakdown { get; set; }

        public static HealingReportEntry FromHealResult(
            string locatorKey,
            UiElementInfo previousSnapshot,
            UiElementInfo acceptedSnapshot,
            HealResult result)
        {
            if (string.IsNullOrWhiteSpace(locatorKey))
            {
                throw new ArgumentException("locatorKey must not be null or empty.", nameof(locatorKey));
            }

            if (previousSnapshot == null)
            {
                throw new ArgumentNullException(nameof(previousSnapshot));
            }

            if (acceptedSnapshot == null)
            {
                throw new ArgumentNullException(nameof(acceptedSnapshot));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return new HealingReportEntry
            {
                LocatorKey = locatorKey,
                Source = result.Source == HealSource.Llm ? result.LlmProviderName ?? "llm" : "heuristic",
                ReviewStatus = result.IsConfident ? "accepted" : "manual-review",
                Score = result.Score,
                ConfidenceThreshold = result.ConfidenceThreshold,
                CandidateCount = result.CandidateCount,
                LlmConfidence = result.LlmConfidence,
                LlmProviderName = result.LlmProviderName,
                LlmReasoning = result.LlmReasoning,
                PreviousSnapshot = UiElementSnapshot.Capture(previousSnapshot),
                AcceptedSnapshot = UiElementSnapshot.Capture(acceptedSnapshot),
                ScoreBreakdown = result.ScoreBreakdown,
            };
        }
    }
}
