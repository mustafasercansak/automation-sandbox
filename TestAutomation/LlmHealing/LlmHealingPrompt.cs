using System;
using System.Text.Json;
using UiModel;

namespace LlmHealing
{
    // Shared prompt/response format so every provider is asked the same question
    // the same way - that's what makes the evaluator's comparison meaningful.
    internal static class LlmHealingPrompt
    {
        public static string Build(UiElementInfo expected, UiElementInfo currentTree)
        {
            var expectedJson = UiTreeSerializer.ToJson(expected);
            var currentTreeJson = UiTreeSerializer.ToJson(currentTree);

            return
$@"You are diagnosing a broken UI test locator for a Windows desktop application.

A locator that used to work no longer finds its element - most likely because the
element's AutomationId changed (e.g. after a refactor). Below is the last known
structural snapshot of that element, and the current full UI tree of the same window.

Last known element (its AutomationId is stale/unreliable - do not use it to match):
{expectedJson}

Current UI tree:
{currentTreeJson}

Find the element in the current tree that is structurally the same control: same
ControlType, similar parent/sibling context, similar screen position, similar Name.
Ignore AutomationId entirely when deciding which node matches - that's the value
that's expected to have changed.

Respond with ONLY a single JSON object, no markdown fences, no other text:
{{""automationId"": ""<AutomationId of your best match, or empty string if none fits>"", ""confidence"": <number 0.0-1.0>, ""reasoning"": ""<one sentence>""}}";
        }

        public static (string? AutomationId, double Confidence, string Reasoning) ParseResponse(string rawText)
        {
            var jsonStart = rawText.IndexOf('{');
            var jsonEnd = rawText.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                throw new FormatException($"No JSON object found in model response: {rawText}");
            }

            var json = rawText.Substring(jsonStart, jsonEnd - jsonStart + 1);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var automationId = root.TryGetProperty("automationId", out var idProp) ? idProp.GetString() : null;
            var confidence = root.TryGetProperty("confidence", out var confProp) ? confProp.GetDouble() : 0.0;
            var reasoning = root.TryGetProperty("reasoning", out var reasonProp) ? reasonProp.GetString() ?? "" : "";

            return (string.IsNullOrWhiteSpace(automationId) ? null : automationId, confidence, reasoning);
        }
    }
}
