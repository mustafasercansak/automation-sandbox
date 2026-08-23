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
            code.AppendLine("import { test, expect } from '@playwright/test';");
            code.AppendLine();
            code.AppendLine($"test('{CodeGenerationUtilities.EscapeSingleQuoted(title)}', async ({{ page }}) => {{");

            foreach (var step in scenario.Steps.OrderBy(step => step.Order))
            {
                AppendStep(code, step, recordingsByStep, recordingsByOrder, recordingsByKey);
            }

            code.AppendLine("});");
            return code.ToString();
        }

        private void AppendStep(
            StringBuilder code,
            IntentStep step,
            IReadOnlyDictionary<IntentStep, IntentLocatorRecordingResult> recordingsByStep,
            IReadOnlyDictionary<int, IntentLocatorRecordingResult> recordingsByOrder,
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

            if (!recordingsByStep.TryGetValue(step, out var recording))
            {
                if (!recordingsByOrder.TryGetValue(step.Order, out recording))
                {
                    if (!string.IsNullOrWhiteSpace(step.TargetDescription) && recordingsByKey.TryGetValue(step.TargetDescription, out recording))
                    {
                    }
                    else
                    {
                        var synthesizedKey = IntentLocatorKeySynthesizer.Synthesize(step);
                        if (!string.IsNullOrWhiteSpace(synthesizedKey))
                        {
                            recordingsByKey.TryGetValue(synthesizedKey, out recording);
                        }
                    }
                }
            }

            var locatorExpression = LocatorExpression(recording?.Candidate, recording?.Record?.Snapshot);
            var locatorRequired = step.ActionType != IntentActionType.Assert || AssertionCodeEmitter.IsLocatorRequired(step.AssertionKind, _options.AssertGenerationMode);
            var locatorKey = recording?.LocatorKey ?? "";
            if (locatorRequired && string.IsNullOrWhiteSpace(locatorExpression))
            {
                var identifier = !string.IsNullOrWhiteSpace(locatorKey) ? locatorKey : step.TargetDescription;
                code.AppendLine($"  test.skip(true, 'No recorded locator for {CodeGenerationUtilities.EscapeSingleQuoted(identifier)}.');");
                return;
            }

            if (_options.IncludeLocatorComments && !string.IsNullOrWhiteSpace(locatorKey) && locatorRequired)
            {
                code.AppendLine($"  // locator: {CodeGenerationUtilities.EscapeComment(locatorKey)}");
            }

            switch (step.ActionType)
            {
                case IntentActionType.Fill:
                    code.AppendLine($"  await {locatorExpression}.fill('{CodeGenerationUtilities.EscapeSingleQuoted(step.Value)}');");
                    break;
                case IntentActionType.Select:
                    code.AppendLine($"  await {locatorExpression}.selectOption('{CodeGenerationUtilities.EscapeSingleQuoted(step.Value)}');");
                    break;
                case IntentActionType.Check:
                    code.AppendLine($"  await {locatorExpression}.check();");
                    break;
                case IntentActionType.Uncheck:
                    code.AppendLine($"  await {locatorExpression}.uncheck();");
                    break;
                case IntentActionType.Click:
                    code.AppendLine($"  await {locatorExpression}.click();");
                    break;
                case IntentActionType.Hover:
                    code.AppendLine($"  await {locatorExpression}.hover();");
                    break;
                case IntentActionType.UploadFile:
                    code.AppendLine($"  await {locatorExpression}.setInputFiles('{CodeGenerationUtilities.EscapeSingleQuoted(step.Value)}');");
                    break;
                case IntentActionType.PressKey:
                    code.AppendLine($"  await {locatorExpression}.press('{CodeGenerationUtilities.EscapeSingleQuoted(step.Value)}');");
                    break;
                case IntentActionType.Wait:
                    code.AppendLine($"  await {locatorExpression}.waitFor({{ state: 'visible', timeout: {CodeGenerationUtilities.WaitTimeoutMilliseconds(step.Value)} }});");
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

            var trimmed = expression!.Trim();
            if (!trimmed.StartsWith("page.", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            var remaining = trimmed.Substring(5);
            var tsBuilder = new StringBuilder("page");

            const string framePrefix = "FrameLocator(\"";
            while (remaining.StartsWith(framePrefix, StringComparison.Ordinal))
            {
                var quoteEnd = remaining.IndexOf("\")", framePrefix.Length, StringComparison.Ordinal);
                if (quoteEnd < 0)
                {
                    return "";
                }

                var rawSelector = remaining.Substring(framePrefix.Length, quoteEnd - framePrefix.Length);
                var unescaped = UnescapeCSharpString(rawSelector);
                tsBuilder.Append($".frameLocator('{CodeGenerationUtilities.EscapeSingleQuoted(unescaped)}')");

                remaining = remaining.Substring(quoteEnd + 2);
                if (remaining.StartsWith(".", StringComparison.Ordinal))
                {
                    remaining = remaining.Substring(1);
                }
            }

            const string testIdPrefix = "GetByTestId(\"";
            if (remaining.StartsWith(testIdPrefix, StringComparison.Ordinal) && remaining.EndsWith("\")", StringComparison.Ordinal))
            {
                var testId = remaining.Substring(testIdPrefix.Length, remaining.Length - testIdPrefix.Length - 2);
                tsBuilder.Append($".getByTestId('{CodeGenerationUtilities.EscapeSingleQuoted(UnescapeCSharpString(testId))}')");
                return tsBuilder.ToString();
            }

            const string locatorPrefix = "Locator(\"";
            if (remaining.StartsWith(locatorPrefix, StringComparison.Ordinal) && remaining.EndsWith("\")", StringComparison.Ordinal))
            {
                var selector = remaining.Substring(locatorPrefix.Length, remaining.Length - locatorPrefix.Length - 2);
                tsBuilder.Append($".locator('{CodeGenerationUtilities.EscapeSingleQuoted(UnescapeCSharpString(selector))}')");
                return tsBuilder.ToString();
            }

            var role = ExtractBetween(remaining, "GetByRole(AriaRole.", ", new()");
            var name = ExtractCSharpStringLiteral(remaining, "Name = \"");
            if (!string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(name))
            {
                tsBuilder.Append($".getByRole('{ToTypeScriptRole(role)}', {{ name: '{CodeGenerationUtilities.EscapeSingleQuoted(name)}' }})");
                return tsBuilder.ToString();
            }

            return "";
        }

        private static string UnescapeCSharpString(string value)
        {
            return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static string ToTypeScriptRole(string role)
        {
            var normalized = role.Replace("-", "").Replace("_", "");
            return normalized.Equals("textbox", StringComparison.OrdinalIgnoreCase)
                ? "textbox"
                : char.ToLowerInvariant(normalized[0]) + normalized.Substring(1).ToLowerInvariant();
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

        private static string ExtractCSharpStringLiteral(string value, string prefix)
        {
            var start = value.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return "";
            }

            start += prefix.Length;
            var result = new StringBuilder();
            for (var index = start; index < value.Length; index++)
            {
                var current = value[index];
                if (current == '"')
                {
                    return result.ToString();
                }

                if (current != '\\')
                {
                    result.Append(current);
                    continue;
                }

                if (++index >= value.Length)
                {
                    return "";
                }

                var escaped = value[index];
                switch (escaped)
                {
                    case '\\':
                        result.Append('\\');
                        break;
                    case '"':
                        result.Append('"');
                        break;
                    case 'r':
                        result.Append('\r');
                        break;
                    case 'n':
                        result.Append('\n');
                        break;
                    case 't':
                        result.Append('\t');
                        break;
                    default:
                        // Preserve unknown sequences instead of silently dropping a selector character.
                        result.Append('\\').Append(escaped);
                        break;
                }
            }

            return "";
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
