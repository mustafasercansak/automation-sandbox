using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UiModel;

namespace IntentAutomation
{
    public sealed class PlaywrightCSharpTestGenerator
    {
        private readonly PlaywrightCSharpTestGenerationOptions _options;

        public PlaywrightCSharpTestGenerator(PlaywrightCSharpTestGenerationOptions? options = null)
        {
            _options = options ?? new PlaywrightCSharpTestGenerationOptions();
        }

        public string Generate(IntentScenario scenario, IReadOnlyList<IntentLocatorRecordingResult> recordingResults)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            if (recordingResults == null)
            {
                throw new ArgumentNullException(nameof(recordingResults));
            }

            var className = string.IsNullOrWhiteSpace(_options.ClassName)
                ? CodeGenerationUtilities.ToIdentifier(scenario.Name, "IntentScenarioTests")
                : CodeGenerationUtilities.ToIdentifier(_options.ClassName, "IntentScenarioTests");
            var methodName = string.IsNullOrWhiteSpace(_options.MethodName)
                ? CodeGenerationUtilities.ToIdentifier(scenario.Goal, "GeneratedIntentScenario")
                : CodeGenerationUtilities.ToIdentifier(_options.MethodName, "GeneratedIntentScenario");
            var namespaceName = CodeGenerationUtilities.ToNamespace(_options.Namespace, "GeneratedTests");
            var recordingsByStep = recordingResults
                .Where(result => result.Step != null)
                .GroupBy(result => result.Step)
                .ToDictionary(group => group.Key, group => group.First());
            var recordingsByOrder = recordingResults
                .Where(result => result.Step != null && result.Step.Order > 0)
                .GroupBy(result => result.Step.Order)
                .ToDictionary(group => group.Key, group => group.First());
            var recordingsByKey = recordingResults
                .Where(result => !string.IsNullOrWhiteSpace(result.LocatorKey))
                .GroupBy(result => result.LocatorKey)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var code = new StringBuilder();
            code.AppendLine("using System.Threading.Tasks;");
            code.AppendLine("using Microsoft.Playwright;");
            code.AppendLine("using Microsoft.Playwright.NUnit;");
            code.AppendLine("using NUnit.Framework;");
            code.AppendLine();
            code.AppendLine($"namespace {namespaceName}");
            code.AppendLine("{");
            code.AppendLine($"    public class {className} : PageTest");
            code.AppendLine("    {");
            code.AppendLine("        [Test]");
            code.AppendLine($"        public async Task {methodName}()");
            code.AppendLine("        {");

            foreach (var step in scenario.Steps.OrderBy(step => step.Order))
            {
                AppendStep(code, step, recordingsByStep, recordingsByOrder, recordingsByKey);
            }

            code.AppendLine("        }");
            code.AppendLine("    }");
            code.AppendLine("}");
            return code.ToString();
        }

        private void AppendStep(
            StringBuilder code,
            IntentStep step,
            IReadOnlyDictionary<IntentStep, IntentLocatorRecordingResult> recordingsByStep,
            IReadOnlyDictionary<int, IntentLocatorRecordingResult> recordingsByOrder,
            IReadOnlyDictionary<string, IntentLocatorRecordingResult> recordingsByKey)
        {
            if (!string.IsNullOrWhiteSpace(step.TestIntent))
            {
                code.AppendLine($"            // {CodeGenerationUtilities.EscapeComment(step.TestIntent)}");
            }

            if (step.ActionType == IntentActionType.Navigate)
            {
                code.AppendLine($"            await Page.GotoAsync(\"{CodeGenerationUtilities.EscapeString(step.Value)}\");");
                return;
            }

            if (!recordingsByStep.TryGetValue(step, out var recording))
            {
                if (!recordingsByOrder.TryGetValue(step.Order, out recording))
                {
                    if (!string.IsNullOrWhiteSpace(step.TargetDescription) && recordingsByKey.TryGetValue(step.TargetDescription, out recording))
                    {
                    }
                    else
                    {
                        var synthesizedKey = IntentLocatorKeySynthesizer.Synthesize(step);
                        if (!string.IsNullOrWhiteSpace(synthesizedKey))
                        {
                            recordingsByKey.TryGetValue(synthesizedKey, out recording);
                        }
                    }
                }
            }

            var locatorExpression = LocatorExpression(recording?.Candidate, recording?.Record?.Snapshot);
            var locatorRequired = step.ActionType != IntentActionType.Assert || AssertionCodeEmitter.IsLocatorRequired(step.AssertionKind, _options.AssertGenerationMode);
            var locatorKey = recording?.LocatorKey ?? "";
            if (locatorRequired && string.IsNullOrWhiteSpace(locatorExpression))
            {
                var identifier = !string.IsNullOrWhiteSpace(locatorKey) ? locatorKey : step.TargetDescription;
                code.AppendLine($"            Assert.Inconclusive(\"No recorded locator for {CodeGenerationUtilities.EscapeString(identifier)}.\");");
                return;
            }

            if (_options.IncludeLocatorComments && !string.IsNullOrWhiteSpace(locatorKey) && locatorRequired)
            {
                code.AppendLine($"            // locator: {CodeGenerationUtilities.EscapeComment(locatorKey)}");
            }

            switch (step.ActionType)
            {
                case IntentActionType.Fill:
                    code.AppendLine($"            await {locatorExpression}.FillAsync(\"{CodeGenerationUtilities.EscapeString(step.Value)}\");");
                    break;
                case IntentActionType.Select:
                    code.AppendLine($"            await {locatorExpression}.SelectOptionAsync(new[] {{ \"{CodeGenerationUtilities.EscapeString(step.Value)}\" }});");
                    break;
                case IntentActionType.Check:
                    code.AppendLine($"            await {locatorExpression}.CheckAsync();");
                    break;
                case IntentActionType.Uncheck:
                    code.AppendLine($"            await {locatorExpression}.UncheckAsync();");
                    break;
                case IntentActionType.Click:
                    code.AppendLine($"            await {locatorExpression}.ClickAsync();");
                    break;
                case IntentActionType.Hover:
                    code.AppendLine($"            await {locatorExpression}.HoverAsync();");
                    break;
                case IntentActionType.UploadFile:
                    code.AppendLine($"            await {locatorExpression}.SetInputFilesAsync(\"{CodeGenerationUtilities.EscapeString(step.Value)}\");");
                    break;
                case IntentActionType.PressKey:
                    code.AppendLine($"            await {locatorExpression}.PressAsync(\"{CodeGenerationUtilities.EscapeString(step.Value)}\");");
                    break;
                case IntentActionType.Wait:
                    code.AppendLine($"            await {locatorExpression}.WaitForAsync(new() {{ State = WaitForSelectorState.Visible, Timeout = {CodeGenerationUtilities.WaitTimeoutMilliseconds(step.Value)} }});");
                    break;
                case IntentActionType.Assert:
                    AssertionCodeEmitter.EmitPlaywrightCSharp(step, locatorExpression, _options.AssertGenerationMode, code);
                    break;
                default:
                    code.AppendLine($"            Assert.Inconclusive(\"Unsupported intent action {step.ActionType}.\");");
                    break;
            }
        }

        private static string LocatorExpression(IntentElementCandidate? candidate, UiElementInfo? snapshot)
        {
            var suggestion = candidate?.LocatorSuggestions.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(suggestion?.Expression))
            {
                var expr = suggestion!.Expression;
                if (expr.StartsWith("page.", StringComparison.Ordinal))
                {
                    return "Page." + expr.Substring("page.".Length);
                }

                return expr;
            }

            if (!string.IsNullOrWhiteSpace(snapshot?.AutomationId))
            {
                return $"Page.GetByTestId(\"{CodeGenerationUtilities.EscapeString(snapshot!.AutomationId)}\")";
            }

            if (!string.IsNullOrWhiteSpace(snapshot?.Name))
            {
                return $"Page.GetByText(\"{CodeGenerationUtilities.EscapeString(snapshot!.Name)}\")";
            }

            return "";
        }
    }
}
