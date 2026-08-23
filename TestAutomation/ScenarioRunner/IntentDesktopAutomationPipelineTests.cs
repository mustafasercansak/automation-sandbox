using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntentAutomation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class IntentDesktopAutomationPipelineTests : IDisposable
    {
        private readonly string _directory;
        private readonly string _filePath;

        public IntentDesktopAutomationPipelineTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "IntentDesktopAutomationPipelineTests_" + Guid.NewGuid().ToString("N"));
            _filePath = Path.Combine(_directory, "desktop.locators.json");
        }

        [Fact]
        public void Run_PlansMatchesRecordsAndGeneratesFlaUiCSharpTest()
        {
            var request = new IntentPlanningRequest
            {
                Name = "Create desktop customer",
                Goal = "Create a customer record with valid email",
                TestData = new Dictionary<string, string>
                {
                    ["email"] = "jane.doe@example.com",
                }
            };
            var repository = new LocatorRepository(_filePath);
            var pipeline = new IntentDesktopAutomationPipeline(options: new IntentDesktopAutomationPipelineOptions
            {
                Recording = new IntentDesktopLocatorRecordingOptions
                {
                    ApplicationName = "CustomerApp",
                },
                Generation = new FlaUiCSharpTestGenerationOptions
                {
                    Namespace = "CustomerApp.Generated",
                }
            });

            var result = pipeline.Run(request, BuildCustomerWindow(), repository);

            Assert.False(result.Planning.RequiresReview);
            Assert.Contains(result.Exploration.StepResults, step => step.Step.TargetDescription == "email" && step.Candidates.Count > 0);
            Assert.Contains(result.RecordingResults, item => item.LocatorKey == "Field.Email" && item.Recorded);
            Assert.Contains(result.RecordingResults, item => item.LocatorKey == "Action.PrimarySubmit" && item.Recorded);
            Assert.Contains(result.RecordingResults, item => item.LocatorKey == "Assert.ResultVisible" && item.Recorded);

            var document = repository.Load();
            Assert.Equal("CustomerApp", document.ApplicationName);
            Assert.Equal("windows-uia", document.Platform);
            Assert.Contains(document.Locators, locator => locator.LocatorKey == "Field.Email" && locator.Snapshot.AutomationId == "txtEmail");
            Assert.Contains(document.Locators, locator => locator.LocatorKey == "Action.PrimarySubmit" && locator.Snapshot.AutomationId == "btnSave");

            Assert.Contains("namespace CustomerApp.Generated", result.FlaUiCSharpTestCode);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"txtEmail\"))!.AsTextBox().Text = \"jane.doe@example.com\";", result.FlaUiCSharpTestCode);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"btnSave\"))!.AsButton().Invoke();", result.FlaUiCSharpTestCode);
            Assert.Contains("Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId(\"dgvRecords\")));", result.FlaUiCSharpTestCode);
        }

        [Fact]
        public void Run_HappyPathProducesExecutableDesktopIntentArtifacts()
        {
            var request = new IntentPlanningRequest
            {
                Goal = "Create a customer record",
                TestData = new Dictionary<string, string>
                {
                    ["email"] = "happy.path@example.com",
                }
            };
            var repository = new LocatorRepository(_filePath);
            var pipeline = new IntentDesktopAutomationPipeline();

            var result = pipeline.Run(request, BuildCustomerWindow(), repository);

            Assert.False(result.Planning.RequiresReview);
            Assert.All(result.Exploration.StepResults.Where(step => step.Step.ActionType != IntentActionType.Navigate), step =>
            {
                Assert.False(step.RequiresReview);
                Assert.NotEmpty(step.Candidates);
            });
            var locatorRecordings = result.RecordingResults
                .Where(item => item.Step.ActionType != IntentActionType.Navigate && item.Step.ActionType != IntentActionType.Unknown)
                .ToList();
            Assert.NotEmpty(locatorRecordings);
            Assert.All(locatorRecordings, item => Assert.True(item.Recorded));
            Assert.Contains("happy.path@example.com", result.FlaUiCSharpTestCode);
        }

        [Fact]
        public void Run_DecoupledPipeline_PhrasingVariationsFlowEndToEndWithValidGeneratedDesktopCode()
        {
            var request = new IntentPlanningRequest
            {
                Name = "Natural Phrasing Desktop Customer",
                Goal = "Register customer with email and save form",
                TestData = new Dictionary<string, string>
                {
                    ["the customer's email address in the edit field"] = "alex.smith@example.test",
                }
            };
            var repository = new LocatorRepository(_filePath);
            var pipeline = new IntentDesktopAutomationPipeline();

            var result = pipeline.Run(request, BuildCustomerWindow(), repository);

            Assert.False(result.Planning.RequiresReview);
            Assert.All(result.Exploration.StepResults.Where(s => s.Step.ActionType != IntentActionType.Navigate), step =>
            {
                Assert.False(step.RequiresReview);
                Assert.NotEmpty(step.Candidates);
            });

            // Validate synthesized keys
            Assert.Contains(result.RecordingResults, r => r.LocatorKey.StartsWith("Field.") && r.Recorded);
            Assert.Contains(result.RecordingResults, r => r.LocatorKey.StartsWith("Action.") && r.Recorded);

            // Validate generated FlaUI C# syntax is clean with 0 errors
            var syntaxTree = CSharpSyntaxTree.ParseText(result.FlaUiCSharpTestCode);
            var errors = syntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.Empty(errors);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private static UiElementInfo BuildCustomerWindow()
        {
            var root = new UiElementInfo
            {
                ControlType = "Window",
                BoundingRectangle = new BoundingRectangle(0, 0, 800, 600),
            };

            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                AutomationId = "txtEmail",
                BoundingRectangle = new BoundingRectangle(100, 80, 220, 24),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save",
                AutomationId = "btnSave",
                BoundingRectangle = new BoundingRectangle(100, 200, 80, 24),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "DataGrid",
                Name = "Customer Records",
                AutomationId = "dgvRecords",
                BoundingRectangle = new BoundingRectangle(100, 260, 400, 200),
            });

            return root;
        }
    }
}
