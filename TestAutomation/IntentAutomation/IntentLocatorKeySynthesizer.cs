using System;
using System.Collections.Generic;
using System.Linq;

namespace IntentAutomation
{
    public static class IntentLocatorKeySynthesizer
    {
        public static string Synthesize(IntentStep step, IntentElementCandidate? candidate = null)
        {
            if (step == null)
            {
                return "";
            }

            if (step.ActionType == IntentActionType.Navigate || step.ActionType == IntentActionType.Unknown)
            {
                return "";
            }

            var target = !string.IsNullOrWhiteSpace(step.TargetDescription)
                ? step.TargetDescription
                : !string.IsNullOrWhiteSpace(candidate?.Element?.AccessibleName)
                    ? candidate!.Element!.AccessibleName
                    : !string.IsNullOrWhiteSpace(candidate?.Element?.TestId)
                        ? candidate!.Element!.TestId
                        : candidate?.Element?.Id ?? "";

            if (string.IsNullOrWhiteSpace(target))
            {
                return "";
            }

            var pascalTarget = ToPascalKey(target);
            if (string.IsNullOrWhiteSpace(pascalTarget))
            {
                return "";
            }

            switch (step.ActionType)
            {
                case IntentActionType.Fill:
                case IntentActionType.Select:
                case IntentActionType.Check:
                case IntentActionType.Uncheck:
                case IntentActionType.UploadFile:
                    return "Field." + pascalTarget;

                case IntentActionType.Click:
                    var lower = target.ToLowerInvariant();
                    if (lower.Contains("submit") || lower.Contains("save") || lower.Contains("primary"))
                    {
                        return "Action.PrimarySubmit";
                    }
                    return "Action.Click." + pascalTarget;

                case IntentActionType.PressKey:
                case IntentActionType.Hover:
                case IntentActionType.Wait:
                    return "Action." + step.ActionType + "." + pascalTarget;

                case IntentActionType.Assert:
                    return "Assert." + (string.Equals(pascalTarget, "ResultRecordsOrConfirmationArea", StringComparison.OrdinalIgnoreCase) || string.Equals(pascalTarget, "ResultVisible", StringComparison.OrdinalIgnoreCase)
                        ? "ResultVisible"
                        : pascalTarget);

                default:
                    return "Element." + pascalTarget;
            }
        }

        public static string Synthesize(IntentStep step, IntentDesktopElementCandidate? candidate)
        {
            if (step == null)
            {
                return "";
            }

            if (step.ActionType == IntentActionType.Navigate || step.ActionType == IntentActionType.Unknown)
            {
                return "";
            }

            var target = !string.IsNullOrWhiteSpace(step.TargetDescription)
                ? step.TargetDescription
                : !string.IsNullOrWhiteSpace(candidate?.Element?.Name)
                    ? candidate!.Element!.Name
                    : candidate?.Element?.AutomationId ?? "";

            if (string.IsNullOrWhiteSpace(target))
            {
                return "";
            }

            var pascalTarget = ToPascalKey(target);
            if (string.IsNullOrWhiteSpace(pascalTarget))
            {
                return "";
            }

            switch (step.ActionType)
            {
                case IntentActionType.Fill:
                case IntentActionType.Select:
                case IntentActionType.Check:
                case IntentActionType.Uncheck:
                case IntentActionType.UploadFile:
                    return "Field." + pascalTarget;

                case IntentActionType.Click:
                    var lower = target.ToLowerInvariant();
                    if (lower.Contains("submit") || lower.Contains("save") || lower.Contains("primary"))
                    {
                        return "Action.PrimarySubmit";
                    }
                    return "Action.Click." + pascalTarget;

                case IntentActionType.PressKey:
                case IntentActionType.Hover:
                case IntentActionType.Wait:
                    return "Action." + step.ActionType + "." + pascalTarget;

                case IntentActionType.Assert:
                    return "Assert." + (string.Equals(pascalTarget, "ResultRecordsOrConfirmationArea", StringComparison.OrdinalIgnoreCase) || string.Equals(pascalTarget, "ResultVisible", StringComparison.OrdinalIgnoreCase)
                        ? "ResultVisible"
                        : pascalTarget);

                default:
                    return "Element." + pascalTarget;
            }
        }

        public static string ToPascalKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            var humanized = key.Replace("_", " ").Replace("-", " ").Replace(".", " ").Trim();
            var parts = humanized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        }
    }
}
