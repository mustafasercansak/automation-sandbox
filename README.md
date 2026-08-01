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

- **UiModel** — the shared `UiElementInfo` tree snapshot + JSON (de)serializer that
  every other layer builds on. No FlaUI dependency, `netstandard2.0`.
- **Discovery** — walk an application's entire UI tree via UI Automation and produce a
  `UiElementInfo` snapshot.
- **SelfHealing** — when a locator (typically an `AutomationId`) breaks, compare the
  current UI tree against the last known snapshot and re-locate the element using
  structural similarity (control type, parent/sibling context, name, screen position) —
  a plain heuristic scorer, no LLM involved.
- **LlmHealing** — a separate comparison harness: sends the same broken-locator
  scenario to multiple LLM providers (Claude, Gemini) in parallel and prints their
  answers side by side, to evaluate whether an LLM-assisted resolution step is worth
  adding to `SelfHealingResolver` as a fallback for low-confidence heuristic matches.
  Not wired into the production path — a research tool, not a fallback chain.
- **ScenarioRunner** — xUnit tests that exercise all of the above against real,
  running applications.

## Repository structure

```
AutomationSandbox.sln
├── WinFormsApp/            .NET Framework 4.8 WinForms app under test
├── WpfApp/                 .NET 8 WPF app under test
└── TestAutomation/
    ├── UiModel/             Shared UI tree snapshot + JSON (de)serializer (no FlaUI dependency)
    ├── Discovery/           UI tree walker (FlaUI.Core, FlaUI.UIA3)
    ├── SelfHealing/         Structural-similarity locator resolver (no FlaUI dependency)
    ├── LlmHealing/          Multi-provider LLM comparison harness for broken locators (no FlaUI dependency)
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
`Discovery`, and `ScenarioRunner` all target `net48` for that reason. `WpfApp` targets
`net8.0-windows` — its TFM is irrelevant to the test tooling since UI Automation talks
to it as an external process either way. `UiModel`, `SelfHealing`, and `LlmHealing`
have no FlaUI dependency at all and target `netstandard2.0`, so `net48` projects can
consume them and they can also be exercised directly from a `net8.0` (or any modern)
console app — useful for iterating on this logic without a Windows machine.

## LLM-assisted resolution (evaluation only)

`LlmHealingProviderTests` (mocked `HttpMessageHandler`, no network, no key) is part of
the required suite and checks the prompt/parsing/plumbing logic. `LlmHealingEvaluationTests.CompareProviders_OnLiveBrokenLocator`
is the live counterpart: it sends the same real broken-locator scenario used by
`SelfHealing_BrokenAutomationId_...` to every configured LLM provider and prints a
comparison. It's opt-in: with neither `ANTHROPIC_API_KEY` nor `GEMINI_API_KEY` set, it's
a no-op by design — it does not fail the build.

To include it in your own CI run, add repository secrets `ANTHROPIC_API_KEY` and/or
`GEMINI_API_KEY` (Settings → Secrets and variables → Actions) — `.github/workflows/ci.yml`
already passes them through if present. Locally, just set the environment variable
before running `dotnet test`.

`GeminiHealingProvider` targets Google's **Interactions API**
(`v1beta/interactions`, `x-goog-api-key` header), not the older per-model
`generateContent` endpoint — as of writing, Google's own docs label `generateContent`
legacy and steer new code to Interactions. This was confirmed via the current docs
(WebFetch), not a live call, so treat the request/response shape as "verified against
documentation, not exercised against a real key" until someone runs it with a key.
Override the model via the `GEMINI_MODEL` environment variable if requests start
failing — this API surface has already changed shape once and may again.

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
end-to-end on real Windows via CI, for both target frameworks. LlmHealing exists as an
opt-in comparison harness (Claude + Gemini); both providers' HTTP request/response
handling are covered by `LlmHealingProviderTests` against a mocked handler, but neither
has been exercised against a live API key yet, and no decision has been made on whether
to promote either into `SelfHealingResolver` as a production fallback.
