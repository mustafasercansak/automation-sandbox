# AGENTS.md

Guidance for AI coding agents working in this repository. Read this before making changes.

## Project overview

**Automation Sandbox** is an open-source locator healing and intent-driven test generation engine for Windows desktop and web. Desktop support is built on [FlaUI](https://github.com/FlaUI/FlaUI) (Microsoft UI Automation); web support on the `Microsoft.Playwright` .NET SDK. When a locator (`AutomationId` or DOM locator) breaks due to a UI refactor, the engine re-resolves the element using a deterministic, pure-heuristic structural similarity scorer, with an opt-in LLM fallback chain (Claude/Gemini/OpenAI/Ollama) guarded against hallucinated picks. It is an open alternative to the black-box locator recovery in commercial tools — not a replacement for their full suite (IDE, recorder, execution grid), which is deliberately out of scope.

Single Visual Studio solution: `AutomationSandbox.sln`. Everything is C#; there is no JavaScript/Python/Rust tooling, no `package.json`, no `pyproject.toml`. `PlaywrightLiveExploration` depends on the `Microsoft.Playwright` NuGet package, but that's a fully managed .NET client - it does not require Node.js at runtime, so this claim still holds. A real Model Context Protocol bridge (the canonical Playwright MCP server is Node.js-based) was deliberately rejected for exactly this reason - see `docs/intent-driven-automation.md`.

### Projects and module divisions

- `WinFormsApp/` — .NET Framework 4.8 (`net48`) WinForms demo app under test. Deliberately contains an unrenamed `panel1` (WinForms auto-surfaces `Control.Name` as UIA `AutomationId`) as the broken-locator case study. Not packable.
- `WpfApp/` — WPF demo app under test (`net8.0-windows`; also `net10.0-windows` when built with a .NET 10 SDK). Contains a `GroupBox` with no explicit `AutomationProperties.AutomationId` (WPF never infers it from `x:Name`) as its case study. Not packable.
- `TestAutomation/UiModel/` — Framework-agnostic UI tree model: `UiElementInfo`, `BoundingRectangle`, `CandidateScore`, `ScoreComponents`, `UiElementSnapshot`, `UiTreeSerializer`. Also owns the persistent locator repository: `LocatorRepositoryDocument`/`LocatorRecord`/`LocatorHealingHistoryEntry`, `LocatorRepositorySerializer` (versioned JSON), and `LocatorRepository` (concurrency-safe file-backed load/save/upsert). No FlaUI dependency.
- `TestAutomation/Discovery/` — Live UI tree capture via FlaUI.Core/FlaUI.UIA3: `UiTreeWalker`, `ApplicationConnector`, `DiscoveryOptions` (MaxDepth, MaxElements, Timeout, CancellationToken, IncludeOffscreen, IgnoredControlTypes/ClassNames) and `DiscoveryResult` telemetry. `net48`-only because FlaUI 5.0.0 ships .NET Framework binaries only.
- `TestAutomation/WebDiscovery/` — M4 web foundation: framework-agnostic DOM snapshot DTOs, `WebElementMapper` to convert web trees into `UiElementInfo`, `PlaywrightDomCaptureScript` for `page.EvaluateAsync` with regular DOM, open Shadow DOM, same-origin iframe, and hidden/offscreen metadata support, and `PlaywrightLocatorEmitter` for `GetByTestId`/`GetByRole`/CSS locator suggestions.
- `TestAutomation/SelfHealing/` — Core engine: `SelfHealingResolver` (`Resolve` / `ResolveAsync` / `ScoreCandidates`), `SimilarityScorer` (explainable `ScoreComponents` breakdown), `SimilarityWeights` (tunables, self-`Validate()`ing), `HealResult`, `LocatorHealingHistoryEntryFactory` (bridges a `HealResult` into the `UiModel`-owned `LocatorHealingHistoryEntry` a `LocatorRepository` persists).
- `TestAutomation/LlmHealing/` — `ILlmHealingProvider` with `ClaudeHealingProvider`, `GeminiHealingProvider`, `OpenAiHealingProvider`, and the offline `OllamaHealingProvider`, all four deriving from `HttpLlmHealingProvider` (#48): the base owns shared state, constructor validation and the non-virtual `ResolveAsync` template, while subclasses supply only `IsAvailable`, `UnavailableErrorMessage`, `CreateRequest(prompt)` and `ExtractText(body)`. HTTP goes through `LlmHttpTransport` (#11) for retry, backoff and the dual timeout. Also `LlmHealingEvaluator`, `LlmHealingPrompt`, `LlmHealingResult`. `ILlmHealingProvider.Name` must be unique within a run — `AgreedProviders`/`ProviderAttempts` key on it, and `OpenAiHealingProvider` takes any OpenAI-compatible endpoint, so several instances of it can legitimately be configured at once (pass `name:`).
- `TestAutomation/IntentAutomation/` — M6 intent-driven automation: `IIntentPlanner` with `DeterministicIntentPlanner` (keyword-based) and `LlmIntentPlanner` (opt-in, Claude-backed, guarded fallback to the deterministic planner); web (`IntentExplorationBridge`, `WebElementInfo`-based) and desktop (`IntentDesktopExplorationBridge`, `UiElementInfo`-based) candidate matching; `IntentLocatorRepositoryRecorder`/`IntentDesktopLocatorRepositoryRecorder`; `PlaywrightCSharpTestGenerator`/`PlaywrightTypeScriptTestGenerator`/`FlaUiCSharpTestGenerator`; `IntentAutomationPipeline`/`IntentDesktopAutomationPipeline` orchestration; `IntentFlowReportDocument` (web only, see docs). No FlaUI/Windows dependency.
- `TestAutomation/PlaywrightLiveExploration/` — `PlaywrightLiveExplorer`: launches a browser via the `Microsoft.Playwright` .NET SDK, navigates to a URL, and captures a `WebElementInfo` snapshot, so callers don't have to hand-write a Playwright test just to feed `IntentAutomationPipeline`/`IntentExplorationBridge`. No Node.js dependency (see note above).
- `TestAutomation/ScenarioRunner/` — xUnit test suite (the only test project). Contains live UIA scenario tests, live headless-Chromium scenario tests, pure-logic/explainability tests, synthetic benchmarks, and an LLM provider comparison harness.

Dependency direction: `UiModel` ← `LlmHealing` ← `SelfHealing` ← `ScenarioRunner`; `UiModel` ← `Discovery` ← `ScenarioRunner`; `UiModel` ← `WebDiscovery` ← `ScenarioRunner`; `UiModel`/`WebDiscovery` ← `IntentAutomation` ← `ScenarioRunner`; `WebDiscovery` ← `PlaywrightLiveExploration` ← `ScenarioRunner`. The demo apps are referenced by tests only as compiled executables, not as project references.

## Technology stack and runtime architecture

- Language: C# with `<LangVersion>latest</LangVersion>`, `ImplicitUsings` and `Nullable` enabled in all `TestAutomation/*` projects; both disabled in the two demo apps (match the file you're editing).
- Target frameworks:
  - Core libraries (`UiModel`, `SelfHealing`, `LlmHealing`): `netstandard2.0;net8.0`, plus `net10.0` automatically via an MSBuild condition on `$(NETCoreSdkVersion)` ≥ 10. They have zero FlaUI/Windows dependency, so the heuristic engine and its pure-logic tests run cross-platform (Linux CI included).
  - `Discovery`, `ScenarioRunner`, `WinFormsApp`: `net48` (Windows-only; FlaUI UIA3 needs Windows UIA COM APIs).
  - `WpfApp`: `net8.0-windows` (+ `net10.0-windows` conditionally), with `EnableWindowsTargeting`.
- Key packages: FlaUI.Core/FlaUI.UIA3 5.0.0, System.Text.Json 8.0.5, xunit 2.5.3, Microsoft.NET.Test.Sdk 17.8.0, coverlet.collector 6.0.0.
- `Directory.Build.props` carries shared NuGet pack metadata (Authors, MIT license expression, version `0.2.0-beta.1`, `Deterministic` builds). Packable by default; demo apps and the test project opt out with `IsPackable=false`.

### Healing pipeline (how the pieces interact)

1. `Discovery.UiTreeWalker` captures a live `UiElementInfo` tree from the app under test.
2. `SelfHealingResolver.Resolve` scores every node with `SimilarityScorer` (weighted sum of ControlType 0.20, ParentControlType 0.20, SiblingPosition 0.15, Name/Levenshtein 0.20, Position 0.25 within `PositionToleranceRadius` 300px), prunes below `MinCandidateScore` (0.05), and accepts the top candidate only if its score ≥ `MinimumConfidence` (0.50) **and** its `EvidenceCoverage` (fraction of weight backed by non-null signals) ≥ `MinimumEvidenceWeight` (0.40) **and** the runner-up margin (`best - runnerUp`) ≥ `MinimumCandidateMargin` (0.05, via the shared `UiModel.CandidateMargin` helper). The evidence gate applies to LLM picks too; the margin gate does not.
3. `ResolveAsync` only calls LLM providers when the heuristic result is not confident. It sends a Top-N shortlist (`MaxCandidatesForLlm` = 20) with synthetic `CandidateId`s (`c0`, `c1`, …) to every available provider in parallel. **Acceptance is by consensus, not confidence** (#10, decided in #19): at least `MinimumConsensusVotes` (2) providers must independently name the same `CandidateId`. Self-reported confidence is recorded (`LlmConfidence` = mean of the agreeing providers) but never compared or thresholded — one model's 0.72 and another's 0.95 are not on the same scale. `MinimumLlmConfidence` still exists on `SimilarityWeights` and is still recorded, but gates nothing; do not reintroduce it as an acceptance rule. Votes are filtered by the Hallucination Guard **before** counting — a `CandidateId` outside the shortlist costs that provider its vote without discarding anyone else's. A single configured provider, a three-way split, and a tie for the lead all mean "no consensus" and degrade to the heuristic result. `HealResult.AgreedProviders` records who agreed, ordinally sorted.
4. Any signal missing on both sides (empty `Name`, empty `ParentControlType`, zero sibling metadata, or a `(0,0,0,0)` bounding rectangle) scores `null`, never `1.0`; its weight is excluded from the denominator so missing data is neither penalized nor falsely rewarded. The healing report (`HealingReportDocument.CurrentSchemaVersion`, currently **v6**) records every scored candidate — unpruned — with `TotalScore`/`Components`/`EvidenceCoverage`, plus the winner's `RunnerUpScore`, `AgreedProviders` (v5, #10) and `ProviderAttempts` (v6, #11), for offline threshold sweeps. Older reports upgrade in place on the next write; only newer-than-current schemas are rejected. When you add a field, bump the version and make it nullable — `null` on an upgraded entry means "this build did not record it", which an empty value would wrongly claim as "there was none".

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

All tests live in `TestAutomation/ScenarioRunner/` (xUnit; multi-targeted for `net48` on Windows and `net8.0` on Linux/cross-platform). Categories:

- **Live UIA scenario tests** (`MainFormScenarioTests.cs`, `WpfMainWindowScenarioTests.cs`): launch the compiled demo app executables via FlaUI UIA3 and exercise discovery and healing end-to-end. Windows-only (`net48`); they resolve the app via a relative path (`..\..\..\..\..\WinFormsApp\bin\Debug\net48\WinFormsApp.exe`), so a Debug build of the whole solution must happen first.
- **Pure-logic tests** (`SelfHealingResolverTests.cs`, `SelfHealingResolverExplainabilityTests.cs`, `UiElementSnapshotTests.cs`, `SyntheticTreeBenchmarkTests.cs`, etc.): in-memory trees, no Windows dependency; the benchmark tests run a 3,000+ control tree. The core libraries target `netstandard2.0` and `net8.0`, running cross-platform across both Windows (`net48`) and Linux (`net8.0`) in CI (`ci.yml`).
- **LLM tests**: `LlmHealingProviderTests.cs` uses mocked HTTP responses (always runs). `OpenAiAndOllamaHealingProviderTests.cs` covers `OpenAiHealingProvider` and `OllamaHealingProvider` the same way (mocked HTTP, always runs). `LlmHealingEvaluationTests.cs` is a side-by-side provider comparison harness, not a required assertion: providers are skipped when `ANTHROPIC_API_KEY` / `GEMINI_API_KEY` are unset, and with neither set the test is a deliberate no-op. Do not make it fail when keys are absent. `LlmIntentPlannerTests.cs` follows the same mocked-HTTP pattern as `LlmHealingProviderTests.cs` (always runs, no API key needed). **Any test that can trigger a retry must pass `delayAsync: (_, _) => Task.CompletedTask`** — providers and `LlmIntentPlanner` take that hook precisely so backoff is not waited on in tests. This includes timeout tests, which are not obviously retry tests but are: a timed-out attempt is retried like any other transient failure, and with the real backoff six such tests cost ~5s of every CI run.
- **Live browser tests** (`PlaywrightLiveExplorerTests.cs`): launch a real headless Chromium via the `Microsoft.Playwright` .NET SDK against a local `file://` HTML fixture (no network dependency for the page itself). Requires the one-time `playwright install chromium` browser download - run `pwsh TestAutomation/ScenarioRunner/bin/Debug/<framework>/playwright.ps1 install chromium` after a Debug build if browsers aren't already cached.

When changing scoring, discovery, or resolver behavior, add or update tests here — the project convention is that every documented behavior in the README has a corresponding test.

## Code style guidelines

- Follow the existing file's conventions: file-scoped vs. block namespaces (existing code uses block-scoped `namespace X { }` with 4-space indentation), `Nullable`/`ImplicitUsings` settings differ per project — don't "fix" them globally.
- Comments are plain, explanatory prose that documents *why* (trade-offs, calibration caveats, guard semantics), not *what*. Keep that tone when editing; update comments when behavior changes.
- Keep the cross-platform boundary clean: no FlaUI, Windows-only APIs, or `net48`-only constructs in `UiModel`, `SelfHealing`, or `LlmHealing` — they must stay compilable for `netstandard2.0`.
- Scoring must remain deterministic and allocation-conscious (O(N) flattening, no per-call provider requirements). `SimilarityWeights.Default` values are tuned against exactly the two demo scenarios — treat changes to defaults as behavior changes needing test updates.
- Public API shape is part of the product (`Resolve`, `ResolveAsync`, `ScoreCandidates`, `DiscoveryOptions`/`DiscoveryResult` are documented in the README with examples). If you change them, update `README.md` (and `PROJECT_SHOWCASE.md` if it describes the same behavior).

## CI and deployment

- `.github/workflows/ci.yml` — on push/PR to `main` and manual dispatch, runs across a matrix (`windows-latest` for `net48` full suite including FlaUI live tests; `ubuntu-latest` for `net8.0` cross-platform core and web suite): sets up .NET 8 and 10 SDKs, restores, builds Debug, installs the Playwright Chromium browser (`playwright.ps1 install chromium`), runs ScenarioRunner tests with XPlat Code Coverage and a TRX logger, uploads test results and coverage as artifacts. `ANTHROPIC_API_KEY`/`GEMINI_API_KEY` repo secrets are optional; the LLM comparison test self-skips without them.
- `.github/workflows/pack.yml` — manual `workflow_dispatch` only, deliberately separate from CI. Packs `UiModel`, `SelfHealing`, `LlmHealing`, `Discovery`, `WebDiscovery`, `IntentAutomation`, and `PlaywrightLiveExploration` in Release and uploads `.nupkg` files as a build artifact. There is intentionally no `dotnet nuget push` step — no publish feed has been chosen yet. Do not add publishing without explicit instruction.
- `.github/workflows/release.yml` — manual `workflow_dispatch` with a `dry_run` input; packs the same seven libraries, uploads artifacts (`if: always()`), and creates the GitHub release. Release notes are built with a **literal** PowerShell here-string (`@'...'@`) and a `__TAG__` placeholder: an expandable here-string treats backticks as escapes and silently corrupts the notes.
- `.github/workflows/llm-smoke.yml` — live smoke test against any OpenAI-compatible endpoint. The nightly schedule is deliberately commented out: GitHub Models answered `HTTP 410 github_models_retirement_brownout` and a nightly nobody can fix trains everyone to ignore red (#44). Endpoint and model come from repo variables, so switching backends is two variables and a secret, no code change.
- `.github/workflows/docs.yml` — publishes `docs/` to GitHub Pages.
- Planned (not implemented — don't assume they exist): NuGet release (no publish feed chosen yet), a real Model Context Protocol bridge (deliberately rejected in favor of `PlaywrightLiveExplorer` — see `docs/intent-driven-automation.md`).

## Security considerations

- LLM providers read API keys from environment variables (`ANTHROPIC_API_KEY`, `GEMINI_API_KEY`, `OPENAI_API_KEY` with a `GITHUB_TOKEN` fallback, `OLLAMA_HOST`/`OLLAMA_MODEL`/`OLLAMA_ENABLED`); never hardcode keys or commit them. Keys are optional everywhere — code and tests must degrade gracefully when they are absent. Model names belong in repo **variables**, not secrets: `ci.yml` reads `vars.GEMINI_MODEL`, and a value stored as a secret is silently invisible to it. GitHub Actions substitutes an unset variable with an empty string rather than a missing env var, which is why the providers use `NullIfEmpty(...)` before falling back to their defaults.
- Authentication headers are set on each `HttpRequestMessage`, never on `HttpClient.DefaultRequestHeaders`. All four providers share one static `HttpClient` (on `HttpLlmHealingProvider`), so a default header would leak one vendor's key into another's request.
- A `Retry-After` longer than `LlmHttpTransport.MaxRetryAfter` (10s) is treated as quota exhaustion and fails fast instead of sleeping: a provider answering `Retry-After: 3600` must not stall a test run for an hour.
- The LLM fallback must never bypass the Hallucination Guard: an LLM pick is valid only as a `CandidateId` that exactly matches the shortlist it was sent. `AutomationId` is not a safe lookup key (it can be empty or duplicated — that is the bug class this project heals).
- LLM prompt size is bounded by `MaxCandidatesForLlm`; keep it that way so live UI data sent to third-party APIs stays minimal.
