# AutomationSandbox.IntentExecution

Live executor for `AutomationSandbox.IntentAutomation` scenarios. `IntentAutomationPipeline.Run` matches one static `WebElementInfo` snapshot and generates test *source*; `IntentWebExecutor` closes that gap by planning a goal and actually performing each step against a live `AutomationSandbox.PlaywrightLiveExploration` session — re-capturing the DOM and re-matching through the existing `IntentExplorationBridge` before every step, so it reflects whatever the previous step changed on the page. Stops at the first failed or unmatched step.

## Install

```bash
dotnet add package AutomationSandbox.IntentExecution --prerelease
```

## Target frameworks

- `netstandard2.0`
- `net8.0`
- `net10.0`

## Typical use

```csharp
await using var session = await PlaywrightWebSession.StartAsync();
var executor = new IntentWebExecutor(); // defaults to DeterministicIntentPlanner

var request = new IntentPlanningRequest
{
    Goal = "Create a customer record with valid email",
    TargetUrl = "https://example.test/customers",
    TestData = new Dictionary<string, string> { ["email"] = "jane.doe@example.com" },
};

IntentWebExecutionResult result = await executor.RunAsync(request, session);
```

`AssertionKind.NotVisible` (and a `Wait` step whose target starts `display:none`) is a permanent limitation, not a gap left to fill: `IntentExplorationBridge` excludes hidden elements from its candidate pool before scoring, so a genuinely hidden target can never be matched to confirm its own absence or discover it before it appears.

## Related packages

- `AutomationSandbox.IntentAutomation` — supplies the planner, scenario model, and matching bridge this package drives live.
- `AutomationSandbox.PlaywrightLiveExploration` — supplies `PlaywrightWebSession`, the live browser session each step executes against.

## Documentation

See `docs/intent-driven-automation.md` in the [Automation Sandbox documentation](https://github.com/mustafasercansak/automation-sandbox/tree/main/docs).
