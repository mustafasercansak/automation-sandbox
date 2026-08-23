using System.Text.Json;
using UiModel;
namespace LlmHealing
{
    // Shared prompt/response format so every provider is asked the same question the same
    // way - that's what makes the evaluator's comparison meaningful. Public so it's
    // independently unit-testable from other assemblies.
    //
    // The model is given a bounded shortlist of pre-scored candidates (not the full UI tree)
    // and asked to pick one by its opaque candidateId - not by AutomationId. This bounds
    // prompt/token cost on large trees and is harder to hallucinate past: the model can only
    // choose from a closed, enumerated set it was actually shown, rather than returning any
    // string it likes.

    public static class LlmHealingPrompt
    {
        public static string Build(UiElementInfo expected, IReadOnlyList<CandidateScore> candidates, string? platform = null, Func<string, string>? textSanitizer = null)
        {
            var effectivePlatform = !string.IsNullOrWhiteSpace(platform)
                ? platform!
                : InferPlatformFallback(expected);

            // The stale AutomationId is never shown to the model, not just discouraged in the
            // instructions: telling a model to "ignore" a field doesn't reliably stop it from
            // being semantically anchored by it (e.g. "txtEmailAddress" nudging it toward any
            // candidate that looks vaguely email-related by Name, even when that's wrong) -
            // observed live against Gemini, where a WinForms accessibility quirk (a control's
            // UIA Name inherited from the wrong neighboring label) combined with that semantic
            // leakage to pick an incorrect candidate at high confidence. Redacting the field
            // entirely removes the leakage vector rather than relying on instruction-following.
            var expectedForPrompt = new UiElementInfo
            {
                ControlType = expected.ControlType,
                Name = Sanitize(expected.Name, textSanitizer) ?? "",
                ClassName = Sanitize(expected.ClassName, textSanitizer) ?? "",
                BoundingRectangle = expected.BoundingRectangle,
                ParentControlType = expected.ParentControlType,
                SiblingIndex = expected.SiblingIndex,
                SiblingCount = expected.SiblingCount,
                TestIntent = Sanitize(expected.TestIntent, textSanitizer) ?? "",
            };
            var expectedJson = UiTreeSerializer.ToJson(expectedForPrompt);
            var candidatesJson = JsonSerializer.Serialize(
                candidates.Select(c => ToPromptCandidate(c, textSanitizer)),
                new JsonSerializerOptions { WriteIndented = true });

            var intentText = Sanitize(expected.TestIntent, textSanitizer);
            var intentHeader = string.IsNullOrWhiteSpace(intentText)
                ? ""
                : $"\nTEST INTENT (Goal of this test step):\n\"{intentText}\"\nUse this intent to pick the candidate that best fulfills this intended action even if names or labels were refactored.\n";

            return
$@"You are diagnosing a broken UI test locator for a {effectivePlatform} application.
A locator that used to work no longer finds its element - most likely because the
element's AutomationId changed (e.g. after a refactor). Its old AutomationId is
deliberately omitted below since it's stale and irrelevant to matching. Below is the
last known structural snapshot of that element, and a shortlist of the current tree's
candidates that are structurally closest to it, each with a heuristic score and its
component breakdown.
{intentHeader}
Last known element (structural fields only - do not try to infer or guess its old
AutomationId from context, it isn't shown for a reason):
{expectedJson}

Candidates (ordered by heuristic score, best first):
{candidatesJson}

Pick the candidate that is structurally the same control: same ControlType, similar
parent/sibling context, similar screen position, similar Name. Respond with the
candidateId of your pick, not its AutomationId.
Respond with ONLY a single JSON object, no markdown fences, no other text:
{{""candidateId"": ""<candidateId of your best match, or empty string if none fits>"", ""confidence"": <number 0.0-1.0>, ""reasoning"": ""<one sentence>""}}";
        }

        // Fallback platform inference for callers that do not explicitly pass a platform.
        // As documented in issue #24, light-DOM web elements (<button>, <input>) are indistinguishable
        // from desktop controls by ControlType/ClassName alone because WebElementMapper normalizes them
        // to desktop ControlTypes (Button, Edit) without adding a scope tag. Therefore, callers should
        // always pass the platform explicitly whenever known.
        public static string InferPlatformFallback(UiElementInfo? expected)
        {
            if (expected == null)
            {
                return "windows-uia";
            }

            var className = expected.ClassName ?? "";
            if (className.Contains("[shadow-dom]") || className.Contains("[iframe]"))
            {
                return "web-playwright";
            }

            if (!string.IsNullOrWhiteSpace(expected.ControlType) && char.IsLower(expected.ControlType[0]))
            {
                return "web-playwright";
            }

            return "windows-uia";
        }

        public static (string? CandidateId, double Confidence, string Reasoning) ParseResponse(string rawText)
        {
            var json = FindFirstResponseJsonObject(rawText);
            if (json is null)
            {
                // Provider diagnostics append the bounded HTTP response body. Repeating the
                // unbounded model text here would bypass that limit and duplicate the payload.
                throw new FormatException("No JSON object found in model response.");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var candidateId = root.TryGetProperty("candidateId", out var idProp) ? idProp.GetString() : null;
            var confidence = root.TryGetProperty("confidence", out var confProp) ? confProp.GetDouble() : 0.0;
            var reasoning = root.TryGetProperty("reasoning", out var reasonProp) ? reasonProp.GetString() ?? "" : "";
            return (string.IsNullOrWhiteSpace(candidateId) ? null : candidateId, confidence, reasoning);
        }

        private static object ToPromptCandidate(CandidateScore c, Func<string, string>? textSanitizer) => new
        {
            candidateId = c.CandidateId,
            automationId = Sanitize(c.Candidate.AutomationId, textSanitizer),
            controlType = c.Candidate.ControlType,
            name = Sanitize(c.Candidate.Name, textSanitizer),
            className = Sanitize(c.Candidate.ClassName, textSanitizer),
            score = Math.Round(c.TotalScore, 2),
            components = new
            {
                // Missing signals stay null in the prompt - the LLM should see "no evidence"
                // rather than a fabricated 0.0/1.0.
                controlTypeScore = RoundOrNull(c.Components.ControlTypeScore),
                parentControlTypeScore = RoundOrNull(c.Components.ParentControlTypeScore),
                siblingPositionScore = RoundOrNull(c.Components.SiblingPositionScore),
                nameScore = RoundOrNull(c.Components.NameScore),
                positionScore = RoundOrNull(c.Components.PositionScore),
            },
        };

        private static string? Sanitize(string? s, Func<string, string>? textSanitizer)
        {
            if (s == null)
            {
                return null;
            }

            return textSanitizer != null ? textSanitizer(s) : s;
        }

        private static double? RoundOrNull(double? value) => value.HasValue ? Math.Round(value.Value, 2) : (double?)null;

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
                    using var doc = JsonDocument.Parse(candidate);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && (doc.RootElement.TryGetProperty("candidateId", out _)
                            || doc.RootElement.TryGetProperty("confidence", out _)))
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
