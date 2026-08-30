# AutomationSandbox.SelfHealing

The core locator-healing engine. When a locator (`AutomationId` or DOM locator) breaks after a UI refactor, `SelfHealingResolver` re-resolves the element with a deterministic, explainable structural-similarity scorer (control type, parent, sibling position, name, and geometry, with tunable `SimilarityWeights`), gated on confidence, evidence coverage, and runner-up margin. An opt-in multi-provider LLM fallback requires an independent-agreement quorum before a pick is accepted, and every attempt — accepted or not — is recorded in an auditable healing report (`HealingReportDocument`, schema v8).

## Install

```bash
dotnet add package AutomationSandbox.SelfHealing --prerelease
```

## Target frameworks

- `netstandard2.0`
- `net8.0`
- `net10.0`

## Typical use

```csharp
HealResult result = SelfHealingResolver.Resolve(expectedSnapshot, liveTreeRoot);

// Opt-in LLM fallback (only consulted when the heuristic answer is not confident):
HealResult result = await SelfHealingResolver.ResolveAsync(
    expectedSnapshot, liveTreeRoot, providers: LlmProviderFactory.CreateConfiguredProviders());

// Explicit opt-in batch reconciliation over one shared tree:
IReadOnlyList<HealResult> batch = SelfHealingResolver.ResolveBatch(expectedSnapshots, liveTreeRoot);
```

`AutomationSandbox.UiModel` and `AutomationSandbox.LlmHealing` are pulled in transitively. Add `AutomationSandbox.Discovery` (Windows desktop) or `AutomationSandbox.WebDiscovery` (web DOM) to produce the live trees this package scores.

## Related packages

- `AutomationSandbox.Discovery` — live FlaUI/UIA tree capture (net48, Windows only).
- `AutomationSandbox.WebDiscovery` + `AutomationSandbox.PlaywrightLiveExploration` — web DOM capture.
- `AutomationSandbox.LlmHealing` — the LLM provider implementations used by `ResolveAsync`.

## Documentation

Scoring, thresholds, and the healing-report schema are documented in the [Automation Sandbox documentation](https://github.com/mustafasercansak/automation-sandbox/tree/main/docs) — start with `docs/healing-reports.md` and `docs/llm-providers.md`.
