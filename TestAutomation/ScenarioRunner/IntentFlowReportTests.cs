using System;
using System.Collections.Generic;
using System.IO;
using IntentAutomation;
using UiModel;
using WebDiscovery;
using Xunit;

namespace ScenarioRunner
{
    public class IntentFlowReportTests : IDisposable
    {
        private readonly string _directory;

        public IntentFlowReportTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "IntentFlowReportTests_" + Guid.NewGuid().ToString("N"));
        }

        [Fact]
        public void FromPipelineResult_CapturesStepsAndGeneratedCode()
        {
            var pipelineResult = BuildPipelineResult();

            var document = IntentFlowReportDocument.FromPipelineResult(pipelineResult);

            Assert.Equal("Create customer", document.ScenarioName);
            Assert.Equal("Create a customer", document.Goal);
            Assert.Equal(2, document.Steps.Count);
            Assert.Equal("Field.Email", document.Steps[0].LocatorKey);
            Assert.True(document.Steps[0].Recorded);
            Assert.Equal(0.95, document.Steps[0].BestCandidateScore);
            Assert.Equal(0.70, document.Steps[0].RunnerUpScore);
            Assert.Contains("CSharp", document.PlaywrightCSharpTestCode);
            Assert.Contains("TypeScript", document.PlaywrightTypeScriptTestCode);
        }

        [Fact]
        public void FromPipelineResult_CarriesStructuredAssertionPerStep()
        {
            // Issue #9: the report must show whether a step produced a real assertion or only a
            // review marker - a reviewer reading the report does not have the generated file open.
            var valueStep = new IntentStep
            {
                Order = 1,
                ActionType = IntentActionType.Assert,
                LocatorKey = "Assert.OrderTotal",
                TestIntent = "Verify the order total",
                ExpectedOutcome = "Order total should be $125",
                AssertionKind = AssertionKind.TextEquals,
                ExpectedValue = "$125",
            };
            var unmappedStep = new IntentStep
            {
                Order = 2,
                ActionType = IntentActionType.Assert,
                LocatorKey = "Assert.Mystery",
                TestIntent = "Verify something the planner could not map",
                ExpectedOutcome = "The workflow behaves correctly",
            };
            var scenario = new IntentScenario
            {
                Name = "Order flow",
                Goal = "Check the order total",
                Steps = new List<IntentStep> { valueStep, unmappedStep },
            };

            var document = IntentFlowReportDocument.FromPipelineResult(new IntentAutomationPipelineResult
            {
                Planning = new IntentPlanningResult { Scenario = scenario },
                Exploration = new IntentExplorationResult
                {
                    Scenario = scenario,
                    StepResults = new List<IntentStepExplorationResult>
                    {
                        new IntentStepExplorationResult { Step = valueStep },
                        new IntentStepExplorationResult { Step = unmappedStep },
                    },
                },
            });

            Assert.Equal(3, IntentFlowReportDocument.CurrentSchemaVersion);
            Assert.Equal(3, document.SchemaVersion);
            Assert.Equal("TextEquals", document.Steps[0].AssertionKind);
            Assert.Equal("$125", document.Steps[0].ExpectedValue);
            // An unmapped outcome must surface as "None" rather than silently looking like a real check.
            Assert.Equal("None", document.Steps[1].AssertionKind);
            Assert.Equal("", document.Steps[1].ExpectedValue);
        }

        [Fact]
        public void FileSink_WritesJsonAndHtmlReports()
        {
            var document = IntentFlowReportDocument.FromPipelineResult(BuildPipelineResult());
            var jsonPath = Path.Combine(_directory, "intent-flow.json");
            var htmlPath = Path.Combine(_directory, "intent-flow.html");

            new IntentFlowReportFileSink(jsonPath, htmlPath).Write(document);

            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(htmlPath));
            Assert.Contains("\"ScenarioName\": \"Create customer\"", File.ReadAllText(jsonPath));
            var html = File.ReadAllText(htmlPath);
            Assert.Contains("Intent Flow Report", html);
            Assert.Contains("Field.Email", html);
            Assert.Contains("runner-up: 0.70", html);
            Assert.Contains("Playwright TypeScript", html);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private static IntentAutomationPipelineResult BuildPipelineResult()
        {
            var emailStep = new IntentStep { Order = 1, ActionType = IntentActionType.Fill, LocatorKey = "Field.Email", TestIntent = "Fill email" };
            var clickStep = new IntentStep { Order = 2, ActionType = IntentActionType.Click, LocatorKey = "Action.PrimarySubmit", TestIntent = "Save customer" };
            var scenario = new IntentScenario
            {
                Name = "Create customer",
                Goal = "Create a customer",
                TargetUrl = "https://example.test/customers",
                Steps = new List<IntentStep> { emailStep, clickStep },
            };
            var emailCandidate = new IntentElementCandidate
            {
                Step = emailStep,
                Element = new WebElementInfo { TagName = "input", Role = "textbox", TestId = "email-input" },
                Score = 0.95,
                SemanticScore = 0.50,
                LocatorSuggestions = new List<PlaywrightLocatorSuggestion>
                {
                    new PlaywrightLocatorSuggestion { Expression = "page.GetByTestId(\"email-input\")", Confidence = 0.98 },
                },
            };
            var runnerUpEmailCandidate = new IntentElementCandidate
            {
                Step = emailStep,
                Element = new WebElementInfo { TagName = "input", Role = "textbox", TestId = "backup-email" },
                Score = 0.70,
                SemanticScore = 0.40,
                LocatorSuggestions = new List<PlaywrightLocatorSuggestion>
                {
                    new PlaywrightLocatorSuggestion { Expression = "page.GetByTestId(\"backup-email\")", Confidence = 0.80 },
                },
            };

            return new IntentAutomationPipelineResult
            {
                Planning = new IntentPlanningResult { Scenario = scenario },
                Exploration = new IntentExplorationResult
                {
                    Scenario = scenario,
                    StepResults = new List<IntentStepExplorationResult>
                    {
                        new IntentStepExplorationResult { Step = emailStep, Candidates = new List<IntentElementCandidate> { emailCandidate, runnerUpEmailCandidate } },
                        new IntentStepExplorationResult { Step = clickStep, RequiresReview = true, Diagnostic = "No visible DOM candidate matched this intent step." },
                    },
                },
                RecordingResults = new List<IntentLocatorRecordingResult>
                {
                    new IntentLocatorRecordingResult
                    {
                        Step = emailStep,
                        Candidate = emailCandidate,
                        LocatorKey = "Field.Email",
                        Recorded = true,
                        Diagnostic = "Recorded best visible DOM candidate.",
                    },
                    new IntentLocatorRecordingResult
                    {
                        Step = clickStep,
                        LocatorKey = "Action.PrimarySubmit",
                        Recorded = false,
                        Diagnostic = "Step requires review; candidate was not recorded.",
                    },
                },
                PlaywrightCSharpTestCode = "// CSharp",
                PlaywrightTypeScriptTestCode = "// TypeScript",
            };
        }
    }
}
