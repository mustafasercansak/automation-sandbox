# Unreleased: public API

Nothing pending. The breaking changes staged here (namespace migration #399, API freeze #400)
shipped in `v0.2.0-beta.6`; see that release's notes for the full breaking-changes list.

Since `v0.2.0-beta.6`, two new packages (`AutomationSandbox.ContentAnalysis` #452,
`AutomationSandbox.IntentExecution` #458) and additive surface on existing packages (the full
`PlaywrightWebSession` interaction/assertion vocabulary #449/#451/#456, `SelfHealing`'s
`HealingReportSummary` #446) have shipped, plus a further round since then:

- `ContentAnalysis`: `ContentAnalysisReportFileSink`/`ContentAnalysisReportDocument`/
  `ContentAnalysisReportEntry`/`ContentAnalysisReportHtmlRenderer` (append-only JSON Lines + HTML
  reporting across a multi-page scan, #471) and `ContentAnalyzer.CountPassages` (#471); `<pre>`/
  `<code>` subtrees are now excluded from passage extraction (#479 - a behavior fix, not an API
  change).
- `PlaywrightLiveExploration`: `PlaywrightWebSession.WaitForTextChangeAsync`/`WaitForTextAsync`
  (wait for an already-visible element's text to change or reach a specific value - `WaitForVisibleAsync`
  alone does nothing for content that re-renders in place, e.g. a SPA language switch) and
  `GetLinksAsync` (#475); `SiteCrawler`/`SiteCrawlOptions`/`SiteCrawlResult`/`SiteCrawlFailure`
  (breadth-first same-origin site walk from one URL, #475).
- `IntentAutomation`/`IntentExecution`: a `Wait` step's `AssertionKind`/`ExpectedValue` (otherwise
  only meaningful on `Assert` steps) now means "wait for this exact/contained text" instead of only
  "wait for visibility", across live web execution and both Playwright code generators (#480/#483)
  and the FlaUI code generator (#482) alike - `AssertionKind.None` (the default) is unchanged, so
  this is additive, not a behavior change for existing scenarios. A pre-existing bug fixed
  alongside it: a `Wait` step's timeout `Value` meant seconds in live web execution but
  milliseconds in all three code generators; it is milliseconds everywhere now (#481).
- Product direction repositioned: web and desktop are equal-priority platforms (#469, superseding
  #402) - no API surface change, but worth noting alongside a release, since #402's framing was
  published in `v0.2.0-beta.6`'s own release notes.

All of it is additive — no existing public member was renamed, removed, or had its signature
changed — so there is still no breaking change to stage here.

Stage the next breaking change here as it lands, and note which release it must ship with.
