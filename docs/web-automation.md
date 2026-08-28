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
using PlaywrightLiveExploration;

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

### Complete Web Automation Example

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using WebDiscovery;
using UiModel;
using SelfHealing;

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
using WebDiscovery;
using UiModel;
using SelfHealing;

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
using PlaywrightLiveExploration;

await using var explorer = await PlaywrightLiveExplorer.LaunchAsync();
WebElementInfo dom = await explorer.CaptureAsync("https://example.test/customers");
```

Bu, elle yakalanmış bir snapshot ile aynı `WebElementInfo` + self-healing akışını besler;
`IntentAutomationPipeline`, `IntentExplorationBridge` ve locator repository kaydı ile aynen
uyumludur. Bu projenin neden gerçek bir MCP köprüsü yerine (bu, saf C#/.NET kod tabanına ilk
kez bir Node.js tabanlı Playwright MCP sunucu süreci gerektirirdi) doğrudan Playwright .NET
SDK'sını kullandığına dair gerekçe için [Intent Tabanlı Otomasyon](intent-driven-automation.md)
sayfasına bakın.

### Tam C# Web Otomasyon Örneği

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using WebDiscovery;
using UiModel;
using SelfHealing;

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
using WebDiscovery;
using UiModel;
using SelfHealing;

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
