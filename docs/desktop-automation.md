# 💻 Desktop Automation Guide / Masaüstü Otomasyon Rehberi

This guide explains how to test Windows desktop applications (WinForms, WPF, WinUI) with **Automation Sandbox**.

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Guide](#-english-guide)
> - [🇹🇷 Türkçe Kılavuz](#-türkçe-kılavuz)

---

## 🇬🇧 English Guide

### How Desktop Capture Works
1. **FlaUI.UIA3** attaches to your running Windows process (e.g. `WinFormsApp.exe`).
2. `UiTreeWalker` reads all controls (Buttons, TextBoxes, DataGrids, Windows).
3. The UI tree is converted into a standard `UiElementInfo` object structure.

### Complete C# Desktop Capture & Test Example

```csharp
using System;
using Discovery;
using FlaUI.Core;
using FlaUI.UIA3;
using UiModel;
using SelfHealing;

class DesktopTest
{
    static void Main()
    {
        // 1. Configure scan options
        var options = new DiscoveryOptions
        {
            MaxDepth = 10,                           // How deep to scan
            MaxElements = 3000,                      // Maximum controls limit
            Timeout = TimeSpan.FromSeconds(5),       // Safety timeout
            IncludeOffscreen = true                  // Also capture offscreen controls
        };

        // 2. Attach to running Windows application
        using var automation = new UIA3Automation();
        var app = Application.Attach("WinFormsApp.exe");
        var mainWindow = app.GetMainWindow(automation);

        // 3. Capture live UI tree
        DiscoveryResult result = UiTreeWalker.Walk(mainWindow, options);

        Console.WriteLine($"Captured {result.CapturedElements} controls in {result.ElapsedMilliseconds}ms.");

        // 4. Resolve broken locator against captured live tree
        var expected = new UiElementInfo
        {
            ControlType = "Button",
            AutomationId = "btnSave_Old",
            Name = "Save Changes"
        };

        HealResult healResult = SelfHealingResolver.Resolve(expected, result.Root);

        if (healResult.IsConfident)
        {
            Console.WriteLine($"✅ Found matched control: {healResult.Matched.AutomationId} (Score: {healResult.Score:F2})");
        }
    }
}
```

---

## 🇹🇷 Türkçe Kılavuz

### Masaüstü Yakalama Nasıl Çalışır?
1. **FlaUI.UIA3** kütüphanesi çalışan Windows uygulamanıza (`WinFormsApp.exe`) bağlanır.
2. `UiTreeWalker` sınıfları ekrandaki tüm elemanları (Butonlar, Metin Kutuları, Tablolar) tarar.
3. Ekran ağacı standart `UiElementInfo` formatına dönüştürülür.

### Tam C# Masaüstü Tarama ve Test Örneği

```csharp
using System;
using Discovery;
using FlaUI.Core;
using FlaUI.UIA3;
using UiModel;
using SelfHealing;

class DesktopTest
{
    static void Main()
    {
        // 1. Configure scan options
        var options = new DiscoveryOptions
        {
            MaxDepth = 10,                           // How deep to scan
            MaxElements = 3000,                      // Maximum controls limit
            Timeout = TimeSpan.FromSeconds(5),       // Safety timeout
            IncludeOffscreen = true                  // Also capture offscreen controls
        };

        // 2. Attach to running Windows application
        using var automation = new UIA3Automation();
        var app = Application.Attach("WinFormsApp.exe");
        var mainWindow = app.GetMainWindow(automation);

        // 3. Capture live UI tree
        DiscoveryResult result = UiTreeWalker.Walk(mainWindow, options);

        Console.WriteLine($"Captured {result.CapturedElements} controls in {result.ElapsedMilliseconds}ms.");

        // 4. Resolve broken locator against captured live tree
        var expected = new UiElementInfo
        {
            ControlType = "Button",
            AutomationId = "btnSave_Old",
            Name = "Save Changes"
        };

        HealResult healResult = SelfHealingResolver.Resolve(expected, result.Root);

        if (healResult.IsConfident)
        {
            Console.WriteLine($"✅ Found matched control: {healResult.Matched.AutomationId} (Score: {healResult.Score:F2})");
        }
    }
}
```
