using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UiModel;

namespace IntentAutomation
{
    // Desktop counterpart to PlaywrightCSharpTestGenerator: emits an xUnit + FlaUI test skeleton
    // from a planned IntentScenario and the locators IntentDesktopLocatorRepositoryRecorder
    // recorded for it, using this project's own Discovery.ApplicationConnector - the same
    // launch/dispose pattern MainFormScenarioTests/WpfMainWindowScenarioTests use - rather than
    // raw FlaUI boilerplate the generated code would otherwise have to reinvent.

    public sealed class FlaUiCSharpTestGenerator
    {
        private readonly FlaUiCSharpTestGenerationOptions _options;

        public FlaUiCSharpTestGenerator(FlaUiCSharpTestGenerationOptions? options = null)
        {
            _options = options ?? new FlaUiCSharpTestGenerationOptions();
        }

        public string Generate(IntentScenario scenario, IReadOnlyList<IntentDesktopLocatorRecordingResult> recordingResults)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            if (recordingResults == null)
            {
                throw new ArgumentNullException(nameof(recordingResults));
            }

            var className = string.IsNullOrWhiteSpace(_options.ClassName)
                ? CodeGenerationUtilities.ToIdentifier(scenario.Name, "IntentScenarioTests")
                : CodeGenerationUtilities.ToIdentifier(_options.ClassName, "IntentScenarioTests");
            var methodName = string.IsNullOrWhiteSpace(_options.MethodName)
                ? CodeGenerationUtilities.ToIdentifier(scenario.Goal, "GeneratedIntentScenario")
                : CodeGenerationUtilities.ToIdentifier(_options.MethodName, "GeneratedIntentScenario");
            var namespaceName = CodeGenerationUtilities.ToNamespace(_options.Namespace, "GeneratedTests");
            var recordingsByStep = recordingResults
                .Where(result => result.Step != null)
                .GroupBy(result => result.Step)
                .ToDictionary(group => group.Key, group => group.First());
            var recordingsByOrder = recordingResults
                .Where(result => result.Step != null && result.Step.Order > 0)
                .GroupBy(result => result.Step.Order)
                .ToDictionary(group => group.Key, group => group.First());
            var recordingsByKey = recordingResults
                .Where(result => !string.IsNullOrWhiteSpace(result.LocatorKey))
                .GroupBy(result => result.LocatorKey)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var code = new StringBuilder();
            code.AppendLine("using System;");
            code.AppendLine("using Discovery;");
            code.AppendLine("using FlaUI.Core.AutomationElements;");
            code.AppendLine("using FlaUI.Core.Input;");
            code.AppendLine("using FlaUI.Core.Tools;");
            code.AppendLine("using FlaUI.Core.WindowsAPI;");
            code.AppendLine("using Xunit;");
            code.AppendLine();
            code.AppendLine($"namespace {namespaceName}");
            code.AppendLine("{");
            code.AppendLine($"    public class {className} : IDisposable");
            code.AppendLine("    {");
            code.AppendLine("        private readonly ApplicationConnector _connector;");
            code.AppendLine();
            code.AppendLine($"        public {className}()");
            code.AppendLine("        {");
            code.AppendLine($"            _connector = ApplicationConnector.Launch(@\"{CodeGenerationUtilities.EscapeVerbatimString(_options.ApplicationExecutablePath)}\");");
            code.AppendLine("        }");
            code.AppendLine();
            code.AppendLine("        [Fact]");
            code.AppendLine($"        public void {methodName}()");
            code.AppendLine("        {");
            code.AppendLine("            var window = _connector.GetMainWindow();");

            foreach (var step in scenario.Steps.OrderBy(step => step.Order))
            {
                AppendStep(code, step, recordingsByStep, recordingsByOrder, recordingsByKey);
            }

            code.AppendLine("        }");
            code.AppendLine();
            code.AppendLine("        public void Dispose() => _connector.Dispose();");
            code.AppendLine("    }");
            code.AppendLine("}");
            return code.ToString();
        }

        private void AppendStep(
            StringBuilder code,
            IntentStep step,
            IReadOnlyDictionary<IntentStep, IntentDesktopLocatorRecordingResult> recordingsByStep,
            IReadOnlyDictionary<int, IntentDesktopLocatorRecordingResult> recordingsByOrder,
            IReadOnlyDictionary<string, IntentDesktopLocatorRecordingResult> recordingsByKey)
        {
            if (!string.IsNullOrWhiteSpace(step.TestIntent))
            {
                code.AppendLine($"            // {CodeGenerationUtilities.EscapeComment(step.TestIntent)}");
            }

            if (step.ActionType == IntentActionType.Navigate)
            {
                code.AppendLine("            // Navigate step has no desktop equivalent - launching the app already brings up its main window.");
                return;
            }

            if (!recordingsByStep.TryGetValue(step, out var recording))
            {
                if (!recordingsByOrder.TryGetValue(step.Order, out recording))
                {
                    if (!string.IsNullOrWhiteSpace(step.TargetDescription) && recordingsByKey.TryGetValue(step.TargetDescription, out recording))
                    {
                    }
                    else
                    {
                        var synthesizedKey = IntentLocatorKeySynthesizer.Synthesize(step, (IntentDesktopElementCandidate?)null);
                        if (!string.IsNullOrWhiteSpace(synthesizedKey))
                        {
                            recordingsByKey.TryGetValue(synthesizedKey, out recording);
                        }
                    }
                }
            }

            var snapshot = recording?.Record?.Snapshot;
            var findExpression = FindExpression(snapshot, out var warningComment);
            if (!string.IsNullOrWhiteSpace(warningComment))
            {
                code.AppendLine($"            {warningComment}");
            }

            var locatorRequired = step.ActionType != IntentActionType.Assert || AssertionCodeEmitter.IsLocatorRequired(step.AssertionKind, _options.AssertGenerationMode);
            var locatorKey = recording?.LocatorKey ?? "";
            if (locatorRequired && string.IsNullOrWhiteSpace(findExpression))
            {
                var identifier = !string.IsNullOrWhiteSpace(locatorKey) ? locatorKey : step.TargetDescription;
                code.AppendLine($"            Assert.True(false, \"No recorded locator for {CodeGenerationUtilities.EscapeString(identifier)}.\");");
                return;
            }

            if (_options.IncludeLocatorComments && !string.IsNullOrWhiteSpace(locatorKey) && locatorRequired)
            {
                code.AppendLine($"            // locator: {CodeGenerationUtilities.EscapeComment(locatorKey)}");
            }

            switch (step.ActionType)
            {
                case IntentActionType.Fill:
                    code.AppendLine($"            window.{findExpression}!.AsTextBox().Text = \"{CodeGenerationUtilities.EscapeString(step.Value)}\";");
                    break;
                case IntentActionType.Select:
                    code.AppendLine($"            window.{findExpression}!.AsComboBox().Select(\"{CodeGenerationUtilities.EscapeString(step.Value)}\");");
                    break;
                case IntentActionType.Check:
                    if (string.Equals(snapshot?.ControlType, "RadioButton", StringComparison.OrdinalIgnoreCase))
                    {
                        // Radio buttons cannot be toggled; clicking selects.
                        code.AppendLine("            // Radio buttons cannot be toggled; clicking selects.");
                        code.AppendLine($"            window.{findExpression}!.AsRadioButton().Click();");
                    }
                    else
                    {
                        code.AppendLine($"            window.{findExpression}!.AsCheckBox().IsChecked = true;");
                    }
                    break;
                case IntentActionType.Uncheck:
                    code.AppendLine($"            window.{findExpression}!.AsCheckBox().IsChecked = false;");
                    break;
                case IntentActionType.Click:
                    code.AppendLine($"            window.{findExpression}!.AsButton().Invoke();");
                    break;
                case IntentActionType.Hover:
                    code.AppendLine($"            Mouse.MoveTo(window.{findExpression}!.GetClickablePoint());");
                    break;
                case IntentActionType.UploadFile:
                    if (string.Equals(snapshot?.ControlType, "Button", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(snapshot?.ControlType, "SplitButton", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(snapshot?.ControlType, "MenuItem", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(snapshot?.ControlType, "Hyperlink", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(snapshot?.ControlType, "Custom", StringComparison.OrdinalIgnoreCase))
                    {
                        code.AppendLine($"            window.{findExpression}!.AsButton().Invoke();");
                        code.AppendLine("            var fileDialog = Retry.WhileNull(() => window.ModalWindows.FirstOrDefault() ?? window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Window)), timeout: TimeSpan.FromSeconds(5)).Result;");
                        code.AppendLine("            Assert.NotNull(fileDialog);");
                        code.AppendLine("            var fileNameEdit = fileDialog!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit))?.AsTextBox();");
                        code.AppendLine("            Assert.NotNull(fileNameEdit);");
                        code.AppendLine($"            fileNameEdit!.Text = \"{CodeGenerationUtilities.EscapeString(step.Value)}\";");
                        code.AppendLine("            var openButton = fileDialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(\"Open\").Or(cf.ByName(\"Aç\"))))?.AsButton() ?? fileDialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button))?.AsButton();");
                        code.AppendLine("            Assert.NotNull(openButton);");
                        code.AppendLine("            openButton!.Invoke();");
                    }
                    else
                    {
                        code.AppendLine("            // Desktop UploadFile requires a trigger button or manual file-dialog automation.");
                        code.AppendLine("            Assert.True(false, \"UploadFile requires manual file-dialog handling.\");");
                    }
                    break;
                case IntentActionType.PressKey:
                    code.AppendLine($"            window.{findExpression}!.Focus();");
                    if (CodeGenerationUtilities.TryGetFlaUiVirtualKey(step.Value, out var virtualKey))
                    {
                        code.AppendLine($"            Keyboard.Type(VirtualKeyShort.{virtualKey});");
                    }
                    else
                    {
                        code.AppendLine($"            Assert.True(false, \"Unsupported desktop key {CodeGenerationUtilities.EscapeString(step.Value)}.\");");
                    }
                    break;
                case IntentActionType.Wait:
                    code.AppendLine($"            Assert.NotNull(Retry.WhileNull(() => window.{findExpression}, timeout: TimeSpan.FromMilliseconds({CodeGenerationUtilities.WaitTimeoutMilliseconds(step.Value)})).Result);");
                    break;
                case IntentActionType.Assert:
                    AssertionCodeEmitter.EmitFlaUiCSharp(step, findExpression, _options.AssertGenerationMode, code);
                    break;
                default:
                    code.AppendLine($"            Assert.True(false, \"Unsupported intent action {step.ActionType}.\");");
                    break;
            }
        }

        // AutomationId first - it is the strongest, most direct FlaUI locator. Name is the next
        // best signal (still ambiguity-prone but visible in the UI, unlike a raw ControlType
        // search). ControlType alone is a last resort: it is exactly the fallback
        // MainFormScenarioTests uses for panel1, whose AutomationId is deliberately meaningless.
        private static string FindExpression(UiElementInfo? snapshot, out string? warningComment)
        {
            warningComment = null;
            if (snapshot == null)
            {
                return "";
            }

            if (!string.IsNullOrWhiteSpace(snapshot.AutomationId))
            {
                return $"FindFirstDescendant(cf => cf.ByAutomationId(\"{CodeGenerationUtilities.EscapeString(snapshot.AutomationId)}\"))";
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Name))
            {
                return $"FindFirstDescendant(cf => cf.ByName(\"{CodeGenerationUtilities.EscapeString(snapshot.Name)}\"))";
            }

            if (!string.IsNullOrWhiteSpace(snapshot.ControlType))
            {
                if (CodeGenerationUtilities.TryGetCanonicalFlaUiControlType(snapshot.ControlType, out var canonicalControlType))
                {
                    return $"FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.{canonicalControlType}))";
                }

                if (!string.IsNullOrWhiteSpace(snapshot.ClassName))
                {
                    warningComment = $"// Warning: ControlType '{CodeGenerationUtilities.EscapeComment(snapshot.ControlType)}' is not a recognized FlaUI.Core.Definitions.ControlType; fell back to ByClassName.";
                    return $"FindFirstDescendant(cf => cf.ByClassName(\"{CodeGenerationUtilities.EscapeString(snapshot.ClassName)}\"))";
                }

                if (!string.IsNullOrWhiteSpace(snapshot.Name))
                {
                    warningComment = $"// Warning: ControlType '{CodeGenerationUtilities.EscapeComment(snapshot.ControlType)}' is not a recognized FlaUI.Core.Definitions.ControlType; fell back to ByName.";
                    return $"FindFirstDescendant(cf => cf.ByName(\"{CodeGenerationUtilities.EscapeString(snapshot.Name)}\"))";
                }

                warningComment = $"// Warning: ControlType '{CodeGenerationUtilities.EscapeComment(snapshot.ControlType)}' is not a recognized FlaUI.Core.Definitions.ControlType; no locator could be emitted.";
                return "";
            }

            if (!string.IsNullOrWhiteSpace(snapshot.ClassName))
            {
                return $"FindFirstDescendant(cf => cf.ByClassName(\"{CodeGenerationUtilities.EscapeString(snapshot.ClassName)}\"))";
            }

            return "";
        }
    }
}
