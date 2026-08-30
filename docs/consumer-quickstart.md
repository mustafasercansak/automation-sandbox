---
layout: default
title: Published Package Quickstart - Automation Sandbox
---

# Published Package Quickstart / Yayınlanmış Paket Hızlı Başlangıcı

## English

This path is for a first-time consumer who wants to prove the published package works
before integrating a real desktop or web tree capture backend. It uses the deterministic
heuristic engine, so it needs no API key, browser, or Windows host.

### 1. Prerequisites

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later and
confirm it is available:

```powershell
dotnet --version
```

### 2. Install from nuget.org

Create an empty console application and add the current prerelease explicitly:

```powershell
mkdir automation-sandbox-first-run
cd automation-sandbox-first-run
dotnet new console --framework net8.0
dotnet add package AutomationSandbox.SelfHealing --prerelease
```

`AutomationSandbox.SelfHealing` brings its `UiModel` and `LlmHealing` dependencies
transitively. No provider is configured in this example, so execution remains local and
heuristic-only.

### 3. Run the maintained end-to-end example

The maintained [`Program.cs`](https://github.com/mustafasercansak/automation-sandbox/blob/main/samples/HeuristicHealingQuickstart/Program.cs) shows the
complete integration shape: create a `LocatorRepository`, describe the stale element,
provide a live `UiElementInfo` tree, and call `ExecuteWithHealingAsync`. The first action
attempt throws the same kind of locator-resolution exception a UI backend would throw;
the engine then resolves the renamed candidate, retries the action, and persists the new
locator only after that retry succeeds.

Replace the generated `Program.cs` in `automation-sandbox-first-run` with that maintained
source, then run:

```powershell
dotnet run
```

Alternatively, run the checked-in project directly:

```powershell
git clone https://github.com/mustafasercansak/automation-sandbox.git
cd automation-sandbox
dotnet run --project samples/HeuristicHealingQuickstart/HeuristicHealingQuickstart.csproj
```

The final output includes:

```text
Success: the stale locator was healed and the retried action passed.
Stored locator: checkout-confirm
Healing source: heuristic
```

Use the same `SelfHealingEngine` integration with a real tree from
`AutomationSandbox.Discovery` on Windows or `AutomationSandbox.WebDiscovery` /
`AutomationSandbox.PlaywrightLiveExploration` for web. The sample keeps capture synthetic
so package installation and healing behavior are runnable on Windows, Linux, and macOS.

### 4. xUnit & NUnit Test Helpers (Before vs. After)

Instead of manually instantiating `LocatorRepository`, managing temp files, and wiring `SelfHealingEngine` in every test class, use `SelfHealingTestFixture` or `SelfHealingTestBase` from `SelfHealing.Testing`:

#### Before: Manual Boilerplate Wiring
```csharp
// Manual wiring in every test class:
public class CheckoutTests : IDisposable
{
    private readonly string _repoPath;
    private readonly LocatorRepository _repo;
    private readonly SelfHealingEngine _engine;

    public CheckoutTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".locator.json");
        _repo = new LocatorRepository(_repoPath);
        _engine = new SelfHealingEngine(_repo, mode: HealingMode.AutoHeal);
    }

    [Fact]
    public async Task ClickCheckout_HealsButton()
    {
        var result = await _engine.ExecuteWithHealingAsync(
            "Checkout.Submit",
            expectedLocator,
            element => ClickButton(element),
            () => CaptureTree());
    }

    public void Dispose()
    {
        File.Delete(_repoPath);
        File.Delete(_repoPath + ".lock");
    }
}
```

#### After: Clean xUnit Class Fixture
```csharp
using SelfHealing.Testing;

public class CheckoutTests : IClassFixture<SelfHealingTestFixture>
{
    private readonly SelfHealingTestFixture _healing;

    public CheckoutTests(SelfHealingTestFixture healing) => _healing = healing;

    [Fact]
    public async Task ClickCheckout_HealsButton()
    {
        await _healing.ExecuteWithHealingAsync(
            "Checkout.Submit",
            expectedLocator,
            element => ClickButton(element),
            () => CaptureTree());
    }
}
```

#### After: Clean NUnit Test Fixture (or Base Class)
```csharp
using NUnit.Framework;
using SelfHealing.Testing;

[TestFixture]
public class CheckoutTests : SelfHealingTestBase
{
    [Test]
    public async Task ClickCheckout_HealsButton()
    {
        await ExecuteWithHealingAsync(
            "Checkout.Submit",
            expectedLocator,
            element => ClickButton(element),
            () => CaptureTree());
    }
}
```

### 5. Verify the sample yourself

Two scripts cover the two questions, split so a version bump never blocks CI (#336):

```powershell
# Per-PR: does the sample's code still build and run against the current engine?
# Swaps the PackageReference for a ProjectReference — no nuget.org involved.
pwsh ./samples/HeuristicHealingQuickstart/verify.ps1

# Release-time: does the actually-published package work for an external consumer?
# Clean package directory, nuget.org as the only source, no cache.
# -Version defaults to Directory.Build.props <Version>; pass one to check a specific release.
pwsh ./samples/HeuristicHealingQuickstart/verify-published.ps1
```

`verify.ps1` is a required per-PR Linux CI job (`Sample Compiles Against Source`).
`verify-published.ps1` runs inside [`release.yml`](https://github.com/mustafasercansak/automation-sandbox/blob/main/.github/workflows/release.yml)
right after the packages are pushed, against the version just published — so the public
package boundary is verified on the real artifact without PR CI waiting on nuget.org
indexing.

---

## Türkçe

Bu yol, gerçek bir masaüstü veya web tree-capture backend'i entegre etmeden önce
yayınlanmış paketin çalıştığını kanıtlamak isteyen ilk kullanıcı içindir. Deterministik
sezgisel motoru kullandığından API anahtarı, tarayıcı veya Windows host gerektirmez.

### 1. Gereksinimler

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) veya daha yeni bir sürümü
kurun ve erişilebilir olduğunu doğrulayın:

```powershell
dotnet --version
```

### 2. nuget.org'dan kurulum

Boş bir console uygulaması oluşturun ve mevcut prerelease sürümünü açıkça ekleyin:

```powershell
mkdir automation-sandbox-first-run
cd automation-sandbox-first-run
dotnet new console --framework net8.0
dotnet add package AutomationSandbox.SelfHealing --prerelease
```

`AutomationSandbox.SelfHealing`, `UiModel` ve `LlmHealing` bağımlılıklarını transitif olarak
getirir. Bu örnekte sağlayıcı yapılandırılmadığı için çalışma yerel ve yalnızca sezgiseldir.

### 3. Bakımı yapılan uçtan uca örneği çalıştırma

Bakımı yapılan [`Program.cs`](https://github.com/mustafasercansak/automation-sandbox/blob/main/samples/HeuristicHealingQuickstart/Program.cs) eksiksiz
entegrasyon biçimini gösterir: bir `LocatorRepository` oluşturur, eski elemanı tanımlar,
canlı `UiElementInfo` ağacını sağlar ve `ExecuteWithHealingAsync` metodunu çağırır. İlk
eylem denemesi, bir UI backend'inin üreteceği locator-resolution hatası türünü fırlatır;
motor yeniden adlandırılmış adayı bulur, eylemi tekrarlar ve yeni locator'ı yalnızca bu
retry başarılı olduktan sonra kaydeder.

`automation-sandbox-first-run` içindeki üretilmiş `Program.cs` dosyasını bu bakımı yapılan
kaynakla değiştirin ve çalıştırın:

```powershell
dotnet run
```

Alternatif olarak repository'deki projeyi doğrudan çalıştırmak için:

```powershell
git clone https://github.com/mustafasercansak/automation-sandbox.git
cd automation-sandbox
dotnet run --project samples/HeuristicHealingQuickstart/HeuristicHealingQuickstart.csproj
```

Son çıktı şunları içerir:

```text
Success: the stale locator was healed and the retried action passed.
Stored locator: checkout-confirm
Healing source: heuristic
```

Aynı `SelfHealingEngine` entegrasyonunu Windows'ta `AutomationSandbox.Discovery` ile veya
web için `AutomationSandbox.WebDiscovery` / `AutomationSandbox.PlaywrightLiveExploration`
ile yakalanmış gerçek bir ağaç üzerinde kullanın. Örnek, paket kurulumu ve healing
davranışının Windows, Linux ve macOS'ta çalışabilmesi için sentetik capture kullanır.

### 4. xUnit & NUnit Test Yardımcıları (Önce vs. Sonra)

Her test sınıfında manuel olarak `LocatorRepository` başlatmak, geçici dosyaları yönetmek ve `SelfHealingEngine` bağlamak yerine `SelfHealing.Testing` altındaki `SelfHealingTestFixture` veya `SelfHealingTestBase` kullanın:

#### Önce: Manuel Şablon Kod (Boilerplate)
```csharp
// Her test sınıfında tekrarlanan manuel bağlantı:
public class CheckoutTests : IDisposable
{
    private readonly string _repoPath;
    private readonly LocatorRepository _repo;
    private readonly SelfHealingEngine _engine;

    public CheckoutTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".locator.json");
        _repo = new LocatorRepository(_repoPath);
        _engine = new SelfHealingEngine(_repo, mode: HealingMode.AutoHeal);
    }

    [Fact]
    public async Task ClickCheckout_HealsButton()
    {
        var result = await _engine.ExecuteWithHealingAsync(
            "Checkout.Submit",
            expectedLocator,
            element => ClickButton(element),
            () => CaptureTree());
    }

    public void Dispose()
    {
        File.Delete(_repoPath);
        File.Delete(_repoPath + ".lock");
    }
}
```

#### Sonra: Temiz xUnit Class Fixture
```csharp
using SelfHealing.Testing;

public class CheckoutTests : IClassFixture<SelfHealingTestFixture>
{
    private readonly SelfHealingTestFixture _healing;

    public CheckoutTests(SelfHealingTestFixture healing) => _healing = healing;

    [Fact]
    public async Task ClickCheckout_HealsButton()
    {
        await _healing.ExecuteWithHealingAsync(
            "Checkout.Submit",
            expectedLocator,
            element => ClickButton(element),
            () => CaptureTree());
    }
}
```

#### Sonra: Temiz NUnit Test Fixture (veya Taban Sınıf)
```csharp
using NUnit.Framework;
using SelfHealing.Testing;

[TestFixture]
public class CheckoutTests : SelfHealingTestBase
{
    [Test]
    public async Task ClickCheckout_HealsButton()
    {
        await ExecuteWithHealingAsync(
            "Checkout.Submit",
            expectedLocator,
            element => ClickButton(element),
            () => CaptureTree());
    }
}
```

### 5. Örneği kendiniz doğrulayın

İki soru, iki betik — bir sürüm bump'ının CI'ı bloklamaması için ayrıldı (#336):

```powershell
# Her PR'da: örneğin kodu mevcut engine kaynağına karşı hâlâ build olup çalışıyor mu?
# PackageReference yerine ProjectReference koyar — nuget.org devrede değil.
pwsh ./samples/HeuristicHealingQuickstart/verify.ps1

# Release anında: gerçekten yayınlanmış paket bir dış consumer için çalışıyor mu?
# Temiz paket dizini, tek kaynak nuget.org, cache yok.
# -Version varsayılan olarak Directory.Build.props <Version>'dur; belirli bir sürüm için parametre verin.
pwsh ./samples/HeuristicHealingQuickstart/verify-published.ps1
```

`verify.ps1` zorunlu bir per-PR Linux CI job'ıdır (`Sample Compiles Against Source`).
`verify-published.ps1`, [`release.yml`](https://github.com/mustafasercansak/automation-sandbox/blob/main/.github/workflows/release.yml)
içinde paketler push edildikten hemen sonra, yeni yayınlanan sürüme karşı çalışır — böylece public
paket sınırı, PR CI'ının nuget.org indexlemesini beklemesine gerek kalmadan gerçek artifact üzerinde
doğrulanır.
