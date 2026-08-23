using System.Collections.Generic;
using System.Linq;
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
                    new IntentStep { Order = 2, ActionType = IntentActionType.Fill, TargetDescription = "Email", Value = "jane.doe@example.com", TestIntent = "Fill customer email" },
                    new IntentStep { Order = 3, ActionType = IntentActionType.Click, TargetDescription = "PrimarySubmit", TestIntent = "Submit customer form" },
                    new IntentStep { Order = 4, ActionType = IntentActionType.Assert, TargetDescription = "ResultVisible", TestIntent = "Verify result", AssertionKind = AssertionKind.Visible },
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
        public void Generate_EmitsFrameLocatorChain_WhenCandidateHasIframeExpression()
        {
            var scenario = new IntentScenario
            {
                Name = "Iframe interaction",
                Goal = "Interact with elements inside iframes",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Fill,
                        TargetDescription = "Field.IframeEmail",
                        Value = "jane@example.com",
                        TestIntent = "Fill email inside iframe",
                    },
                    new IntentStep
                    {
                        Order = 2,
                        ActionType = IntentActionType.Click,
                        TargetDescription = "Action.NestedSave",
                        TestIntent = "Click save in nested iframe",
                    },
                }
            };
            var recordingResults = new List<IntentLocatorRecordingResult>
            {
                Recorded("Field.IframeEmail", "email-input", "page.FrameLocator(\"iframe[name='details']\").GetByTestId(\"email-input\")"),
                Recorded("Action.NestedSave", "save-btn", "page.FrameLocator(\"iframe#parent\").FrameLocator(\"iframe#child\").GetByRole(AriaRole.Button, new() { Name = \"Save\" })"),
            };

            var code = new PlaywrightTypeScriptTestGenerator().Generate(scenario, recordingResults);

            Assert.Contains("await page.frameLocator('iframe[name=\\'details\\']').getByTestId('email-input').fill('jane@example.com');", code);
            Assert.Contains("await page.frameLocator('iframe#parent').frameLocator('iframe#child').getByRole('button', { name: 'Save' }).click();", code);
        }

        [Fact]
        public void Generate_EmitsCommonInteractionActions()
        {
            var scenario = new IntentScenario
            {
                Goal = "Use common interactions",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Hover, TargetDescription = "Action.Hover" },
                    new IntentStep { Order = 2, ActionType = IntentActionType.UploadFile, TargetDescription = "Field.ResumeFile", Value = "/tmp/resume.pdf" },
                    new IntentStep { Order = 3, ActionType = IntentActionType.PressKey, TargetDescription = "Action.SearchKey", Value = "Enter" },
                    new IntentStep { Order = 4, ActionType = IntentActionType.Wait, TargetDescription = "Wait.Confirmation", Value = "3000" },
                }
            };
            var recordings = scenario.Steps.Select(step =>
                Recorded(step.TargetDescription, step.TargetDescription, $"page.GetByTestId(\"{step.TargetDescription}\")")).ToList();

            var code = new PlaywrightTypeScriptTestGenerator().Generate(scenario, recordings);

            Assert.Contains("await page.getByTestId('Action.Hover').hover();", code);
            Assert.Contains("await page.getByTestId('Field.ResumeFile').setInputFiles('/tmp/resume.pdf');", code);
            Assert.Contains("await page.getByTestId('Action.SearchKey').press('Enter');", code);
            Assert.Contains("await page.getByTestId('Wait.Confirmation').waitFor({ state: 'visible', timeout: 3000 });", code);
        }

        [Fact]
        public void Generate_EmitterAccessibleNameWithEscapedQuote_PreservesNameInCSharpAndTypeScript()
        {
            var scenario = new IntentScenario
            {
                Name = "Quoted accessible name",
                Goal = "Click save draft",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Click,
                        TargetDescription = "Action.SaveDraft",
                    }
                }
            };
            var suggestion = PlaywrightLocatorEmitter.Suggest(new WebElementInfo
            {
                Role = "button",
                AccessibleName = "Save \"draft\"",
            }).Single(item => item.Strategy == "Role");
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Action.SaveDraft", "save-draft", suggestion.Expression),
            };

            var csharpCode = new PlaywrightCSharpTestGenerator().Generate(scenario, recordings);
            var typeScriptCode = new PlaywrightTypeScriptTestGenerator().Generate(scenario, recordings);

            Assert.Contains(
                "await Page.GetByRole(AriaRole.Button, new() { Name = \"Save \\\"draft\\\"\" }).ClickAsync();",
                csharpCode);
            Assert.Contains(
                "await page.getByRole('button', { name: 'Save \"draft\"' }).click();",
                typeScriptCode);
        }

        [Fact]
        public void Generate_EmitsTextEqualsAssertion_WhenAssertionKindIsTextEquals()
        {
            var scenario = new IntentScenario
            {
                Goal = "Verify order total",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Assert,
                        TargetDescription = "OrderTotal",
                        AssertionKind = AssertionKind.TextEquals,
                        ExpectedValue = "$125",
                        ExpectedOutcome = "Order total should be $125",
                    }
                }
            };
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Assert.OrderTotal", "order-total", "page.GetByTestId(\"order-total\")")
            };

            var code = new PlaywrightTypeScriptTestGenerator().Generate(scenario, recordings);

            Assert.Contains("await expect(page.getByTestId('order-total')).toHaveText('$125');", code);
        }

        [Fact]
        public void Generate_EmitsUrlAssertion_WithoutRequiringElementLocator()
        {
            var scenario = new IntentScenario
            {
                Goal = "Verify checkout URL",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Assert,
                        AssertionKind = AssertionKind.UrlEquals,
                        ExpectedValue = "https://example.test/checkout/success",
                        ExpectedOutcome = "Navigates to https://example.test/checkout/success",
                    }
                }
            };

            var code = new PlaywrightTypeScriptTestGenerator().Generate(scenario, new List<IntentLocatorRecordingResult>());

            Assert.Contains("await expect(page).toHaveURL('https://example.test/checkout/success');", code);
        }

        [Fact]
        public void Generate_EmitsSkip_WhenAssertionKindIsNone_InStrictMode()
        {
            var scenario = new IntentScenario
            {
                Goal = "Verify strange outcome",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Assert,
                        TargetDescription = "Outcome",
                        AssertionKind = AssertionKind.None,
                        ExpectedOutcome = "Complex unspecified business state",
                    }
                }
            };
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Assert.Outcome", "outcome-box", "page.GetByTestId(\"outcome-box\")")
            };

            var code = new PlaywrightTypeScriptTestGenerator(new PlaywrightTypeScriptTestGenerationOptions { AssertGenerationMode = AssertGenerationMode.Strict }).Generate(scenario, recordings);

            Assert.Contains("test.skip(true, 'Review: Unmapped assertion outcome Complex unspecified business state.');", code);
        }

        [Fact]
        public void Generate_EmitsReviewCommentAndVisibility_WhenAssertionKindIsNone_InLenientMode()
        {
            var scenario = new IntentScenario
            {
                Goal = "Verify strange outcome",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Assert,
                        TargetDescription = "Outcome",
                        AssertionKind = AssertionKind.None,
                        ExpectedOutcome = "Complex unspecified business state",
                    }
                }
            };
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Assert.Outcome", "outcome-box", "page.GetByTestId(\"outcome-box\")")
            };

            var code = new PlaywrightTypeScriptTestGenerator(new PlaywrightTypeScriptTestGenerationOptions { AssertGenerationMode = AssertGenerationMode.Lenient }).Generate(scenario, recordings);

            Assert.Contains("// TODO: Review unmapped expected outcome: Complex unspecified business state", code);
            Assert.Contains("await expect(page.getByTestId('outcome-box')).toBeVisible();", code);
        }

        [Fact]
        public void Generate_EmitsCheckAndUncheckCalls_ForCheckAndUncheckSteps()
        {
            // #198: Check/Uncheck steps must emit checkable-element calls, never selectOption.
            var scenario = new IntentScenario
            {
                Goal = "Toggle form options",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Check, TargetDescription = "Newsletter" },
                    new IntentStep { Order = 2, ActionType = IntentActionType.Uncheck, TargetDescription = "Terms" },
                }
            };
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Field.Newsletter", "newsletter", "page.GetByTestId(\"newsletter\")"),
                Recorded("Field.Terms", "terms", "page.GetByTestId(\"terms\")"),
            };

            var code = new PlaywrightTypeScriptTestGenerator().Generate(scenario, recordings);

            Assert.Contains("await page.getByTestId('newsletter').check();", code);
            Assert.Contains("await page.getByTestId('terms').uncheck();", code);
            Assert.DoesNotContain("selectOption", code);
        }

        [Fact]
        public void Generate_EmitsSkip_WhenStepHasNoRecordedLocator()
        {
            var scenario = new IntentScenario
            {
                Goal = "Create a customer",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Fill, TargetDescription = "Field.Email" },
                }
            };

            var code = new PlaywrightTypeScriptTestGenerator().Generate(scenario, new List<IntentLocatorRecordingResult>());

            Assert.Contains("test.skip(true, 'No recorded locator for Field.Email.');", code);
        }

        [Fact]
        public void Generate_EscapesNewlinesTabsAndSingleQuotesInValuesAndIntents()
        {
            var scenario = new IntentScenario
            {
                Name = "Multiline TS test",
                Goal = "Test single quotes and newlines",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Fill,
                        TargetDescription = "Field.Notes",
                        Value = "It's line 1\r\nLine 2\twith tab",
                        TestIntent = "Fill multi-line\r\nnotes with\ttab",
                    },
                    new IntentStep
                    {
                        Order = 2,
                        ActionType = IntentActionType.Assert,
                        TargetDescription = "Field.Notes",
                        AssertionKind = AssertionKind.TextEquals,
                        ExpectedValue = "It's line 1\r\nLine 2",
                        ExpectedOutcome = "Expected\r\nmultiline outcome",
                    }
                }
            };
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Field.Notes", "txtNotes", "page.GetByTestId(\"txtNotes\")")
            };

            var code = new PlaywrightTypeScriptTestGenerator().Generate(scenario, recordings);

            Assert.Contains("// Fill multi-line  notes with tab", code);
            Assert.Contains(".fill('It\\'s line 1\\r\\nLine 2\\twith tab');", code);
            Assert.Contains(".toHaveText('It\\'s line 1\\r\\nLine 2');", code);
        }

        [Fact]
        public void Generate_AllActionsUnderDecoupledModel_ProducesValidTypeScriptStructure()
        {
            var scenario = new IntentScenario
            {
                Name = "All actions full flow",
                Goal = "Execute end-to-end multi-step flow",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Navigate, Value = "https://example.test/portal", TestIntent = "Open portal" },
                    new IntentStep { Order = 2, ActionType = IntentActionType.Hover, TargetDescription = "User menu", TestIntent = "Hover user menu" },
                    new IntentStep { Order = 3, ActionType = IntentActionType.Fill, TargetDescription = "Email address", Value = "user@example.test", TestIntent = "Fill email" },
                    new IntentStep { Order = 4, ActionType = IntentActionType.Select, TargetDescription = "Role dropdown", Value = "Admin", TestIntent = "Select role" },
                    new IntentStep { Order = 5, ActionType = IntentActionType.Check, TargetDescription = "Newsletter checkbox", TestIntent = "Check newsletter" },
                    new IntentStep { Order = 6, ActionType = IntentActionType.Uncheck, TargetDescription = "Terms checkbox", TestIntent = "Uncheck terms" },
                    new IntentStep { Order = 7, ActionType = IntentActionType.UploadFile, TargetDescription = "Resume attachment", Value = "/tmp/cv.pdf", TestIntent = "Upload CV" },
                    new IntentStep { Order = 8, ActionType = IntentActionType.PressKey, TargetDescription = "Search edit", Value = "Enter", TestIntent = "Press Enter" },
                    new IntentStep { Order = 9, ActionType = IntentActionType.Wait, TargetDescription = "Spinner element", Value = "3000", TestIntent = "Wait for spinner" },
                    new IntentStep { Order = 10, ActionType = IntentActionType.Click, TargetDescription = "Submit button", TestIntent = "Submit form" },
                    new IntentStep { Order = 11, ActionType = IntentActionType.Assert, TargetDescription = "Confirmation message", AssertionKind = AssertionKind.Visible, ExpectedOutcome = "Confirmation is displayed" },
                    new IntentStep { Order = 12, ActionType = IntentActionType.Assert, TargetDescription = "Total amount", AssertionKind = AssertionKind.TextEquals, ExpectedValue = "$100", ExpectedOutcome = "Total is $100" },
                }
            };

            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded(scenario.Steps[1], "Action.Hover.UserMenu", "user-menu", "page.GetByTestId(\"user-menu\")"),
                Recorded(scenario.Steps[2], "Field.EmailAddress", "email-input", "page.GetByTestId(\"email-input\")"),
                Recorded(scenario.Steps[3], "Field.RoleDropdown", "role-select", "page.GetByTestId(\"role-select\")"),
                Recorded(scenario.Steps[4], "Field.NewsletterCheckbox", "newsletter-chk", "page.GetByTestId(\"newsletter-chk\")"),
                Recorded(scenario.Steps[5], "Field.TermsCheckbox", "terms-chk", "page.GetByTestId(\"terms-chk\")"),
                Recorded(scenario.Steps[6], "Field.ResumeAttachment", "resume-up", "page.GetByTestId(\"resume-up\")"),
                Recorded(scenario.Steps[7], "Action.PressKey.SearchEdit", "search-txt", "page.GetByTestId(\"search-txt\")"),
                Recorded(scenario.Steps[8], "Action.Wait.SpinnerElement", "spinner", "page.GetByTestId(\"spinner\")"),
                Recorded(scenario.Steps[9], "Action.Click.SubmitButton", "submit-btn", "page.GetByTestId(\"submit-btn\")"),
                Recorded(scenario.Steps[10], "Assert.ConfirmationMessage", "confirm-box", "page.GetByTestId(\"confirm-box\")"),
                Recorded(scenario.Steps[11], "Assert.TotalAmount", "total-box", "page.GetByTestId(\"total-box\")"),
            };

            var code = new PlaywrightTypeScriptTestGenerator().Generate(scenario, recordings);

            Assert.Contains("import { test, expect } from '@playwright/test';", code);
            Assert.Contains("await page.goto('https://example.test/portal');", code);
            Assert.Contains("await page.getByTestId('user-menu').hover();", code);
            Assert.Contains("await page.getByTestId('email-input').fill('user@example.test');", code);
            Assert.Contains("await page.getByTestId('role-select').selectOption('Admin');", code);
            Assert.Contains("await page.getByTestId('newsletter-chk').check();", code);
            Assert.Contains("await page.getByTestId('terms-chk').uncheck();", code);
            Assert.Contains("await page.getByTestId('resume-up').setInputFiles('/tmp/cv.pdf');", code);
            Assert.Contains("await page.getByTestId('search-txt').press('Enter');", code);
            Assert.Contains("await page.getByTestId('spinner').waitFor({ state: 'visible', timeout: 3000 });", code);
            Assert.Contains("await page.getByTestId('submit-btn').click();", code);
            Assert.Contains("await expect(page.getByTestId('confirm-box')).toBeVisible();", code);
            Assert.Contains("await expect(page.getByTestId('total-box')).toHaveText('$100');", code);

            // Verify balanced braces and parentheses
            Assert.Equal(code.Count(c => c == '{'), code.Count(c => c == '}'));
            Assert.Equal(code.Count(c => c == '('), code.Count(c => c == ')'));
        }

        private static IntentLocatorRecordingResult Recorded(IntentStep step, string locatorKey, string automationId, string expression)
        {
            var result = Recorded(locatorKey, automationId, expression);
            result.Step = step;
            return result;
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
