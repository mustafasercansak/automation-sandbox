# AGENTS.md

Guidance for AI coding agents working in this repository. Read this before making changes.

## Project overview

**Automation Sandbox** is an open-source locator healing and intent-driven test generation engine for Windows desktop and web. Desktop support is built on [FlaUI](https://github.com/FlaUI/FlaUI) (Microsoft UI Automation); web support on the `Microsoft.Playwright` .NET SDK. When a locator (`AutomationId` or DOM locator) breaks due to a UI refactor, the engine re-resolves the element using a deterministic, pure-heuristic structural similarity scorer, with an opt-in multi-provider LLM fallback (Claude, Gemini, OpenAI and any OpenAI-compatible endpoint, plus offline Ollama) that is guarded against hallucinated picks and accepted only by consensus between independent providers. It is an open alternative to the black-box locator recovery in commercial tools — not a replacement for their full suite (IDE, recorder, execution grid), which is deliberately out of scope.

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
- `Directory.Build.props` carries shared NuGet pack metadata (Authors, MIT license expression, version `0.2.0-beta.2`, `Deterministic` builds). Packable by default; demo apps and the test project opt out with `IsPackable=false`.

### Healing pipeline (how the pieces interact)

1. `Discovery.UiTreeWalker` captures a live `UiElementInfo` tree from the app under test.
2. `SelfHealingResolver.Resolve` scores every node with `SimilarityScorer` (weighted sum of ControlType 0.20, ParentControlType 0.20, SiblingPosition 0.15, Name/Levenshtein 0.20, Position 0.25 within `PositionToleranceRadius` 300px), prunes below `MinCandidateScore` (0.05), and accepts the top candidate only if its score ≥ `MinimumConfidence` (0.50) **and** its `EvidenceCoverage` (fraction of weight backed by non-null signals) ≥ `MinimumEvidenceWeight` (0.40) **and** the runner-up margin (`best - runnerUp`) ≥ `MinimumCandidateMargin` (0.05, via the shared `UiModel.CandidateMargin` helper). The evidence gate applies to LLM picks too; the margin gate does not.
3. `ResolveAsync` only calls LLM providers when the heuristic result is not confident. It sends a Top-N shortlist (`MaxCandidatesForLlm` = 20) with synthetic `CandidateId`s (`c0`, `c1`, …) to every available provider in parallel. **Acceptance is by consensus, not confidence** (#10, decided in #19): at least `MinimumConsensusVotes` (2) providers must independently name the same `CandidateId`. Self-reported confidence is recorded (`LlmConfidence` = mean of the agreeing providers) but never compared or thresholded — one model's 0.72 and another's 0.95 are not on the same scale. `MinimumLlmConfidence` still exists on `SimilarityWeights` and is still recorded, but gates nothing; do not reintroduce it as an acceptance rule. Votes are filtered by the Hallucination Guard **before** counting — a `CandidateId` outside the shortlist costs that provider its vote without discarding anyone else's. A single configured provider, a three-way split, and a tie for the lead all mean "no consensus" and degrade to the heuristic result. `HealResult.AgreedProviders` records who agreed, ordinally sorted.
4. Any signal missing on both sides (empty `Name`, empty `ParentControlType`, zero sibling metadata, or a `(0,0,0,0)` bounding rectangle) scores `null`, never `1.0`; its weight is excluded from the denominator so missing data is neither penalized nor falsely rewarded. The healing report (`HealingReportDocument.CurrentSchemaVersion`, currently **v7**) records every resolution attempt with an explicit `Outcome` (`accepted`, `accepted-unverified`, `retry-failed`, `ambiguous`, `low-evidence`, `low-confidence`, `no-candidates`, `no-consensus`, `provider-error`, or `unspecified`), `Platform`, `ProposedSnapshot`, and `ProviderErrors` (#82). `unspecified` must remain distinct from `low-confidence`: it means a decision path failed to classify itself and must not contaminate measured calibration data. The report also preserves every scored candidate — unpruned — with `TotalScore`/`Components`/`EvidenceCoverage`, plus the winner's `RunnerUpScore`, `AgreedProviders` (v5, #10) and `ProviderAttempts` (v6, #11), for offline threshold sweeps. `HealingReportDocument.AcceptedEvents` supplies the accepted-only compatibility view; legacy entries with a null `Outcome` count as accepted because pre-v7 reports contained accepted heals only. Older reports upgrade in place on the next write; only newer-than-current schemas are rejected. When you add a field, bump the version and make it nullable — `null` on an upgraded entry means "this build did not record it", which an empty value would wrongly claim as "there was none".

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

**Tests must assert behavior, not implementation.** This is what makes refactoring possible: #48 rewrote all four providers onto a shared base with "every existing test passes unchanged" as its acceptance criterion, and that criterion only worked because the tests asserted what the resolver *does*. Tests that pin internals would have had to be rewritten alongside the code, and the safety net would have evaporated exactly when it was needed.

**When a refactor forces tests to be rewritten, account for every test that disappears.** The "no test changes" criterion above only applies when the behaviour is unchanged *and* the model is. Sometimes it genuinely changes — #71 replaced `AppPairSurveyRecord` with `AppHopSurveyRecord`, so the old tests could not compile and had to be rewritten rather than kept.

That is where coverage leaks. The positive path gets rewritten because it is the thing being built; the **rejection paths quietly vanish**, because a test that asserts something is *refused* is invisible unless you look for it. #71 lost `RejectsUnsettledApps` and `RejectsLowEmptyIdFraction` while the `if (!v1.Settled)` and `if (maxEmptyFraction < MinimumEmptyAutomationIdFraction)` branches they guarded stayed in the code, untested.

Same check as the one for documentation headings, applied to test names:

```bash
git diff -- '*Tests.cs' | grep -E "^-.*public (async )?(void|Task) [A-Za-z_]+"
```

Every name it prints must be one you deliberately dropped, and a dropped test needs a replacement covering the same behaviour under the new model. Test count staying flat is not evidence that nothing was lost — #71 went 269 → 269 while two behaviours fell out of coverage.

**Match the verification to the failure mode; a unit test is not always it.** Of roughly ten real defects found across #10, #11, #47, #48 and the `0.2.0-beta.2` release, exactly one was caught by a test — the suite was green for all the others:

| Failure mode | What catches it |
| :--- | :--- |
| Behavior change | A test asserting that behavior |
| Platform-specific language or API use | Compiling for `net48` — see the cross-platform notes above |
| Resource leaks (an undisposed `HttpResponseMessage`) | Code review; the lines are covered and the tests pass |
| A field silently never populated | An end-to-end run, then a test locking it in |
| Documentation drift or lost sections | Diffing the removed headings |
| Package metadata | Opening the dry-run artifact before publishing |
| Suite slowdown | Measuring the duration |

Coverage is blind to most of this by construction, because it counts execution rather than verification (see #54). The `ProviderAttempts` bug lived on fully covered lines: the paths ran, nothing asserted the result.

## Code style guidelines

- Follow the existing file's conventions: file-scoped vs. block namespaces (existing code uses block-scoped `namespace X { }` with 4-space indentation), `Nullable`/`ImplicitUsings` settings differ per project — don't "fix" them globally.
- Comments are plain, explanatory prose that documents *why* (trade-offs, calibration caveats, guard semantics), not *what*. Keep that tone when editing; update comments when behavior changes.
- Keep the cross-platform boundary clean: no FlaUI, Windows-only APIs, or `net48`-only constructs in `UiModel`, `SelfHealing`, or `LlmHealing` — they must stay compilable for `netstandard2.0`.
- **The boundary is violated in both directions, and the modern-into-`net48` direction is the one that keeps breaking CI.** `ScenarioRunner` targets `net48` alongside `net8.0` on Windows, and the core libraries target `netstandard2.0`, so several ordinary C# features are unavailable there:
  - `record` types and `init` accessors need `System.Runtime.CompilerServices.IsExternalInit` (.NET 5+). A positional record compiles fine on `net8.0` and fails the Windows leg with `CS0518` — this cost a red CI run on #47. Use a plain class with get-only properties, as every other DTO here does.
  - `KeyValuePair<K,V>` deconstruction — `foreach (var (key, value) in dictionary)` — needs `KeyValuePair.Deconstruct`, added in .NET Core 2.0 / netstandard2.1. Use `kvp.Key` / `kvp.Value`. Deconstructing a `ValueTuple` returned from a method is fine; only the dictionary form breaks.
  - `required` members, `Random.Shared`, `DateOnly`/`TimeOnly`/`Half`, `Index`/`Range` (`^1`, `a..b`), `Dictionary.TryAdd`, `Enumerable.ToHashSet`, `Math.Clamp`, `StringBuilder.AppendJoin` and the `StringComparison`/`char` overloads of `string.Contains` are likewise unavailable. For thread-safe randomness use `[ThreadStatic] private static Random?`, which is what `LlmHttpTransport` does.

  **Linux cannot catch any of this, and cannot be made to.** `dotnet build`/`dotnet test` on Linux only ever compiles `net8.0`, so a green local run proves nothing about `net48`. Forcing it with `-p:TargetFrameworks=net48` does not work either: the property cascades to every referenced project, and `UiModel`/`SelfHealing`/`LlmHealing`/`IntentAutomation` do not target `net48` at all, so the build collapses on `System.Net.Http` long before reaching your code.

  **The trap is not always a missing method name — it is often a missing overload.** `new Dictionary<string, int>(someReadOnlyDictionary)` compiles on `net8.0` because `Dictionary(IEnumerable<KeyValuePair<,>>)` exists there; on `net48` only `Dictionary(IDictionary<,>)` and `Dictionary(int capacity)` exist, so an `IReadOnlyDictionary` argument silently binds to the capacity overload and fails with `CS1503`. Use `.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)`. Grepping for API names will not find this class of problem.

  **Only `ScenarioRunner` is exposed.** `UiModel`, `SelfHealing`, `LlmHealing`, `IntentAutomation` and `WebDiscovery` all target `netstandard2.0`, whose surface is close enough to `net48` that a local `dotnet build -f netstandard2.0` catches these mistakes before they reach CI. `ScenarioRunner` has no such target — it is `net8.0` on Linux and `net48;net8.0` on Windows — which is why every failure of this kind has landed in test code rather than library code. When writing in `ScenarioRunner`, ask whether the API you are reaching for would compile in one of the `netstandard2.0` libraries; if you are unsure, it probably would not.

  **Worse than an uncompilable file is a file that is never compiled at all.** Eight files are `Compile Remove`d outside `net48` — including `OpenSourceAppSurveyRunner.cs`, `WindowsAppSurveyRunner.cs` and the live test files. On Linux they are not type-checked, not parsed, not seen. A rename in shared code leaves them referencing a member that no longer exists and nothing warns you: #71 renamed `Pairs` to `Chains` and left `OpenSourceAppSurveyLiveTests.cs` broken through a full green Linux run. So a verification plan that offers `dotnet build AutomationSandbox.sln` or `dotnet test` on Linux as evidence for a change in one of these files is offering no evidence at all — #78's plan did exactly that. When you touch a `Compile Remove`d file, say plainly that Linux cannot verify it and name the Windows CI run that can.

  So: either build on Windows, or check your diff against the list above and then treat the Windows CI leg as the actual gate — do not merge on a green Linux leg alone. This class of failure has now broken CI four times (#23, #24, and twice on #47).
- Scoring must remain deterministic and allocation-conscious (O(N) flattening, no per-call provider requirements). `SimilarityWeights.Default` values are tuned against exactly the two demo scenarios — treat changes to defaults as behavior changes needing test updates.
- Public API shape is part of the product (`Resolve`, `ResolveAsync`, `ScoreCandidates`, `DiscoveryOptions`/`DiscoveryResult` are documented in the README with examples). If you change them, update `README.md` (and `PROJECT_SHOWCASE.md` if it describes the same behavior).

## Every change is tied to an issue

`main` is protected, so all work arrives through a PR. Open (or reuse) an issue first and link the PR to it: the issue carries **why**, the PR carries **what changed**. A PR with no issue loses the reasoning the moment it is merged.

This is not bookkeeping. Recording the reasoning up front is what has kept decisions from being relitigated: #48 opened with the duplication measured in lines, so the design discussion started from the number instead of from opinion; #47 recorded that it was blocked by #11, and that ordering held on its own weeks later; #10 recorded which alternatives #19 had rejected, which stopped a later plan from quietly reintroducing them.

When something is discovered mid-change that is real but out of scope, file it rather than widening the PR — #48, #54 and #56 all came out of work on other issues.

### Open the issue before the code, and merge only what was reviewed

Two failures here are cheap to prevent and expensive to unwind, and both happened repeatedly in one day.

**The issue comes first, then the branch, then the edit.** An issue opened after the code exists cannot be closed by the PR that implements it: #74, #76 and #77 all merged with no closing reference because their issues were written once the work was already in flight, leaving #73 open behind merged code and, at one point, two open issues (#73 and #75) describing the same goal. Retroactive linkage also loses what the issue is for — the reasoning as it stood *before* the implementation.

**Do not merge while review findings are still uncommitted.** #79 was merged with the reviewed fix sitting unstaged in a working tree. Because its body said `Closes #78`, the merge closed the issue too, so a defect that was already understood — a container check matching `"Portable"` as `"table"` — landed on `main` with nothing tracking it, and #78 had to be reopened to get it back. Before merging, confirm that the code on the branch is the code that was reviewed. If a finding is not going in, say so in the PR and move it to a follow-up issue; do not let a merge decide it silently.

**`Closes #N` only when the PR finishes the issue.** The keyword closes the issue on merge, so it is a claim that every acceptance criterion is now met — not that some code shipped. Two issues had to be reopened the same day for want of that check: #85 required the diagrams to render and they no longer parsed; #97 required the LLM arm to be measured and only the harness had been built. Both PRs were good work; both closed an issue that was not finished.

Before writing the keyword, read the issue's acceptance criteria and confirm this PR ticks every box — literally. Tick the checkboxes in the issue body as each criterion is verified during review, so that at merge time "is this finished?" is something you can see rather than reconstruct. An issue with unticked boxes is not ready for a closing keyword, and if a box cannot honestly be ticked, say why in a comment instead of closing around it. When it delivers only part, write `Part of #N` instead — the link still shows in the timeline, the issue stays open, and what remains gets listed in a comment on it. Dropping closing keywords altogether is worse and was tried first: #74, #76 and #77 merged with no reference at all, which left #73 open behind merged code and produced a second issue (#75) describing the same goal.

## Design principles

**Duplicated code is a defect, not a style preference.** Extract it. The judgement is *how*, and the deciding question is whether the duplicated parts share state:

- Shared behaviour only → compose. `LlmHttpTransport` is a static helper because retry/backoff needs no per-provider state.
- Shared behaviour **and** shared fields → inherit. `HttpLlmHealingProvider` (#48) exists because the four providers repeated ~45 lines of orchestration *plus* six fields and their validation; a static helper would have left the fields duplicated and needed seven arguments at every call site. Removing it was net −375 lines with zero test edits.

Keep the template method non-virtual and put only the genuinely vendor-specific parts behind abstract hooks. If a subclass has to fight the template, the template is wrong.

**But do not abstract speculatively.** Three deliberate non-abstractions in this codebase would all be "fixed" by a naive reading of SOLID, and all three are correct:

- `LlmIntentPlanner` duplicates the retry semantics instead of sharing `HttpLlmHealingProvider`, because `IntentAutomation` must not depend on `LlmHealing` (#11). A clean package boundary outranks removing one copy.
- `ClaudeHealingProvider` and `GeminiHealingProvider` are not collapsed into `OpenAiHealingProvider` even though both vendors offer OpenAI-compatible endpoints, because Claude's `thinking = disabled` / `output_config.effort = low` have no equivalent in that wire format and exist to avoid paying for reasoning tokens. Tidiness does not outrank a per-call cost.
- `SimilarityScorer` and `SelfHealingResolver` are static and procedural on purpose. Scoring must stay deterministic and allocation-conscious; injecting interfaces there adds indirection and buys nothing.

So: the SOLID ideas worth applying here are the ones about **substitutability and stable contracts** — an `ILlmHealingProvider` implementation must behave like every other one (its `Name` unique, its failures returned rather than thrown), and a public abstraction's shape is part of the product. Interface-per-class, constructor injection everywhere, and layers of indirection are not goals; each abstraction has to pay for the indirection it introduces.

**On TypeScript:** there is no TypeScript source in this repository. TypeScript appears only as *output* from `PlaywrightTypeScriptTestGenerator`. Rules about idiomatic TypeScript therefore apply to the emitted test code — it must be code a TypeScript developer would accept in review — not to anything under `TestAutomation/`.

## Documentation is part of every change

**Behavior change and documentation change land in the same commit.** Documentation here is not a nice-to-have that trails the code — it is what the next agent reads to decide how the system works, so stale documentation actively causes wrong implementations. This has already happened: `README.md`'s central sequence diagram and this file's own pipeline section both kept describing single-provider LLM acceptance after #10 replaced it with consensus, and a published release note claimed live provider calls were "unverified in CI" while the Windows CI leg had been making a real Gemini call on every run.

Before opening a PR, check every surface below and update the ones your change touched. If none needed updating, that should be because you checked, not because you didn't look.

| Surface | Update when |
| :--- | :--- |
| `AGENTS.md` | Any change to acceptance rules, thresholds, schema versions, test conventions, workflows, or the module layout. **The healing-pipeline section is the one most likely to go stale — re-read it whenever resolver behavior changes.** |
| `README.md` | Public API shape, defaults, the resolution-flow sequence diagram, the implementation-status and test-coverage tables, the roadmap, and target frameworks per project. The diagram is the most-read part and the easiest to forget. |
| `docs/*.md` | The detailed guides — `llm-providers.md` for provider and consensus behavior, `nuget-packaging.md` for release mechanics. These are published to GitHub Pages, so they are user-facing. Keep the English and Turkish sections in sync; updating only one is worse than updating neither, because the other silently becomes a contradiction. |
| `PROJECT_SHOWCASE.md` | When it describes behavior your change altered. |
| Release notes in `release.yml` | Every behavior change that reaches a package. Breaking changes go at the top, and a change that fails silently (like consensus disabling single-provider healing) must say so explicitly — users get no error to tell them. |
| Package `<Description>` in each `.csproj` | When a package gains or loses a capability. This metadata is frozen inside the `.nupkg` at pack time and cannot be corrected after publishing. |

Three rules that follow from the same principle:

- **Never document intent as fact.** If a threshold is an estimate rather than a measured value, say so where it is documented. The calibration caveats in `README.md` and `SimilarityWeights` exist for this reason.
- **When you remove a behavior, remove its documentation in the same change** — and where the old rule was prominent, say explicitly that it no longer applies. `MinimumLlmConfidence` still exists as a property, so its documentation now states that it gates nothing; deleting the mention would have left readers guessing.
- **Never overwrite or truncate entire documentation files.** Always perform targeted in-place edits (`replace_file_content`). Existing setup guides, step-by-step instructions, examples, notes, and dual-language sections (English and Turkish) must be preserved in full. Do not replace entire files with partial summaries. Adding a section is not a licence to drop neighbouring ones: while `LlmProviderFactory` documentation was being added to `docs/llm-providers.md`, the file went **net −94 lines** and took the Ollama setup guide, the GitHub Models section, "Naming providers" and the entire Turkish resilience section with it — nothing in the change looked wrong, the content was simply gone.

  Verify the outcome rather than trusting the method, because a targeted edit can still swallow sections. Before opening a PR, list what your diff removed:

  ```bash
  git diff docs/llm-providers.md | grep -E "^-#{2,4} "   # headings this change deleted
  ```

  Every heading it prints must be one you meant to remove. For the bilingual guides also confirm the halves still line up — same section count, same order, one-to-one.

## CI and deployment

- `.github/workflows/ci.yml` — on push/PR to `main` and manual dispatch, runs across a matrix (`windows-latest` for `net48` full suite including FlaUI live tests; `ubuntu-latest` for `net8.0` cross-platform core and web suite): sets up .NET 8 and 10 SDKs, restores, builds Debug, installs the Playwright Chromium browser (`playwright.ps1 install chromium`), runs ScenarioRunner tests with XPlat Code Coverage and a TRX logger, uploads test results and coverage as artifacts. `ANTHROPIC_API_KEY`/`GEMINI_API_KEY` repo secrets are optional; the LLM comparison test self-skips without them.
- `.github/workflows/pack.yml` — manual `workflow_dispatch` only, deliberately separate from CI. Packs `UiModel`, `SelfHealing`, `LlmHealing`, `Discovery`, `WebDiscovery`, `IntentAutomation`, and `PlaywrightLiveExploration` in Release and uploads `.nupkg` files as a build artifact. There is intentionally no `dotnet nuget push` step — no publish feed has been chosen yet. Do not add publishing without explicit instruction.
- `.github/workflows/release.yml` — manual `workflow_dispatch` with a `dry_run` input; packs the same seven libraries, uploads artifacts (`if: always()`), and creates the GitHub release. Release notes are built with a **literal** PowerShell here-string (`@'...'@`) and a `__TAG__` placeholder: an expandable here-string treats backticks as escapes and silently corrupts the notes.
- `.github/workflows/llm-smoke.yml` — live smoke test against any OpenAI-compatible endpoint. The nightly schedule is deliberately commented out: GitHub Models first answered `HTTP 410 github_models_retirement_brownout` and its inference API was then fully retired on July 30, 2026; a nightly nobody can fix trains everyone to ignore red (#44). Endpoint and model come from repo variables, so switching backends is two variables and a secret, no code change.
- `.github/workflows/nightly-consensus.yml` — nightly (02:00 UTC) and manual multi-provider consensus evaluation on Linux/`net8.0` (#47). Its first live step is a **gating** Gemini + Groq/Llama assertion on `Desktop_AmbiguousSiblingTabs` (#84): both providers must return shortlist-valid votes for the ground-truth candidate and appear in `AgreedProviders`, with the outcome written to `$GITHUB_STEP_SUMMARY`. No credentials means a clean opt-in skip; exactly one configured required provider is a hard failure proving quorum is real. The broader `CONSENSUS_EVALUATION=1` run follows as non-gating data collection, includes optional Cloudflare Workers AI when its token/account/model triplet is configured, writes the full JSON artifact and Markdown table, and may record provider rate limits/failures without failing on its aggregate metrics. These flags are set only by this workflow so `ci.yml` does not consume live quota on each PR. `GITHUB_TOKEN` is deliberately **not** exported: `OpenAiHealingProvider` could otherwise send it to the wrong endpoint. The live gate is nightly-only, not a release gate, because third-party availability must not block a manual package release.
- `.github/workflows/docs.yml` — publishes `docs/` to GitHub Pages.

**Action versions.** Pin every `actions/*` use to its **current major**, and never let two majors of the same action coexist in this repository. Patches then arrive on their own, which is what pinning a major is for — this is not a rule to chase every new release on sight.

The failure it exists to prevent already happened: `upload-artifact` ran at `v7` in three workflows and `v4` in the two most recently added ones, because a new workflow was written by copying an older file and inherited whatever that file was pinned to. `download-artifact` sat four majors behind. **When you add a workflow, take the action versions from the newest workflow in the directory, not from whichever file you copied.**

**A deprecation warning is a work item, not decoration.** The `Node.js 20 is deprecated` line printed on every run for some time and was read as background text; it was announcing that the runtime those pinned actions depend on is being removed. If a warning appears on every run, either act on it or record why it is being accepted — do not let it become scenery.

Most workflows here are `workflow_dispatch` or scheduled, so **a pull request does not exercise them**. Only `ci.yml` and `docs.yml` run automatically. After changing any other workflow, dispatch it once and confirm it still works: `release.yml` has a `dry_run` input for exactly this, and `windows-app-survey.yml` is the one where an upload/download major mismatch would break a real data path rather than merely warn.
- Planned (not implemented — don't assume they exist): NuGet release (no publish feed chosen yet), a real Model Context Protocol bridge (deliberately rejected in favor of `PlaywrightLiveExplorer` — see `docs/intent-driven-automation.md`).

## Security considerations

- LLM providers read API keys from environment variables (`ANTHROPIC_API_KEY`, `GEMINI_API_KEY`, `OPENAI_API_KEY` with a `GITHUB_TOKEN` fallback, `CLOUDFLARE_API_TOKEN`, `OLLAMA_HOST`/`OLLAMA_MODEL`/`OLLAMA_ENABLED`); never hardcode keys or commit them. Cloudflare is created only when its token, `CLOUDFLARE_ACCOUNT_ID`, and `CLOUDFLARE_MODEL` are all present; the account ID and model belong in repo variables, while the token is a secret. Keys are optional everywhere — code and tests must degrade gracefully when they are absent. Model names belong in repo **variables**, not secrets: `ci.yml` reads `vars.GEMINI_MODEL`, and a value stored as a secret is silently invisible to it. GitHub Actions substitutes an unset variable with an empty string rather than a missing env var, which is why the providers use `NullIfEmpty(...)` before falling back to their defaults.
- Authentication headers are set on each `HttpRequestMessage`, never on `HttpClient.DefaultRequestHeaders`. All four providers share one static `HttpClient` (on `HttpLlmHealingProvider`), so a default header would leak one vendor's key into another's request.
- A `Retry-After` longer than `LlmHttpTransport.MaxRetryAfter` (10s) is treated as quota exhaustion and fails fast instead of sleeping: a provider answering `Retry-After: 3600` must not stall a test run for an hour.
- The LLM fallback must never bypass the Hallucination Guard: an LLM pick is valid only as a `CandidateId` that exactly matches the shortlist it was sent. `AutomationId` is not a safe lookup key (it can be empty or duplicated — that is the bug class this project heals).
- LLM prompt size is bounded by `MaxCandidatesForLlm`; keep it that way so live UI data sent to third-party APIs stays minimal.
