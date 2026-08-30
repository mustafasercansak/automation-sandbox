# AutomationSandbox.WebDiscovery

The web foundation for Automation Sandbox: framework-agnostic DOM snapshot DTOs (`WebElementInfo`, including input-type and hidden/offscreen metadata), a `WebElementMapper` that converts web trees into the same `UiElementInfo` model used by desktop healing, a `PlaywrightDomCaptureScript` for `page.EvaluateAsync` that handles regular DOM, open Shadow DOM, and same-origin iframes, and a `PlaywrightLocatorEmitter` that suggests `GetByTestId` / `GetByRole` / CSS locators.

This package is pure managed code with no Playwright dependency — it emits data structures and a capture script; a browser connection comes from `AutomationSandbox.PlaywrightLiveExploration` or your own Playwright session.

## Install

```bash
dotnet add package AutomationSandbox.WebDiscovery --prerelease
```

## Target frameworks

- `netstandard2.0`
- `net8.0`
- `net10.0`

## Typical use

```csharp
// domJson: result of evaluating PlaywrightDomCaptureScript.JavaScript in a page
var webRoot = JsonSerializer.Deserialize<WebElementInfo>(domJson);
UiElementInfo uiTree = WebElementMapper.ToUiElementTree(webRoot); // ready for SelfHealing

IReadOnlyList<PlaywrightLocatorSuggestion> locators = PlaywrightLocatorEmitter.Suggest(element);
```

## Related packages

- `AutomationSandbox.PlaywrightLiveExploration` — captures `WebElementInfo` from a live browser.
- `AutomationSandbox.SelfHealing` — heals broken web locators on the mapped tree.
- `AutomationSandbox.IntentAutomation` — web intent-driven test generation (depends on this package).

## Documentation

See `docs/web-automation.md` in the [Automation Sandbox documentation](https://github.com/mustafasercansak/automation-sandbox/tree/main/docs).
