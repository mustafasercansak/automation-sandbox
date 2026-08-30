# AutomationSandbox.IntentAutomation

Intent-driven test automation: describe a flow in plain intent ("navigate, fill, select, click, check, hover, upload, press, wait, assert") and get a reviewed, generated test. Contains the `IIntentPlanner` contract with a keyword-based `DeterministicIntentPlanner` and an opt-in Claude-backed `LlmIntentPlanner` (guarded fallback to deterministic), web and desktop candidate matching bridges, locator-repository recorders, the `PlaywrightCSharpTestGenerator` / `PlaywrightTypeScriptTestGenerator` / `FlaUiCSharpTestGenerator` code emitters, and the `IntentAutomationPipeline` / `IntentDesktopAutomationPipeline` orchestrators with flow reporting.

## Install

```bash
dotnet add package AutomationSandbox.IntentAutomation --prerelease
```

## Target frameworks

- `netstandard2.0`
- `net8.0`
- `net10.0`

## Typical use

```csharp
var pipeline = new IntentAutomationPipeline(); // keyword-based deterministic planner by default
IntentAutomationPipelineResult result = pipeline.Run(request, domRoot, repository);
// result carries the generated Playwright C#/TypeScript test source and a flow report
```

The pipeline consumes web snapshots (`WebElementInfo`, from `AutomationSandbox.PlaywrightLiveExploration`) or desktop trees (`UiElementInfo`, from `AutomationSandbox.Discovery`) so it never needs a live browser or app during planning.

## Related packages

- `AutomationSandbox.WebDiscovery` — DOM model and capture script (transitive).
- `AutomationSandbox.PlaywrightLiveExploration` — live web page capture for the web pipeline.
- `AutomationSandbox.Discovery` — live desktop tree capture for the desktop pipeline (net48, Windows only).

## Documentation

See `docs/intent-driven-automation.md` in the [Automation Sandbox documentation](https://github.com/mustafasercansak/automation-sandbox/tree/main/docs).
