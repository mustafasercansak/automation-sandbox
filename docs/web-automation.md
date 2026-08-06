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

## 🇹🇷 Türkçe Kılavuz

### Web Playwright Tarama Akışı
1. Playwright'ın `page.EvaluateAsync` fonksiyonu ile `PlaywrightDomCaptureScript.JavaScript` kodunu tarayıcıda çalıştırın.
2. Dönen DOM JSON verisini `PlaywrightApplicationConnector.ParseJson` fonksiyonuna verin.
3. Oluşan standart `UiElementInfo` ağacını iyileştirme motoruna verin veya `PlaywrightLocatorEmitter` ile önerilen Playwright kodlarını alın.

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
