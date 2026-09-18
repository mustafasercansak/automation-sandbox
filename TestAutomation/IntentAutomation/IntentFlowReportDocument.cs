using System;
using System.Collections.Generic;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Editable versioned report of planning, matching, recording, and generated test sources for a web or desktop flow.</summary>
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
        /// <summary>Newest document schema version this build can read and write.</summary>
        public const int CurrentSchemaVersion = 4;
        /// <summary>Version of the persisted document shape; newer unsupported versions are rejected by the reader.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        /// <summary>Timestamp when this report document was generated.</summary>
        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Human-readable name of the planned scenario.</summary>
        public string ScenarioName { get; set; } = "";
        /// <summary>Original user goal retained as report context.</summary>
        public string Goal { get; set; } = "";

        // "web" or "desktop". Web reports carry a TargetUrl; desktop reports do not.
        /// <summary>Platform label recorded with the document, normally web or desktop.</summary>
        public string Platform { get; set; } = "web";
        /// <summary>Starting URL retained for web navigation and report context.</summary>
        public string TargetUrl { get; set; } = "";
        /// <summary>Whether the planner requested human review before using the scenario.</summary>
        public bool PlanningRequiresReview { get; set; }
        /// <summary>Planner messages retained for review and troubleshooting.</summary>
        public List<string> PlanningDiagnostics { get; set; } = new List<string>();
        /// <summary>Ordered steps recorded in this scenario or flow report.</summary>
        public List<IntentFlowReportStep> Steps { get; set; } = new List<IntentFlowReportStep>();
        /// <summary>Generated Playwright C# test source; generation does not execute the test.</summary>
        public string PlaywrightCSharpTestCode { get; set; } = "";
        /// <summary>Generated Playwright TypeScript test source; generation does not execute the test.</summary>
        public string PlaywrightTypeScriptTestCode { get; set; } = "";
        /// <summary>Generated FlaUI C# test source; execution requires Windows and the target application.</summary>
        public string FlaUiCSharpTestCode { get; set; } = "";

        /// <summary>Copies web pipeline outputs into a versioned flow report.</summary>
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
        /// <summary>Desktop counterpart to FromPipelineResult (issue #372). The desktop exploration/recording result types are structurally parallel to the web ones - same per-step Candidates, Score, SemanticScore, RequiresReview and Diagnostic - so the report renders identically. The only shape difference is that a desktop candidate has no PlaywrightLocatorSuggestion list, so BestCandidateLocator is derived from the element&apos;s strongest identifier.</summary>
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

        private static string DesktopLocatorExpression(AutomationSandbox.UiModel.UiElementInfo element)
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

    /// <summary>Persisted action, matching evidence, and recording decision for one flow step.</summary>
    public sealed class IntentFlowReportStep
    {
        /// <summary>Execution order assigned to this scenario step.</summary>
        public int Order { get; set; }
        /// <summary>Supported operation this step requests.</summary>
        public string ActionType { get; set; } = "";
        /// <summary>Logical repository key identifying a locator independently of its current automation identifier.</summary>
        public string LocatorKey { get; set; } = "";
        /// <summary>Narrative business intent retained for prompts, generated tests, and reports.</summary>
        public string TestIntent { get; set; } = "";
        /// <summary>Authoritative free-text description used to match the step to a captured element.</summary>
        public string TargetDescription { get; set; } = "";
        /// <summary>Action-specific payload, such as text, an option, a path, a key, or a wait timeout.</summary>
        public string Value { get; set; } = "";
        /// <summary>Narrative state expected after the step; this text does not identify its target for candidate matching.</summary>
        public string ExpectedOutcome { get; set; } = "";

        // Structured assertion the step generated code from (issue #9). "None" means the outcome
        // could not be mapped to a known AssertionKind, so the generated test carries a review
        // marker instead of a real assertion - visible here without opening the generated file.
        /// <summary>Structured assertion the step generated code from (issue #9). &quot;None&quot; means the outcome could not be mapped to a known AssertionKind, so the generated test carries a review marker instead of a real assertion - visible here without opening the generated file.</summary>
        public string AssertionKind { get; set; } = "";
        /// <summary>Expected assertion operand, interpreted according to AssertionKind.</summary>
        public string ExpectedValue { get; set; } = "";

        /// <summary>Number of candidates considered by this result&apos;s resolution or matching stage.</summary>
        public int CandidateCount { get; set; }
        /// <summary>Overall matching score of the highest-ranked candidate, if one exists.</summary>
        public double? BestCandidateScore { get; set; }
        /// <summary>Semantic matching score of the highest-ranked candidate, if one exists.</summary>
        public double? BestCandidateSemanticScore { get; set; }
        /// <summary>Score of the second-ranked candidate, or null when there is no competitor.</summary>
        public double? RunnerUpScore { get; set; }
        /// <summary>Locator expression or identifier associated with the highest-ranked candidate.</summary>
        public string BestCandidateLocator { get; set; } = "";
        /// <summary>Whether automatic use is declined pending human review.</summary>
        public bool RequiresReview { get; set; }
        /// <summary>Explanation of the candidate matching or review decision.</summary>
        public string ExplorationDiagnostic { get; set; } = "";
        /// <summary>Whether this attempt wrote a locator record.</summary>
        public bool Recorded { get; set; }
        /// <summary>Explanation of the locator recording decision.</summary>
        public string RecordingDiagnostic { get; set; } = "";
    }
}
