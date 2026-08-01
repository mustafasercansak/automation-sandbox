# Automation Sandbox

![CI](https://github.com/mustafasercansak/automation-sandbox/actions/workflows/ci.yml/badge.svg)

A proof of concept for an open-source alternative to Ranorex: Windows desktop UI test
automation built on [FlaUI](https://github.com/FlaUI/FlaUI) (.NET, Microsoft UI
Automation). The goal is a framework-agnostic architecture — the same discovery and
self-healing code drives both WinForms and WPF applications unmodified.

## Why this exists

Commercial tools like Ranorex hide a lot behind proprietary object repositories. This
sandbox explores the same territory with plain FlaUI and a small amount of custom
tooling:

- **Discovery** — walk an application's entire UI tree via UI Automation and serialize
  it to JSON (a format meant to eventually be fed to an LLM as context).
- **SelfHealing** — when a locator (typically an `AutomationId`) breaks, compare the
  current UI tree against the last known snapshot and re-locate the element using
  structural similarity (control type, parent/sibling context, name, screen position) —
  no LLM yet, a plain heuristic scorer.
- **ScenarioRunner** — xUnit tests that exercise both of the above against real,
  running applications.

## Repository structure

```
AutomationSandbox.sln
├── WinFormsApp/            .NET Framework 4.8 WinForms app under test
├── WpfApp/                 .NET 8 WPF app under test
└── TestAutomation/
    ├── Discovery/           UI tree walker + JSON serializer (FlaUI.Core, FlaUI.UIA3)
    ├── SelfHealing/         Structural-similarity locator resolver (no FlaUI dependency)
    └── ScenarioRunner/      xUnit tests, live UIA scenarios + pure-logic unit tests
```

Both `WinFormsApp` and `WpfApp` implement the same small customer-registration form:
first/last name + email with required-field validation, a record-type combo box that
toggles a company-name panel, and a save button that appends a row to a grid. Each app
has one control **deliberately** left with a weak/missing locator, for a realistic
reason specific to its framework:

- **WinForms** (`panel1`): the designer's auto-generated default name was never
  renamed — extremely common in legacy WinForms codebases, and WinForms conveniently
  (or inconveniently) surfaces `Control.Name` as the UIA `AutomationId`.
- **WPF** (`CompanyPanel`, a `GroupBox`): WPF never infers `AutomationId` from
  `x:Name` — you have to set `AutomationProperties.AutomationId` explicitly, and
  forgetting to is the single most common cause of brittle WPF UI automation.

`SelfHealingResolver` never looks at `AutomationId` when scoring candidates — that's
precisely the piece of information that's missing or wrong when it gets invoked.

## Why .NET Framework 4.8 for the FlaUI-dependent projects

`FlaUI.Core` / `FlaUI.UIA3` 5.0.0 only ship .NET Framework binaries. `WinFormsApp`,
`Discovery`, `SelfHealing`, and `ScenarioRunner` all target `net48` for that reason.
`WpfApp` targets `net8.0-windows` — its TFM is irrelevant to the test tooling since UI
Automation talks to it as an external process either way.

## Running locally

Requires Windows (UI Automation / FlaUI.UIA3 do not work anywhere else, and WinForms
targets .NET Framework).

```powershell
dotnet build AutomationSandbox.sln --configuration Debug
dotnet test TestAutomation/ScenarioRunner/ScenarioRunner.csproj --configuration Debug --no-build
```

## Continuous Integration

The primary development environment for this repo is Linux, which cannot run UI
Automation or a WinForms/.NET Framework runtime at all. `.github/workflows/ci.yml`
builds and runs the full test suite — including live FlaUI/UIA scenarios against both
running apps — on a `windows-latest` GitHub-hosted runner on every push, which is the
only practical way to validate this code without owning a Windows machine.

## Status

WinFormsApp, WpfApp, Discovery, and SelfHealing are all implemented and validated
end-to-end on real Windows via CI, for both target frameworks. Not yet started: an
LLM-assisted resolution step for locators the heuristic scorer can't confidently match.
