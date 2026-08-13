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
                ? ToIdentifier(scenario.Name, "IntentScenarioTests")
                : ToIdentifier(_options.ClassName, "IntentScenarioTests");
            var methodName = string.IsNullOrWhiteSpace(_options.MethodName)
                ? ToIdentifier(scenario.Goal, "GeneratedIntentScenario")
                : ToIdentifier(_options.MethodName, "GeneratedIntentScenario");
            var namespaceName = string.IsNullOrWhiteSpace(_options.Namespace) ? "GeneratedTests" : _options.Namespace.Trim();
            var recordingsByKey = recordingResults
                .Where(result => result.Recorded && !string.IsNullOrWhiteSpace(result.LocatorKey))
                .GroupBy(result => result.LocatorKey)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var code = new StringBuilder();
            code.AppendLine("using System;");
            code.AppendLine("using Discovery;");
            code.AppendLine("using FlaUI.Core.AutomationElements;");
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
            code.AppendLine($"            _connector = ApplicationConnector.Launch(@\"{EscapeVerbatimString(_options.ApplicationExecutablePath)}\");");
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
                code.AppendLine($"            // {EscapeComment(step.TestIntent)}");
            }

            if (step.ActionType == IntentActionType.Navigate)
            {
                code.AppendLine("            // Navigate step has no desktop equivalent - launching the app already brings up its main window.");
                return;
            }

            recordingsByKey.TryGetValue(step.LocatorKey, out var recording);
            var findExpression = FindExpression(recording?.Record?.Snapshot);
            var locatorRequired = step.ActionType != IntentActionType.Assert || AssertionCodeEmitter.IsLocatorRequired(step.AssertionKind, _options.AssertGenerationMode);
            if (locatorRequired && string.IsNullOrWhiteSpace(findExpression))
            {
                code.AppendLine($"            Assert.True(false, \"No recorded locator for {EscapeString(step.LocatorKey)}.\");");
                return;
            }

            if (_options.IncludeLocatorComments && !string.IsNullOrWhiteSpace(step.LocatorKey) && locatorRequired)
            {
                code.AppendLine($"            // locator: {EscapeComment(step.LocatorKey)}");
            }

            switch (step.ActionType)
            {
                case IntentActionType.Fill:
                    code.AppendLine($"            window.{findExpression}!.AsTextBox().Text = \"{EscapeString(step.Value)}\";");
                    break;
                case IntentActionType.Select:
                    code.AppendLine($"            window.{findExpression}!.AsComboBox().Select(\"{EscapeString(step.Value)}\");");
                    break;
                case IntentActionType.Click:
                    code.AppendLine($"            window.{findExpression}!.AsButton().Invoke();");
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
        private static string FindExpression(UiElementInfo? snapshot)
        {
            if (snapshot == null)
            {
                return "";
            }

            if (!string.IsNullOrWhiteSpace(snapshot.AutomationId))
            {
                return $"FindFirstDescendant(cf => cf.ByAutomationId(\"{EscapeString(snapshot.AutomationId)}\"))";
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Name))
            {
                return $"FindFirstDescendant(cf => cf.ByName(\"{EscapeString(snapshot.Name)}\"))";
            }

            if (!string.IsNullOrWhiteSpace(snapshot.ControlType))
            {
                return $"FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.{snapshot.ControlType}))";
            }

            return "";
        }

        private static string ToIdentifier(string value, string fallback)
        {
            var parts = (value ?? "")
                .Split(new[] { ' ', '-', '_', '.', '/', '\\', ':', ';', ',', '(', ')', '[', ']', '{', '}', '\'', '"' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanIdentifierPart)
                .Where(part => part.Length > 0)
                .ToList();
            var identifier = string.Concat(parts);
            if (identifier.Length == 0)
            {
                identifier = fallback;
            }

            if (!char.IsLetter(identifier[0]) && identifier[0] != '_')
            {
                identifier = "_" + identifier;
            }

            return identifier;
        }

        private static string CleanIdentifierPart(string value)
        {
            var chars = value.Where(char.IsLetterOrDigit).ToArray();
            if (chars.Length == 0)
            {
                return "";
            }

            return char.ToUpperInvariant(chars[0]) + new string(chars.Skip(1).ToArray());
        }

        private static string EscapeString(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string EscapeVerbatimString(string value)
        {
            return (value ?? "").Replace("\"", "\"\"");
        }

        private static string EscapeComment(string value)
        {
            return (value ?? "").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
