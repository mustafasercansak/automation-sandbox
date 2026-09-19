using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using AutomationSandbox.UiModel;

namespace AutomationSandbox.ContentAnalysis
{
    // Prompt construction and tolerant response parsing for ClaudeContentAnalysisProvider, kept
    // separate so both halves of the round-trip can be unit tested without an HTTP call - the
    // same split IntentAutomation uses between LlmIntentPlanner and LlmIntentPlanningPrompt.
    internal static class ContentAnalysisPrompt
    {
        public static string Build(IReadOnlyList<ContentPassage> passages, Func<string, string>? textSanitizer = null)
        {
            var sanitize = textSanitizer ?? SensitiveDataSanitizer.Default;
            var sb = new StringBuilder();
            sb.AppendLine("You are reviewing text captured from a live web page for genuine content-quality " +
                "defects only: spelling mistakes, grammar errors, or wording whose meaning is unclear or " +
                "broken. Do not flag stylistic choices, brand names, informal tone, or anything that is " +
                "merely short or terse.");
            sb.AppendLine();
            sb.AppendLine("Passages (selector: text):");
            foreach (var passage in passages)
            {
                sb.AppendLine($"- {passage.CssSelector}: \"{sanitize(passage.Text)}\"");
            }

            sb.AppendLine();
            sb.AppendLine("Respond with ONLY a single JSON array, no markdown fences, no other text. Each " +
                "element: {\"selector\": \"<exact selector from the list above>\", " +
                "\"issueType\": \"Spelling\"|\"Grammar\"|\"Meaning\", \"message\": \"<short description>\"}. " +
                "Return [] if there are no genuine issues.");
            return sb.ToString();
        }

        public static IReadOnlyList<ContentIssue> ParseIssues(string rawText, IReadOnlyList<ContentPassage> passages)
        {
            var json = ExtractJsonArray(rawText);
            if (json is null)
            {
                return Array.Empty<ContentIssue>();
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ContentIssue>();
            }

            // Built with TryAdd rather than ToDictionary: two passages sharing a selector must not
            // crash parsing of an otherwise-usable response, an invariant this code must hold even
            // though ContentAnalyzer.ExtractPassages does not currently produce such duplicates.
            var textBySelector = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var passage in passages)
            {
                // Dictionary<K,V>.TryAdd isn't available on netstandard2.0 (this project's target
                // alongside net8.0), hence the explicit ContainsKey check rather than that helper.
                if (!textBySelector.ContainsKey(passage.CssSelector))
                {
                    textBySelector.Add(passage.CssSelector, passage.Text);
                }
            }
            var issues = new List<ContentIssue>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var selector = element.TryGetProperty("selector", out var selectorProp) ? selectorProp.GetString() : null;
                var issueType = element.TryGetProperty("issueType", out var issueTypeProp) ? issueTypeProp.GetString() : null;
                var message = element.TryGetProperty("message", out var messageProp) ? messageProp.GetString() : null;

                // Only accept an issue on a selector this call actually sent - guards against a
                // hallucinated selector pointing at content that was never in the prompt.
                // The null-forgiving operators below are safe given the IsNullOrEmpty checks just
                // above - string.IsNullOrEmpty's [NotNullWhen] annotation isn't present on the
                // netstandard2.0 reference assemblies this project also targets, so the compiler
                // can't narrow it there the way it does on net8.0.
                if (string.IsNullOrEmpty(selector) || !textBySelector.TryGetValue(selector!, out var text) ||
                    string.IsNullOrEmpty(issueType) || string.IsNullOrEmpty(message))
                {
                    continue;
                }

                issues.Add(new ContentIssue(selector!, text, issueType!, message!, ContentIssueSource.Llm));
            }

            return issues;
        }

        private static string? ExtractJsonArray(string rawText)
        {
            var start = rawText.IndexOf('[');
            var end = rawText.LastIndexOf(']');
            return start >= 0 && end > start ? rawText.Substring(start, end - start + 1) : null;
        }
    }
}
