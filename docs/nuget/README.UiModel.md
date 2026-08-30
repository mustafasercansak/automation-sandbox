# AutomationSandbox.UiModel

Framework-agnostic UI tree snapshot model for the Automation Sandbox self-healing engine. `UiElementInfo` describes any element (desktop or web) with its control type, name, bounding rectangle, parent and sibling context; `UiElementSnapshot`, `UiTreeSerializer`, `CandidateScore`/`ScoreComponents`, and the file-backed, concurrency-safe `LocatorRepository` sit on top of it. There is no FlaUI or browser dependency — this is the shared vocabulary every other `AutomationSandbox.*` package speaks.

## Install

```bash
dotnet add package AutomationSandbox.UiModel --prerelease
```

## Target frameworks

- `netstandard2.0`
- `net8.0`
- `net10.0`

## Typical use

```csharp
UiElementInfo snapshot = UiElementSnapshot.CaptureByAutomationId(treeRoot, "okButton");
string json = UiElementSnapshot.ToJson(snapshot);

var repo = new LocatorRepository("locators.json");
repo.Upsert("submitButton", snapshot); // versioned JSON, safe for concurrent writers
```

Most consumers do not reference this package directly — it arrives transitively with `AutomationSandbox.SelfHealing`, `AutomationSandbox.Discovery`, or `AutomationSandbox.WebDiscovery`. Reference it directly when you want to persist or exchange UI snapshots without the healing engine.

## Related packages

- `AutomationSandbox.SelfHealing` — scores and re-resolves broken locators against these snapshots.
- `AutomationSandbox.Discovery` — captures live desktop trees into `UiElementInfo` (Windows only).
- `AutomationSandbox.WebDiscovery` — maps web DOM snapshots into the same model.

## Documentation

Full guides, calibration notes, and the JSON schema live in the [Automation Sandbox documentation](https://github.com/mustafasercansak/automation-sandbox/tree/main/docs).
