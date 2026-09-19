# AutomationSandbox.ContentAnalysis

Content-quality checks over a captured `WebElementInfo` tree. Zero-dependency heuristics (duplicate consecutive word, leftover template placeholders like `TODO`/`lorem ipsum`, 4+ repeated punctuation characters) always run; an optional single-provider Claude review (`ClaudeContentAnalysisProvider`) adds grammar/spelling/meaning judgment the heuristics can't make. The LLM layer is guarded the same way `AutomationSandbox.IntentAutomation`'s `LlmIntentPlanner` is: no dependency on `AutomationSandbox.LlmHealing`'s multi-provider consensus machinery, and it degrades to heuristic-only results on any failure or missing API key — it never throws.

## Install

```bash
dotnet add package AutomationSandbox.ContentAnalysis --prerelease
```

## Target frameworks

- `netstandard2.0`
- `net8.0`
- `net10.0`

## Typical use

```csharp
WebElementInfo dom = await session.CaptureAsync(); // AutomationSandbox.PlaywrightLiveExploration

// Heuristics only, no dependency beyond WebDiscovery:
IReadOnlyList<ContentIssue> issues = ContentAnalyzer.RunHeuristics(dom);

// Heuristics + an optional LLM review (skipped automatically without ANTHROPIC_API_KEY):
issues = await ContentAnalyzer.AnalyzeAsync(dom, new ClaudeContentAnalysisProvider());
```

## Related packages

- `AutomationSandbox.WebDiscovery` — the `WebElementInfo` DOM model this package analyzes (transitive).
- `AutomationSandbox.PlaywrightLiveExploration` — typically supplies the captured `WebElementInfo` tree via `PlaywrightWebSession`/`PlaywrightLiveExplorer`, though this package has no dependency on it.

## Documentation

See `docs/intent-driven-automation.md` in the [Automation Sandbox documentation](https://github.com/mustafasercansak/automation-sandbox/tree/main/docs).
