using System;
using System.Collections.Generic;
using UiModel;

namespace SelfHealing
{
    public sealed class HealingReportDocument
    {
        // v5 (issue #10): entries carry AgreedProviders - which providers reached consensus
        // on an LLM pick, the evidence behind the acceptance decision itself.
        // v4 (issue #6): entries carry DivergedFromHeuristic, HeuristicSnapshot, HeuristicScore
        // for full explainability when an LLM pick diverges from the heuristic winner.
        // v3: entries carry RunnerUpScore (margin gate, issue #4).
        // v2: added EvidenceCoverage and Candidates (#3).
        // Older reports upgrade in place; only newer-than-current schemas are rejected.
        public const int CurrentSchemaVersion = 5;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<HealingReportEntry> Events { get; set; } = new List<HealingReportEntry>();
    }

    // Slim per-candidate record for the report: enough to re-run offline threshold sweeps
    // (#15 benchmark) without persisting the whole UiElementInfo tree per candidate. Null
    // signals inside Components are the "no evidence" markers.

    public sealed class HealingReportCandidate
    {
        public string AutomationId { get; set; } = "";
        public string Name { get; set; } = "";
        public string ControlType { get; set; } = "";
        public double TotalScore { get; set; }
        public double EvidenceCoverage { get; set; }
        public ScoreComponents? Components { get; set; }
    }

    public sealed class HealingReportEntry
    {
        // Renamed from HeuristicReviewMargin: that name collided with the runner-up margin
        // gate (issue #4). This one is different - it flags heuristic matches whose score
        // sits barely above the confidence threshold, i.e. proximity to the threshold, not
        // distance from the runner-up.
        private const double ReviewProximityBand = 0.10;

        public const string AcceptedStatus = "accepted";
        public const string AcceptedWithLlmStatus = "accepted-with-llm";
        public const string ManualReviewStatus = "manual-review";

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

        // Providers that agreed on this pick (issue #10). Null - not an empty list - on
        // entries upgraded from a v4 report: "this build did not record it", as opposed to
        // "nobody agreed", which an empty list would claim. Same distinction EvidenceCoverage
        // makes for v1 upgrades.

        public List<string>? AgreedProviders { get; set; }
        public UiElementInfo? PreviousSnapshot { get; set; }
        public UiElementInfo? AcceptedSnapshot { get; set; }
        public ScoreComponents? ScoreBreakdown { get; set; }

        // Null on entries upgraded from a v1 report: "unknown", not "no evidence" - a 0.0
        // would be misread as thin evidence by offline threshold sweeps.

        public double? EvidenceCoverage { get; set; }

        // Second-best candidate score at decision time (null = no runner-up). Persisted so
        // the margin gate's behavior is auditable offline, not just the final verdict.

        public double? RunnerUpScore { get; set; }

        // Heuristic winner baseline and divergence tracking (issue #6)
        public bool DivergedFromHeuristic { get; set; }
        public UiElementInfo? HeuristicSnapshot { get; set; }
        public double? HeuristicScore { get; set; }

        public List<HealingReportCandidate>? Candidates { get; set; }

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
                ReviewStatus = ClassifyReviewStatus(result),
                Score = result.Score,
                ConfidenceThreshold = result.ConfidenceThreshold,
                CandidateCount = result.CandidateCount,
                LlmConfidence = result.LlmConfidence,
                LlmProviderName = result.LlmProviderName,
                AgreedProviders = result.AgreedProviders.Count == 0 ? null : new List<string>(result.AgreedProviders),
                LlmReasoning = result.LlmReasoning,
                PreviousSnapshot = UiElementSnapshot.Capture(previousSnapshot),
                AcceptedSnapshot = UiElementSnapshot.Capture(acceptedSnapshot),
                ScoreBreakdown = result.ScoreBreakdown,
                EvidenceCoverage = result.EvidenceCoverage,
                RunnerUpScore = result.RunnerUpScore,
                DivergedFromHeuristic = result.DivergedFromHeuristic,
                HeuristicSnapshot = result.HeuristicMatched is null ? null : UiElementSnapshot.Capture(result.HeuristicMatched),
                HeuristicScore = result.HeuristicScore,
                Candidates = result.Candidates?
                    .Select(c => new HealingReportCandidate
                    {
                        AutomationId = c.Candidate.AutomationId,
                        Name = c.Candidate.Name,
                        ControlType = c.Candidate.ControlType,
                        TotalScore = c.TotalScore,
                        EvidenceCoverage = c.EvidenceCoverage,
                        Components = c.Components,
                    })
                    .ToList(),
            };
        }

        private static string ClassifyReviewStatus(HealResult result)
        {
            if (!result.IsConfident)
            {
                return ManualReviewStatus;
            }

            if (result.Source == HealSource.Llm)
            {
                return AcceptedWithLlmStatus;
            }

            return result.Score - result.ConfidenceThreshold <= ReviewProximityBand
                ? ManualReviewStatus
                : AcceptedStatus;
        }
    }
}
