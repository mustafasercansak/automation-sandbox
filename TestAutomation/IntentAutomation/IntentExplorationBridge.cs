using System;
using System.Collections.Generic;
using System.Linq;
using UiModel;
using WebDiscovery;

namespace IntentAutomation
{
    public sealed class IntentExplorationBridge
    {
        private readonly IntentExplorationOptions _options;

        public IntentExplorationBridge(IntentExplorationOptions? options = null)
        {
            _options = options ?? new IntentExplorationOptions();
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

        public IntentExplorationResult Match(IntentScenario scenario, WebElementInfo root)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            var elements = Flatten(root)
                .Where(element => !element.IsHidden && !element.IsOffscreen)
                .ToList();

            var result = new IntentExplorationResult { Scenario = scenario };
            foreach (var step in scenario.Steps.OrderBy(step => step.Order))
            {
                result.StepResults.Add(MatchStep(step, elements));
            }

            return result;
        }

        private IntentStepExplorationResult MatchStep(IntentStep step, IReadOnlyList<WebElementInfo> elements)
        {
            var result = new IntentStepExplorationResult { Step = step };
            if (step.ActionType == IntentActionType.Navigate || step.ActionType == IntentActionType.Unknown)
            {
                result.Diagnostic = "Step does not require a page element candidate.";
                return result;
            }

            var candidates = elements
                .Select(element => Score(step, element))
                .Where(candidate => candidate.Score > 0.0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.LocatorSuggestions.Count == 0 ? 0.0 : candidate.LocatorSuggestions[0].Confidence)
                .ThenBy(candidate => CandidateLabel(candidate.Element), StringComparer.OrdinalIgnoreCase)
                .Take(_options.MaxCandidatesPerStep)
                .ToList();

            result.Candidates.AddRange(candidates);
            if (candidates.Count == 0)
            {
                result.RequiresReview = true;
                result.Diagnostic = "No visible DOM candidate matched this intent step.";
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

        private static IntentElementCandidate Score(IntentStep step, WebElementInfo element)
        {
            var actionScore = ActionCompatibility(step.ActionType, element);
            if (actionScore <= 0.0)
            {
                return new IntentElementCandidate { Step = step, Element = element };
            }

            var targetText = Join(step.TargetDescription, step.TestIntent, step.ExpectedOutcome, step.LocatorKey);
            var elementText = Join(
                element.AccessibleName,
                element.Text,
                element.TestId,
                element.Id,
                element.NameAttribute);
            var semanticScore = TokenOverlap(targetText, elementText);
            var locatorSuggestions = PlaywrightLocatorEmitter.Suggest(element).ToList();
            var locatorScore = locatorSuggestions.Count == 0 ? 0.0 : locatorSuggestions[0].Confidence;
            var exactBonus = ContainsNormalized(elementText, step.TargetDescription)
                || ContainsNormalized(elementText, step.LocatorKey)
                ? 0.15
                : 0.0;

            var score = Math.Min(1.0, (actionScore * 0.45) + (semanticScore * 0.40) + (locatorScore * 0.15) + exactBonus);
            return new IntentElementCandidate
            {
                Step = step,
                Element = element,
                Score = score,
                SemanticScore = semanticScore,
                Reason = $"action={actionScore:F2}; semantic={semanticScore:F2}; locator={locatorScore:F2}",
                LocatorSuggestions = locatorSuggestions,
            };
        }

        private static double ActionCompatibility(IntentActionType actionType, WebElementInfo element)
        {
            var tag = element.TagName.ToLowerInvariant();
            var role = element.Role.ToLowerInvariant();

            switch (actionType)
            {
                case IntentActionType.Fill:
                    return tag == "input" || tag == "textarea" || role == "textbox" || role == "searchbox" || role == "spinbutton"
                        ? 1.0
                        : 0.0;
                case IntentActionType.Select:
                    return tag == "select" || role == "combobox" || role == "listbox" || role == "radio" || role == "checkbox"
                        ? 1.0
                        : 0.0;
                case IntentActionType.Click:
                    if (tag == "button" || tag == "a" || role == "button" || role == "link")
                    {
                        return 1.0;
                    }

                    return !string.IsNullOrWhiteSpace(element.TestId)
                        ? 0.25
                        : 0.15;
                case IntentActionType.Assert:
                    if (tag == "table" || tag == "tbody" || role == "grid" || role == "table" || role == "status" || role == "alert")
                    {
                        return 1.0;
                    }

                    return !string.IsNullOrWhiteSpace(element.AccessibleName) || !string.IsNullOrWhiteSpace(element.Text)
                        ? 0.30
                        : 0.10;
                default:
                    return 0.0;
            }
        }

        private static IEnumerable<WebElementInfo> Flatten(WebElementInfo root)
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

        private static double TokenOverlap(string targetText, string elementText)
        {
            var targetTokens = Tokens(targetText).ToList();
            if (targetTokens.Count == 0)
            {
                return 0.0;
            }

            var elementTokens = new HashSet<string>(Tokens(elementText), StringComparer.OrdinalIgnoreCase);
            if (elementTokens.Count == 0)
            {
                return 0.0;
            }

            var matches = targetTokens.Count(elementTokens.Contains);
            return (double)matches / targetTokens.Count;
        }

        private static IEnumerable<string> Tokens(string value)
        {
            return NormalizeText(value)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 1);
        }

        private static bool ContainsNormalized(string haystack, string needle)
        {
            var normalizedNeedle = NormalizeText(needle).Replace(" ", "");
            return normalizedNeedle.Length > 1
                && NormalizeText(haystack).Replace(" ", "").Contains(normalizedNeedle);
        }

        private static string NormalizeText(string value)
        {
            var chars = (value ?? "").Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
            return new string(chars.ToArray());
        }

        private static string Join(params string[] values)
        {
            return string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string CandidateLabel(WebElementInfo element)
        {
            return Join(element.TestId, element.Id, element.NameAttribute, element.AccessibleName, element.Text, element.CssSelector);
        }
    }
}
