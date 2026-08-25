using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UiModel;

namespace IntentAutomation
{
    // Prompt/response contract for LlmIntentPlanner, kept in its own type (mirroring
    // LlmHealing.LlmHealingPrompt) so it's independently unit-testable. Unlike the
    // healing prompt, there is no closed candidate set to constrain the model against -
    // ParseScenario is the guard here: any step with an unparseable ActionType or an
    // empty TargetDescription fails the whole response, which LlmIntentPlanner treats
    // the same as an HTTP failure and degrades to DeterministicIntentPlanner for.

    public static class LlmIntentPlanningPrompt
    {
        public static string Build(IntentPlanningRequest request, Func<string, string>? textSanitizer = null)
        {
            var dataJson = JsonSerializer.Serialize(
                NormalizeData(request.TestData, textSanitizer),
                new JsonSerializerOptions { WriteIndented = true });

            var sanitizedUrl = Sanitize(request.TargetUrl, textSanitizer);
            var urlLine = string.IsNullOrWhiteSpace(sanitizedUrl)
                ? ""
                : $"\nTarget URL: {sanitizedUrl!.Trim()}\n";

            var sanitizedGoal = Sanitize(request.Goal, textSanitizer)?.Trim() ?? "";

            return
$@"You are planning an automated UI test from a plain-language goal.
Goal: {sanitizedGoal}
{urlLine}
Available test data (field name -> value):
{dataJson}

Break the goal down into an ordered list of steps needed to accomplish it end to end,
using only these action types: Navigate, Fill, Click, Select, Check, Uncheck, Hover,
UploadFile, PressKey, Wait, Assert. Use Navigate only if a target URL is given. Use
Fill/Select/UploadFile for each relevant piece of test data (UploadFile for a file path,
Select for a dropdown or choice-like field, Fill otherwise). Use Check/Uncheck for
checkboxes and radio buttons (a radio button can only be checked, never unchecked);
Select is only for real dropdown/select/combobox elements. Use PressKey for a named
keyboard key, Hover for a
hover-triggered interaction, and Wait to poll for a target element to become visible;
for Wait, put a timeout in milliseconds in value or leave it empty for 5000. Add a final
Click step for whatever submits/saves/completes the goal, and a final Assert step
describing how to verify it succeeded, when the goal implies a checkable outcome. For Assert steps, also include
""assertionKind"" (Visible, NotVisible, TextEquals, TextContains, ValueEquals, UrlEquals, UrlContains)
and ""expectedValue"" (the expected text, value, or URL, empty for Visible/NotVisible).

Respond with ONLY a single JSON object, no markdown fences, no other text, in this shape:
{{""steps"": [
  {{""actionType"": ""Navigate|Fill|Click|Select|Check|Uncheck|Hover|UploadFile|PressKey|Wait|Assert"", ""targetDescription"": ""<short human description of the target element>"", ""value"": ""<input/file/key/timeout value, empty when unused>"", ""testIntent"": ""<one sentence: why this step exists>"", ""expectedOutcome"": ""<one sentence: what should be true after this step>"", ""assertionKind"": ""<Visible|NotVisible|TextEquals|TextContains|ValueEquals|UrlEquals|UrlContains>"", ""expectedValue"": ""<expected value for assertion>""}}
]}}";
        }

        public static IntentScenario ParseScenario(string rawText, IntentPlanningRequest request, Func<string, string>? textSanitizer = null)
        {
            var json = FindFirstResponseJsonObject(rawText);
            if (json is null)
            {
                throw new FormatException($"No JSON object found in model response: {rawText}");
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Model response did not contain a \"steps\" array.");
            }

            var scenario = new IntentScenario
            {
                Name = string.IsNullOrWhiteSpace(request.Name) ? BuildScenarioName(request.Goal) : request.Name,
                Goal = request.Goal.Trim(),
                TargetUrl = request.TargetUrl?.Trim() ?? "",
            };

            // The model only ever sees the sanitized rendering of TestData/TargetUrl (see Build
            // above), so a step whose value the model copied verbatim from the prompt comes back
            // as the redaction token, not the real data. Map those tokens back to the original
            // values here so generated Fill/Navigate/etc. steps use real data, not "[REDACTED_*]"
            // literals. Only unambiguous restorations are applied (see BuildRestoreMap).
            var restoreMap = BuildRestoreMap(request.TestData, request.TargetUrl, textSanitizer);

            var order = 1;
            foreach (var stepElement in stepsElement.EnumerateArray())
            {
                scenario.Steps.Add(ParseStep(stepElement, order++, restoreMap));
            }

            if (scenario.Steps.Count == 0)
            {
                throw new FormatException("Model response contained an empty \"steps\" array.");
            }

            return scenario;
        }

        private static IReadOnlyDictionary<string, string> BuildRestoreMap(
            IDictionary<string, string>? testData,
            string? targetUrl,
            Func<string, string>? textSanitizer)
        {
            var sanitizer = textSanitizer ?? SensitiveDataSanitizer.Default;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);

            void Track(string? original)
            {
                if (string.IsNullOrEmpty(original))
                {
                    return;
                }

                var sanitized = sanitizer(original!) ?? "";
                if (sanitized == original || ambiguous.Contains(sanitized))
                {
                    return;
                }

                if (map.TryGetValue(sanitized, out var existing) && existing != original)
                {
                    // Two different original values redact to the same token: restoring either
                    // one would be a guess, so leave the token in place rather than risk
                    // substituting the wrong secret back into the generated test.
                    map.Remove(sanitized);
                    ambiguous.Add(sanitized);
                    return;
                }

                map[sanitized] = original!;
            }

            foreach (var pair in testData ?? new Dictionary<string, string>())
            {
                Track(pair.Value);
            }

            Track(targetUrl);

            return map;
        }

        private static IntentStep ParseStep(JsonElement element, int order, IReadOnlyDictionary<string, string> restoreMap)
        {
            var actionTypeText = GetString(element, "actionType");
            if (string.IsNullOrWhiteSpace(actionTypeText) || !Enum.TryParse<IntentActionType>(actionTypeText, ignoreCase: true, out var actionType) || actionType == IntentActionType.Unknown)
            {
                throw new FormatException($"Step {order} has an invalid actionType: \"{actionTypeText}\".");
            }

            var targetDescription = GetString(element, "targetDescription");
            if (string.IsNullOrWhiteSpace(targetDescription))
            {
                throw new FormatException($"Step {order} is missing a targetDescription.");
            }

            var expectedOutcome = GetString(element, "expectedOutcome") ?? "";
            var testIntent = GetString(element, "testIntent") ?? "";
            var assertionKindText = GetString(element, "assertionKind");
            var expectedValue = GetString(element, "expectedValue") ?? "";
            var assertionKind = AssertionKind.None;

            if (!string.IsNullOrWhiteSpace(assertionKindText) && Enum.TryParse<AssertionKind>(assertionKindText, ignoreCase: true, out var parsedKind))
            {
                assertionKind = parsedKind;
            }

            if (actionType == IntentActionType.Assert && assertionKind == AssertionKind.None)
            {
                var (derivedKind, derivedValue) = DeterministicIntentPlanner.DeriveAssertion(expectedOutcome, testIntent);
                assertionKind = derivedKind != AssertionKind.None ? derivedKind : AssertionKind.Visible;
                if (string.IsNullOrEmpty(expectedValue))
                {
                    expectedValue = derivedValue;
                }
            }

            return new IntentStep
            {
                Order = order,
                ActionType = actionType,
                TargetDescription = targetDescription!,
                Value = RestoreOriginal(GetString(element, "value") ?? "", restoreMap),
                TestIntent = testIntent,
                ExpectedOutcome = expectedOutcome,
                AssertionKind = assertionKind,
                ExpectedValue = RestoreOriginal(expectedValue, restoreMap),
            };
        }

        private static string RestoreOriginal(string value, IReadOnlyDictionary<string, string> restoreMap)
        {
            return restoreMap.TryGetValue(value, out var original) ? original : value;
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;
        }

        private static IEnumerable<KeyValuePair<string, string>> NormalizeData(IDictionary<string, string>? data, Func<string, string>? textSanitizer)
        {
            return (data ?? new Dictionary<string, string>())
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Select(pair => new KeyValuePair<string, string>(
                    Sanitize(pair.Key, textSanitizer) ?? "",
                    Sanitize(pair.Value, textSanitizer) ?? ""))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase);
        }

        private static string? Sanitize(string? s, Func<string, string>? textSanitizer)
        {
            if (s == null)
            {
                return null;
            }

            var sanitizer = textSanitizer ?? SensitiveDataSanitizer.Default;
            return sanitizer(s);
        }

        private static string BuildScenarioName(string goal)
        {
            var trimmed = goal.Trim();
            return trimmed.Length <= 80 ? trimmed : trimmed.Substring(0, 80).Trim();
        }

        private static string? FindFirstResponseJsonObject(string rawText)
        {
            for (var start = 0; start < rawText.Length; start++)
            {
                if (rawText[start] != '{')
                {
                    continue;
                }

                var end = FindMatchingObjectEnd(rawText, start);
                if (end < 0)
                {
                    continue;
                }

                var candidate = rawText.Substring(start, end - start + 1);
                try
                {
                    using var candidateDoc = JsonDocument.Parse(candidate);
                    if (candidateDoc.RootElement.ValueKind == JsonValueKind.Object && candidateDoc.RootElement.TryGetProperty("steps", out _))
                    {
                        return candidate;
                    }
                }

                catch (JsonException)
                {
                }

                start = end;
            }

            return null;
        }

        private static int FindMatchingObjectEnd(string text, int start)
        {
            var depth = 0;
            var inString = false;
            var escaping = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escaping)
                    {
                        escaping = false;
                    }

                    else if (c == '\\')
                    {
                        escaping = true;
                    }

                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }

                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }
    }
}
