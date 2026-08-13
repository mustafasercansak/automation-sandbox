using System.Collections.Generic;
using IntentAutomation;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class FlaUiCSharpTestGeneratorTests
    {
        [Fact]
        public void Generate_EmitsFlaUiCSharpTestFromIntentScenarioAndRecordedLocators()
        {
            var scenario = new IntentScenario
            {
                Name = "Create customer",
                Goal = "Create a customer record",
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
                        ActionType = IntentActionType.Select,
                        LocatorKey = "Field.RecordType",
                        Value = "Corporate",
                        TestIntent = "Select corporate record type",
                    },
                    new IntentStep
                    {
                        Order = 4,
                        ActionType = IntentActionType.Click,
                        LocatorKey = "Action.PrimarySubmit",
                        TestIntent = "Submit customer form",
                    },
                    new IntentStep
                    {
                        Order = 5,
                        ActionType = IntentActionType.Assert,
                        LocatorKey = "Assert.ResultVisible",
                        TestIntent = "Verify customer result appears",
                        AssertionKind = AssertionKind.Visible,
                    },
                }
            };
            var recordingResults = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Field.Email", "txtEmail"),
                Recorded("Field.RecordType", "cmbRecordType"),
                Recorded("Action.PrimarySubmit", "btnSave"),
                Recorded("Assert.ResultVisible", "dgvRecords"),
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordingResults);

            Assert.Contains("public class CreateCustomer : IDisposable", code);
            Assert.Contains("public void CreateACustomerRecord()", code);
            Assert.Contains("Navigate step has no desktop equivalent", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"txtEmail\"))!.AsTextBox().Text = \"jane.doe@example.com\";", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"cmbRecordType\"))!.AsComboBox().Select(\"Corporate\");", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"btnSave\"))!.AsButton().Invoke();", code);
            Assert.Contains("Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId(\"dgvRecords\")));", code);
            Assert.Contains("// locator: Field.Email", code);
            Assert.Contains("ApplicationConnector.Launch(@\"", code);
            Assert.Contains("_connector.Dispose();", code);
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
            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Assert.OrderTotal", "lblTotal")
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("Assert.Equal(\"$125\", window.FindFirstDescendant(cf => cf.ByAutomationId(\"lblTotal\"))!.Name);", code);
        }

        [Fact]
        public void Generate_EmitsValueEqualsAssertion_WhenAssertionKindIsValueEquals()
        {
            var scenario = new IntentScenario
            {
                Goal = "Verify input field value",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Assert,
                        LocatorKey = "Assert.CustomerEmail",
                        AssertionKind = AssertionKind.ValueEquals,
                        ExpectedValue = "jane@example.com",
                        ExpectedOutcome = "Email field value is jane@example.com",
                    }
                }
            };
            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Assert.CustomerEmail", "txtEmail")
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("Assert.Equal(\"jane@example.com\", window.FindFirstDescendant(cf => cf.ByAutomationId(\"txtEmail\"))!.AsTextBox().Text);", code);
        }

        [Fact]
        public void Generate_EmitsFailingAssertion_WhenAssertionKindIsNone_InStrictMode()
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
                        ExpectedOutcome = "Complex unspecified desktop state",
                    }
                }
            };
            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Assert.Outcome", "pnlOutcome")
            };

            var code = new FlaUiCSharpTestGenerator(new FlaUiCSharpTestGenerationOptions { AssertGenerationMode = AssertGenerationMode.Strict }).Generate(scenario, recordings);

            Assert.Contains("Assert.True(false, \"Review: Unmapped assertion outcome 'Complex unspecified desktop state'.\");", code);
        }

        [Fact]
        public void Generate_EmitsFailingAssertion_WhenUrlAssertionUsedOnDesktop_InStrictMode()
        {
            var scenario = new IntentScenario
            {
                Goal = "Verify desktop URL",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Assert,
                        AssertionKind = AssertionKind.UrlEquals,
                        ExpectedValue = "https://example.test",
                        ExpectedOutcome = "Navigates to https://example.test",
                    }
                }
            };

            var code = new FlaUiCSharpTestGenerator(new FlaUiCSharpTestGenerationOptions { AssertGenerationMode = AssertGenerationMode.Strict }).Generate(scenario, new List<IntentDesktopLocatorRecordingResult>());

            Assert.Contains("Assert.True(false, \"Review: URL assertions are not supported on desktop targets.\");", code);
        }

        [Fact]
        public void Generate_EmitsReviewCommentAndWindowNotNull_WhenUrlAssertionUsedOnDesktop_InLenientMode()
        {
            var scenario = new IntentScenario
            {
                Goal = "Verify desktop URL",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Assert,
                        AssertionKind = AssertionKind.UrlEquals,
                        ExpectedValue = "https://example.test",
                        ExpectedOutcome = "Navigates to https://example.test",
                    }
                }
            };

            var code = new FlaUiCSharpTestGenerator(new FlaUiCSharpTestGenerationOptions { AssertGenerationMode = AssertGenerationMode.Lenient }).Generate(scenario, new List<IntentDesktopLocatorRecordingResult>());

            Assert.Contains("// TODO: Review unmapped desktop URL assertion: Navigates to https://example.test", code);
            Assert.Contains("Assert.NotNull(window);", code);
        }

        [Fact]
        public void Generate_EmitsReviewCommentAndNotNull_WhenAssertionKindIsNone_InLenientMode()
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
                        ExpectedOutcome = "Complex unspecified desktop state",
                    }
                }
            };
            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Assert.Outcome", "pnlOutcome")
            };

            var code = new FlaUiCSharpTestGenerator(new FlaUiCSharpTestGenerationOptions { AssertGenerationMode = AssertGenerationMode.Lenient }).Generate(scenario, recordings);

            Assert.Contains("// TODO: Review unmapped expected outcome: Complex unspecified desktop state", code);
            Assert.Contains("Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId(\"pnlOutcome\")));", code);
        }

        [Fact]
        public void Generate_FallsBackToNameThenControlType_WhenAutomationIdIsMissing()
        {
            var scenario = new IntentScenario
            {
                Goal = "Toggle corporate panel",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Assert, LocatorKey = "Panel.Company", AssertionKind = AssertionKind.Visible },
                }
            };
            var recordingResults = new List<IntentDesktopLocatorRecordingResult>
            {
                new IntentDesktopLocatorRecordingResult
                {
                    LocatorKey = "Panel.Company",
                    Recorded = true,
                    Record = new LocatorRecord
                    {
                        LocatorKey = "Panel.Company",
                        Snapshot = new UiElementInfo { ControlType = "Pane" },
                    },
                }
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordingResults);

            Assert.Contains("cf.ByControlType(FlaUI.Core.Definitions.ControlType.Pane)", code);
        }

        [Fact]
        public void Generate_EmitsFailingAssertion_WhenStepHasNoRecordedLocator()
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

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, new List<IntentDesktopLocatorRecordingResult>());

            Assert.Contains("Assert.True(false, \"No recorded locator for Field.Email.\");", code);
        }

        private static IntentDesktopLocatorRecordingResult Recorded(string locatorKey, string automationId)
        {
            return new IntentDesktopLocatorRecordingResult
            {
                LocatorKey = locatorKey,
                Recorded = true,
                Record = new LocatorRecord
                {
                    LocatorKey = locatorKey,
                    Snapshot = new UiElementInfo { AutomationId = automationId },
                },
            };
        }
    }
}
