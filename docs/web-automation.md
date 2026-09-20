---
layout: default
title: Web Automation - Automation Sandbox
---

# 🌐 Web Automation Guide / Web Otomasyon Rehberi

This guide explains how to capture Web DOM trees with **Playwright**, support Shadow DOM / iframes, and generate prioritized locators.

> 🧭 **M6:** Live page capture is implemented via `PlaywrightLiveExplorer` (Microsoft.Playwright
> .NET SDK) - see [Intent-Driven Automation](intent-driven-automation.md) for why this uses
> Playwright directly instead of the Model Context Protocol.

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Guide](#-english-guide)
> - [🇹🇷 Türkçe Kılavuz](#-türkçe-kılavuz)

---

## 🇬🇧 English Guide

### Web Playwright Capture Workflow
1. Run `PlaywrightDomCaptureScript.JavaScript` inside browser using Playwright's `page.EvaluateAsync`.
2. Pass the returned DOM JSON to `PlaywrightApplicationConnector.ParseJson`.
3. Use the resulting `UiElementInfo` tree for self-healing or locator generation with `PlaywrightLocatorEmitter`.

`PlaywrightLocatorEmitter` expressions are C# source fragments consumed directly by the
C# test generator. String values therefore escape quotes, backslashes, and CR/LF/tab
characters before emission. A `[name='...']` locator applies CSS single-quoted-string
escaping first and C# string-literal escaping second, so characters required by the CSS
selector survive compilation instead of being consumed by the C# parser.
An ID locator follows the browser's `CSS.escape()` identifier rules, including leading
digits, whitespace, control characters, and CSS punctuation such as `#` and `[`, before
the resulting selector is escaped for C# source.
When no ID, test ID, or name is available, capture emits an ancestor-qualified structural
CSS selector using `:nth-of-type(...)`. Such a selector is marked with
`WebElementInfo.IsStructuralCssSelector` and emitted at `0.35` confidence, below the
`0.55` confidence used for attribute-based CSS fallback selectors.
When the same suggestion is converted to TypeScript, the generator reads the complete
C# string literal before re-emitting it, so an escaped quote in an accessible name does
not truncate the generated `getByRole(..., { name })` locator.

### Live Page Exploration

`PlaywrightLiveExplorer` (`AutomationSandbox.PlaywrightLiveExploration`) drives a browser,
navigates to a URL, and captures a `WebElementInfo` DOM snapshot directly - no hand-written
Playwright test required, and no external Model Context Protocol server:

```csharp
using AutomationSandbox.PlaywrightLiveExploration;

await using var explorer = await PlaywrightLiveExplorer.LaunchAsync();
WebElementInfo dom = await explorer.CaptureAsync("https://example.test/customers");
```

This feeds the same `WebElementInfo` + self-healing pipeline as a manually captured
snapshot, so it composes with `IntentAutomationPipeline`, `IntentExplorationBridge`, and
locator repository recording exactly like `CaptureDomSnapshotSomehow()` did in earlier
examples. See [Intent-Driven Automation](intent-driven-automation.md) for why this project
uses the Playwright .NET SDK directly rather than a real MCP bridge (which would have
required a Node.js-based Playwright MCP server process - a first for this otherwise pure
C#/.NET codebase).

### Persistent Sessions and Live Execution

`PlaywrightLiveExplorer` above is a one-shot capture: launch, navigate, capture, dispose.
For a scenario that needs to interact with a page across many steps -
`PlaywrightWebSession` (`AutomationSandbox.PlaywrightLiveExploration`, #448/#450/#456) is the
long-lived counterpart: one page reused across the full `IntentActionType` vocabulary
(`NavigateAsync`/`CaptureAsync`/`FillAsync`/`ClickAsync`/`SelectAsync`/`CheckAsync`/
`UncheckAsync`/`HoverAsync`/`UploadFileAsync`/`PressKeyAsync`/`WaitForVisibleAsync`, plus
`IsVisibleAsync`/`IsCheckedAsync`/`GetTextAsync`/`GetValueAsync`/`CurrentUrl` reads for
assertions), with console/network/request-failure/page-error observation and
`SaveStorageStateAsync`/`StartAsync(storageStatePath:)` for authenticate-once reuse across
sessions. Every action goes through `IPage.Locator`, which only resolves the main frame, so
cross-frame elements needing a `FrameLocator` chain (see the iframe sections below) are out
of scope for `PlaywrightWebSession` itself.

`IntentWebExecutor` (`AutomationSandbox.IntentExecution`, #458) plans a goal - by default with
`DeterministicIntentPlanner` - and executes each step against a live `PlaywrightWebSession`:
`Navigate` and page-level `Assert` (`UrlEquals`/`UrlContains`) need no element match; every
other step captures a fresh DOM and matches it through `IntentExplorationBridge.Match` before
dispatching to the matching `PlaywrightWebSession` method. It stops at the first
failed/unmatched step.

```csharp
using AutomationSandbox.PlaywrightLiveExploration;
using AutomationSandbox.IntentAutomation;
using AutomationSandbox.IntentExecution;

await using var session = await PlaywrightWebSession.StartAsync();
var executor = new IntentWebExecutor(); // defaults to DeterministicIntentPlanner

var request = new IntentPlanningRequest
{
    Goal = "Create a customer record with valid email",
    TargetUrl = "https://example.test/customers",
};

IntentWebExecutionResult result = await executor.RunAsync(request, session);
```

See [Intent-Driven Automation](intent-driven-automation.md) for the full action vocabulary,
assertion semantics, and worked examples, and the runnable
[`samples/WebObservationQuickstart`](https://github.com/mustafasercansak/automation-sandbox/tree/main/samples/WebObservationQuickstart)
sample for `PlaywrightWebSession` authenticate-once storage-state reuse plus
`AutomationSandbox.ContentAnalysis` against a real local HTTP server.

### Waiting for Dynamic Content, and Crawling a Site

`CaptureAsync` is a point-in-time snapshot - it does not wait for a client-side re-render.
`WaitForVisibleAsync` only helps when an element's *visibility* changes, which does nothing for
a language switch or any other update that mutates an already-visible element's text.
`WaitForTextChangeAsync` waits for that instead:

```csharp
var before = await session.GetTextAsync("#greeting");
await session.ClickAsync("#language-switch");
await session.WaitForTextChangeAsync("#greeting", before); // resolves once the text actually differs
```

`SiteCrawler.CrawlAsync` (#475) walks a site breadth-first from one starting URL using
`session.GetLinksAsync()` (every absolute `http(s)` link on the current page) - so a caller with
no per-page navigation script can hand over just a URL:

```csharp
var result = await SiteCrawler.CrawlAsync(
    session,
    "https://example.com",
    new SiteCrawlOptions { MaxPages = 50, MaxDepth = 3 }, // same-origin only by default
    onPageCaptured: async (url, dom, ct) =>
    {
        // run AutomationSandbox.ContentAnalysis here, record locators, or anything else per page -
        // SiteCrawler itself has no dependency on those packages
    });
```

A page that fails to navigate or capture is recorded in `result.Failures` and the crawl
continues with the rest of the queue rather than aborting the whole run. Whatever the session
was started with - headless or headed, with or without a saved storage state - applies to every
page the crawl visits, so an authenticated session (`PlaywrightWebSession.StartAsync` with
`storageStatePath`) crawls behind login for free.

### Bounding a Capture (Depth / Element Count / Timeout)

`CaptureAsync` accepts an optional `WebDiscoveryOptions` (`MaxDepth`, `MaxElements`,
`Timeout`), mirroring `Discovery.DiscoveryOptions` on the desktop side, and defaults to
`WebDiscoveryOptions.Default` (`MaxDepth = 25`, `MaxElements = 5000`, `Timeout = 10s`) when
omitted - so a large SPA or data-grid page cannot produce an unbounded JSON payload over the
Playwright protocol even when the caller passes nothing:

```csharp
var dom = await explorer.CaptureAsync(
    "https://example.test/customers",
    new WebDiscoveryOptions { MaxDepth = 15, MaxElements = 2000, Timeout = TimeSpan.FromSeconds(5) });

if (dom.HitMaxDepth || dom.HitMaxElements || dom.TimedOut)
{
    Console.WriteLine($"Capture truncated (elements: {dom.CapturedCount}); results may be incomplete.");
}
```

The bounds are enforced by the browser-side script itself, not by .NET, because a single
`page.EvaluateAsync` call cannot be interrupted mid-flight once it starts running: the
generated `walk()` tracks a running element count and a `Date.now()` deadline, stops
descending once either is exceeded, and stamps `HitMaxDepth`/`HitMaxElements`/`TimedOut`/
`CapturedCount` onto the returned root element so a cut-short capture is observable rather
than silently missing nodes. Calling `PlaywrightDomCaptureScript.JavaScript` directly (as
shown above and in the cross-origin iframe example below) still runs the original unbounded
walk for callers who evaluate the script themselves; use
`PlaywrightDomCaptureScript.BuildJavaScript(options)` to get the same bounded script that
`CaptureAsync` uses.

### Complete Web Automation Example

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using AutomationSandbox.WebDiscovery;
using AutomationSandbox.UiModel;
using AutomationSandbox.SelfHealing;

class WebTest
{
    static async Task Main()
    {
        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.GotoAsync("https://example.com/login");

        // 1. Evaluate JavaScript snippet in browser page
        // Wrap in JSON.stringify(...): EvaluateAsync<string> expects the script's result to
        // already be a string, and the capture script itself returns an object.
        string domJson = await page.EvaluateAsync<string>($"() => JSON.stringify(({PlaywrightDomCaptureScript.JavaScript})())");

        // 2. Convert DOM JSON into standard UiElementInfo tree
        UiElementInfo webTree = PlaywrightApplicationConnector.ParseJson(domJson);

        // 3. Generate prioritized locator suggestions for an element
        var targetElement = new WebElementInfo
        {
            TagName = "input",
            Role = "textbox",
            AccessibleName = "Email",
            TestId = "user-email-input",
            Id = "txtEmail"
        };

        var suggestions = PlaywrightLocatorEmitter.Suggest(targetElement);

        Console.WriteLine("Suggested Locators:");
        foreach (var suggestion in suggestions)
        {
            Console.WriteLine($"[{suggestion.Strategy}] ({suggestion.Confidence * 100}% Confidence): {suggestion.Expression}");
        }
    }
}
```

---

### Iframe Support: Same-Origin vs. Cross-Origin

Web applications frequently embed iframes for isolated widgets, forms, payment gateways, or authentication providers. AutomationSandbox provides distinct handling depending on iframe origin security:

#### 1. Same-Origin Iframes (Automatic Traversal)

When an `<iframe>` shares the same origin (protocol, domain, and port) as the parent page:
- `PlaywrightDomCaptureScript` running in `page.EvaluateAsync` automatically traverses into `iframe.contentDocument.body`.
- `WebElementInfo.FrameAncestry` tracks the ordered hierarchy of parent iframe selectors (e.g. `["iframe[name='details']", "iframe#nestedFrame"]`).
- `PlaywrightLocatorEmitter` suggests iframe-aware locators:
  ```csharp
  // Single iframe
  page.FrameLocator("iframe[name='details']").GetByRole(AriaRole.Button, new() { Name = "Save" })

  // Nested iframes
  page.FrameLocator("iframe[name='details']").FrameLocator("iframe#nestedFrame").GetByTestId("submit-btn")
  ```
- Test generators (`PlaywrightCSharpTestGenerator` and `PlaywrightTypeScriptTestGenerator`) automatically preserve and emit correct `Page.FrameLocator(...)` (C#) and `page.frameLocator(...)` (TypeScript) code.

#### 2. Cross-Origin Iframes (Direct Frame Evaluation)

When an `<iframe>` is hosted on a different origin (e.g. `https://checkout.stripe.com`, `https://accounts.google.com`, third-party reCAPTCHA):
- **Browser Security Restriction**: The browser's Same-Origin Policy (SOP) blocks JavaScript running in the parent page from reading `iframe.contentDocument`. The capture script safely skips inaccessible frame documents without failing the capture.
- **The boundary is recorded, not silent**: the `<iframe>` element itself is still captured, with `WebElementInfo.IsCrossOriginFrame = true` and no children. Without that flag a blocked frame would be indistinguishable from an empty same-origin one — both are an iframe node with zero children — so a caller could not tell "this frame is empty" from "I was not allowed to look inside". Elements *inside* the frame never reach the snapshot, so `PlaywrightLocatorEmitter` never emits a locator for content it could not see; the suggestions it produces for the iframe node locate the iframe element itself, which is valid.
- **Playwright Solution**: Playwright operates out-of-process and has direct access to all frames via `page.Frames`, `page.FrameByUrl()`, or `page.FrameByName()`.
- **Cross-Origin Capture Workflow**: Evaluate `PlaywrightDomCaptureScript.JavaScript` directly inside the frame's execution context:

```csharp
using System.Linq;
using System.Text.Json;
using Microsoft.Playwright;
using AutomationSandbox.WebDiscovery;
using AutomationSandbox.UiModel;
using AutomationSandbox.SelfHealing;

// 1. Locate the cross-origin frame via Playwright
IFrame? paymentFrame = page.Frames.FirstOrDefault(f => f.Url.Contains("checkout.stripe.com"))
    ?? page.FrameByName("stripe-frame");

if (paymentFrame != null)
{
    // 2. Evaluate capture script inside the frame context directly
    string frameDomJson = await paymentFrame.EvaluateAsync<string>(
        $"() => JSON.stringify(({PlaywrightDomCaptureScript.JavaScript})())");

    // 3. Deserialize and map to UiElementInfo tree
    var frameDom = JsonSerializer.Deserialize<WebElementInfo>(
        frameDomJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    UiElementInfo frameTree = WebElementMapper.ToUiElementTree(frameDom!);

    // 4. Resolve / heal target element inside the cross-origin frame
    var healResult = SelfHealingResolver.Resolve(expectedCardInputSnapshot, frameTree);

    // 5. In test scripts, target cross-origin elements via FrameLocator or frame instance:
    // C#: await Page.FrameLocator("iframe[src*='checkout.stripe.com']").GetByTestId("card-number").FillAsync("4242...");
    // TS: await page.frameLocator('iframe[src*=\'checkout.stripe.com\']').getByTestId('card-number').fill('4242...');
}
```

---

## 🇹🇷 Türkçe Kılavuz

### Web Playwright Tarama Akışı
1. Playwright'ın `page.EvaluateAsync` fonksiyonu ile `PlaywrightDomCaptureScript.JavaScript` kodunu tarayıcıda çalıştırın.
2. Dönen DOM JSON verisini `PlaywrightApplicationConnector.ParseJson` fonksiyonuna verin.
3. Oluşan standart `UiElementInfo` ağacını iyileştirme motoruna verin veya `PlaywrightLocatorEmitter` ile önerilen Playwright kodlarını alın.

`PlaywrightLocatorEmitter` ifadeleri C# test üreticisinin doğrudan kullandığı C# kaynak
parçalarıdır. Bu nedenle tırnak, ters eğik çizgi ve CR/LF/tab karakterleri üretilmeden önce
kaçırılır. `[name='...']` locator'ında önce CSS tek-tırnaklı string kaçışı, ardından C#
string literal kaçışı uygulanır; böylece CSS seçicisinin gerektirdiği karakterler C# parser
tarafından tüketilmeden derlenmiş koda ulaşır.
ID locator'ı; baştaki rakamlar, boşluklar, kontrol karakterleri ile `#` ve `[` gibi CSS
noktalama işaretleri dahil olmak üzere tarayıcının `CSS.escape()` identifier kurallarını
uygular, ardından oluşan seçiciyi C# kaynak kodu için kaçırır.
ID, test ID veya name olmadığında tarama; `:nth-of-type(...)` kullanan, üst öğelerle
nitelenmiş yapısal bir CSS seçicisi üretir. Bu seçici `WebElementInfo.IsStructuralCssSelector`
ile işaretlenir ve öznitelik tabanlı CSS fallback seçicilerinin `0.55` değerinden düşük olan
`0.35` güvenle yayımlanır.
Aynı öneri TypeScript'e dönüştürülürken üretici C# string literal'ının tamamını okur; bu
sayede erişilebilir addaki kaçırılmış bir çift tırnak, üretilen
`getByRole(..., { name })` locator'ını yarıda kesmez.

### Canlı Sayfa Keşfi

`PlaywrightLiveExplorer` (`AutomationSandbox.PlaywrightLiveExploration`) bir tarayıcıyı
yönetip verilen URL'ye gider ve doğrudan bir `WebElementInfo` DOM snapshot'ı yakalar - elle
yazılmış bir Playwright testine ya da harici bir Model Context Protocol sunucusuna gerek
kalmadan:

```csharp
using AutomationSandbox.PlaywrightLiveExploration;

await using var explorer = await PlaywrightLiveExplorer.LaunchAsync();
WebElementInfo dom = await explorer.CaptureAsync("https://example.test/customers");
```

Bu, elle yakalanmış bir snapshot ile aynı `WebElementInfo` + self-healing akışını besler;
`IntentAutomationPipeline`, `IntentExplorationBridge` ve locator repository kaydı ile aynen
uyumludur. Bu projenin neden gerçek bir MCP köprüsü yerine (bu, saf C#/.NET kod tabanına ilk
kez bir Node.js tabanlı Playwright MCP sunucu süreci gerektirirdi) doğrudan Playwright .NET
SDK'sını kullandığına dair gerekçe için [Intent Tabanlı Otomasyon](intent-driven-automation.md)
sayfasına bakın.

### Kalıcı Oturumlar ve Canlı Yürütme

Yukarıdaki `PlaywrightLiveExplorer` tek seferlik bir yakalamadır: başlat, git, yakala, kapat.
Bir sayfayla birçok adım boyunca etkileşim kurması gereken bir senaryo için
`PlaywrightWebSession` (`AutomationSandbox.PlaywrightLiveExploration`, #448/#450/#456) uzun
ömürlü karşılığıdır: tüm `IntentActionType` sözcük dağarcığında
(`NavigateAsync`/`CaptureAsync`/`FillAsync`/`ClickAsync`/`SelectAsync`/`CheckAsync`/
`UncheckAsync`/`HoverAsync`/`UploadFileAsync`/`PressKeyAsync`/`WaitForVisibleAsync`, ayrıca
assertion'lar için `IsVisibleAsync`/`IsCheckedAsync`/`GetTextAsync`/`GetValueAsync`/`CurrentUrl`
okumaları) yeniden kullanılan tek bir sayfa; konsol/ağ/istek-hatası/sayfa-hatası gözlemi ve
oturumlar arası kimlik doğrulamayı bir kez yapıp tekrar kullanmak için
`SaveStorageStateAsync`/`StartAsync(storageStatePath:)` ile birlikte gelir. Her eylem yalnızca
ana çerçeveyi çözen `IPage.Locator` üzerinden geçer; bu yüzden `FrameLocator` zinciri
gerektiren çapraz-çerçeve elemanlar (aşağıdaki iframe bölümlerine bakın) `PlaywrightWebSession`
için kapsam dışıdır.

`IntentWebExecutor` (`AutomationSandbox.IntentExecution`, #458) bir hedefi planlar - varsayılan
olarak `DeterministicIntentPlanner` ile - ve her adımı canlı bir `PlaywrightWebSession`'a karşı
yürütür: `Navigate` ve sayfa düzeyindeki `Assert` (`UrlEquals`/`UrlContains`) eleman eşleşmesi
gerektirmez; diğer her adım, `PlaywrightWebSession` metoduna göndermeden önce taze bir DOM
yakalar ve `IntentExplorationBridge.Match` ile eşler. İlk başarısız/eşleşmeyen adımda durur.

```csharp
using AutomationSandbox.PlaywrightLiveExploration;
using AutomationSandbox.IntentAutomation;
using AutomationSandbox.IntentExecution;

await using var session = await PlaywrightWebSession.StartAsync();
var executor = new IntentWebExecutor(); // varsayılan olarak DeterministicIntentPlanner

var request = new IntentPlanningRequest
{
    Goal = "Create a customer record with valid email",
    TargetUrl = "https://example.test/customers",
};

IntentWebExecutionResult result = await executor.RunAsync(request, session);
```

Tam eylem sözcük dağarcığı, assertion semantiği ve çalıştırılabilir örnekler için
[Intent Tabanlı Otomasyon](intent-driven-automation.md) sayfasına, `PlaywrightWebSession`
oturum kalıcılığı ile `AutomationSandbox.ContentAnalysis`'in gerçek bir yerel HTTP sunucusuna
karşı birlikte çalışmasını gösteren çalıştırılabilir örnek için
[`samples/WebObservationQuickstart`](https://github.com/mustafasercansak/automation-sandbox/tree/main/samples/WebObservationQuickstart)
örneğine bakın.

### Dinamik İçeriği Beklemek ve Bir Siteyi Taramak

`CaptureAsync`, o anki DOM'un tek bir anlık görüntüsüdür - istemci tarafı bir yeniden
render'ı beklemez. `WaitForVisibleAsync` yalnızca bir elemanın *görünürlüğü* değiştiğinde
yardımcı olur; bu, zaten görünür olan bir elemanın metnini değiştiren bir dil değişimi veya
başka bir güncelleme için işe yaramaz. `WaitForTextChangeAsync` tam olarak bunu bekler:

```csharp
var before = await session.GetTextAsync("#greeting");
await session.ClickAsync("#language-switch");
await session.WaitForTextChangeAsync("#greeting", before); // metin gerçekten değişince döner
```

`SiteCrawler.CrawlAsync` (#475), `session.GetLinksAsync()`'i (mevcut sayfadaki her mutlak
`http(s)` bağlantı) kullanarak tek bir başlangıç URL'sinden siteyi genişlik-öncelikli (breadth-first)
geziyor - sayfa başına elle yazılmış bir gezinme betiğine gerek kalmadan sadece bir URL
verilebiliyor:

```csharp
var result = await SiteCrawler.CrawlAsync(
    session,
    "https://example.com",
    new SiteCrawlOptions { MaxPages = 50, MaxDepth = 3 }, // varsayılan olarak sadece aynı origin
    onPageCaptured: async (url, dom, ct) =>
    {
        // burada AutomationSandbox.ContentAnalysis çalıştırılabilir, locator kaydedilebilir
        // veya sayfa başına başka herhangi bir iş yapılabilir - SiteCrawler'ın bu paketlere
        // bir bağımlılığı yoktur
    });
```

Gezinemediği veya yakalayamadığı bir sayfa `result.Failures`'a kaydedilir ve tarama tüm
çalışmayı iptal etmek yerine kuyruktaki diğer sayfalarla devam eder. Oturum ne şekilde
başlatıldıysa - headless/headed, kayıtlı bir storage state ile veya onsuz - taranan her
sayfada aynen geçerli olur; yani kimlik doğrulamalı bir oturum
(`storageStatePath` ile `PlaywrightWebSession.StartAsync`) giriş arkasını ücretsiz tarar.

### Taramayı Sınırlama (Derinlik / Eleman Sayısı / Zaman Aşımı)

`CaptureAsync`, masaüstü tarafındaki `Discovery.DiscoveryOptions`'ı yansıtan isteğe bağlı bir
`WebDiscoveryOptions` (`MaxDepth`, `MaxElements`, `Timeout`) parametresi kabul eder ve
belirtilmediğinde `WebDiscoveryOptions.Default` (`MaxDepth = 25`, `MaxElements = 5000`,
`Timeout = 10s`) kullanılır - böylece büyük bir SPA veya veri ızgarası sayfası, çağıran hiçbir
şey vermese bile Playwright protokolü üzerinden sınırsız bir JSON yükü üretemez:

```csharp
var dom = await explorer.CaptureAsync(
    "https://example.test/customers",
    new WebDiscoveryOptions { MaxDepth = 15, MaxElements = 2000, Timeout = TimeSpan.FromSeconds(5) });

if (dom.HitMaxDepth || dom.HitMaxElements || dom.TimedOut)
{
    Console.WriteLine($"Tarama kesildi (eleman sayısı: {dom.CapturedCount}); sonuçlar eksik olabilir.");
}
```

Sınırlar .NET tarafından değil, tarayıcı tarafındaki betiğin kendisi tarafından uygulanır;
çünkü tek bir `page.EvaluateAsync` çağrısı çalışmaya başladıktan sonra yarıda kesilemez:
üretilen `walk()` fonksiyonu çalışan bir eleman sayacı ve bir `Date.now()` son tarihi takip
eder, ikisinden biri aşıldığında derinleşmeyi durdurur ve dönen kök elemana
`HitMaxDepth`/`HitMaxElements`/`TimedOut`/`CapturedCount` değerlerini damgalar; böylece
kesilen bir tarama, düğümleri sessizce kaybetmek yerine gözlemlenebilir olur.
`PlaywrightDomCaptureScript.JavaScript` betiğini doğrudan çağırmak (yukarıda ve aşağıdaki
cross-origin iframe örneğinde olduğu gibi) hâlâ, betiği kendisi değerlendiren çağıranlar için
orijinal sınırsız taramayı çalıştırır; `CaptureAsync`'in kullandığı aynı sınırlı betiği almak
için `PlaywrightDomCaptureScript.BuildJavaScript(options)` kullanın.

### Tam C# Web Otomasyon Örneği

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using AutomationSandbox.WebDiscovery;
using AutomationSandbox.UiModel;
using AutomationSandbox.SelfHealing;

class WebTest
{
    static async Task Main()
    {
        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.GotoAsync("https://example.com/login");

        // 1. Evaluate JavaScript snippet in browser page
        // Wrap in JSON.stringify(...): EvaluateAsync<string> expects the script's result to
        // already be a string, and the capture script itself returns an object.
        string domJson = await page.EvaluateAsync<string>($"() => JSON.stringify(({PlaywrightDomCaptureScript.JavaScript})())");

        // 2. Convert DOM JSON into standard UiElementInfo tree
        UiElementInfo webTree = PlaywrightApplicationConnector.ParseJson(domJson);

        // 3. Generate prioritized locator suggestions for an element
        var targetElement = new WebElementInfo
        {
            TagName = "input",
            Role = "textbox",
            AccessibleName = "Email",
            TestId = "user-email-input",
            Id = "txtEmail"
        };

        var suggestions = PlaywrightLocatorEmitter.Suggest(targetElement);

        Console.WriteLine("Suggested Locators:");
        foreach (var suggestion in suggestions)
        {
            Console.WriteLine($"[{suggestion.Strategy}] ({suggestion.Confidence * 100}% Confidence): {suggestion.Expression}");
        }
    }
}
```

### Iframe Desteği: Same-Origin ve Cross-Origin

Web uygulamaları bağımsız bileşenler, formlar, ödeme sistemleri veya kimlik doğrulama sağlayıcıları için sıklıkla iframe kullanır. AutomationSandbox, iframe'in origin güvenliğine göre iki farklı yaklaşım sunar:

#### 1. Same-Origin Iframe'ler (Otomatik Ağaç Gezintisi)

Bir `<iframe>` ana sayfa ile aynı origin'i (protokol, alan adı, port) paylaştığında:
- `page.EvaluateAsync` içinde çalışan `PlaywrightDomCaptureScript`, otomatik olarak `iframe.contentDocument.body` içerisine iner.
- `WebElementInfo.FrameAncestry`, üst iframe seçicilerinin hiyerarşik sırasını (`["iframe[name='details']", "iframe#nestedFrame"]`) saklar.
- `PlaywrightLocatorEmitter`, iframe'e duyarlı zincirleme locator'lar önerir:
  ```csharp
  // Tekli iframe
  page.FrameLocator("iframe[name='details']").GetByRole(AriaRole.Button, new() { Name = "Save" })

  // İç içe (nested) iframe'ler
  page.FrameLocator("iframe[name='details']").FrameLocator("iframe#nestedFrame").GetByTestId("submit-btn")
  ```
- Test üreticileri (`PlaywrightCSharpTestGenerator` ve `PlaywrightTypeScriptTestGenerator`), üretilen C# (`Page.FrameLocator(...)`) ve TypeScript (`page.frameLocator(...)`) kodlarında bu zincirleri korur.

#### 2. Cross-Origin Iframe'ler (Doğrudan Frame Değerlendirmesi)

Bir `<iframe>` farklı bir origin'den yüklendiğinde (örn. `https://checkout.stripe.com`, `https://accounts.google.com`, üçüncü parti reCAPTCHA):
- **Tarayıcı Güvenlik Kısıtlaması**: Tarayıcının Same-Origin Policy (SOP) kuralı gereği ana sayfada koşan JavaScript, `iframe.contentDocument` içeriğine erişemez (SecurityError fırlatır veya null döner). Tarama betiği erişilemeyen frame'leri güvenle atlar ve ana sayfa taramasını kesintiye uğratmaz.
- **Playwright Çözümü**: Playwright tarayıcı sürecinin dışından çalıştığı için `page.Frames`, `page.FrameByUrl()` veya `page.FrameByName()` üzerinden tüm frame'lere doğrudan erişebilir.
- **Cross-Origin Tarama Akışı**: `PlaywrightDomCaptureScript.JavaScript` betiğini doğrudan hedef frame'in bağlamında çalıştırın:

```csharp
using System.Linq;
using System.Text.Json;
using Microsoft.Playwright;
using AutomationSandbox.WebDiscovery;
using AutomationSandbox.UiModel;
using AutomationSandbox.SelfHealing;

// 1. Playwright üzerinden cross-origin frame'i bulun
IFrame? paymentFrame = page.Frames.FirstOrDefault(f => f.Url.Contains("checkout.stripe.com"))
    ?? page.FrameByName("stripe-frame");

if (paymentFrame != null)
{
    // 2. Tarama betiğini doğrudan frame bağlamında çalıştırın
    string frameDomJson = await paymentFrame.EvaluateAsync<string>(
        $"() => JSON.stringify(({PlaywrightDomCaptureScript.JavaScript})())");

    // 3. JSON'ı deserialize edip UiElementInfo ağacına dönüştürün
    var frameDom = JsonSerializer.Deserialize<WebElementInfo>(
        frameDomJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    UiElementInfo frameTree = WebElementMapper.ToUiElementTree(frameDom!);

    // 4. Cross-origin frame içerisindeki hedef elemanı iyileştirin / bulun
    var healResult = SelfHealingResolver.Resolve(expectedCardInputSnapshot, frameTree);

    // 5. Test kodlarında FrameLocator veya frame nesnesi ile hedefleyin:
    // C#: await Page.FrameLocator("iframe[src*='checkout.stripe.com']").GetByTestId("card-number").FillAsync("4242...");
    // TS: await page.frameLocator('iframe[src*=\'checkout.stripe.com\']').getByTestId('card-number').fill('4242...');
}
```
