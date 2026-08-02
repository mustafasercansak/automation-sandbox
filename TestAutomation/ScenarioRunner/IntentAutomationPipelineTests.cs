using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntentAutomation;
using UiModel;
using WebDiscovery;
using Xunit;

namespace ScenarioRunner
{
    public class IntentAutomationPipelineTests : IDisposable
    {
        private readonly string _directory;
        private readonly string _filePath;

        public IntentAutomationPipelineTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "IntentAutomationPipelineTests_" + Guid.NewGuid().ToString("N"));
            _filePath = Path.Combine(_directory, "intent.locators.json");
        }

        [Fact]
        public void Run_PlansMatchesRecordsAndGeneratesPlaywrightCSharpTest()
        {
            var request = new IntentPlanningRequest
            {
                Name = "Create web customer",
                Goal = "Create a customer record with valid email",
                TargetUrl = "https://example.test/customers",
                TestData = new Dictionary<string, string>
                {
                    ["email"] = "jane.doe@example.com",
                }
            };
            var repository = new LocatorRepository(_filePath);
            var pipeline = new IntentAutomationPipeline(options: new IntentAutomationPipelineOptions
            {
                Recording = new IntentLocatorRecordingOptions
                {
                    ApplicationName = "CustomerPortal",
                },
                Generation = new PlaywrightCSharpTestGenerationOptions
                {
                    Namespace = "CustomerPortal.Generated",
                }
            });

            var result = pipeline.Run(request, BuildCustomerDom(), repository);

            Assert.False(result.Planning.RequiresReview);
            Assert.Contains(result.Exploration.StepResults, step => step.Step.LocatorKey == "Field.Email" && step.Candidates.Count > 0);
            Assert.Contains(result.RecordingResults, item => item.LocatorKey == "Field.Email" && item.Recorded);
            Assert.Contains(result.RecordingResults, item => item.LocatorKey == "Action.PrimarySubmit" && item.Recorded);
            Assert.Contains(result.RecordingResults, item => item.LocatorKey == "Assert.ResultVisible" && item.Recorded);

            var document = repository.Load();
            Assert.Equal("CustomerPortal", document.ApplicationName);
            Assert.Equal("web-playwright", document.Platform);
            Assert.Contains(document.Locators, locator => locator.LocatorKey == "Field.Email" && locator.Snapshot.AutomationId == "email-input");
            Assert.Contains(document.Locators, locator => locator.LocatorKey == "Action.PrimarySubmit" && locator.Snapshot.AutomationId == "save-button");

            Assert.Contains("namespace CustomerPortal.Generated", result.PlaywrightCSharpTestCode);
            Assert.Contains("await Page.GotoAsync(\"https://example.test/customers\");", result.PlaywrightCSharpTestCode);
            Assert.Contains("await Page.GetByTestId(\"email-input\").FillAsync(\"jane.doe@example.com\");", result.PlaywrightCSharpTestCode);
            Assert.Contains("await Page.GetByTestId(\"save-button\").ClickAsync();", result.PlaywrightCSharpTestCode);
            Assert.Contains("await Expect(Page.GetByTestId(\"customer-records\")).ToBeVisibleAsync();", result.PlaywrightCSharpTestCode);
            Assert.Contains("await page.getByTestId('email-input').fill('jane.doe@example.com');", result.PlaywrightTypeScriptTestCode);
            Assert.Contains("await page.getByTestId('save-button').click();", result.PlaywrightTypeScriptTestCode);
            Assert.Equal("Create web customer", result.Report.ScenarioName);
            Assert.Contains(result.Report.Steps, step => step.LocatorKey == "Field.Email" && step.Recorded);
        }

        [Fact]
        public void Run_HappyPathProducesExecutableIntentAutomationArtifacts()
        {
            var request = new IntentPlanningRequest
            {
                Goal = "Create a customer record",
                TargetUrl = "https://example.test/customers",
                TestData = new Dictionary<string, string>
                {
                    ["email"] = "happy.path@example.com",
                }
            };
            var repository = new LocatorRepository(_filePath);
            var pipeline = new IntentAutomationPipeline();

            var result = pipeline.Run(request, BuildCustomerDom(), repository);

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
            Assert.Contains(result.RecordingResults, item => item.Step.ActionType == IntentActionType.Navigate && !item.Recorded);
            Assert.Contains("happy.path@example.com", result.PlaywrightCSharpTestCode);
            Assert.Contains("happy.path@example.com", result.PlaywrightTypeScriptTestCode);
            Assert.NotEmpty(result.Report.Steps);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private static WebElementInfo BuildCustomerDom()
        {
            var root = new WebElementInfo
            {
                TagName = "body",
                BoundingRectangle = new BoundingRectangle(0, 0, 1024, 768),
            };

            root.Children.Add(new WebElementInfo
            {
                TagName = "input",
                Role = "textbox",
                AccessibleName = "Email",
                TestId = "email-input",
                Id = "email",
                NameAttribute = "email",
            });
            root.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Save",
                TestId = "save-button",
            });
            root.Children.Add(new WebElementInfo
            {
                TagName = "table",
                Role = "grid",
                AccessibleName = "Customer Records",
                TestId = "customer-records",
            });

            return root;
        }
    }
}
