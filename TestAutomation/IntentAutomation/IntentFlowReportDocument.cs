using System;
using System.Collections.Generic;

namespace IntentAutomation
{
    public sealed class IntentFlowReportDocument
    {
        // Schema v4 (issue #372): added Platform and FlaUiCSharpTestCode so the desktop pipeline
        // (IntentDesktopAutomationPipeline) emits the same report as the web pipeline. Platform is
        // "web" or "desktop"; the code field for the other platform stays empty.
        // Schema v3 (issue #9): added AssertionKind and ExpectedValue to IntentFlowReportStep so a
        // reviewer can tell from the report whether a step produced a real assertion or only a
        // review marker - the generated code is not always at hand when the report is read.
        // Schema v2 (issue #5): added BestCandidateSemanticScore and RunnerUpScore to IntentFlowReportStep
        // for full visibility into semantic gating and candidate runner-up margins.
        // Schema v1: initial pipeline report with BestCandidateScore and locator expression.
        public const int CurrentSchemaVersion = 4;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        public string ScenarioName { get; set; } = "";
        public string Goal { get; set; } = "";

        // "web" or "desktop". Web reports carry a TargetUrl; desktop reports do not.
        public string Platform { get; set; } = "web";
        public string TargetUrl { get; set; } = "";
        public bool PlanningRequiresReview { get; set; }
        public List<string> PlanningDiagnostics { get; set; } = new List<string>();
        public List<IntentFlowReportStep> Steps { get; set; } = new List<IntentFlowReportStep>();
        public string PlaywrightCSharpTestCode { get; set; } = "";
        public string PlaywrightTypeScriptTestCode { get; set; } = "";
        public string FlaUiCSharpTestCode { get; set; } = "";

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
                var recording = FindRecording(result, stepResult.Step);
                var best = stepResult.Candidates.Count == 0 ? null : stepResult.Candidates[0];
                var runnerUp = stepResult.Candidates.Count > 1 ? stepResult.Candidates[1] : null;
                document.Steps.Add(new IntentFlowReportStep
                {
                    Order = stepResult.Step.Order,
                    ActionType = stepResult.Step.ActionType.ToString(),
                    LocatorKey = recording?.LocatorKey ?? "",
                    TestIntent = stepResult.Step.TestIntent,
                    TargetDescription = stepResult.Step.TargetDescription,
                    Value = stepResult.Step.Value,
                    ExpectedOutcome = stepResult.Step.ExpectedOutcome,
                    AssertionKind = stepResult.Step.AssertionKind.ToString(),
                    ExpectedValue = stepResult.Step.ExpectedValue,
                    CandidateCount = stepResult.Candidates.Count,
                    BestCandidateScore = best?.Score,
                    BestCandidateSemanticScore = best?.SemanticScore,
                    RunnerUpScore = runnerUp?.Score,
                    BestCandidateLocator = best?.LocatorSuggestions.Count > 0 ? best.LocatorSuggestions[0].Expression : "",
                    RequiresReview = stepResult.RequiresReview,
                    ExplorationDiagnostic = stepResult.Diagnostic,
                    Recorded = recording?.Recorded ?? false,
                    RecordingDiagnostic = recording?.Diagnostic ?? "",
                });
            }

            return document;
        }

        private static IntentLocatorRecordingResult? FindRecording(IntentAutomationPipelineResult result, IntentStep step)
        {
            foreach (var recording in result.RecordingResults)
            {
                if (ReferenceEquals(recording.Step, step) || (recording.Step != null && recording.Step.Order == step.Order))
                {
                    return recording;
                }
            }

            return null;
        }

        // Desktop counterpart to FromPipelineResult (issue #372). The desktop exploration/recording
        // result types are structurally parallel to the web ones - same per-step Candidates, Score,
        // SemanticScore, RequiresReview and Diagnostic - so the report renders identically. The
        // only shape difference is that a desktop candidate has no PlaywrightLocatorSuggestion
        // list, so BestCandidateLocator is derived from the element's strongest identifier.
        public static IntentFlowReportDocument FromDesktopPipelineResult(IntentDesktopAutomationPipelineResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var document = new IntentFlowReportDocument
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Platform = "desktop",
                ScenarioName = result.Planning.Scenario.Name,
                Goal = result.Planning.Scenario.Goal,
                TargetUrl = result.Planning.Scenario.TargetUrl,
                PlanningRequiresReview = result.Planning.RequiresReview,
                PlanningDiagnostics = new List<string>(result.Planning.Diagnostics),
                FlaUiCSharpTestCode = result.FlaUiCSharpTestCode,
            };

            foreach (var stepResult in result.Exploration.StepResults)
            {
                var recording = FindDesktopRecording(result, stepResult.Step);
                var best = stepResult.Candidates.Count == 0 ? null : stepResult.Candidates[0];
                var runnerUp = stepResult.Candidates.Count > 1 ? stepResult.Candidates[1] : null;
                document.Steps.Add(new IntentFlowReportStep
                {
                    Order = stepResult.Step.Order,
                    ActionType = stepResult.Step.ActionType.ToString(),
                    LocatorKey = recording?.LocatorKey ?? "",
                    TestIntent = stepResult.Step.TestIntent,
                    TargetDescription = stepResult.Step.TargetDescription,
                    Value = stepResult.Step.Value,
                    ExpectedOutcome = stepResult.Step.ExpectedOutcome,
                    AssertionKind = stepResult.Step.AssertionKind.ToString(),
                    ExpectedValue = stepResult.Step.ExpectedValue,
                    CandidateCount = stepResult.Candidates.Count,
                    BestCandidateScore = best?.Score,
                    BestCandidateSemanticScore = best?.SemanticScore,
                    RunnerUpScore = runnerUp?.Score,
                    BestCandidateLocator = best == null ? "" : DesktopLocatorExpression(best.Element),
                    RequiresReview = stepResult.RequiresReview,
                    ExplorationDiagnostic = stepResult.Diagnostic,
                    Recorded = recording?.Recorded ?? false,
                    RecordingDiagnostic = recording?.Diagnostic ?? "",
                });
            }

            return document;
        }

        private static IntentDesktopLocatorRecordingResult? FindDesktopRecording(IntentDesktopAutomationPipelineResult result, IntentStep step)
        {
            foreach (var recording in result.RecordingResults)
            {
                if (ReferenceEquals(recording.Step, step) || (recording.Step != null && recording.Step.Order == step.Order))
                {
                    return recording;
                }
            }

            return null;
        }

        private static string DesktopLocatorExpression(UiModel.UiElementInfo element)
        {
            if (element == null)
            {
                return "";
            }

            if (!string.IsNullOrWhiteSpace(element.AutomationId))
            {
                return "AutomationId=" + element.AutomationId;
            }

            if (!string.IsNullOrWhiteSpace(element.Name))
            {
                return string.IsNullOrWhiteSpace(element.ControlType)
                    ? "Name=" + element.Name
                    : element.ControlType + " Name=" + element.Name;
            }

            return element.ControlType ?? "";
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

        // Structured assertion the step generated code from (issue #9). "None" means the outcome
        // could not be mapped to a known AssertionKind, so the generated test carries a review
        // marker instead of a real assertion - visible here without opening the generated file.
        public string AssertionKind { get; set; } = "";
        public string ExpectedValue { get; set; } = "";

        public int CandidateCount { get; set; }
        public double? BestCandidateScore { get; set; }
        public double? BestCandidateSemanticScore { get; set; }
        public double? RunnerUpScore { get; set; }
        public string BestCandidateLocator { get; set; } = "";
        public bool RequiresReview { get; set; }
        public string ExplorationDiagnostic { get; set; } = "";
        public bool Recorded { get; set; }
        public string RecordingDiagnostic { get; set; } = "";
    }
}
