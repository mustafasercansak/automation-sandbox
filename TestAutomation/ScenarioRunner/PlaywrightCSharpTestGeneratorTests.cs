using System.Collections.Generic;
using IntentAutomation;
using UiModel;
using WebDiscovery;
using Xunit;

namespace ScenarioRunner
{
    public class PlaywrightCSharpTestGeneratorTests
    {
        [Fact]
        public void Generate_EmitsPlaywrightCSharpTestFromIntentScenarioAndRecordedLocators()
        {
            var scenario = new IntentScenario
            {
                Name = "Create customer",
                Goal = "Create a customer record",
                TargetUrl = "https://example.test/customers",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Navigate,
                        Value = "https://example.test/customers",
                        TestIntent = "Open customer form",
                    },
                    new IntentStep
                    {
                        Order = 2,
                        ActionType = IntentActionType.Fill,
                        LocatorKey = "Field.Email",
                        Value = "jane.doe@example.com",
                        TestIntent = "Fill customer email",
                    },
                    new IntentStep
                    {
                        Order = 3,
                        ActionType = IntentActionType.Click,
                        LocatorKey = "Action.PrimarySubmit",
                        TestIntent = "Submit customer form",
                    },
                    new IntentStep
                    {
                        Order = 4,
                        ActionType = IntentActionType.Assert,
                        LocatorKey = "Assert.ResultVisible",
                        TestIntent = "Verify customer result appears",
                    },
                }
            };
            var recordingResults = new List<IntentLocatorRecordingResult>
            {
                Recorded("Field.Email", "email-input", "page.GetByTestId(\"email-input\")"),
                Recorded("Action.PrimarySubmit", "save-button", "page.GetByRole(AriaRole.Button, new() { Name = \"Save\" })"),
                Recorded("Assert.ResultVisible", "customer-records", "page.GetByTestId(\"customer-records\")"),
            };

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, recordingResults);

            Assert.Contains("public class CreateCustomer : PageTest", code);
            Assert.Contains("public async Task CreateACustomerRecord()", code);
            Assert.Contains("await Page.GotoAsync(\"https://example.test/customers\");", code);
            Assert.Contains("await Page.GetByTestId(\"email-input\").FillAsync(\"jane.doe@example.com\");", code);
            Assert.Contains("await Page.GetByRole(AriaRole.Button, new() { Name = \"Save\" }).ClickAsync();", code);
            Assert.Contains("await Expect(Page.GetByTestId(\"customer-records\")).ToBeVisibleAsync();", code);
            Assert.Contains("// locator: Field.Email", code);
        }

        [Fact]
        public void Generate_EmitsInconclusive_WhenStepHasNoRecordedLocator()
        {
            var scenario = new IntentScenario
            {
                Goal = "Create a customer",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Fill,
                        LocatorKey = "Field.Email",
                    }
                }
            };

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, new List<IntentLocatorRecordingResult>());

            Assert.Contains("Assert.Inconclusive(\"No recorded locator for Field.Email.\");", code);
        }

        private static IntentLocatorRecordingResult Recorded(string locatorKey, string automationId, string expression)
        {
            return new IntentLocatorRecordingResult
            {
                LocatorKey = locatorKey,
                Recorded = true,
                Record = new LocatorRecord
                {
                    LocatorKey = locatorKey,
                    Snapshot = new UiElementInfo { AutomationId = automationId },
                },
                Candidate = new IntentElementCandidate
                {
                    LocatorSuggestions = new List<PlaywrightLocatorSuggestion>
                    {
                        new PlaywrightLocatorSuggestion { Strategy = "Test", Expression = expression, Confidence = 0.9 }
                    }
                }
            };
        }
    }
}
