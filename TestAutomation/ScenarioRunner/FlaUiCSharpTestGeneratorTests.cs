using System.Collections.Generic;
using System.Linq;
using IntentAutomation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
                        TargetDescription = "Email",
                        Value = "jane.doe@example.com",
                        TestIntent = "Fill customer email",
                    },
                    new IntentStep
                    {
                        Order = 3,
                        ActionType = IntentActionType.Select,
                        TargetDescription = "RecordType",
                        Value = "Corporate",
                        TestIntent = "Select corporate record type",
                    },
                    new IntentStep
                    {
                        Order = 4,
                        ActionType = IntentActionType.Click,
                        TargetDescription = "PrimarySubmit",
                        TestIntent = "Submit customer form",
                    },
                    new IntentStep
                    {
                        Order = 5,
                        ActionType = IntentActionType.Assert,
                        TargetDescription = "ResultVisible",
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
            AssertValidCSharpSyntax(code);
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
            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Assert.OrderTotal", "lblTotal")
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("Assert.Equal(\"$125\", window.FindFirstDescendant(cf => cf.ByAutomationId(\"lblTotal\"))!.Name);", code);
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
                    new IntentStep { Order = 2, ActionType = IntentActionType.UploadFile, TargetDescription = "Field.ResumeFile", Value = @"C:\temp\resume.pdf" },
                    new IntentStep { Order = 3, ActionType = IntentActionType.PressKey, TargetDescription = "Action.SearchKey", Value = "Enter" },
                    new IntentStep { Order = 4, ActionType = IntentActionType.Wait, TargetDescription = "Wait.Confirmation", Value = "3000" },
                }
            };
            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Action.Hover", "btnHover", "Button"),
                Recorded("Field.ResumeFile", "btnUpload", "Button"),
                Recorded("Action.SearchKey", "txtSearch", "Edit"),
                Recorded("Wait.Confirmation", "lblStatus", "Text"),
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("Mouse.MoveTo(window.FindFirstDescendant(cf => cf.ByAutomationId(\"btnHover\"))!.GetClickablePoint());", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"btnUpload\"))!.AsButton().Invoke();", code);
            Assert.Contains("var fileDialog = Retry.WhileNull(() => window.ModalWindows.FirstOrDefault() ?? window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Window)), timeout: TimeSpan.FromSeconds(5)).Result;", code);
            Assert.Contains("Assert.NotNull(fileDialog);", code);
            Assert.Contains("var fileNameEdit = fileDialog!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit))?.AsTextBox();", code);
            Assert.Contains("Assert.NotNull(fileNameEdit);", code);
            Assert.Contains("fileNameEdit!.Text = \"C:\\\\temp\\\\resume.pdf\";", code);
            Assert.Contains("openButton!.Invoke();", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"txtSearch\"))!.Focus();", code);
            Assert.Contains("Keyboard.Type(VirtualKeyShort.RETURN);", code);
            Assert.Contains("Assert.NotNull(Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId(\"lblStatus\")), timeout: TimeSpan.FromMilliseconds(3000)).Result);", code);
            AssertValidCSharpSyntax(code);
        }

        [Fact]
        public void Generate_EmitsExplicitFailure_WhenUploadFileControlIsNotTriggerButton()
        {
            var scenario = new IntentScenario
            {
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.UploadFile, TargetDescription = "Field.ResumeFile", Value = @"C:\temp\resume.pdf" },
                }
            };
            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Field.ResumeFile", "txtResume", "Edit"),
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("// Desktop UploadFile requires a trigger button or manual file-dialog automation.", code);
            Assert.Contains("Assert.True(false, \"UploadFile requires manual file-dialog handling.\");", code);
            AssertValidCSharpSyntax(code);
        }

        [Fact]
        public void Generate_EmitsExplicitFailure_ForUnsupportedDesktopKey()
        {
            var scenario = new IntentScenario
            {
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.PressKey, TargetDescription = "Action.EditorKey", Value = "Control+Shift+P" },
                }
            };

            var code = new FlaUiCSharpTestGenerator().Generate(
                scenario,
                new List<IntentDesktopLocatorRecordingResult> { Recorded("Action.EditorKey", "editor") });

            Assert.Contains("Assert.True(false, \"Unsupported desktop key Control+Shift+P.\");", code);
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
                        TargetDescription = "CustomerEmail",
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
                        TargetDescription = "Outcome",
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
                        TargetDescription = "Outcome",
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
                    new IntentStep { Order = 1, ActionType = IntentActionType.Assert, TargetDescription = "Panel.Company", AssertionKind = AssertionKind.Visible },
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
        public void Generate_EmitsCheckBoxAssignments_ForCheckAndUncheckSteps()
        {
            // #198: Check/Uncheck steps must emit AsCheckBox(), never AsComboBox().Select(...).
            var scenario = new IntentScenario
            {
                Goal = "Toggle form options",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Check, TargetDescription = "Newsletter" },
                    new IntentStep { Order = 2, ActionType = IntentActionType.Uncheck, TargetDescription = "Terms" },
                }
            };
            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Field.Newsletter", "chkNewsletter", "CheckBox"),
                Recorded("Field.Terms", "chkTerms", "CheckBox"),
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"chkNewsletter\"))!.AsCheckBox().IsChecked = true;", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"chkTerms\"))!.AsCheckBox().IsChecked = false;", code);
            Assert.DoesNotContain("AsComboBox()", code);
        }

        [Fact]
        public void Generate_EmitsRadioButtonClick_ForCheckStepOnRadioButton()
        {
            // #198: a radio button has no checked-state setter; clicking selects it.
            var scenario = new IntentScenario
            {
                Goal = "Choose shipping method",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Check, TargetDescription = "ShippingMethod" },
                }
            };
            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Field.ShippingMethod", "radExpress", "RadioButton"),
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("// Radio buttons cannot be toggled; clicking selects.", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"radExpress\"))!.AsRadioButton().Click();", code);
            Assert.DoesNotContain("AsCheckBox()", code);
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
                        TargetDescription = "Field.Email",
                    }
                }
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, new List<IntentDesktopLocatorRecordingResult>());

            Assert.Contains("Assert.True(false, \"No recorded locator for Field.Email.\");", code);
        }

        [Fact]
        public void Generate_FallsBackToClassNameWithWarning_WhenControlTypeIsUnrecognized()
        {
            var scenario = new IntentScenario
            {
                Goal = "Toggle custom grid",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Click, TargetDescription = "Grid.Custom" }
                }
            };
            var recordingResults = new List<IntentDesktopLocatorRecordingResult>
            {
                new IntentDesktopLocatorRecordingResult
                {
                    LocatorKey = "Grid.Custom",
                    Recorded = true,
                    Record = new LocatorRecord
                    {
                        LocatorKey = "Grid.Custom",
                        Snapshot = new UiElementInfo
                        {
                            ControlType = "InvalidCustomControlWidget",
                            ClassName = "CustomGridViewClass"
                        }
                    }
                }
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordingResults);

            Assert.Contains("// Warning: ControlType 'InvalidCustomControlWidget' is not a recognized FlaUI.Core.Definitions.ControlType; fell back to ByClassName.", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByClassName(\"CustomGridViewClass\"))!.AsButton().Invoke();", code);
        }

        [Fact]
        public void Generate_EmitsWarningAndFailure_WhenControlTypeIsUnrecognizedAndNoFallbackAvailable()
        {
            var scenario = new IntentScenario
            {
                Goal = "Toggle unknown control",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Click, TargetDescription = "Unknown.Control" }
                }
            };
            var recordingResults = new List<IntentDesktopLocatorRecordingResult>
            {
                new IntentDesktopLocatorRecordingResult
                {
                    LocatorKey = "Unknown.Control",
                    Recorded = true,
                    Record = new LocatorRecord
                    {
                        LocatorKey = "Unknown.Control",
                        Snapshot = new UiElementInfo
                        {
                            ControlType = "NonExistentControlType"
                        }
                    }
                }
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordingResults);

            Assert.Contains("// Warning: ControlType 'NonExistentControlType' is not a recognized FlaUI.Core.Definitions.ControlType; no locator could be emitted.", code);
            Assert.Contains("Assert.True(false, \"No recorded locator for Unknown.Control.\");", code);
        }

        [Fact]
        public void Generate_EscapesNewlinesTabsAndQuotesInValuesAndIntents()
        {
            var scenario = new IntentScenario
            {
                Name = "Multiline desktop test",
                Goal = "Test desktop multiline values",
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
                        AssertionKind = AssertionKind.ValueEquals,
                        ExpectedValue = "Line 1\r\nLine 2",
                        ExpectedOutcome = "Expected\r\nmultiline outcome",
                    }
                }
            };
            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Field.Notes", "txtNotes")
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("// Fill multi-line  notes with tab", code);
            Assert.Contains(".Text = \"Line 1\\r\\nLine 2\\twith \\\"quotes\\\"\";", code);
            Assert.Contains("Assert.Equal(\"Line 1\\r\\nLine 2\", window.FindFirstDescendant(cf => cf.ByAutomationId(\"txtNotes\"))!.AsTextBox().Text);", code);
        }

        [Theory]
        [InlineData("Desktop Suite.WinForms-App.v2", "DesktopSuite.WinFormsApp.V2")]
        [InlineData("my-app.tests", "MyApp.Tests")]
        [InlineData("123.456", "_123._456")]
        [InlineData("  ", "GeneratedTests")]
        [InlineData(null, "GeneratedTests")]
        public void Generate_SanitizesNamespace_AndProducesValidCSharpSyntax(string? rawNamespace, string expectedNamespace)
        {
            var scenario = new IntentScenario
            {
                Name = "Interaction flow",
                Goal = "Execute desktop interaction flow",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Hover, TargetDescription = "Hover" },
                    new IntentStep { Order = 2, ActionType = IntentActionType.UploadFile, TargetDescription = "ResumeFile", Value = @"C:\temp\resume.pdf" },
                    new IntentStep { Order = 3, ActionType = IntentActionType.PressKey, TargetDescription = "SearchKey", Value = "Enter" },
                    new IntentStep { Order = 4, ActionType = IntentActionType.Wait, TargetDescription = "Confirmation", Value = "3000" },
                }
            };
            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded("Action.Hover", "btnHover", "Button"),
                Recorded("Field.ResumeFile", "btnUpload", "Button"),
                Recorded("Action.SearchKey", "txtSearch", "Edit"),
                Recorded("Wait.Confirmation", "lblStatus", "Text"),
            };
            var options = new FlaUiCSharpTestGenerationOptions
            {
                Namespace = rawNamespace!,
                ClassName = "Desktop Form Tests",
                MethodName = "Execute Interaction Flow",
                ApplicationExecutablePath = @"C:\Apps\DemoApp.exe",
            };

            var code = new FlaUiCSharpTestGenerator(options).Generate(scenario, recordings);

            Assert.Contains($"namespace {expectedNamespace}", code);
            Assert.Contains("public class DesktopFormTests : IDisposable", code);
            Assert.Contains("public void ExecuteInteractionFlow()", code);
            AssertValidCSharpSyntax(code);
        }

        [Fact]
        public void Generate_AllActionsUnderDecoupledModel_ProducesCompilableFlaUiCSharpCode()
        {
            var scenario = new IntentScenario
            {
                Name = "All actions desktop full flow",
                Goal = "Execute end-to-end multi-step desktop flow",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Hover, TargetDescription = "User menu", TestIntent = "Hover user menu" },
                    new IntentStep { Order = 2, ActionType = IntentActionType.Fill, TargetDescription = "Email address", Value = "user@example.test", TestIntent = "Fill email" },
                    new IntentStep { Order = 3, ActionType = IntentActionType.Select, TargetDescription = "Role combobox", Value = "Admin", TestIntent = "Select role" },
                    new IntentStep { Order = 4, ActionType = IntentActionType.Check, TargetDescription = "Newsletter checkbox", TestIntent = "Check newsletter" },
                    new IntentStep { Order = 5, ActionType = IntentActionType.Uncheck, TargetDescription = "Terms checkbox", TestIntent = "Uncheck terms" },
                    new IntentStep { Order = 6, ActionType = IntentActionType.UploadFile, TargetDescription = "Browse button", Value = @"C:\tmp\cv.pdf", TestIntent = "Upload CV" },
                    new IntentStep { Order = 7, ActionType = IntentActionType.PressKey, TargetDescription = "Search edit", Value = "Enter", TestIntent = "Press Enter" },
                    new IntentStep { Order = 8, ActionType = IntentActionType.Wait, TargetDescription = "Spinner element", Value = "3000", TestIntent = "Wait for spinner" },
                    new IntentStep { Order = 9, ActionType = IntentActionType.Click, TargetDescription = "Submit button", TestIntent = "Submit form" },
                    new IntentStep { Order = 10, ActionType = IntentActionType.Assert, TargetDescription = "Confirmation message", AssertionKind = AssertionKind.Visible, ExpectedOutcome = "Confirmation is displayed" },
                    new IntentStep { Order = 11, ActionType = IntentActionType.Assert, TargetDescription = "Total amount", AssertionKind = AssertionKind.TextEquals, ExpectedValue = "$100", ExpectedOutcome = "Total is $100" },
                }
            };

            var recordings = new List<IntentDesktopLocatorRecordingResult>
            {
                Recorded(scenario.Steps[0], "Action.Hover.UserMenu", "btnUserMenu", "MenuItem"),
                Recorded(scenario.Steps[1], "Field.EmailAddress", "txtEmail", "Edit"),
                Recorded(scenario.Steps[2], "Field.RoleCombobox", "cmbRole", "ComboBox"),
                Recorded(scenario.Steps[3], "Field.NewsletterCheckbox", "chkNewsletter", "CheckBox"),
                Recorded(scenario.Steps[4], "Field.TermsCheckbox", "chkTerms", "CheckBox"),
                Recorded(scenario.Steps[5], "Field.BrowseButton", "btnBrowse", "Button"),
                Recorded(scenario.Steps[6], "Action.PressKey.SearchEdit", "txtSearch", "Edit"),
                Recorded(scenario.Steps[7], "Action.Wait.SpinnerElement", "lblSpinner", "Text"),
                Recorded(scenario.Steps[8], "Action.Click.SubmitButton", "btnSubmit", "Button"),
                Recorded(scenario.Steps[9], "Assert.ConfirmationMessage", "lblConfirm", "Text"),
                Recorded(scenario.Steps[10], "Assert.TotalAmount", "lblTotal", "Text"),
            };

            var code = new FlaUiCSharpTestGenerator().Generate(scenario, recordings);

            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"txtEmail\"))!.AsTextBox().Text = \"user@example.test\";", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"cmbRole\"))!.AsComboBox().Select(\"Admin\");", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"chkNewsletter\"))!.AsCheckBox().IsChecked = true;", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"chkTerms\"))!.AsCheckBox().IsChecked = false;", code);
            Assert.Contains("window.FindFirstDescendant(cf => cf.ByAutomationId(\"btnSubmit\"))!.AsButton().Invoke();", code);
            Assert.Contains("Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId(\"lblConfirm\")));", code);
            Assert.Contains("Assert.Equal(\"$100\", window.FindFirstDescendant(cf => cf.ByAutomationId(\"lblTotal\"))!.Name);", code);

            AssertValidCSharpSyntax(code);
        }

        private static IntentDesktopLocatorRecordingResult Recorded(IntentStep step, string locatorKey, string automationId, string controlType)
        {
            var result = Recorded(locatorKey, automationId, controlType);
            result.Step = step;
            return result;
        }

        private static void AssertValidCSharpSyntax(string code)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var errors = syntaxTree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            Assert.Empty(errors);
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

        private static IntentDesktopLocatorRecordingResult Recorded(string locatorKey, string automationId, string controlType)
        {
            return new IntentDesktopLocatorRecordingResult
            {
                LocatorKey = locatorKey,
                Recorded = true,
                Record = new LocatorRecord
                {
                    LocatorKey = locatorKey,
                    Snapshot = new UiElementInfo { AutomationId = automationId, ControlType = controlType },
                },
            };
        }
    }
}
