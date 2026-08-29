using System.Collections.Generic;
using System.Linq;
using IntentAutomation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
                        TargetDescription = "Email",
                        Value = "jane.doe@example.com",
                        TestIntent = "Fill customer email",
                    },
                    new IntentStep
                    {
                        Order = 3,
                        ActionType = IntentActionType.Click,
                        TargetDescription = "PrimarySubmit",
                        TestIntent = "Submit customer form",
                    },
                    new IntentStep
                    {
                        Order = 4,
                        ActionType = IntentActionType.Assert,
                        TargetDescription = "ResultVisible",
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
            AssertValidCSharpSyntax(code);
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

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, recordingResults);

            Assert.Contains("await Page.FrameLocator(\"iframe[name='details']\").GetByTestId(\"email-input\").FillAsync(\"jane@example.com\");", code);
            Assert.Contains("await Page.FrameLocator(\"iframe#parent\").FrameLocator(\"iframe#child\").GetByRole(AriaRole.Button, new() { Name = \"Save\" }).ClickAsync();", code);
        }

        [Fact]
        public void Generate_EmitsCommonInteractionActions()
        {
            var scenario = CommonInteractionScenario();
            var recordings = scenario.Steps.Select(step =>
                Recorded(step.TargetDescription, step.TargetDescription, $"page.GetByTestId(\"{step.TargetDescription}\")")).ToList();

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("await Page.GetByTestId(\"Action.Hover\").HoverAsync();", code);
            Assert.Contains("await Page.GetByTestId(\"Field.ResumeFile\").SetInputFilesAsync(\"/tmp/resume.pdf\");", code);
            Assert.Contains("await Page.GetByTestId(\"Action.SearchKey\").PressAsync(\"Enter\");", code);
            Assert.Contains("await Page.GetByTestId(\"Wait.Confirmation\").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });", code);
            Assert.Contains("// locator: Action.Hover", code);
            Assert.Contains("// locator: Field.ResumeFile", code);
            Assert.Contains("// locator: Action.SearchKey", code);
            Assert.Contains("// locator: Wait.Confirmation", code);
        }

        [Fact]
        public void Generate_EmitsTextAssertion_WhenCandidateHasRecordedLocator()
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
        public void Generate_EmitsUrlContainsAssertion_ProducesCompilableCSharpWithEscapedRegex()
        {
            var scenario = new IntentScenario
            {
                Goal = "Verify query parameters in URL",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Assert,
                        AssertionKind = AssertionKind.UrlContains,
                        ExpectedValue = "https://example.test/items?id=1&name=test",
                        ExpectedOutcome = "Navigates to query page",
                    }
                }
            };

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, new List<IntentLocatorRecordingResult>());

            Assert.Contains("await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(\"https://example\\\\.test/items\\\\?id=1&name=test\"));", code);
            AssertValidCSharpSyntax(code);
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
                        TargetDescription = "Outcome",
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
                        TargetDescription = "Address",
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

            var code = new PlaywrightCSharpTestGenerator(new PlaywrightCSharpTestGenerationOptions { AssertGenerationMode = AssertGenerationMode.Lenient }).Generate(scenario, recordings);

            Assert.Contains("// TODO: Review unmapped expected outcome: Complex unspecified business state", code);
            Assert.Contains("await Expect(Page.GetByTestId(\"outcome-box\")).ToBeVisibleAsync();", code);
        }

        [Fact]
        public void Generate_EmitsFallbackVisibilityAssertion_WithoutTodoComments_WhenAssertGenerationModeIsFallback()
        {
            var scenario = new IntentScenario
            {
                Goal = "Verify complex state",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Assert,
                        TargetDescription = "Outcome",
                        ExpectedOutcome = "Complex unspecified business state",
                        AssertionKind = (AssertionKind)999
                    }
                }
            };
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Assert.Outcome", "outcome-box", "page.GetByTestId(\"outcome-box\")")
            };

            var code = new PlaywrightCSharpTestGenerator(new PlaywrightCSharpTestGenerationOptions { AssertGenerationMode = AssertGenerationMode.Fallback }).Generate(scenario, recordings);

            Assert.DoesNotContain("// TODO:", code);
            Assert.DoesNotContain("Assert.Inconclusive", code);
            Assert.Contains("await Expect(Page.GetByTestId(\"outcome-box\")).ToBeVisibleAsync();", code);
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

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("await Page.GetByTestId(\"newsletter\").CheckAsync();", code);
            Assert.Contains("await Page.GetByTestId(\"terms\").UncheckAsync();", code);
            Assert.DoesNotContain("SelectOptionAsync", code);
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
                        TargetDescription = "Field.Email",
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
                        TargetDescription = "Field.Notes",
                        Value = "Line 1\r\nLine 2\twith \"quotes\"",
                        TestIntent = "Fill multi-line\r\nnotes with\ttab",
                    },
                    new IntentStep
                    {
                        Order = 2,
                        ActionType = IntentActionType.Assert,
                        TargetDescription = "Field.Notes",
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

        [Fact]
        public void Generate_EmitterAccessibleNameWithControlCharacters_ProducesEscapedLocatorSource()
        {
            var scenario = ClickScenario("Action.SaveDraft");
            var suggestion = PlaywrightLocatorEmitter.Suggest(new WebElementInfo
            {
                Role = "button",
                AccessibleName = "Save\r\nDraft\tNow",
            }).Single(s => s.Strategy == "Role");
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Action.SaveDraft", "save-draft", suggestion.Expression),
            };

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains(
                "await Page.GetByRole(AriaRole.Button, new() { Name = \"Save\\r\\nDraft\\tNow\" }).ClickAsync();",
                code);
        }

        [Fact]
        public void Generate_EmitterNameAttributeWithDoubleQuote_ProducesEscapedLocatorSource()
        {
            var scenario = ClickScenario("Action.ProfileAlias");
            var suggestion = PlaywrightLocatorEmitter.Suggest(new WebElementInfo
            {
                NameAttribute = "profile\"alias",
            }).Single();
            var recordings = new List<IntentLocatorRecordingResult>
            {
                Recorded("Action.ProfileAlias", "profile-alias", suggestion.Expression),
            };

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("await Page.Locator(\"[name='profile\\\"alias']\").ClickAsync();", code);
        }

        private static IntentScenario ClickScenario(string targetDescription)
        {
            return new IntentScenario
            {
                Name = "Escaped locator",
                Goal = "Click escaped locator",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Click,
                        TargetDescription = targetDescription,
                    }
                }
            };
        }

        private static IntentScenario CommonInteractionScenario()
        {
            return new IntentScenario
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
        }

        [Theory]
        [InlineData("My Tests.Suite 1", "MyTests.Suite1")]
        [InlineData("foo-bar.v2_feature", "FooBar.V2Feature")]
        [InlineData("123.456", "_123._456")]
        [InlineData("  ", "GeneratedTests")]
        [InlineData(null, "GeneratedTests")]
        public void Generate_SanitizesNamespace_AndProducesValidCSharpSyntax(string? rawNamespace, string expectedNamespace)
        {
            var scenario = CommonInteractionScenario();
            var recordings = scenario.Steps.Select(step =>
                Recorded(step.TargetDescription, step.TargetDescription, $"page.GetByTestId(\"{step.TargetDescription}\")")).ToList();
            var options = new PlaywrightCSharpTestGenerationOptions
            {
                Namespace = rawNamespace!,
                ClassName = "Custom Tests",
                MethodName = "Run All Steps",
            };

            var code = new PlaywrightCSharpTestGenerator(options).Generate(scenario, recordings);

            Assert.Contains($"namespace {expectedNamespace}", code);
            Assert.Contains("public class CustomTests : PageTest", code);
            Assert.Contains("public async Task RunAllSteps()", code);
            AssertValidCSharpSyntax(code);
        }

        [Fact]
        public void Generate_AllActionsUnderDecoupledModel_ProducesCompilableCSharpCode()
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

            var code = new PlaywrightCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("await Page.GotoAsync(\"https://example.test/portal\");", code);
            Assert.Contains("await Page.GetByTestId(\"user-menu\").HoverAsync();", code);
            Assert.Contains("await Page.GetByTestId(\"email-input\").FillAsync(\"user@example.test\");", code);
            Assert.Contains("await Page.GetByTestId(\"role-select\").SelectOptionAsync(new[] { \"Admin\" });", code);
            Assert.Contains("await Page.GetByTestId(\"newsletter-chk\").CheckAsync();", code);
            Assert.Contains("await Page.GetByTestId(\"terms-chk\").UncheckAsync();", code);
            Assert.Contains("await Page.GetByTestId(\"resume-up\").SetInputFilesAsync(\"/tmp/cv.pdf\");", code);
            Assert.Contains("await Page.GetByTestId(\"search-txt\").PressAsync(\"Enter\");", code);
            Assert.Contains("await Page.GetByTestId(\"spinner\").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });", code);
            Assert.Contains("await Page.GetByTestId(\"submit-btn\").ClickAsync();", code);
            Assert.Contains("await Expect(Page.GetByTestId(\"confirm-box\")).ToBeVisibleAsync();", code);
            Assert.Contains("await Expect(Page.GetByTestId(\"total-box\")).ToHaveTextAsync(\"$100\");", code);

            AssertValidCSharpSyntax(code);
        }

        private static void AssertValidCSharpSyntax(string code)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var errors = syntaxTree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            Assert.Empty(errors);
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

        private static IntentLocatorRecordingResult Recorded(IntentStep step, string locatorKey, string automationId, string expression)
        {
            var result = Recorded(locatorKey, automationId, expression);
            result.Step = step;
            return result;
        }
    }
}
