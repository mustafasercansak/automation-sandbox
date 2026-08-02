using System;
using System.Collections.Generic;

namespace IntentAutomation
{
    public sealed class IntentFlowReportDocument
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        public string ScenarioName { get; set; } = "";
        public string Goal { get; set; } = "";
        public string TargetUrl { get; set; } = "";
        public bool PlanningRequiresReview { get; set; }
        public List<string> PlanningDiagnostics { get; set; } = new List<string>();
        public List<IntentFlowReportStep> Steps { get; set; } = new List<IntentFlowReportStep>();
        public string PlaywrightCSharpTestCode { get; set; } = "";
        public string PlaywrightTypeScriptTestCode { get; set; } = "";

        public static IntentFlowReportDocument FromPipelineResult(IntentAutomationPipelineResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var document = new IntentFlowReportDocument
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                ScenarioName = result.Planning.Scenario.Name,
                Goal = result.Planning.Scenario.Goal,
                TargetUrl = result.Planning.Scenario.TargetUrl,
                PlanningRequiresReview = result.Planning.RequiresReview,
                PlanningDiagnostics = new List<string>(result.Planning.Diagnostics),
                PlaywrightCSharpTestCode = result.PlaywrightCSharpTestCode,
                PlaywrightTypeScriptTestCode = result.PlaywrightTypeScriptTestCode,
            };

            foreach (var stepResult in result.Exploration.StepResults)
            {
                var recording = FindRecording(result, stepResult.Step.LocatorKey);
                var best = stepResult.Candidates.Count == 0 ? null : stepResult.Candidates[0];
                document.Steps.Add(new IntentFlowReportStep
                {
                    Order = stepResult.Step.Order,
                    ActionType = stepResult.Step.ActionType.ToString(),
                    LocatorKey = stepResult.Step.LocatorKey,
                    TestIntent = stepResult.Step.TestIntent,
                    TargetDescription = stepResult.Step.TargetDescription,
                    Value = stepResult.Step.Value,
                    ExpectedOutcome = stepResult.Step.ExpectedOutcome,
                    CandidateCount = stepResult.Candidates.Count,
                    BestCandidateScore = best?.Score,
                    BestCandidateLocator = best?.LocatorSuggestions.Count > 0 ? best.LocatorSuggestions[0].Expression : "",
                    RequiresReview = stepResult.RequiresReview,
                    ExplorationDiagnostic = stepResult.Diagnostic,
                    Recorded = recording?.Recorded ?? false,
                    RecordingDiagnostic = recording?.Diagnostic ?? "",
                });
            }

            return document;
        }

        private static IntentLocatorRecordingResult? FindRecording(IntentAutomationPipelineResult result, string locatorKey)
        {
            foreach (var recording in result.RecordingResults)
            {
                if (recording.LocatorKey == locatorKey)
                {
                    return recording;
                }
            }

            return null;
        }
    }

    public sealed class IntentFlowReportStep
    {
        public int Order { get; set; }
        public string ActionType { get; set; } = "";
        public string LocatorKey { get; set; } = "";
        public string TestIntent { get; set; } = "";
        public string TargetDescription { get; set; } = "";
        public string Value { get; set; } = "";
        public string ExpectedOutcome { get; set; } = "";
        public int CandidateCount { get; set; }
        public double? BestCandidateScore { get; set; }
        public string BestCandidateLocator { get; set; } = "";
        public bool RequiresReview { get; set; }
        public string ExplorationDiagnostic { get; set; } = "";
        public bool Recorded { get; set; }
        public string RecordingDiagnostic { get; set; } = "";
    }
}
