using System;
using System.Collections.Generic;
using System.Linq;
using UiModel;

namespace IntentAutomation
{
    // Desktop counterpart to IntentExplorationBridge: matches intent steps against a live
    // UiElementInfo tree (as captured by Discovery.UiTreeWalker) instead of a WebElementInfo
    // DOM snapshot. Scoring uses UIA ControlType names in place of HTML tag/role, and
    // Name/AutomationId/ClassName in place of accessible name/testId/CSS selector.

    public sealed class IntentDesktopExplorationBridge
    {
        private readonly IntentDesktopExplorationOptions _options;

        public IntentDesktopExplorationBridge(IntentDesktopExplorationOptions? options = null)
        {
            _options = options ?? new IntentDesktopExplorationOptions();
            if (_options.MaxCandidatesPerStep < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MaxCandidatesPerStep must be at least one.");
            }
            if (_options.ReviewThreshold < 0.0 || _options.ReviewThreshold > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "ReviewThreshold must be between 0.0 and 1.0.");
            }
            if (_options.MinimumSemanticScore < 0.0 || _options.MinimumSemanticScore > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MinimumSemanticScore must be between 0.0 and 1.0.");
            }
            if (_options.MinimumCandidateMargin < 0.0 || _options.MinimumCandidateMargin > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MinimumCandidateMargin must be between 0.0 and 1.0.");
            }
        }

        public IntentDesktopExplorationResult Match(IntentScenario scenario, UiElementInfo root)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            // Controls with zero width and height mark offscreen/collapsed controls - the same
            // convention SimilarityScorer.PositionScore uses to exclude unusable position data.
            // Unlike web (where Playwright auto-scrolls below-the-fold elements), desktop UIA controls
            // with no usable geometry are genuinely collapsed or inaccessible.
            var elements = Flatten(root)
                .Where(element => element.BoundingRectangle.IsUsable)
                .ToList();

            var result = new IntentDesktopExplorationResult { Scenario = scenario };
            foreach (var step in scenario.Steps.OrderBy(step => step.Order))
            {
                result.StepResults.Add(MatchStep(step, elements));
            }

            return result;
        }

        private IntentDesktopStepExplorationResult MatchStep(IntentStep step, IReadOnlyList<UiElementInfo> elements)
        {
            var result = new IntentDesktopStepExplorationResult { Step = step };
            if (step.ActionType == IntentActionType.Navigate || step.ActionType == IntentActionType.Unknown)
            {
                result.Diagnostic = "Step does not require a desktop element candidate.";
                return result;
            }

            var candidates = elements
                .Select(element => Score(step, element))
                .Where(candidate => candidate.Score > 0.0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => CandidateLabel(candidate.Element), StringComparer.OrdinalIgnoreCase)
                .Take(_options.MaxCandidatesPerStep)
                .ToList();

            result.Candidates.AddRange(candidates);
            if (candidates.Count == 0)
            {
                result.RequiresReview = true;
                result.Diagnostic = "No usable desktop candidate matched this intent step.";
            }
            else
            {
                var runnerUpScore = candidates.Count > 1 ? candidates[1].Score : (double?)null;
                var review = IntentCandidateReviewEvaluator.Evaluate(
                    bestScore: candidates[0].Score,
                    bestSemanticScore: candidates[0].SemanticScore,
                    runnerUpScore: runnerUpScore,
                    reviewThreshold: _options.ReviewThreshold,
                    minimumSemanticScore: _options.MinimumSemanticScore,
                    minimumCandidateMargin: _options.MinimumCandidateMargin);

                if (review.RequiresReview)
                {
                    result.RequiresReview = true;
                    result.Diagnostic = review.Diagnostic;
                }
            }

            return result;
        }

        private static IntentDesktopElementCandidate Score(IntentStep step, UiElementInfo element)
        {
            var actionScore = ActionCompatibility(step.ActionType, element.ControlType);
            if (actionScore <= 0.0)
            {
                return new IntentDesktopElementCandidate { Step = step, Element = element };
            }

            var targetText = IntentTextScoring.BuildMatchingText(step);
            var elementText = IntentTextScoring.Join(element.Name, element.AutomationId, element.ClassName);
            var semanticScore = IntentTextScoring.TokenOverlap(targetText, elementText);
            var exactBonus = IntentTextScoring.ContainsNormalized(elementText, step.TargetDescription)
                ? 0.15
                : 0.0;

            var score = Math.Min(1.0, (actionScore * 0.55) + (semanticScore * 0.45) + exactBonus);
            return new IntentDesktopElementCandidate
            {
                Step = step,
                Element = element,
                Score = score,
                SemanticScore = semanticScore,
                Reason = $"action={actionScore:F2}; semantic={semanticScore:F2}",
            };
        }

        // UIA ControlType names (see https://learn.microsoft.com/dotnet/api/system.windows.automation.controltype),
        // matched case-insensitively since Discovery/UiTreeWalker records them as plain strings.
        private static double ActionCompatibility(IntentActionType actionType, string controlType)
        {
            var type = (controlType ?? "").Trim();

            switch (actionType)
            {
                case IntentActionType.Fill:
                    return EqualsAny(type, "Edit", "Document", "Spinner") ? 1.0 : 0.0;
                case IntentActionType.Select:
                    // ComboBox only: FlaUiCSharpTestGenerator emits AsComboBox().Select(...),
                    // which exists only for ComboBox - CheckBox/RadioButton moved to
                    // Check/Uncheck, and List/ListItem/Tab/TabItem were removed for the same
                    // reason (AsComboBox() would throw on them at runtime) (#198).
                    return EqualsAny(type, "ComboBox") ? 1.0 : 0.0;
                case IntentActionType.Check:
                    return EqualsAny(type, "CheckBox", "RadioButton") ? 1.0 : 0.0;
                case IntentActionType.Uncheck:
                    // CheckBox only: a radio button can be checked but never unchecked.
                    return EqualsAny(type, "CheckBox") ? 1.0 : 0.0;
                case IntentActionType.UploadFile:
                    return EqualsAny(type, "Button", "SplitButton", "MenuItem", "Hyperlink", "Custom") ? 1.0 : 0.0;
                case IntentActionType.PressKey:
                    return EqualsAny(type, "Edit", "Document", "ComboBox", "List", "ListItem", "Button", "MenuItem") ? 1.0 : 0.25;
                case IntentActionType.Hover:
                case IntentActionType.Wait:
                    return 1.0;
                case IntentActionType.Click:
                    if (EqualsAny(type, "Button", "Hyperlink", "SplitButton", "MenuItem"))
                    {
                        return 1.0;
                    }

                    return 0.15;
                case IntentActionType.Assert:
                    return EqualsAny(type, "DataGrid", "Table", "Group", "Pane", "StatusBar", "Text") ? 1.0 : 0.1;
                default:
                    return 0.0;
            }
        }

        private static bool EqualsAny(string value, params string[] candidates)
        {
            return candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
        }


        private static IEnumerable<UiElementInfo> Flatten(UiElementInfo root)
        {
            yield return root;
            foreach (var child in root.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }

        private static string CandidateLabel(UiElementInfo element)
        {
            return IntentTextScoring.Join(element.AutomationId, element.Name, element.ClassName, element.ControlType);
        }
    }
}
