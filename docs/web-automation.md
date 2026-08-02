# 🌐 Web Automation Guide / Web Otomasyon Rehberi

This guide explains how to capture Web DOM trees with **Playwright**, support Shadow DOM / iframes, and generate prioritized locators.

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Guide](#-english-guide)
> - [🇹🇷 Türkçe Kılavuz](#-türkçe-kılavuz)

---

## 🇬🇧 English Guide

### Web Playwright Capture Workflow
1. Run `PlaywrightDomCaptureScript.JavaScript` inside browser using Playwright's `page.EvaluateAsync`.
2. Pass the returned DOM JSON to `PlaywrightApplicationConnector.ParseJson`.
3. Use the resulting `UiElementInfo` tree for self-healing or locator generation with `PlaywrightLocatorEmitter`.

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
        string domJson = await page.EvaluateAsync<string>(PlaywrightDomCaptureScript.JavaScript);

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

### Tam C# Web Otomasyon Örneği

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using WebDiscovery;
using UiModel;
using SelfHealing;

class WebTesti
{
    static async Task Main()
    {
        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.GotoAsync("https://example.com/login");

        // 1. JavaScript tarama kodunu tarayıcı sayfasında çalıştırın
        string domJson = await page.EvaluateAsync<string>(PlaywrightDomCaptureScript.JavaScript);

        // 2. DOM JSON verisini standart UI ağacına dönüştürün
        UiElementInfo webAgaci = PlaywrightApplicationConnector.ParseJson(domJson);

        // 3. Eleman için önerilen en kararlı Playwright kod ifadelerini alın
        var hedefEleman = new WebElementInfo
        {
            TagName = "input",
            Role = "textbox",
            AccessibleName = "E-posta",
            TestId = "user-email-input",
            Id = "txtEmail"
        };

        var oneriler = PlaywrightLocatorEmitter.Suggest(hedefEleman);

        Console.WriteLine("Önerilen Playwright Kod İfadeleri:");
        foreach (var oneri in oneriler)
        {
            Console.WriteLine($"[{oneri.Strategy}] (%{oneri.Confidence * 100} Güven): {oneri.Expression}");
        }
    }
}
```
