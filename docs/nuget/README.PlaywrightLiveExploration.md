# AutomationSandbox.PlaywrightLiveExploration

Live browser page exploration through the Microsoft.Playwright .NET SDK. `PlaywrightLiveExplorer` launches a browser, navigates to a URL, and captures a `WebElementInfo` DOM snapshot — so callers of `AutomationSandbox.IntentAutomation` / `IntentExplorationBridge` do not have to hand-write a Playwright test just to obtain a page model. It is a fully managed .NET client and does not require Node.js at runtime.

## Install

```bash
dotnet add package AutomationSandbox.PlaywrightLiveExploration --prerelease
```

Playwright browsers are downloaded once per machine after the first build:

```bash
pwsh bin/Debug/net8.0/playwright.ps1 install chromium
```

## Target frameworks

- `netstandard2.0`
- `net8.0`
- `net10.0`

## Typical use

```csharp
await using var explorer = await PlaywrightLiveExplorer.LaunchAsync(
    new PlaywrightLiveExplorerOptions { Headless = true });

WebElementInfo snapshot = await explorer.CaptureAsync("https://example.com/login");
```

The snapshot feeds `AutomationSandbox.IntentAutomation` pipelines and `AutomationSandbox.WebDiscovery` mapping directly.

## Related packages

- `AutomationSandbox.WebDiscovery` — the DOM snapshot model this package produces (transitive).
- `AutomationSandbox.IntentAutomation` — consumes these snapshots for intent-driven test generation.
- `AutomationSandbox.SelfHealing` — heals locators against snapshots mapped via `WebElementMapper`.

## Documentation

See `docs/web-automation.md` and `docs/intent-driven-automation.md` in the [Automation Sandbox documentation](https://github.com/mustafasercansak/automation-sandbox/tree/main/docs).
