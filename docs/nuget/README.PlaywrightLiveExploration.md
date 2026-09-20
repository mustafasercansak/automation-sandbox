# AutomationSandbox.PlaywrightLiveExploration

Live browser page exploration through the Microsoft.Playwright .NET SDK. `PlaywrightLiveExplorer` launches a browser, navigates to a URL, and captures a `WebElementInfo` DOM snapshot — so callers of `AutomationSandbox.IntentAutomation` / `IntentExplorationBridge` do not have to hand-write a Playwright test just to obtain a page model. It is a fully managed .NET client and does not require Node.js at runtime.

## Install

```bash
dotnet add package AutomationSandbox.PlaywrightLiveExploration --prerelease
```

Playwright browsers are downloaded once per machine after the first build:

```bash
pwsh bin/Debug/net8.0/playwright.ps1 install chromium
```

## Target frameworks

- `netstandard2.0`
- `net8.0`
- `net10.0`

## Typical use

```csharp
await using var explorer = await PlaywrightLiveExplorer.LaunchAsync(
    new PlaywrightLiveExplorerOptions { Headless = true });

WebElementInfo snapshot = await explorer.CaptureAsync("https://example.com/login");
```

The snapshot feeds `AutomationSandbox.IntentAutomation` pipelines and `AutomationSandbox.WebDiscovery` mapping directly.

## Long-lived sessions, waiting for dynamic content, and crawling a site

`PlaywrightLiveExplorer` opens one page per call. `PlaywrightWebSession` reuses a single page across many actions instead - authenticate once, then fill/click/navigate repeatedly:

```csharp
await using var session = await PlaywrightWebSession.StartAsync(); // or StartAsync(storageStatePath: "auth.json") to skip login
await session.NavigateAsync("https://example.com/dashboard");
WebElementInfo dom = await session.CaptureAsync();
```

A capture is a point-in-time snapshot - it does not wait for a client-side re-render. `WaitForVisibleAsync` only helps when an element's *visibility* changes; a language switch or any other update that mutates an already-visible element's text needs `WaitForTextChangeAsync` instead:

```csharp
var before = await session.GetTextAsync("#greeting");
await session.ClickAsync("#language-switch");
await session.WaitForTextChangeAsync("#greeting", before); // resolves once the text actually differs
```

`SiteCrawler.CrawlAsync` walks a site breadth-first from a single starting URL - no hand-authored per-page navigation needed:

```csharp
var result = await SiteCrawler.CrawlAsync(
    session,
    "https://example.com",
    new SiteCrawlOptions { MaxPages = 50, MaxDepth = 3 }, // same-origin only by default
    onPageCaptured: async (url, dom, ct) =>
    {
        // e.g. run AutomationSandbox.ContentAnalysis here, or record locators, or anything else per page
    });

// result.VisitedUrls, result.SkippedUrls (off-origin), result.Failures (navigation/capture errors, crawl continues past them)
```

Whatever the session was started with - headless or headed, with or without a saved storage state - applies to every page the crawl visits, so an authenticated session crawls behind login for free.

## Related packages

- `AutomationSandbox.WebDiscovery` — the DOM snapshot model this package produces (transitive).
- `AutomationSandbox.IntentAutomation` — consumes these snapshots for intent-driven test generation.
- `AutomationSandbox.SelfHealing` — heals locators against snapshots mapped via `WebElementMapper`.

## Documentation

See `docs/web-automation.md` and `docs/intent-driven-automation.md` in the [Automation Sandbox documentation](https://github.com/mustafasercansak/automation-sandbox/tree/main/docs).
