# 🚀 Getting Started / Başlangıç Rehberi

Welcome to the beginner-friendly setup guide for **Automation Sandbox**! / **Automation Sandbox** için adım adım başlangıç rehberine hoş geldiniz!

---

## 🇬🇧 English: Step-by-Step Guide

### Step 1: Requirements
Before writing code, make sure you have:
1. **.NET SDK 8.0** or later installed ([Download .NET](https://dotnet.microsoft.com/download)).
2. An IDE such as Visual Studio 2022, VS Code, or Rider.

---

### Step 2: Understanding the Concept
Whenever you perform a test step, **Automation Sandbox** manages elements using a 3-step process:
1. **Repository Check:** It looks up the saved element snapshot in `my_locators.locator.json`.
2. **Execute Action:** It tries to perform your action (e.g. click).
3. **Automatic Healing:** If the element was renamed or moved, it automatically finds the new element, updates `my_locators.locator.json`, and retries your action.

> **Which failures trigger healing?** By default, `ExecuteWithHealingAsync` only heals exceptions whose exact type name is a known locator/element-resolution failure (e.g. `ElementNotFoundException`, `NoSuchElementException`, FlaUI's `ElementNotAvailableException`). Any other exception (assertion, timeout, backend error) is rethrown without retrying your action — this reduces the risk of duplicate execution for a non-idempotent step (like placing an order), though it isn't an absolute guarantee: a multi-step action can still have a side effect occur before a correctly-classified locator failure, and the retry re-runs the whole action. Pass the optional `shouldHeal: ex => ...` parameter to define your own policy.

---

### Step 3: Complete Working Code Example

Create a new console or test project and copy this complete code:

```csharp
using System;
using System.Threading.Tasks;
using UiModel;
using SelfHealing;
using LlmHealing;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 Automation Sandbox Starting...");

        // 1. Specify the path to store locators on disk
        var repository = new LocatorRepository("my_locators.locator.json");

        // 2. Initialize optional AI providers (Local Ollama for 100% free AI healing)
        var llmProviders = new ILlmHealingProvider[]
        {
            new OllamaHealingProvider(host: "http://localhost:11434")
        };

        // 3. Create the SelfHealingEngine instance
        var engine = new SelfHealingEngine(repository, llmProviders: llmProviders);

        // 4. Define what element you expect to find
        var expectedElement = new UiElementInfo
        {
            ControlType = "Button",
            AutomationId = "btnSubmit_Old", // Broken ID
            Name = "Submit Form",
            TestIntent = "Click the main registration form submission button"
        };

        // 5. Execute action with automatic self-healing
        bool result = await engine.ExecuteWithHealingAsync(
            locatorKey: "Registration.SubmitButton",
            expected: expectedElement,
            action: async (healedElement) =>
            {
                // This callback runs with the correct healed element!
                Console.WriteLine($"✅ Successfully clicked element: AutomationId='{healedElement.AutomationId}'");
                return true;
            },
            captureTreeRoot: () => CaptureLiveTreeMock(), // Function that returns live screen tree
            testIntent: "Click the main registration form submission button"
        );

        Console.WriteLine($"🎉 Step Execution Completed Successfully: {result}");
    }

    // Mock function representing live screen capture
    static UiElementInfo CaptureLiveTreeMock()
    {
        return new UiElementInfo
        {
            ControlType = "Window",
            Children =
            {
                new UiElementInfo
                {
                    ControlType = "Button",
                    AutomationId = "btnSubmit_Renamed2026", // The renamed element on live screen
                    Name = "Submit Form"
                }
            }
        };
    }
}
```

---

## 🇹🇷 Türkçe: Adım Adım Başlangıç Rehberi

### Adım 1: Gereksinimler
Koda başlamadan önce bilgisayarınızda şunların kurulu olduğundan emin olun:
1. **.NET SDK 8.0** veya üstü ([.NET İndirme Bağlantısı](https://dotnet.microsoft.com/download)).
2. Visual Studio 2022, VS Code veya Rider geliştirme ortamı.

---

### Adım 2: Çalışma Mantığını Anlama
Bir test adımı çalıştırdığınızda **Automation Sandbox** 3 adımda işlemleri yönetir:
1. **Depo Kontrolü:** `my_locators.locator.json` dosyasından kaydedilmiş eleman bilgilerini okur.
2. **Eylemi Çalıştırma:** Tıklama veya metin yazma eyleminizi dener.
3. **Otomatik İyileştirme (Self-Healing):** Elemanın adı/ID'si değiştiği için hata alınırsa, ekrandaki en yakın elemanı otomatik bulur, JSON dosyasını günceller ve tıklama işlemini **başarıyla tamamlar**.

> **Hangi hatalar iyileştirmeyi tetikler?** `ExecuteWithHealingAsync` varsayılan olarak yalnızca istisnanın tam tip adı bilinen bir locator/eleman çözümleme hatasıyla eşleşiyorsa iyileştirme yapar (örn. `ElementNotFoundException`, `NoSuchElementException`, FlaUI'nin `ElementNotAvailableException`'ı). Diğer tüm hatalar (assertion, zaman aşımı, backend hatası) eyleminizi tekrar çalıştırmadan geri fırlatılır — bu, sipariş verme gibi tekrar çalıştırılamayan bir adımda yinelenen çalıştırma riskini azaltır, ancak mutlak bir garanti değildir: çok adımlı bir action'da, doğru sınıflandırılmış bir locator hatasından önce bir side effect zaten gerçekleşmiş olabilir ve retry tüm action'ı yeniden çalıştırır. Kendi politikanızı tanımlamak için isteğe bağlı `shouldHeal: ex => ...` parametresini kullanın.

---

### Adım 3: Tam ve Çalışan Kopyala-Yapıştır Kod Örneği

Yeni bir konsol projesi oluşturun ve aşağıdaki tam kodu kopyalayıp çalıştırın:

```csharp
using System;
using System.Threading.Tasks;
using UiModel;
using SelfHealing;
using LlmHealing;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 Automation Sandbox Starting...");

        // 1. Specify the path to store locators on disk
        var repository = new LocatorRepository("my_locators.locator.json");

        // 2. Initialize optional AI providers (Local Ollama for 100% free AI healing)
        var llmProviders = new ILlmHealingProvider[]
        {
            new OllamaHealingProvider(host: "http://localhost:11434")
        };

        // 3. Create the SelfHealingEngine instance
        var engine = new SelfHealingEngine(repository, llmProviders: llmProviders);

        // 4. Define what element you expect to find
        var expectedElement = new UiElementInfo
        {
            ControlType = "Button",
            AutomationId = "btnSubmit_Old", // Broken ID
            Name = "Submit Form",
            TestIntent = "Click the main registration form submission button"
        };

        // 5. Execute action with automatic self-healing
        bool result = await engine.ExecuteWithHealingAsync(
            locatorKey: "Registration.SubmitButton",
            expected: expectedElement,
            action: async (healedElement) =>
            {
                // This callback runs with the correct healed element!
                Console.WriteLine($"✅ Successfully clicked element: AutomationId='{healedElement.AutomationId}'");
                return true;
            },
            captureTreeRoot: () => CaptureLiveTreeMock(), // Function that returns live screen tree
            testIntent: "Click the main registration form submission button"
        );

        Console.WriteLine($"🎉 Step Execution Completed Successfully: {result}");
    }

    // Mock function representing live screen capture
    static UiElementInfo CaptureLiveTreeMock()
    {
        return new UiElementInfo
        {
            ControlType = "Window",
            Children =
            {
                new UiElementInfo
                {
                    ControlType = "Button",
                    AutomationId = "btnSubmit_Renamed2026", // The renamed element on live screen
                    Name = "Submit Form"
                }
            }
        };
    }
}
```
