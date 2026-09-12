---
layout: default
title: Public API Audit
---

# Public API audit (#400)

## English

### Scope and measurement

Baseline: `8eec935` on `main`, before this change. The source inventory parses the seven library directories, includes public/protected declarations and interface members, excludes internal/private types, and combines partial declarations. It is a source review artifact, not a binary compatibility checker. Compiler-generated constructors and inherited members are not enumerated. No target-specific public declarations exist in the audited sources; Windows runtime validation remains separate.

| Metric | Before | After |
| :--- | ---: | ---: |
| Public types | 116 | 110 |
| Public properties with public setters | 358 | 338 |

The issue's original 360-property estimate was approximate. Re-running the same syntax-based inventory against the baseline and this branch gives the counts above. Get-only properties holding mutable objects are not counted as public setters and are not claimed to be deeply immutable.

Generate the [reviewable API list](public-api.json) from the repository root:

```sh
dotnet run --project eng/PublicApiAudit -- . docs/public-api.json
```

The exporter uses Roslyn from the selected .NET SDK without adding a NuGet dependency. Use .NET 8 or 10; build the libraries with .NET 10 for the same analyzer/compiler combination as CI.

### Package boundaries

All seven packages are retained. Fewer package names would not remove code: merging these boundaries would force optional platform or browser dependencies into consumers that do not use them.

| Package | Reason to retain |
| :--- | :--- |
| UiModel | Shared cross-platform evidence and persistence types; avoids circular dependencies between scoring and providers. |
| LlmHealing | Provider implementation and transport boundary; no provider configuration is required for heuristic use. |
| SelfHealing | Resolver, engine policy, and healing reports; the core consumer entry point. |
| Discovery | FlaUI/UIA3 and Windows dependencies must remain optional. |
| WebDiscovery | DOM mapping and locator suggestions remain usable without launching a browser. |
| PlaywrightLiveExploration | Isolates the live Microsoft.Playwright dependency and browser lifecycle. |
| IntentAutomation | Optional planning/recording/generation capability; core healing does not depend on it. |

### Mutability and visibility decisions

The audit retains deliberate authoring, configuration, persistence, and extension contracts. Making them immutable would remove supported editing workflows. Fixed scoring and locator proposals are constructor-set. This distinction is part of the reviewed API, not a claim that all DTOs are immutable.

The following implementation helpers are now internal: `AssertionCodeEmitter`, `CodeGenerationUtilities`, `IntentCandidateReviewEvaluator`, `IntentRecordingLookupTable<TRecording>`, `IntentRecordingLookupTable`, and `IntentTextScoring`. Their public callers remain the planners, exploration bridges, and generators. Existing helper behavior tests remain accessible through `InternalsVisibleTo("ScenarioRunner")`; none were deleted.

Every remaining public type is listed below. Setter counts are after the change.

| Package / type | Public setters | Decision |
| :--- | ---: | :--- |
| Discovery / `ApplicationConnector` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| Discovery / `DiscoveryOptions` | 7 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| Discovery / `DiscoveryResult` | 11 | Keep editable: traversal accumulates counters, warnings, and the tree as elements are visited. |
| Discovery / `UiTreeWalker` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `AssertGenerationMode` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `AssertionKind` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `AssertionKindExtensions` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `DeterministicIntentPlanner` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `FlaUiCSharpTestGenerationOptions` | 6 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| IntentAutomation / `FlaUiCSharpTestGenerator` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IIntentPlanner` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IntentActionType` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IntentAutomationPipeline` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IntentAutomationPipelineOptions` | 5 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| IntentAutomation / `IntentAutomationPipelineResult` | 6 | Keep editable: the pipeline derives Report after assembling planning, exploration, recordings, and source. |
| IntentAutomation / `IntentDesktopAutomationPipeline` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IntentDesktopAutomationPipelineOptions` | 4 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| IntentAutomation / `IntentDesktopAutomationPipelineResult` | 5 | Keep editable: the pipeline derives its report after assembling the desktop stage outputs. |
| IntentAutomation / `IntentDesktopElementCandidate` | 0 | Freeze five properties; referenced editable step/element objects are intentionally shared. |
| IntentAutomation / `IntentDesktopExplorationBridge` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IntentDesktopExplorationOptions` | 4 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| IntentAutomation / `IntentDesktopExplorationResult` | 2 | Keep editable: caller-supplied desktop exploration results are reviewed before repository recording. |
| IntentAutomation / `IntentDesktopLocatorRecordingOptions` | 4 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| IntentAutomation / `IntentDesktopLocatorRecordingResult` | 6 | Keep editable: generators accept consumer-authored or reviewed desktop recordings independently of the pipeline. |
| IntentAutomation / `IntentDesktopLocatorRepositoryRecorder` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IntentDesktopPlanningRequest` | 4 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| IntentAutomation / `IntentDesktopStepExplorationResult` | 4 | Keep editable: reviewers may curate desktop candidates and the review decision before recording. |
| IntentAutomation / `IntentElementCandidate` | 0 | Freeze six properties and copy the suggestion list; referenced editable step/element objects are intentionally shared. |
| IntentAutomation / `IntentExplorationBridge` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IntentExplorationOptions` | 4 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| IntentAutomation / `IntentExplorationResult` | 2 | Keep editable: caller-supplied exploration results are reviewed or curated before repository recording. |
| IntentAutomation / `IntentFlowReportDocument` | 12 | Keep editable: pipeline reporting and JSON consumers assemble, serialize, and review the report. |
| IntentAutomation / `IntentFlowReportFileSink` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IntentFlowReportHtmlRenderer` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IntentFlowReportStep` | 18 | Keep editable: reporting combines planning, exploration, and recording evidence into each persisted step. |
| IntentAutomation / `IntentLocatorKeySynthesizer` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IntentLocatorRecordingOptions` | 4 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| IntentAutomation / `IntentLocatorRecordingResult` | 6 | Keep editable: generators accept consumer-authored or reviewed web recordings independently of the pipeline. |
| IntentAutomation / `IntentLocatorRepositoryRecorder` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `IntentPlanningRequest` | 4 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| IntentAutomation / `IntentPlanningResult` | 3 | Keep editable: IIntentPlanner is an extension boundary; custom planners and reviewers refine scenarios and diagnostics. |
| IntentAutomation / `IntentScenario` | 4 | Keep editable: users and planners compose and review an ordered scenario before generation. |
| IntentAutomation / `IntentStep` | 8 | Keep editable: planners and reviewers refine action, target, and assertion data before execution/generation. |
| IntentAutomation / `IntentStepExplorationResult` | 4 | Keep editable: reviewers may curate the shortlist and diagnostic/review decision before recording. |
| IntentAutomation / `LlmIntentPlanner` | 1 | Keep sanitizer mutable: callers configure the disclosure policy before planning. |
| IntentAutomation / `LlmIntentPlanningPrompt` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `PlaywrightCSharpTestGenerationOptions` | 5 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| IntentAutomation / `PlaywrightCSharpTestGenerator` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| IntentAutomation / `PlaywrightTypeScriptTestGenerationOptions` | 3 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| IntentAutomation / `PlaywrightTypeScriptTestGenerator` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| LlmHealing / `ClaudeHealingProvider` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| LlmHealing / `GeminiHealingProvider` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| LlmHealing / `HttpLlmHealingProvider` | 3 | Keep tunables mutable: sanitizer and batch timeout overrides are deliberate provider-instance configuration. |
| LlmHealing / `ILlmHealingProvider` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| LlmHealing / `LlmHealingEvaluator` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| LlmHealing / `LlmHealingPrompt` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| LlmHealing / `LlmHealingResult` | 9 | Keep editable: custom ILlmHealingProvider implementations create proposals; the transport adds elapsed/attempt diagnostics after parsing. |
| LlmHealing / `LlmHttpResponse` | 7 | Keep editable: transport outcomes combine HTTP response, retry, timeout, and cancellation telemetry. |
| LlmHealing / `LlmHttpTransport` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| LlmHealing / `LlmProviderConfiguration` | 8 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| LlmHealing / `LlmProviderFactory` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| LlmHealing / `OllamaHealingProvider` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| LlmHealing / `OpenAiHealingProvider` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| PlaywrightLiveExploration / `PlaywrightLiveExplorer` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| PlaywrightLiveExploration / `PlaywrightLiveExplorerOptions` | 2 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| SelfHealing / `BatchHealingItemResult` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `BatchHealingRequest` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `BatchHealingResult` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `BatchReconciliationDisposition` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `HealResolutionStatus` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `HealResult` | 29 | Keep editable: fallback, ownership reconciliation, engine reporting, and custom integration results attach evidence in stages. |
| SelfHealing / `HealSource` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `HealingMode` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `HealingReportCandidate` | 6 | Keep editable: persisted report evidence is loaded and transformed for offline analysis. |
| SelfHealing / `HealingReportDocument` | 3 | Keep editable: file sinks append events and upgrade loaded report versions. |
| SelfHealing / `HealingReportEntry` | 27 | Keep editable: the engine records proposal evidence and adds execution outcome after the retry. |
| SelfHealing / `HealingReportFileSink` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `HealingReportHtmlRenderer` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `IHealingReportSink` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `LocatorHealingHistoryEntryFactory` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `ProfileCalibrationResult` | 11 | Keep editable: calibration accumulates per-probe counters; consumers can assemble measurements for reporting. |
| SelfHealing / `SelfHealingEngine` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `SelfHealingResolver` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `SimilarityWeights` | 14 | Keep tunables mutable: consumers configure weights and gates; Default returns a fresh instance. |
| SelfHealing / `SelfHealingTestBase` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `SelfHealingTestFixture` | 1 | Keep LogAction mutable: fixture consumers install diagnostic callbacks. |
| SelfHealing / `SelfHealingTestOptions` | 8 | Keep mutable: caller-authored configuration or planning input, validated by its consuming operation. |
| SelfHealing / `ThresholdProfile` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| SelfHealing / `TreeCalibrationReport` | 7 | Keep editable: application labels, measured profile results, and recommendations form an authorable report. |
| SelfHealing / `TreeCalibrator` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| UiModel / `BoundingRectangle` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| UiModel / `CandidateMargin` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| UiModel / `CandidateScore` | 5 | Keep editable: the resolver adds snapshot-local CandidateId values when materializing a provider shortlist; callers supply scored shortlists to providers. |
| UiModel / `LocatorHealingHistoryEntry` | 10 | Keep editable: accepted history is a versioned persistence DTO, also authored by integrations. |
| UiModel / `LocatorRecord` | 7 | Keep editable: Upsert and consumer review update stored snapshots, descriptions, and history. |
| UiModel / `LocatorRepository` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| UiModel / `LocatorRepositoryDocument` | 4 | Keep editable: callers load, edit, migrate, and save versioned locator documents. |
| UiModel / `LocatorRepositorySerializer` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| UiModel / `ScoreComponents` | 0 | Freeze five scalar properties: construct missing/zero/present evidence explicitly; JSON round-trip preserves nulls. |
| UiModel / `SensitiveDataSanitizer` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| UiModel / `UiElementInfo` | 12 | Keep editable: capture adapters build children and denormalize parent/sibling metadata; callers author stored evidence. |
| UiModel / `UiElementSnapshot` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| UiModel / `UiElementTreeExtensions` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| UiModel / `UiTreeSerializer` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| WebDiscovery / `PlaywrightApplicationConnector` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| WebDiscovery / `PlaywrightDomCaptureScript` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| WebDiscovery / `PlaywrightLocatorEmitter` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |
| WebDiscovery / `PlaywrightLocatorSuggestion` | 0 | Freeze four scalar properties: the strategy, expression, ranking, and explanation form one proposal. |
| WebDiscovery / `WebElementInfo` | 19 | Keep editable: DOM capture and consumer adapters assemble trees and frame metadata. |
| WebDiscovery / `WebElementMapper` | 0 | Retain public: supported operation, extension contract, enum, or existing read-only value; no public setter to remove. |

### Migration and verification

Use named constructor arguments for the four fixed proposal types:

```csharp
var components = new ScoreComponents(controlTypeScore: 1.0, nameScore: 0.8);
var locator = new PlaywrightLocatorSuggestion(
    strategy: "TestId", expression: "page.GetByTestId(\"save\")", confidence: 0.98);
var candidate = new IntentElementCandidate(
    step: step, element: element, score: 0.9, locatorSuggestions: new[] { locator });
```

These source-breaking changes belong in the next minor release alongside #399; see the [unreleased migration notes](release-notes/unreleased-public-api.md). Existing published packages are unchanged.

XML comments are generated for all seven libraries. Each library project enables documentation before the SDK computes output items. `Directory.Build.targets` evaluates after `IsPackable` is known, enables analyzers, and fails missing/malformed public documentation. `eng/Validate-NuGetPackages.ps1` opens every package and requires non-empty, assembly-matched XML documentation beside every library DLL.

Behavioral verification covers missing-versus-zero score evidence through JSON, round-trip preservation of locator proposals, and isolation from subsequent mutations of an input suggestion list. Existing rejection-path tests remain in place. The complete cross-platform suite and NUnit consumer fixture are required, together with `netstandard2.0` library compilation, package validation, security audit, and the Windows CI leg for UIA/net48 execution.

## Türkçe

### Kapsam ve ölçüm

İnceleme yedi paketin tamamını kapsar. Aynı kaynak envanteriyle public tip sayısı 116 → 110, public setter sayısı 358 → 338 olarak ölçülmüştür. Altı uygulama yardımcısı internal yapılmıştır; hiçbir mevcut davranış testi silinmemiştir.

Kaynak envanteri `dotnet run --project eng/PublicApiAudit -- . docs/public-api.json` komutuyla yeniden üretilir. Partial bildirimler birleştirilir; internal/private tipler, derleyicinin ürettiği constructor'lar ve kalıtımla gelen üyeler sayılmaz. Bu liste bir binary uyumluluk denetleyicisi değildir.

```sh
dotnet run --project eng/PublicApiAudit -- . docs/public-api.json
```

### Paket sınırları

Yedi paket korunmuştur: `UiModel` ortak veri modelini, `LlmHealing` provider katmanını, `SelfHealing` çekirdek iyileştirmeyi, `Discovery` Windows/FlaUI bağımlılığını, `WebDiscovery` tarayıcı başlatmadan DOM eşlemeyi, `PlaywrightLiveExploration` canlı tarayıcı bağımlılığını ve `IntentAutomation` isteğe bağlı planlama/kod üretimini ayırır. Paketleri birleştirmek, kullanılmayan platform bağımlılıklarını tüketicilere zorunlu kılacaktır.

### Değiştirilebilirlik ve görünürlük kararları

`ScoreComponents`, `PlaywrightLocatorSuggestion`, `IntentElementCandidate` ve `IntentDesktopElementCandidate` artık adlandırılmış constructor parametreleriyle oluşturulur. Adayın locator öneri listesi kopyalanır ve salt okunurdur. Adayın işaret ettiği senaryo adımı ve UI elemanı düzenlenebilir kalır; tüm ağacın derin değişmezliği vaat edilmez.

Ağaç oluşturma, ayar yapma, JSON belgelerini düzenleme, özel planner/provider uygulamaları ve inceleme akışları için gerekli değiştirilebilirlik korunmuştur. Yukarıdaki tablo her public tipin kararını kaydeder. Windows/FlaUI, canlı tarayıcı ve çekirdek bağımlılıklarını ayrı tutmak için yedi paket korunmuştur.

### Geçiş ve doğrulama

Örneğin `new ScoreComponents { NameScore = 0.8 }` yerine `new ScoreComponents(nameScore: 0.8)` kullanılır. Internal yapılan yardımcılar yerine public planner, exploration bridge ve test generator girişleri kullanılmalıdır. JSON round-trip, eksik/skoru sıfır olan kanıt ayrımı ve öneri listesinin sonradan değiştirilmesinden yalıtımı test edilir. Windows UIA çalıştırması için Windows CI sonucu gerekir.

```csharp
var components = new ScoreComponents(controlTypeScore: 1.0, nameScore: 0.8);
var locator = new PlaywrightLocatorSuggestion(
    strategy: "TestId", expression: "page.GetByTestId(\"save\")", confidence: 0.98);
var candidate = new IntentElementCandidate(
    step: step, element: element, score: 0.9, locatorSuggestions: new[] { locator });
```

Tüm paketler XML IntelliSense dosyası üretir; eksik public açıklamalar build hatasıdır ve paket doğrulayıcısı her DLL için XML dosyasını denetler. Bu kırıcı değişiklikler #399 namespace geçişiyle aynı sonraki minor sürümde yayımlanmalıdır. Mevcut yayımlanmış paketler bu kaynak değişikliğiyle değişmez.
