using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.IntentAutomation;
using AutomationSandbox.PlaywrightLiveExploration;

namespace AutomationSandbox.IntentExecution
{
    /// <summary>Plans a goal and executes it step by step against a live <see cref="PlaywrightWebSession" />:
    /// captures a fresh DOM before each step, matches it through the existing <see cref="IntentExplorationBridge" />
    /// (reused as-is, one step at a time - no separate matching logic), and performs the matched action. Stops at
    /// the first failed or unmatched step, since a broken early step usually makes the rest of the scenario
    /// meaningless.</summary>
    public sealed class IntentWebExecutor
    {
        private readonly IIntentPlanner _planner;
        private readonly IntentExplorationBridge _explorationBridge;

        /// <summary>Configures the planner and matching bridge; both default to their deterministic/standard
        /// implementations when omitted.</summary>
        public IntentWebExecutor(IIntentPlanner? planner = null, IntentExplorationBridge? explorationBridge = null)
        {
            _planner = planner ?? new DeterministicIntentPlanner();
            _explorationBridge = explorationBridge ?? new IntentExplorationBridge();
        }

        /// <summary>Plans <paramref name="request" /> and executes the resulting scenario's steps in order against
        /// <paramref name="session" />.</summary>
        public async Task<IntentWebExecutionResult> RunAsync(
            IntentPlanningRequest request,
            PlaywrightWebSession session,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var planning = _planner.Plan(request);
            var result = new IntentWebExecutionResult { Scenario = planning.Scenario };

            foreach (var step in planning.Scenario.Steps.OrderBy(s => s.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stepResult = await ExecuteStepAsync(step, session, cancellationToken).ConfigureAwait(false);
                result.StepResults.Add(stepResult);
                if (!stepResult.Success)
                {
                    result.Success = false;
                    return result;
                }
            }

            result.Success = true;
            return result;
        }

        private async Task<IntentStepExecutionResult> ExecuteStepAsync(IntentStep step, PlaywrightWebSession session, CancellationToken cancellationToken)
        {
            if (step.ActionType == IntentActionType.Navigate)
            {
                await session.NavigateAsync(step.Value).ConfigureAwait(false);
                return new IntentStepExecutionResult(step, success: true, cssSelector: null, "Navigated.");
            }

            if (step.ActionType == IntentActionType.Assert && step.AssertionKind is AssertionKind.UrlEquals or AssertionKind.UrlContains)
            {
                return EvaluateUrlAssertion(step, session.CurrentUrl);
            }

            // A NotVisible target is, by definition, filtered out of IntentExplorationBridge.Match's candidate
            // pool before scoring even runs (it only considers elements with IsHidden == false) - there is no
            // element to match, so this assertion can never be evaluated through the normal match-then-act path.
            // That is a real limitation of the shared matching bridge, not something to route around here.
            if (step.ActionType == IntentActionType.Assert && step.AssertionKind == AssertionKind.NotVisible)
            {
                return new IntentStepExecutionResult(step, success: false, cssSelector: null,
                    "AssertionKind.NotVisible is not supported: IntentExplorationBridge excludes hidden elements " +
                    "from matching, so a genuinely hidden target can never be matched to confirm its own absence.");
            }

            var dom = await session.CaptureAsync().ConfigureAwait(false);
            var oneStepScenario = new IntentScenario { Steps = { step } };
            var exploration = _explorationBridge.Match(oneStepScenario, dom);
            var stepMatch = exploration.StepResults[0];

            if (stepMatch.RequiresReview || stepMatch.Candidates.Count == 0)
            {
                return new IntentStepExecutionResult(step, success: false, cssSelector: null,
                    $"No confident match found: {stepMatch.Diagnostic}");
            }

            var cssSelector = stepMatch.Candidates[0].Element.CssSelector;

            try
            {
                switch (step.ActionType)
                {
                    case IntentActionType.Fill:
                        await session.FillAsync(cssSelector, step.Value).ConfigureAwait(false);
                        break;
                    case IntentActionType.Click:
                        await session.ClickAsync(cssSelector).ConfigureAwait(false);
                        break;
                    case IntentActionType.Select:
                        await session.SelectAsync(cssSelector, step.Value).ConfigureAwait(false);
                        break;
                    case IntentActionType.Check:
                        await session.CheckAsync(cssSelector).ConfigureAwait(false);
                        break;
                    case IntentActionType.Uncheck:
                        await session.UncheckAsync(cssSelector).ConfigureAwait(false);
                        break;
                    case IntentActionType.Hover:
                        await session.HoverAsync(cssSelector).ConfigureAwait(false);
                        break;
                    case IntentActionType.UploadFile:
                        await session.UploadFileAsync(cssSelector, step.Value).ConfigureAwait(false);
                        break;
                    case IntentActionType.PressKey:
                        await session.PressKeyAsync(cssSelector, step.Value).ConfigureAwait(false);
                        break;
                    case IntentActionType.Wait:
                        // Same limitation as NotVisible above: the match just above this switch already
                        // required the target to be a non-hidden candidate, so a Wait step can only ever
                        // confirm/wait on a target that is already visible (or becomes actionable) when
                        // captured - it cannot discover a target that is currently display:none/hidden and
                        // becomes visible later, since IntentExplorationBridge would have excluded it before
                        // this code ever runs. WaitForVisibleAsync itself handles delayed visibility fine
                        // (see PlaywrightWebSessionTests); the constraint is entirely on the matching step.
                        var timeout = double.TryParse(step.Value, out var seconds) ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
                        await session.WaitForVisibleAsync(cssSelector, timeout).ConfigureAwait(false);
                        break;
                    case IntentActionType.Assert:
                        return await EvaluateElementAssertionAsync(step, session, cssSelector).ConfigureAwait(false);
                    default:
                        return new IntentStepExecutionResult(step, success: false, cssSelector,
                            $"Unsupported action type: {step.ActionType}");
                }
            }
            catch (Exception ex)
            {
                return new IntentStepExecutionResult(step, success: false, cssSelector, $"Action failed: {ex.Message}");
            }

            return new IntentStepExecutionResult(step, success: true, cssSelector, "Executed.");
        }

        private static IntentStepExecutionResult EvaluateUrlAssertion(IntentStep step, string currentUrl)
        {
            var success = step.AssertionKind == AssertionKind.UrlEquals
                ? currentUrl == step.ExpectedValue
                : currentUrl.Contains(step.ExpectedValue);
            var diagnostic = success
                ? "URL assertion passed."
                : $"Expected URL {step.AssertionKind} '{step.ExpectedValue}' but was '{currentUrl}'.";
            return new IntentStepExecutionResult(step, success, cssSelector: null, diagnostic);
        }

        private static async Task<IntentStepExecutionResult> EvaluateElementAssertionAsync(IntentStep step, PlaywrightWebSession session, string cssSelector)
        {
            switch (step.AssertionKind)
            {
                case AssertionKind.Visible:
                case AssertionKind.None:
                    var visible = await session.IsVisibleAsync(cssSelector).ConfigureAwait(false);
                    return new IntentStepExecutionResult(step, visible, cssSelector,
                        visible ? "Element is visible." : "Element is not visible.");

                case AssertionKind.TextEquals:
                    var textForEquals = await session.GetTextAsync(cssSelector).ConfigureAwait(false);
                    var textEqualsOk = textForEquals == step.ExpectedValue;
                    return new IntentStepExecutionResult(step, textEqualsOk, cssSelector,
                        textEqualsOk ? "Text matched." : $"Expected text '{step.ExpectedValue}' but was '{textForEquals}'.");

                case AssertionKind.TextContains:
                    var textForContains = await session.GetTextAsync(cssSelector).ConfigureAwait(false);
                    var textContainsOk = textForContains.Contains(step.ExpectedValue);
                    return new IntentStepExecutionResult(step, textContainsOk, cssSelector,
                        textContainsOk ? "Text contained expected substring." : $"Expected text to contain '{step.ExpectedValue}' but was '{textForContains}'.");

                case AssertionKind.ValueEquals:
                    var value = await session.GetValueAsync(cssSelector).ConfigureAwait(false);
                    var valueOk = value == step.ExpectedValue;
                    return new IntentStepExecutionResult(step, valueOk, cssSelector,
                        valueOk ? "Value matched." : $"Expected value '{step.ExpectedValue}' but was '{value}'.");

                default:
                    return new IntentStepExecutionResult(step, success: false, cssSelector,
                        $"Unsupported assertion kind for a matched element: {step.AssertionKind}");
            }
        }
    }
}
