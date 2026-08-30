# AutomationSandbox.Discovery

Resilient live UI tree capture for Windows desktop applications via FlaUI / UI Automation (UIA3). `UiTreeWalker` walks a live window into the framework-agnostic `UiElementInfo` model, `ApplicationConnector` launches or attaches to a process with a configurable main-window readiness wait, early-exit detection, and classified startup diagnostics, and `DiscoveryOptions` / `DiscoveryResult` control depth, element budget, offscreen inclusion, and capture telemetry.

## Install

```bash
dotnet add package AutomationSandbox.Discovery --prerelease
```

## Target frameworks

- `net48` only — FlaUI 5.0.0 ships .NET Framework binaries, and UIA3 requires Windows COM APIs. Windows is required at build and run time.

## Typical use

```csharp
using var connector = ApplicationConnector.Launch(@"C:\path\to\YourApp.exe");
var window = connector.GetMainWindow();

DiscoveryResult result = UiTreeWalker.Discover(window, new DiscoveryOptions { MaxDepth = 12 });
UiElementInfo liveTree = result.Root;
```

Feed the captured tree into `AutomationSandbox.SelfHealing` to re-resolve broken locators against it.

## Related packages

- `AutomationSandbox.UiModel` — the snapshot model produced here (transitive).
- `AutomationSandbox.SelfHealing` — scores live trees from this package against stored snapshots.
- `AutomationSandbox.IntentAutomation` — uses these trees for desktop intent-driven test generation.

## Documentation

Desktop capture and startup diagnostics are covered in the [Automation Sandbox documentation](https://github.com/mustafasercansak/automation-sandbox/tree/main/docs) — see `docs/desktop-automation.md`.
