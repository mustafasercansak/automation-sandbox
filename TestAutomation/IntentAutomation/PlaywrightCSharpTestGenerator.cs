using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UiModel;

namespace IntentAutomation
{
    public sealed class PlaywrightCSharpTestGenerator
    {
        private readonly PlaywrightCSharpTestGenerationOptions _options;

        public PlaywrightCSharpTestGenerator(PlaywrightCSharpTestGenerationOptions? options = null)
        {
            _options = options ?? new PlaywrightCSharpTestGenerationOptions();
        }

        public string Generate(IntentScenario scenario, IReadOnlyList<IntentLocatorRecordingResult> recordingResults)
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
            code.AppendLine("using System.Threading.Tasks;");
            code.AppendLine("using Microsoft.Playwright;");
            code.AppendLine("using Microsoft.Playwright.NUnit;");
            code.AppendLine("using NUnit.Framework;");
            code.AppendLine();
            code.AppendLine($"namespace {namespaceName}");
            code.AppendLine("{");
            code.AppendLine($"    public class {className} : PageTest");
            code.AppendLine("    {");
            code.AppendLine("        [Test]");
            code.AppendLine($"        public async Task {methodName}()");
            code.AppendLine("        {");

            foreach (var step in scenario.Steps.OrderBy(step => step.Order))
            {
                AppendStep(code, step, recordingsByKey);
            }

            code.AppendLine("        }");
            code.AppendLine("    }");
            code.AppendLine("}");
            return code.ToString();
        }

        private void AppendStep(
            StringBuilder code,
            IntentStep step,
            IReadOnlyDictionary<string, IntentLocatorRecordingResult> recordingsByKey)
        {
            if (!string.IsNullOrWhiteSpace(step.TestIntent))
            {
                code.AppendLine($"            // {EscapeComment(step.TestIntent)}");
            }

            if (step.ActionType == IntentActionType.Navigate)
            {
                code.AppendLine($"            await Page.GotoAsync(\"{EscapeString(step.Value)}\");");
                return;
            }

            recordingsByKey.TryGetValue(step.LocatorKey, out var recording);
            var locatorExpression = LocatorExpression(recording?.Candidate, recording?.Record?.Snapshot);
            if (string.IsNullOrWhiteSpace(locatorExpression))
            {
                code.AppendLine($"            Assert.Inconclusive(\"No recorded locator for {EscapeString(step.LocatorKey)}.\");");
                return;
            }

            if (_options.IncludeLocatorComments && !string.IsNullOrWhiteSpace(step.LocatorKey))
            {
                code.AppendLine($"            // locator: {EscapeComment(step.LocatorKey)}");
            }

            switch (step.ActionType)
            {
                case IntentActionType.Fill:
                    code.AppendLine($"            await {locatorExpression}.FillAsync(\"{EscapeString(step.Value)}\");");
                    break;
                case IntentActionType.Select:
                    code.AppendLine($"            await {locatorExpression}.SelectOptionAsync(new[] {{ \"{EscapeString(step.Value)}\" }});");
                    break;
                case IntentActionType.Click:
                    code.AppendLine($"            await {locatorExpression}.ClickAsync();");
                    break;
                case IntentActionType.Assert:
                    code.AppendLine($"            await Expect({locatorExpression}).ToBeVisibleAsync();");
                    break;
                default:
                    code.AppendLine($"            Assert.Inconclusive(\"Unsupported intent action {step.ActionType}.\");");
                    break;
            }
        }

        private static string LocatorExpression(IntentElementCandidate? candidate, UiElementInfo? snapshot)
        {
            var suggestion = candidate?.LocatorSuggestions.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(suggestion?.Expression))
            {
                return suggestion!.Expression.Replace("page.", "Page.");
            }

            if (!string.IsNullOrWhiteSpace(snapshot?.AutomationId))
            {
                return $"Page.GetByTestId(\"{EscapeString(snapshot!.AutomationId)}\")";
            }

            if (!string.IsNullOrWhiteSpace(snapshot?.Name))
            {
                return $"Page.GetByText(\"{EscapeString(snapshot!.Name)}\")";
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

        private static string EscapeComment(string value)
        {
            return (value ?? "").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
