using System;
using System.Collections.Generic;
using System.Linq;

namespace IntentAutomation
{
    public sealed class DeterministicIntentPlanner : IIntentPlanner
    {
        public IntentPlanningResult Plan(IntentPlanningRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Goal))
            {
                throw new ArgumentException("Goal must not be empty.", nameof(request));
            }

            var result = new IntentPlanningResult
            {
                Scenario = new IntentScenario
                {
                    Name = string.IsNullOrWhiteSpace(request.Name) ? BuildScenarioName(request.Goal) : request.Name,
                    Goal = request.Goal.Trim(),
                    TargetUrl = request.TargetUrl?.Trim() ?? "",
                }
            };

            var order = 1;
            if (!string.IsNullOrWhiteSpace(result.Scenario.TargetUrl))
            {
                result.Scenario.Steps.Add(new IntentStep
                {
                    Order = order++,
                    ActionType = IntentActionType.Navigate,
                    TargetDescription = "target page",
                    Value = result.Scenario.TargetUrl,
                    TestIntent = "Navigate to the target page for: " + result.Scenario.Goal,
                    ExpectedOutcome = "The target page is loaded",
                    LocatorKey = "Navigation.TargetPage",
                });
            }

            foreach (var pair in NormalizeData(request.TestData))
            {
                result.Scenario.Steps.Add(CreateDataEntryStep(order++, result.Scenario.Goal, pair.Key, pair.Value));
            }

            var goal = request.Goal.ToLowerInvariant();
            var hasDirectAction = result.Scenario.Steps.Any(step =>
                step.ActionType == IntentActionType.UploadFile ||
                step.ActionType == IntentActionType.PressKey);

            if (IntentTextScoring.Tokens(goal).Contains("hover"))
            {
                AddDirectAction(
                    result.Scenario.Steps,
                    ref order,
                    IntentActionType.Hover,
                    ExtractTargetAfterMarker(result.Scenario.Goal, "hover over", "hover on", "hover"),
                    "",
                    result.Scenario.Goal);
                hasDirectAction = true;
            }

            if (goal.Contains("wait for"))
            {
                AddDirectAction(
                    result.Scenario.Steps,
                    ref order,
                    IntentActionType.Wait,
                    RemoveDurationQualifier(ExtractTargetAfterMarker(result.Scenario.Goal, "wait for")),
                    ExtractWaitTimeoutMilliseconds(result.Scenario.Goal),
                    result.Scenario.Goal);
                hasDirectAction = true;
            }

            if (!result.Scenario.Steps.Any(step => step.ActionType == IntentActionType.PressKey) &&
                ContainsAny(goal, "press ", "keypress", "hit the "))
            {
                var key = ExtractKeyName(result.Scenario.Goal);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    AddDirectAction(
                        result.Scenario.Steps,
                        ref order,
                        IntentActionType.PressKey,
                        ExtractKeyTarget(result.Scenario.Goal),
                        key,
                        result.Scenario.Goal);
                    hasDirectAction = true;
                }
            }

            if (ContainsAny(goal, "corporate", "company", "organization", "organisation"))
            {
                EnsureStep(result.Scenario.Steps, ref order, "record type", IntentActionType.Select, "Corporate", result.Scenario.Goal);
            }

            if (ContainsAny(goal, "save", "submit", "create", "register", "complete"))
            {
                result.Scenario.Steps.Add(new IntentStep
                {
                    Order = order++,
                    ActionType = IntentActionType.Click,
                    TargetDescription = "primary submit or save action",
                    TestIntent = "Click the primary action to complete: " + result.Scenario.Goal,
                    ExpectedOutcome = "The requested workflow is submitted",
                    LocatorKey = "Action.PrimarySubmit",
                });
            }
            else if (!hasDirectAction)
            {
                result.Diagnostics.Add("No submit/save/register verb was detected; planner did not add a final click step.");
                result.RequiresReview = true;
            }

            if (ContainsAny(goal, "record", "row", "grid", "created", "saved", "registration") || ContainsAny(goal, "should be", "equals", "verify", "assert", "total", "price", "amount"))
            {
                var (kind, expectedVal) = DeriveAssertion(result.Scenario.Goal, result.Scenario.Goal);
                if (kind == AssertionKind.None)
                {
                    kind = AssertionKind.Visible;
                }

                result.Scenario.Steps.Add(new IntentStep
                {
                    Order = order,
                    ActionType = IntentActionType.Assert,
                    TargetDescription = "result records or confirmation area",
                    TestIntent = "Verify the expected result for: " + result.Scenario.Goal,
                    ExpectedOutcome = string.IsNullOrEmpty(expectedVal)
                        ? "The created or updated result is visible"
                        : "Result should be " + expectedVal,
                    LocatorKey = "Assert.ResultVisible",
                    AssertionKind = kind,
                    ExpectedValue = expectedVal,
                });
            }

            if (result.Scenario.Steps.Count == 0)
            {
                result.Diagnostics.Add("Planner could not derive any executable steps.");
                result.RequiresReview = true;
            }

            return result;
        }

        public static (AssertionKind Kind, string ExpectedValue) DeriveAssertion(string expectedOutcome, string goal)
        {
            var text = (expectedOutcome ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                text = (goal ?? "").Trim();
            }

            var lower = text.ToLowerInvariant();

            // 1. NotVisible (highest priority)
            if (ContainsAny(lower, "not visible", "hidden", "gone", "disappears", "invisible", "no longer visible"))
            {
                return (AssertionKind.NotVisible, "");
            }

            // 2. UrlEquals / UrlContains (checked before Visible so phrases like
            // "The page is loaded and the URL should be https://..." are classified as URL assertions)
            if (lower.Contains("url") || lower.Contains("navigates to") || lower.Contains("http://") || lower.Contains("https://"))
            {
                var urlMatch = System.Text.RegularExpressions.Regex.Match(text, @"https?://[^\s""',;]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (urlMatch.Success)
                {
                    return lower.Contains("contains")
                        ? (AssertionKind.UrlContains, urlMatch.Value)
                        : (AssertionKind.UrlEquals, urlMatch.Value);
                }
            }

            // 3. Visible & Value-in-Visible
            // Note: covers common success/state phrases: "visible", "appears", "displayed", "is shown", "loaded", "submitted".
            // Rationale for value token taking precedence: when an intent contains both a visibility word and an explicit
            // value expectation (e.g. "The saved total should be $125"), the caller is asserting the final value, so we
            // promote it to TextEquals/TextContains rather than stopping at a generic presence check.
            if (ContainsAny(lower, "visible", "appears", "displayed", "is shown", "loaded", "submitted", "created", "saved", "registration"))
            {
                var extracted = ExtractValueToken(text);
                if (!string.IsNullOrEmpty(extracted))
                {
                    if (lower.Contains("contains"))
                    {
                        return (AssertionKind.TextContains, extracted);
                    }
                    return (AssertionKind.TextEquals, extracted);
                }

                return (AssertionKind.Visible, "");
            }

            // 4. Value / Text patterns with explicit value token
            var valueToken = ExtractValueToken(text);
            if (!string.IsNullOrEmpty(valueToken))
            {
                if (lower.Contains("value") || lower.Contains("input"))
                {
                    return (AssertionKind.ValueEquals, valueToken);
                }
                if (lower.Contains("contains") || lower.Contains("shows") || lower.Contains("including"))
                {
                    return (AssertionKind.TextContains, valueToken);
                }
                return (AssertionKind.TextEquals, valueToken);
            }

            return (AssertionKind.None, "");
        }

        private static string ExtractValueToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            // Pattern A: Quoted string: "..." or '...'
            var quoteMatch = System.Text.RegularExpressions.Regex.Match(text, @"[""']([^""']+)[""']");
            if (quoteMatch.Success && !string.IsNullOrWhiteSpace(quoteMatch.Groups[1].Value))
            {
                return quoteMatch.Groups[1].Value.Trim();
            }

            // Pattern B: Currency or numbers following keywords like "should be", "is", "equals", "total", "amount", "price"
            var currencyMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:should be|is|equals|total|amount|price|value)\s+([$€£¥]?\d+(?:\.\d+)?%?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (currencyMatch.Success && !string.IsNullOrWhiteSpace(currencyMatch.Groups[1].Value))
            {
                return currencyMatch.Groups[1].Value.Trim();
            }

            // Pattern C: "should be / is / equals <phrase>" capturing multi-word values until punctuation/conjunctions
            var shouldBeMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:should be|is|equals)\s+(.+?)(?:[.,;!]|\s*$|\s+(?:and|with|in)\b)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (shouldBeMatch.Success)
            {
                var val = shouldBeMatch.Groups[1].Value.Trim();
                if (val.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || val.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return ""; // Let URL matcher handle URLs
                }

                var lowerVal = val.ToLowerInvariant();
                var genericWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "visible", "hidden", "loaded", "submitted", "created", "saved", "updated", "true", "false", "displayed", "not visible", "a record"
                };
                if (!genericWords.Contains(lowerVal))
                {
                    return val;
                }
            }

            return "";
        }

        private static IntentStep CreateDataEntryStep(int order, string goal, string key, string value)
        {
            var normalizedKey = NormalizeToken(key);
            var keyTokens = IntentTextScoring.Tokens(HumanizeKey(key)).ToList();
            var actionType = IntentActionType.Fill;
            if (keyTokens.Any(token => token == "file" || token == "upload" || token == "attachment"))
            {
                actionType = IntentActionType.UploadFile;
            }
            else if (keyTokens.Any(token => token == "keypress" || token == "keyboard") ||
                     (keyTokens.Contains("key") && LooksLikeKeyName(value)))
            {
                actionType = IntentActionType.PressKey;
            }
            else if (normalizedKey.Contains("type") || normalizedKey.Contains("country") || normalizedKey.Contains("status"))
            {
                actionType = IntentActionType.Select;
            }

            var target = actionType == IntentActionType.PressKey
                ? WithoutActionWords(key, "key", "keypress", "keyboard")
                : HumanizeKey(key);
            var actionName = actionType == IntentActionType.UploadFile
                ? "Upload"
                : actionType == IntentActionType.PressKey
                    ? "Press key on"
                    : actionType == IntentActionType.Select
                        ? "Select"
                        : "Fill";
            var locatorPrefix = actionType == IntentActionType.PressKey ? "Action." : "Field.";

            return new IntentStep
            {
                Order = order,
                ActionType = actionType,
                TargetDescription = target,
                Value = value,
                TestIntent = $"{actionName} {target} for: {goal}",
                ExpectedOutcome = actionType == IntentActionType.UploadFile
                    ? $"{target} has the requested file"
                    : actionType == IntentActionType.PressKey
                        ? $"{value} is pressed on {target}"
                        : $"{target} has the requested value",
                LocatorKey = locatorPrefix + ToPascalKey(target),
            };
        }

        private static void AddDirectAction(
            List<IntentStep> steps,
            ref int order,
            IntentActionType actionType,
            string target,
            string value,
            string goal)
        {
            steps.Add(new IntentStep
            {
                Order = order++,
                ActionType = actionType,
                TargetDescription = target,
                Value = value,
                TestIntent = $"{actionType} {target} for: {goal}",
                ExpectedOutcome = actionType == IntentActionType.Wait
                    ? $"{target} becomes available"
                    : $"{actionType} completes for {target}",
                LocatorKey = "Action." + actionType + "." + ToPascalKey(target),
            });
        }

        private static string ExtractTargetAfterMarker(string goal, params string[] markers)
        {
            foreach (var marker in markers)
            {
                var index = goal.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    var target = goal.Substring(index + marker.Length).Trim(' ', '.', ',', ';', ':');
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        return target;
                    }
                }
            }

            return "target element";
        }

        private static string ExtractKeyTarget(string goal)
        {
            var markers = new[] { " on ", " in " };
            foreach (var marker in markers)
            {
                var index = goal.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    var target = goal.Substring(index + marker.Length).Trim(' ', '.', ',', ';', ':');
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        return target;
                    }
                }
            }

            return "focused element";
        }

        private static readonly string[] KnownKeyNames =
        {
            "ArrowDown", "ArrowUp", "ArrowLeft", "ArrowRight", "Backspace",
            "PageDown", "PageUp", "Escape", "Delete", "Enter", "Space", "Tab", "Home", "End"
        };

        private static string ExtractKeyName(string goal)
        {
            return KnownKeyNames.FirstOrDefault(key => System.Text.RegularExpressions.Regex.IsMatch(
                goal,
                @"\b" + System.Text.RegularExpressions.Regex.Escape(key) + @"\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ?? "";
        }

        private static bool LooksLikeKeyName(string value)
        {
            var trimmed = (value ?? "").Trim();
            if (trimmed.Length == 1)
            {
                return char.IsLetterOrDigit(trimmed[0]);
            }

            return KnownKeyNames.Any(key => string.Equals(key, trimmed, StringComparison.OrdinalIgnoreCase));
        }

        private static string ExtractWaitTimeoutMilliseconds(string goal)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                goal,
                @"\b(\d+)\s*(milliseconds?|ms|seconds?|s)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var amount) || amount <= 0)
            {
                return "5000";
            }

            var unit = match.Groups[2].Value;
            return unit.StartsWith("s", StringComparison.OrdinalIgnoreCase)
                ? ((long)amount * 1000L).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string RemoveDurationQualifier(string target)
        {
            return System.Text.RegularExpressions.Regex.Replace(
                    target,
                    @"\s+(?:for|within|up to)\s+\d+\s*(?:milliseconds?|ms|seconds?|s)\s*$",
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Trim(' ', '.', ',', ';', ':');
        }

        private static string WithoutActionWords(string key, params string[] excludedWords)
        {
            var excluded = new HashSet<string>(excludedWords, StringComparer.OrdinalIgnoreCase);
            var words = HumanizeKey(key)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(word => !excluded.Contains(word));
            var target = string.Join(" ", words);
            return string.IsNullOrWhiteSpace(target) ? "focused element" : target;
        }

        private static void EnsureStep(List<IntentStep> steps, ref int order, string target, IntentActionType actionType, string value, string goal)
        {
            if (steps.Any(step => NormalizeToken(step.TargetDescription).Contains(NormalizeToken(target))))
            {
                return;
            }

            steps.Add(new IntentStep
            {
                Order = order++,
                ActionType = actionType,
                TargetDescription = target,
                Value = value,
                TestIntent = $"{actionType} {target} for: {goal}",
                ExpectedOutcome = $"{target} has the requested value",
                LocatorKey = "Field." + ToPascalKey(target),
            });
        }

        private static IEnumerable<KeyValuePair<string, string>> NormalizeData(IDictionary<string, string>? data)
        {
            return (data ?? new Dictionary<string, string>())
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase);
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            return needles.Any(text.Contains);
        }

        private static string BuildScenarioName(string goal)
        {
            var trimmed = goal.Trim();
            return trimmed.Length <= 80 ? trimmed : trimmed.Substring(0, 80).Trim();
        }

        private static string HumanizeKey(string key)
        {
            return key.Replace("_", " ").Replace("-", " ").Trim();
        }

        private static string ToPascalKey(string key)
        {
            var parts = HumanizeKey(key)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        }

        private static string NormalizeToken(string value)
        {
            return new string((value ?? "")
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }
    }
}
