# AGENTS.md

Guidance for AI coding agents working in this repository. Read this before making changes.

## Project overview

**Automation Sandbox** is a transparent, explainable, self-healing UI test automation engine for Windows desktop applications, built on [FlaUI](https://github.com/FlaUI/FlaUI) (Microsoft UI Automation). When a locator (`AutomationId`) breaks due to a UI refactor, the engine re-resolves the element using a deterministic, pure-heuristic structural similarity scorer, with an opt-in LLM fallback chain (Claude/Gemini) guarded against hallucinated picks. It is an open alternative to commercial black-box tools (Ranorex, Tosca).

Single Visual Studio solution: `AutomationSandbox.sln`. Everything is C#; there is no JavaScript/Python/Rust tooling, no `package.json`, no `pyproject.toml`.

### Projects and module divisions

- `WinFormsApp/` — .NET Framework 4.8 (`net48`) WinForms demo app under test. Deliberately contains an unrenamed `panel1` (WinForms auto-surfaces `Control.Name` as UIA `AutomationId`) as the broken-locator case study. Not packable.
- `WpfApp/` — WPF demo app under test (`net8.0-windows`; also `net10.0-windows` when built with a .NET 10 SDK). Contains a `GroupBox` with no explicit `AutomationProperties.AutomationId` as its case study: WPF falls back to `FrameworkElement.Name` (`x:Name`) for the `AutomationId`, so renaming `x:Name` in a refactor silently breaks id-based locators without a compile-time warning. Not packable.
- `TestAutomation/UiModel/` — Framework-agnostic UI tree model: `UiElementInfo`, `BoundingRectangle`, `CandidateScore`, `ScoreComponents`, `UiElementSnapshot`, `UiTreeSerializer`. Also owns the persistent locator repository: `LocatorRepositoryDocument`/`LocatorRecord`/`LocatorHealingHistoryEntry`, `LocatorRepositorySerializer` (versioned JSON), and `LocatorRepository` (concurrency-safe file-backed load/save/upsert). No FlaUI dependency.
- `TestAutomation/Discovery/` — Live UI tree capture via FlaUI.Core/FlaUI.UIA3: `UiTreeWalker`, `ApplicationConnector`, `DiscoveryOptions` (MaxDepth, MaxElements, Timeout, CancellationToken, IncludeOffscreen, IgnoredControlTypes/ClassNames) and `DiscoveryResult` telemetry. `net48`-only because FlaUI 5.0.0 ships .NET Framework binaries only.
- `TestAutomation/WebDiscovery/` — M4 web foundation: framework-agnostic DOM snapshot DTOs, `WebElementMapper` to convert web trees into `UiElementInfo`, `PlaywrightDomCaptureScript` for `page.EvaluateAsync` with regular DOM, open Shadow DOM, same-origin iframe, and hidden/offscreen metadata support, and `PlaywrightLocatorEmitter` for `GetByTestId`/`GetByRole`/CSS locator suggestions.
- `TestAutomation/SelfHealing/` — Core engine: `SelfHealingResolver` (`Resolve` / `ResolveAsync` / `ScoreCandidates`), `SimilarityScorer` (explainable `ScoreComponents` breakdown), `SimilarityWeights` (tunables, self-`Validate()`ing), `HealResult`, `LocatorHealingHistoryEntryFactory` (bridges a `HealResult` into the `UiModel`-owned `LocatorHealingHistoryEntry` a `LocatorRepository` persists).
- `TestAutomation/LlmHealing/` — `ILlmHealingProvider` with `ClaudeHealingProvider` and `GeminiHealingProvider` (HTTP via `HttpClient`), `LlmHealingEvaluator`, `LlmHealingPrompt`, `LlmHealingResult`.
- `TestAutomation/ScenarioRunner/` — xUnit test suite (the only test project). Contains live UIA scenario tests, pure-logic/explainability tests, synthetic benchmarks, and an LLM provider comparison harness.

Dependency direction: `UiModel` ← `LlmHealing` ← `SelfHealing` ← `ScenarioRunner`; `UiModel` ← `Discovery` ← `ScenarioRunner`; `UiModel` ← `WebDiscovery` ← `ScenarioRunner`. The demo apps are referenced by tests only as compiled executables, not as project references.

## Technology stack and runtime architecture

- Language: C# with `<LangVersion>latest</LangVersion>`, `ImplicitUsings` and `Nullable` enabled in all `TestAutomation/*` projects; both disabled in the two demo apps (match the file you're editing).
- Target frameworks:
  - Core libraries (`UiModel`, `SelfHealing`, `LlmHealing`): `netstandard2.0;net8.0`, plus `net10.0` automatically via an MSBuild condition on `$(NETCoreSdkVersion)` ≥ 10. They have zero FlaUI/Windows dependency, so the heuristic engine and its pure-logic tests run cross-platform (Linux CI included).
  - `Discovery`, `ScenarioRunner`, `WinFormsApp`: `net48` (Windows-only; FlaUI UIA3 needs Windows UIA COM APIs).
  - `WpfApp`: `net8.0-windows` (+ `net10.0-windows` conditionally), with `EnableWindowsTargeting`.
- Key packages: FlaUI.Core/FlaUI.UIA3 5.0.0, System.Text.Json 8.0.5, xunit 2.5.3, Microsoft.NET.Test.Sdk 17.8.0, coverlet.collector 6.0.0.
- `Directory.Build.props` carries shared NuGet pack metadata (Authors, MIT license expression, version `0.1.0-preview`, `Deterministic` builds). Packable by default; demo apps and the test project opt out with `IsPackable=false`.

### Healing pipeline (how the pieces interact)

1. `Discovery.UiTreeWalker` captures a live `UiElementInfo` tree from the app under test.
2. `SelfHealingResolver.Resolve` scores every node with `SimilarityScorer` (weighted sum of ControlType 0.20, ParentControlType 0.20, SiblingPosition 0.15, Name/Levenshtein 0.20, Position 0.25 within `PositionToleranceRadius` 300px), prunes below `MinCandidateScore` (0.05), and accepts the top candidate if its score ≥ `MinimumConfidence` (0.50).
3. `ResolveAsync` only calls LLM providers when the heuristic result is not confident. It sends a Top-N shortlist (`MaxCandidatesForLlm` = 20) with synthetic `CandidateId`s (`c0`, `c1`, …). A pick is accepted only if its self-reported confidence ≥ `MinimumLlmConfidence` (0.50) **and** its `CandidateId` is an exact member of the shortlist (the "Hallucination Guard"). Any provider failure or rejection degrades safely to the heuristic result.
4. Elements with a `(0,0,0,0)` bounding rectangle get `PositionScore = null`; that weight is excluded from the denominator so offscreen controls are neither penalized nor falsely rewarded.

## Build and test commands

Requirements: Windows (for FlaUI/UIA3 and `net48`), .NET SDK 8.0 and/or 10.0, .NET Framework 4.8 Developer Pack. On Linux only the cross-platform core builds; the `net48`/Windows projects and live tests will not run.

```powershell
# Restore and build everything (Debug is required — ScenarioRunner tests
# hardcode the path to WinFormsApp\bin\Debug\net48\WinFormsApp.exe)
dotnet restore AutomationSandbox.sln
dotnet build AutomationSandbox.sln --configuration Debug

# Run the full test suite (live UIA + pure logic + benchmarks)
dotnet test TestAutomation/ScenarioRunner/ScenarioRunner.csproj --configuration Debug --no-build

# Code coverage (writes coverage.cobertura.xml under TestResults/)
dotnet test TestAutomation/ScenarioRunner/ScenarioRunner.csproj --collect:"XPlat Code Coverage"

# Pack the four libraries (artifacts only; no publish feed is configured)
dotnet pack TestAutomation/<Project>/<Project>.csproj --configuration Release
```

## Testing strategy

All tests live in `TestAutomation/ScenarioRunner/` (xUnit, net48). Categories:

- **Live UIA scenario tests** (`MainFormScenarioTests.cs`, `WpfMainWindowScenarioTests.cs`): launch the compiled demo app executables via FlaUI UIA3 and exercise discovery and healing end-to-end. Windows-only; they resolve the app via a relative path (`..\..\..\..\..\WinFormsApp\bin\Debug\net48\WinFormsApp.exe`), so a Debug build of the whole solution must happen first.
- **Pure-logic tests** (`SelfHealingResolverTests.cs`, `SelfHealingResolverExplainabilityTests.cs`, `UiElementSnapshotTests.cs`, `SyntheticTreeBenchmarkTests.cs`, `DiscoveryRobustnessTests.cs`): in-memory trees, no Windows dependency; the benchmark tests run a 3,000+ control tree and also execute on Linux CI.
- **LLM tests**: `LlmHealingProviderTests.cs` uses mocked HTTP responses (always runs). `LlmHealingEvaluationTests.cs` is a side-by-side provider comparison harness, not a required assertion: providers are skipped when `ANTHROPIC_API_KEY` / `GEMINI_API_KEY` are unset, and with neither set the test is a deliberate no-op. Do not make it fail when keys are absent.

When changing scoring, discovery, or resolver behavior, add or update tests here — the project convention is that every documented behavior in the README has a corresponding test.

## Code style guidelines

- Follow the existing file's conventions: file-scoped vs. block namespaces (existing code uses block-scoped `namespace X { }` with 4-space indentation), `Nullable`/`ImplicitUsings` settings differ per project — don't "fix" them globally.
- Comments are plain, explanatory prose that documents *why* (trade-offs, calibration caveats, guard semantics), not *what*. Keep that tone when editing; update comments when behavior changes.
- Keep the cross-platform boundary clean: no FlaUI, Windows-only APIs, or `net48`-only constructs in `UiModel`, `SelfHealing`, or `LlmHealing` — they must stay compilable for `netstandard2.0`.
- Scoring must remain deterministic and allocation-conscious (O(N) flattening, no per-call provider requirements). `SimilarityWeights.Default` values are tuned against exactly the two demo scenarios — treat changes to defaults as behavior changes needing test updates.
- Public API shape is part of the product (`Resolve`, `ResolveAsync`, `ScoreCandidates`, `DiscoveryOptions`/`DiscoveryResult` are documented in the README with examples). If you change them, update `README.md` (and `PROJECT_SHOWCASE.md` if it describes the same behavior).

## CI and deployment

- `.github/workflows/ci.yml` — on push/PR to `main` and manual dispatch, on `windows-latest`: sets up .NET 8 and 10 SDKs, restores, builds Debug, runs the full ScenarioRunner suite with XPlat Code Coverage and a TRX logger, uploads test results and coverage as artifacts. `ANTHROPIC_API_KEY`/`GEMINI_API_KEY` repo secrets are optional; the LLM comparison test self-skips without them.
- `.github/workflows/pack.yml` — manual `workflow_dispatch` only, deliberately separate from CI. Packs `UiModel`, `SelfHealing`, `LlmHealing`, `Discovery`, and `WebDiscovery` in Release and uploads `.nupkg` files as a build artifact. There is intentionally no `dotnet nuget push` step — no publish feed has been chosen yet. Do not add publishing without explicit instruction.
- Planned (not implemented — don't assume they exist): live browser-backed Playwright capture tests, NuGet release.

## Security considerations

- LLM providers read API keys from environment variables (`ANTHROPIC_API_KEY`, `GEMINI_API_KEY`); never hardcode keys or commit them. Keys are optional everywhere — code and tests must degrade gracefully when they are absent.
- The LLM fallback must never bypass the Hallucination Guard: an LLM pick is valid only as a `CandidateId` that exactly matches the shortlist it was sent. `AutomationId` is not a safe lookup key (it can be empty or duplicated — that is the bug class this project heals).
- LLM prompt size is bounded by `MaxCandidatesForLlm`; keep it that way so live UI data sent to third-party APIs stays minimal.
