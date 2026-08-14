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
                        AssertionKind = AssertionKind.Visible,
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
                        LocatorKey = "Field.IframeEmail",
                        Value = "jane@example.com",
                        TestIntent = "Fill email inside iframe",
                    },
                    new IntentStep
                    {
                        Order = 2,
                        ActionType = IntentActionType.Click,
                        LocatorKey = "Action.NestedSave",
                        TestIntent = "Click save in nested iframe",
                    },
                }
            };
            var recordingResults = new List<IntentLocatorRecordingResult>
            {
                Recorded("Field.IframeEmail", "email-input", "page.FrameLocator(\"iframe[name='details']\").GetByTestId(\"email-input\")"),
                Recorded("Action.NestedSave", "save-btn", "page.FrameLocator(\"iframe#parent\").FrameLocator(\"iframe#child\").GetByRole(AriaRole.Button, new() { Name = \"Save\" })"),
            };

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, recordingResults);

            Assert.Contains("await Page.FrameLocator(\"iframe[name='details']\").GetByTestId(\"email-input\").FillAsync(\"jane@example.com\");", code);
            Assert.Contains("await Page.FrameLocator(\"iframe#parent\").FrameLocator(\"iframe#child\").GetByRole(AriaRole.Button, new() { Name = \"Save\" }).ClickAsync();", code);
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
                        LocatorKey = "Assert.OrderTotal",
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

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("await Expect(Page.GetByTestId(\"order-total\")).ToHaveTextAsync(\"$125\");", code);
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

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, new List<IntentLocatorRecordingResult>());

            Assert.Contains("await Expect(Page).ToHaveURLAsync(\"https://example.test/checkout/success\");", code);
        }

        [Fact]
        public void Generate_EmitsInconclusive_WhenAssertionKindIsNone_InStrictMode_EvenWithoutRecordedLocator()
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
                        LocatorKey = "Assert.Outcome",
                        AssertionKind = AssertionKind.None,
                        ExpectedOutcome = "Complex unspecified business state",
                    }
                }
            };

            // No locator recorded for Assert.Outcome
            var code = new PlaywrightCSharpTestGenerator(new PlaywrightCSharpTestGenerationOptions { AssertGenerationMode = AssertGenerationMode.Strict }).Generate(scenario, new List<IntentLocatorRecordingResult>());

            Assert.Contains("Assert.Inconclusive(\"Review: Unmapped assertion outcome 'Complex unspecified business state'.\");", code);
            Assert.DoesNotContain("No recorded locator", code);
        }

        [Fact]
        public void Generate_EscapesNewlines_InExpectedValue()
        {
            var scenario = new IntentScenario
            {
                Goal = "Verify multiline text",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Assert,
                        LocatorKey = "Assert.Address",
                        AssertionKind = AssertionKind.TextEquals,
                        ExpectedValue = "Line 1\r\nLine 2",
                        ExpectedOutcome = "Address should be Line 1\nLine 2",
                    }
                }
            };
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Assert.Address", "address-box", "page.GetByTestId(\"address-box\")")
            };

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("await Expect(Page.GetByTestId(\"address-box\")).ToHaveTextAsync(\"Line 1\\r\\nLine 2\");", code);
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
                        LocatorKey = "Assert.Outcome",
                        AssertionKind = AssertionKind.None,
                        ExpectedOutcome = "Complex unspecified business state",
                    }
                }
            };
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Assert.Outcome", "outcome-box", "page.GetByTestId(\"outcome-box\")")
            };

            var code = new PlaywrightCSharpTestGenerator(new PlaywrightCSharpTestGenerationOptions { AssertGenerationMode = AssertGenerationMode.Lenient }).Generate(scenario, recordings);

            Assert.Contains("// TODO: Review unmapped expected outcome: Complex unspecified business state", code);
            Assert.Contains("await Expect(Page.GetByTestId(\"outcome-box\")).ToBeVisibleAsync();", code);
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

        [Fact]
        public void Generate_EscapesNewlinesTabsAndQuotesInValuesAndIntents()
        {
            var scenario = new IntentScenario
            {
                Name = "Multiline test",
                Goal = "Test multiline values",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Fill,
                        LocatorKey = "Field.Notes",
                        Value = "Line 1\r\nLine 2\twith \"quotes\"",
                        TestIntent = "Fill multi-line\r\nnotes with\ttab",
                    },
                    new IntentStep
                    {
                        Order = 2,
                        ActionType = IntentActionType.Assert,
                        LocatorKey = "Field.Notes",
                        AssertionKind = AssertionKind.TextEquals,
                        ExpectedValue = "Line 1\r\nLine 2",
                        ExpectedOutcome = "Expected\r\nmultiline outcome",
                    }
                }
            };
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Field.Notes", "txtNotes", "page.GetByTestId(\"txtNotes\")")
            };

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("// Fill multi-line  notes with tab", code);
            Assert.Contains(".FillAsync(\"Line 1\\r\\nLine 2\\twith \\\"quotes\\\"\");", code);
            Assert.Contains(".ToHaveTextAsync(\"Line 1\\r\\nLine 2\");", code);
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
