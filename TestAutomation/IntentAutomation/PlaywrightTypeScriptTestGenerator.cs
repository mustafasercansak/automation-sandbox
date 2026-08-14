using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UiModel;

namespace IntentAutomation
{
    public sealed class PlaywrightTypeScriptTestGenerator
    {
        private readonly PlaywrightTypeScriptTestGenerationOptions _options;

        public PlaywrightTypeScriptTestGenerator(PlaywrightTypeScriptTestGenerationOptions? options = null)
        {
            _options = options ?? new PlaywrightTypeScriptTestGenerationOptions();
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

            var title = string.IsNullOrWhiteSpace(_options.TestTitle)
                ? FirstNonEmpty(scenario.Name, scenario.Goal, "generated intent scenario")
                : _options.TestTitle;
            var recordingsByKey = recordingResults
                .Where(result => result.Recorded && !string.IsNullOrWhiteSpace(result.LocatorKey))
                .GroupBy(result => result.LocatorKey)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var code = new StringBuilder();
            code.AppendLine("import { test, expect } from '@playwright/test';");
            code.AppendLine();
            code.AppendLine($"test('{CodeGenerationUtilities.EscapeSingleQuoted(title)}', async ({{ page }}) => {{");

            foreach (var step in scenario.Steps.OrderBy(step => step.Order))
            {
                AppendStep(code, step, recordingsByKey);
            }

            code.AppendLine("});");
            return code.ToString();
        }

        private void AppendStep(
            StringBuilder code,
            IntentStep step,
            IReadOnlyDictionary<string, IntentLocatorRecordingResult> recordingsByKey)
        {
            if (!string.IsNullOrWhiteSpace(step.TestIntent))
            {
                code.AppendLine($"  // {CodeGenerationUtilities.EscapeComment(step.TestIntent)}");
            }

            if (step.ActionType == IntentActionType.Navigate)
            {
                code.AppendLine($"  await page.goto('{CodeGenerationUtilities.EscapeSingleQuoted(step.Value)}');");
                return;
            }

            recordingsByKey.TryGetValue(step.LocatorKey, out var recording);
            var locatorExpression = LocatorExpression(recording?.Candidate, recording?.Record?.Snapshot);
            var locatorRequired = step.ActionType != IntentActionType.Assert || AssertionCodeEmitter.IsLocatorRequired(step.AssertionKind, _options.AssertGenerationMode);
            if (locatorRequired && string.IsNullOrWhiteSpace(locatorExpression))
            {
                code.AppendLine($"  test.skip(true, 'No recorded locator for {CodeGenerationUtilities.EscapeSingleQuoted(step.LocatorKey)}.');");
                return;
            }

            if (_options.IncludeLocatorComments && !string.IsNullOrWhiteSpace(step.LocatorKey) && locatorRequired)
            {
                code.AppendLine($"  // locator: {CodeGenerationUtilities.EscapeComment(step.LocatorKey)}");
            }

            switch (step.ActionType)
            {
                case IntentActionType.Fill:
                    code.AppendLine($"  await {locatorExpression}.fill('{CodeGenerationUtilities.EscapeSingleQuoted(step.Value)}');");
                    break;
                case IntentActionType.Select:
                    code.AppendLine($"  await {locatorExpression}.selectOption('{CodeGenerationUtilities.EscapeSingleQuoted(step.Value)}');");
                    break;
                case IntentActionType.Click:
                    code.AppendLine($"  await {locatorExpression}.click();");
                    break;
                case IntentActionType.Assert:
                    AssertionCodeEmitter.EmitPlaywrightTypeScript(step, locatorExpression, _options.AssertGenerationMode, code);
                    break;
                default:
                    code.AppendLine($"  test.skip(true, 'Unsupported intent action {step.ActionType}.');");
                    break;
            }
        }

        private static string LocatorExpression(IntentElementCandidate? candidate, UiElementInfo? snapshot)
        {
            var suggestion = candidate?.LocatorSuggestions.FirstOrDefault();
            var converted = ConvertCSharpLocator(suggestion?.Expression);
            if (!string.IsNullOrWhiteSpace(converted))
            {
                return converted;
            }

            if (!string.IsNullOrWhiteSpace(snapshot?.AutomationId))
            {
                return $"page.getByTestId('{CodeGenerationUtilities.EscapeSingleQuoted(snapshot!.AutomationId)}')";
            }

            if (!string.IsNullOrWhiteSpace(snapshot?.Name))
            {
                return $"page.getByText('{CodeGenerationUtilities.EscapeSingleQuoted(snapshot!.Name)}')";
            }

            return "";
        }

        private static string ConvertCSharpLocator(string? expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return "";
            }

            const string testIdPrefix = "page.GetByTestId(\"";
            if (expression!.StartsWith(testIdPrefix, StringComparison.Ordinal) && expression.EndsWith("\")", StringComparison.Ordinal))
            {
                return "page.getByTestId('" + CodeGenerationUtilities.EscapeSingleQuoted(expression.Substring(testIdPrefix.Length, expression.Length - testIdPrefix.Length - 2)) + "')";
            }

            const string locatorPrefix = "page.Locator(\"";
            if (expression.StartsWith(locatorPrefix, StringComparison.Ordinal) && expression.EndsWith("\")", StringComparison.Ordinal))
            {
                return "page.locator('" + CodeGenerationUtilities.EscapeSingleQuoted(expression.Substring(locatorPrefix.Length, expression.Length - locatorPrefix.Length - 2)) + "')";
            }

            var role = ExtractBetween(expression, "page.GetByRole(AriaRole.", ", new()");
            var name = ExtractBetween(expression, "Name = \"", "\"");
            if (!string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(name))
            {
                return $"page.getByRole('{ToTypeScriptRole(role)}', {{ name: '{CodeGenerationUtilities.EscapeSingleQuoted(name)}' }})";
            }

            return "";
        }

        private static string ToTypeScriptRole(string role)
        {
            return string.Concat(role.Select((ch, index) => index == 0 ? char.ToLowerInvariant(ch) : ch));
        }

        private static string ExtractBetween(string value, string prefix, string suffix)
        {
            var start = value.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return "";
            }

            start += prefix.Length;
            var end = value.IndexOf(suffix, start, StringComparison.Ordinal);
            return end < 0 ? "" : value.Substring(start, end - start);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return "";
        }
    }
}
