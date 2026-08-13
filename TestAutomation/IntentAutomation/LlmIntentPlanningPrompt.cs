using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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
        public static string Build(IntentPlanningRequest request)
        {
            var dataJson = JsonSerializer.Serialize(
                NormalizeData(request.TestData),
                new JsonSerializerOptions { WriteIndented = true });

            var urlLine = string.IsNullOrWhiteSpace(request.TargetUrl)
                ? ""
                : $"\nTarget URL: {request.TargetUrl.Trim()}\n";

            return
$@"You are planning an automated UI test from a plain-language goal.
Goal: {request.Goal.Trim()}
{urlLine}
Available test data (field name -> value):
{dataJson}

Break the goal down into an ordered list of steps needed to accomplish it end to end,
using only these action types: Navigate, Fill, Click, Select, Assert. Use Navigate only
if a target URL is given. Use Fill/Select for each relevant piece of test data (Select
for a dropdown/choice-like field, Fill otherwise). Add a final Click step for whatever
submits/saves/completes the goal, and a final Assert step describing how to verify it
succeeded, when the goal implies a checkable outcome. For Assert steps, also include
""assertionKind"" (Visible, NotVisible, TextEquals, TextContains, ValueEquals, UrlEquals, UrlContains)
and ""expectedValue"" (the expected text, value, or URL, empty for Visible/NotVisible).

Respond with ONLY a single JSON object, no markdown fences, no other text, in this shape:
{{""steps"": [
  {{""actionType"": ""Navigate|Fill|Click|Select|Assert"", ""targetDescription"": ""<short human description of the target element>"", ""value"": ""<value to enter/select, empty for Click/Assert>"", ""testIntent"": ""<one sentence: why this step exists>"", ""expectedOutcome"": ""<one sentence: what should be true after this step>"", ""locatorKey"": ""<short stable dotted key, e.g. Field.Email or Action.PrimarySubmit>"", ""assertionKind"": ""<Visible|NotVisible|TextEquals|TextContains|ValueEquals|UrlEquals|UrlContains>"", ""expectedValue"": ""<expected value for assertion>""}}
]}}";
        }

        public static IntentScenario ParseScenario(string rawText, IntentPlanningRequest request)
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

            var order = 1;
            foreach (var stepElement in stepsElement.EnumerateArray())
            {
                scenario.Steps.Add(ParseStep(stepElement, order++));
            }

            if (scenario.Steps.Count == 0)
            {
                throw new FormatException("Model response contained an empty \"steps\" array.");
            }

            return scenario;
        }

        private static IntentStep ParseStep(JsonElement element, int order)
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
                Value = GetString(element, "value") ?? "",
                TestIntent = testIntent,
                ExpectedOutcome = expectedOutcome,
                LocatorKey = GetString(element, "locatorKey") ?? "",
                AssertionKind = assertionKind,
                ExpectedValue = expectedValue,
            };
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;
        }

        private static IEnumerable<KeyValuePair<string, string>> NormalizeData(IDictionary<string, string>? data)
        {
            return (data ?? new Dictionary<string, string>())
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase);
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
