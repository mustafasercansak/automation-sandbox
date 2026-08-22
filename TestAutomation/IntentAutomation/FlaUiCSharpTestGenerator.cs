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
            var namespaceName = string.IsNullOrWhiteSpace(_options.Namespace) ? "GeneratedTests" : _options.Namespace.Trim();
            var recordingsByKey = recordingResults
                .Where(result => result.Recorded && !string.IsNullOrWhiteSpace(result.LocatorKey))
                .GroupBy(result => result.LocatorKey)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

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
                AppendStep(code, step, recordingsByKey);
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

            recordingsByKey.TryGetValue(step.LocatorKey, out var recording);
            var snapshot = recording?.Record?.Snapshot;
            var findExpression = FindExpression(snapshot, out var warningComment);
            if (!string.IsNullOrWhiteSpace(warningComment))
            {
                code.AppendLine($"            {warningComment}");
            }

            var locatorRequired = step.ActionType != IntentActionType.Assert || AssertionCodeEmitter.IsLocatorRequired(step.AssertionKind, _options.AssertGenerationMode);
            if (locatorRequired && string.IsNullOrWhiteSpace(findExpression))
            {
                code.AppendLine($"            Assert.True(false, \"No recorded locator for {CodeGenerationUtilities.EscapeString(step.LocatorKey)}.\");");
                return;
            }

            if (_options.IncludeLocatorComments && !string.IsNullOrWhiteSpace(step.LocatorKey) && locatorRequired)
            {
                code.AppendLine($"            // locator: {CodeGenerationUtilities.EscapeComment(step.LocatorKey)}");
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
                    code.AppendLine($"            window.{findExpression}!.AsTextBox().Text = \"{CodeGenerationUtilities.EscapeString(step.Value)}\";");
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
