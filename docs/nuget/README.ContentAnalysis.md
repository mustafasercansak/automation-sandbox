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

### Reporting across a multi-page scan

`ContentAnalysisReportFileSink` persists findings as an append-only JSON Lines log (one `ContentAnalysisReportEntry` per page, the same pattern `AutomationSandbox.SelfHealing`'s `HealingReportFileSink` uses) plus an optional HTML dashboard, so a scan across many pages accumulates one readable report instead of an in-memory list per call:

```csharp
var sink = new ContentAnalysisReportFileSink("content-report.json"); // content-report.html alongside it

foreach (var url in urlsToScan)
{
    await session.NavigateAsync(url);
    var dom = await session.CaptureAsync();
    var issues = await ContentAnalyzer.AnalyzeAsync(dom, new ClaudeContentAnalysisProvider());
    sink.Record(ContentAnalysisReportEntry.FromAnalysis(url, dom, issues));
}
```

`ContentAnalysisReportEntry.PassageCount` (via `ContentAnalyzer.CountPassages`) records how much text a page actually had: zero passages on a page expected to show text is a signal the DOM was captured before dynamic content rendered, not that the page is clean.

## Related packages

- `AutomationSandbox.WebDiscovery` — the `WebElementInfo` DOM model this package analyzes (transitive).
- `AutomationSandbox.PlaywrightLiveExploration` — typically supplies the captured `WebElementInfo` tree via `PlaywrightWebSession`/`PlaywrightLiveExplorer`, though this package has no dependency on it.

## Documentation

See `docs/intent-driven-automation.md` in the [Automation Sandbox documentation](https://github.com/mustafasercansak/automation-sandbox/tree/main/docs).
