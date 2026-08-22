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
dotnet add package AutomationSandbox.SelfHealing --version 0.2.0-beta.3
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

### 4. Repeat the clean package-install verification

From the repository root, run:

```powershell
pwsh ./samples/HeuristicHealingQuickstart/verify.ps1
```

The script rejects a sample containing `ProjectReference`, copies it outside the repository
into an isolated temporary consumer directory, restores into a fresh temporary package
directory with nuget.org as the only source, builds, and runs the scenario. The same
command is a required Linux CI job, so the public package boundary is continuously checked
instead of relying on a one-off release-PR experiment.

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
dotnet add package AutomationSandbox.SelfHealing --version 0.2.0-beta.3
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

### 4. Temiz paket-kurulum doğrulamasını tekrarlama

Repository kökünden şu komutu çalıştırın:

```powershell
pwsh ./samples/HeuristicHealingQuickstart/verify.ps1
```

Betik `ProjectReference` içeren bir örneği reddeder, örneği repository dışındaki yalıtılmış
bir geçici consumer dizinine kopyalar, yalnızca nuget.org kaynağını kullanıp temiz bir geçici
paket dizinine restore eder, ardından senaryoyu build edip çalıştırır. Aynı komut zorunlu
bir Linux CI job'ıdır; böylece public paket sınırı tek seferlik release PR deneyimine bağlı
kalmadan sürekli doğrulanır.
