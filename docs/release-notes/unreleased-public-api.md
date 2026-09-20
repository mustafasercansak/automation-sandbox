# Unreleased: public API

Nothing pending. The breaking changes staged here (namespace migration #399, API freeze #400)
shipped in `v0.2.0-beta.6`; see that release's notes for the full breaking-changes list.

Since `v0.2.0-beta.6`, two new packages (`AutomationSandbox.ContentAnalysis` #452,
`AutomationSandbox.IntentExecution` #458) and additive surface on existing packages (the full
`PlaywrightWebSession` interaction/assertion vocabulary #449/#451/#456, `SelfHealing`'s
`HealingReportSummary` #446) have shipped. All of it is additive — no existing public member was
renamed, removed, or had its signature changed — so there is still no breaking change to stage here.

Stage the next breaking change here as it lands, and note which release it must ship with.
