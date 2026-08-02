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
            else
            {
                result.Diagnostics.Add("No submit/save/register verb was detected; planner did not add a final click step.");
                result.RequiresReview = true;
            }

            if (ContainsAny(goal, "record", "row", "grid", "created", "saved", "registration"))
            {
                result.Scenario.Steps.Add(new IntentStep
                {
                    Order = order,
                    ActionType = IntentActionType.Assert,
                    TargetDescription = "result record or confirmation area",
                    TestIntent = "Verify the expected result for: " + result.Scenario.Goal,
                    ExpectedOutcome = "The created or updated result is visible",
                    LocatorKey = "Assert.ResultVisible",
                });
            }

            if (result.Scenario.Steps.Count == 0)
            {
                result.Diagnostics.Add("Planner could not derive any executable steps.");
                result.RequiresReview = true;
            }

            return result;
        }

        private static IntentStep CreateDataEntryStep(int order, string goal, string key, string value)
        {
            var normalizedKey = NormalizeToken(key);
            var actionType = normalizedKey.Contains("type") || normalizedKey.Contains("country") || normalizedKey.Contains("status")
                ? IntentActionType.Select
                : IntentActionType.Fill;

            return new IntentStep
            {
                Order = order,
                ActionType = actionType,
                TargetDescription = HumanizeKey(key),
                Value = value,
                TestIntent = $"{(actionType == IntentActionType.Select ? "Select" : "Fill")} {HumanizeKey(key)} for: {goal}",
                ExpectedOutcome = $"{HumanizeKey(key)} has the requested value",
                LocatorKey = "Field." + ToPascalKey(key),
            };
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
