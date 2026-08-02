using System.Collections.Generic;
using IntentAutomation;
using UiModel;
using WebDiscovery;
using Xunit;

namespace ScenarioRunner
{
    public class PlaywrightTypeScriptTestGeneratorTests
    {
        [Fact]
        public void Generate_EmitsPlaywrightTypeScriptTestFromIntentScenarioAndRecordedLocators()
        {
            var scenario = new IntentScenario
            {
                Name = "Create customer",
                Goal = "Create a customer record",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Navigate, Value = "https://example.test/customers", TestIntent = "Open customer form" },
                    new IntentStep { Order = 2, ActionType = IntentActionType.Fill, LocatorKey = "Field.Email", Value = "jane.doe@example.com", TestIntent = "Fill customer email" },
                    new IntentStep { Order = 3, ActionType = IntentActionType.Click, LocatorKey = "Action.PrimarySubmit", TestIntent = "Submit customer form" },
                    new IntentStep { Order = 4, ActionType = IntentActionType.Assert, LocatorKey = "Assert.ResultVisible", TestIntent = "Verify result" },
                }
            };
            var recordingResults = new List<IntentLocatorRecordingResult>
            {
                Recorded("Field.Email", "email-input", "page.GetByTestId(\"email-input\")"),
                Recorded("Action.PrimarySubmit", "save-button", "page.GetByRole(AriaRole.Button, new() { Name = \"Save\" })"),
                Recorded("Assert.ResultVisible", "customer-records", "page.GetByTestId(\"customer-records\")"),
            };

            var code = new PlaywrightTypeScriptTestGenerator().Generate(scenario, recordingResults);

            Assert.Contains("import { test, expect } from '@playwright/test';", code);
            Assert.Contains("test('Create customer', async ({ page }) => {", code);
            Assert.Contains("await page.goto('https://example.test/customers');", code);
            Assert.Contains("await page.getByTestId('email-input').fill('jane.doe@example.com');", code);
            Assert.Contains("await page.getByRole('button', { name: 'Save' }).click();", code);
            Assert.Contains("await expect(page.getByTestId('customer-records')).toBeVisible();", code);
        }

        [Fact]
        public void Generate_EmitsSkip_WhenStepHasNoRecordedLocator()
        {
            var scenario = new IntentScenario
            {
                Goal = "Create a customer",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Fill, LocatorKey = "Field.Email" },
                }
            };

            var code = new PlaywrightTypeScriptTestGenerator().Generate(scenario, new List<IntentLocatorRecordingResult>());

            Assert.Contains("test.skip(true, 'No recorded locator for Field.Email.');", code);
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
                        new PlaywrightLocatorSuggestion { Strategy = "Test", Expression = expression, Confidence = 0.9 },
                    },
                },
            };
        }
    }
}
