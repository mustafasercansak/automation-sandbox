---
layout: default
title: CLI & CI Integration Guide - Automation Sandbox
---

# 🛠️ CLI & CI Integration Guide / CLI ve CI Entegrasyon Rehberi

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Guide](#-english-guide)
> - [🇹🇷 Türkçe Kılavuz](#-türkçe-kılavuz)

---

## 🇬🇧 English Guide

Every other guide on this site assumes a .NET project: reference a package, write C#, call `SelfHealingResolver` in-process. `automation-sandbox-heal` (`AutomationSandbox.Cli`) removes that requirement entirely. `SelfHealingResolver.Resolve` operates purely on the framework-agnostic `UiElementInfo` tree - no FlaUI, no Playwright .NET SDK, no Windows dependency - so the only real barrier between "any language, any CI" and this engine was packaging. This guide covers the whole story: the CLI itself, the JSON contract it reads and writes, the GitHub Action that wraps it, and a working capture example for a language other than C#.

### Install

```bash
dotnet tool install -g AutomationSandbox.Cli --prerelease
```

`--prerelease` is required during the `0.x` beta series; drop it once a stable `1.0` ships. This still requires the .NET runtime to be present on the machine - the tool itself is a real .NET executable, just one that needs no .NET *project* from its caller.

### Basic Usage

```bash
automation-sandbox-heal --expected stale-element.json --tree live-tree.json --profile balanced
```

- `--expected` - a single `UiElementInfo` JSON object: the stale/broken locator's last known snapshot.
- `--tree` - a `UiElementInfo` JSON tree: a freshly captured snapshot of the live application.
- `--profile` - `balanced` (default), `conservative`, or `aggressive`. See the healing-pipeline thresholds in `AGENTS.md` for what each trades off.

Prints a `HealResult` as JSON to stdout - the same explainable `ScoreComponents` breakdown, `RunnerUpScore`, and `EvidenceCoverage` every other API surface in this project exposes. Diagnostic logging goes to stderr, so stdout is always pure, parseable JSON. Exit code `0` means `IsConfident` (safe to accept `Matched`); `1` means not confident (do not auto-accept); `2` means an error occurred before a decision could be made (bad input, missing file) - never a raw stack trace.

### The JSON Schema

Both JSON inputs, and the tool's own output, follow a published [JSON Schema](schemas/ui-element-info.schema.json). Point your editor or a validator at it for autocomplete and validation without reading any C# source:

```json
{
  "$schema": "https://mustafasercansak.github.io/automation-sandbox/schemas/ui-element-info.schema.json",
  "ControlType": "Button"
}
```

Only the fields behind `SimilarityScorer`'s five weighted signals (`ControlType`, `Name`, `AutomationId`, `BoundingRectangle`, `ParentControlType`) are required; everything else is optional, matching the C# defaults the engine falls back to when a property is absent.

### Using It in CI: the GitHub Action

A composite GitHub Action wraps the install, run, and JSON-parsing steps into one line:

```yaml
- uses: mustafasercansak/automation-sandbox/.github/actions/heal@main
  with:
    expected: path/to/stale-element.json
    tree: path/to/live-tree.json
    profile: balanced
```

By default this step fails the job when the result is not confident - the same exit-code contract as the CLI itself, so a bare `uses:` line is already a working pass/fail gate. Add `continue-on-error: true` and read the `is-confident`/`result` outputs if you want to branch on the outcome instead of failing the build. An optional `source` input lets the action install from a private feed instead of nuget.org.

### Capturing JSON from Any Language: Web (JS/Playwright)

The CLI is only useful once something produces its input. The repository ships a real, working reference for this: [`examples/js-playwright-capture`](https://github.com/mustafasercansak/automation-sandbox/tree/main/examples/js-playwright-capture), a Node.js + Playwright script that captures a web page's DOM into the exact JSON shape above - mirroring the same role/accessible-name detection and `ControlType`/`AutomationId` mapping rules `AutomationSandbox.WebDiscovery` uses on the .NET side, so a tree captured in JS scores consistently with one captured in .NET. It's deliberately minimal (regular DOM only - no Shadow DOM or iframe traversal), and ships with a bundled fixture so it runs with zero network dependency out of the box.

### Capturing JSON from Any Language: Desktop (Python/pywinauto)

The same idea for Windows desktop applications: [`examples/python-pywinauto-capture`](https://github.com/mustafasercansak/automation-sandbox/tree/main/examples/python-pywinauto-capture), a Python script using [pywinauto](https://github.com/pywinauto/pywinauto)'s `uia` backend - the same underlying Windows UI Automation COM API `FlaUI`/`UIA3` uses, so it produces genuinely equivalent data, not just a matching JSON shape. Unlike the JS example, this one could not be verified locally by the assistant that wrote it (no Windows/UI Automation access in that environment) - its real verification is a CI job (`Desktop Capture (Python)`) that builds this repository's own WinForms demo app, launches it, captures its real UI tree, and confirms `automation-sandbox-heal` heals a synthetic stale locator built from that real data.

A minimal Python sketch, if your own tooling already produces a dict in the same shape:

```python
import json, subprocess

result = subprocess.run(
    ["automation-sandbox-heal", "--expected", "expected.json", "--tree", "tree.json", "--profile", "balanced"],
    capture_output=True, text=True,
)
heal_result = json.loads(result.stdout)
if result.returncode == 0:
    print(f"Healed: use {heal_result['Matched']['AutomationId']} instead")
```

### Why a CLI, Not Just the Library

`AutomationSandbox.SelfHealing`/`AutomationSandbox.UiModel` are the right choice for a .NET project that already captures its own `UiElementInfo` trees (via `Discovery`, `WebDiscovery`, or a hand-rolled adapter) and wants to call the resolver in-process. The CLI is for everything else: a non-.NET test suite, a shell script in a CI pipeline, or any tool that can produce a JSON tree but has no reason to take on a .NET dependency just to get a heal decision.

### Known Limitations

- The published package currently only ships as a `--prerelease` version during the `0.x` beta series.
- The JSON Schema is validated manually, not CI-enforced (this repo's own tooling stays C#/PowerShell-only) - if `UiElementInfo` gains or renames a property, nothing currently catches the schema silently drifting out of sync.
- The JS/Playwright capture example is deliberately minimal and is not CI-wired against a live browser.
- The Python/pywinauto desktop example is CI-verified on `windows-latest`, but was never run locally by the assistant that wrote it - no Windows/UI Automation access in that environment.

---

## 🇹🇷 Türkçe Kılavuz

Bu sitedeki diğer her rehber bir .NET projesi varsayar: bir paket referansı ekleyin, C# yazın, `SelfHealingResolver`'ı süreç içinde çağırın. `automation-sandbox-heal` (`AutomationSandbox.Cli`) bu gereksinimi tamamen ortadan kaldırır. `SelfHealingResolver.Resolve` yalnızca framework'ten bağımsız `UiElementInfo` ağacı üzerinde çalışır - FlaUI yok, Playwright .NET SDK'sı yok, Windows bağımlılığı yok - yani "her dilden, her CI'dan" bu motora ulaşmanın önündeki tek gerçek engel paketlemeydi. Bu rehber tüm hikayeyi kapsar: CLI'nin kendisi, okuyup yazdığı JSON sözleşmesi, onu saran GitHub Action, ve C# dışında bir dil için çalışan gerçek bir yakalama örneği.

### Kurulum

```bash
dotnet tool install -g AutomationSandbox.Cli --prerelease
```

`0.x` beta serisi boyunca `--prerelease` gereklidir; kararlı bir `1.0` yayınlandığında kaldırın. Bu hâlâ makinede .NET runtime'ının bulunmasını gerektirir - aracın kendisi gerçek bir .NET çalıştırılabilir dosyasıdır, sadece çağıranın bir .NET *projesine* ihtiyacı yoktur.

### Temel Kullanım

```bash
automation-sandbox-heal --expected stale-element.json --tree live-tree.json --profile balanced
```

- `--expected` - tek bir `UiElementInfo` JSON nesnesi: eski/kırık locator'ın bilinen son hali.
- `--tree` - bir `UiElementInfo` JSON ağacı: canlı uygulamanın taze bir anlık görüntüsü.
- `--profile` - `balanced` (varsayılan), `conservative`, veya `aggressive`. Her birinin neyi değiş tokuş ettiği için `AGENTS.md`'deki healing-pipeline eşiklerine bakın.

Stdout'a bir `HealResult` JSON olarak basılır - bu projedeki her API yüzeyinin sergilediği aynı açıklanabilir `ScoreComponents` dökümü, `RunnerUpScore`, ve `EvidenceCoverage`. Tanılama günlükleri stderr'e gider, yani stdout her zaman saf, ayrıştırılabilir JSON'dur. Çıkış kodu `0` `IsConfident` anlamına gelir (`Matched`'i kabul etmek güvenlidir); `1` yeterince emin olunmadığını gösterir (otomatik kabul etmeyin); `2` bir karara varılmadan önce bir hata oluştuğu anlamına gelir (kötü girdi, eksik dosya) - asla ham bir stack trace değil.

### JSON Şeması

Her iki JSON girdisi de, aracın kendi çıktısı da yayınlanmış bir [JSON Schema](schemas/ui-element-info.schema.json)'yı takip eder. Herhangi bir C# kaynağını okumadan otomatik tamamlama ve doğrulama için editörünüzü veya bir doğrulayıcıyı buna yönlendirin:

```json
{
  "$schema": "https://mustafasercansak.github.io/automation-sandbox/schemas/ui-element-info.schema.json",
  "ControlType": "Button"
}
```

Yalnızca `SimilarityScorer`'ın beş ağırlıklı sinyalinin (`ControlType`, `Name`, `AutomationId`, `BoundingRectangle`, `ParentControlType`) arkasındaki alanlar zorunludur; geri kalan her şey isteğe bağlıdır, bu da motorun bir özellik girdi JSON'unda yoksa başvurduğu C# varsayılanlarıyla eşleşir.

### CI'da Kullanım: GitHub Action

Bir composite GitHub Action, kurulum, çalıştırma ve JSON ayrıştırma adımlarını tek bir satıra sarmalar:

```yaml
- uses: mustafasercansak/automation-sandbox/.github/actions/heal@main
  with:
    expected: path/to/stale-element.json
    tree: path/to/live-tree.json
    profile: balanced
```

Varsayılan olarak bu adım, sonuç yeterince emin değilse job'u başarısız yapar - tıpkı CLI'nin kendi exit-code sözleşmesi gibi, yani sade bir `uses:` satırı zaten çalışan bir pass/fail kapısıdır. Build'i başarısız yapmak yerine sonuca göre dallanmak isterseniz `continue-on-error: true` ekleyin ve `is-confident`/`result` çıktılarını okuyun. İsteğe bağlı bir `source` girdisi, action'ın nuget.org yerine özel bir feed'den kurulum yapmasına izin verir.

### Herhangi Bir Dilden JSON Yakalamak: Web (JS/Playwright)

CLI, ancak girdisini üreten bir şey olduğunda işe yarar. Depo bunun için gerçek, çalışan bir referans sunar: [`examples/js-playwright-capture`](https://github.com/mustafasercansak/automation-sandbox/tree/main/examples/js-playwright-capture), bir web sayfasının DOM'unu yukarıdaki tam JSON şeklinde yakalayan bir Node.js + Playwright betiği - `AutomationSandbox.WebDiscovery`'nin .NET tarafında kullandığı aynı rol/erişilebilir-isim tespiti ve `ControlType`/`AutomationId` eşleme kurallarını yansıtır, böylece JS'de yakalanan bir ağaç .NET'te yakalanmış biriyle tutarlı puanlanır. Bilinçli olarak minimaldir (sadece normal DOM - Shadow DOM veya iframe gezinmesi yok), ve sıfır ağ bağımlılığıyla çalışması için birlikte gelen bir fixture içerir.

### Herhangi Bir Dilden JSON Yakalamak: Masaüstü (Python/pywinauto)

Windows masaüstü uygulamaları için aynı fikir: [`examples/python-pywinauto-capture`](https://github.com/mustafasercansak/automation-sandbox/tree/main/examples/python-pywinauto-capture), [pywinauto](https://github.com/pywinauto/pywinauto)'nun `uia` backend'ini kullanan bir Python betiği - `FlaUI`/`UIA3`'ün kullandığı aynı Windows UI Automation COM API'si, yani sadece eşleşen bir JSON şekli değil, gerçekten eşdeğer veri üretir. JS örneğinin aksine, bu örnek bunu yazan asistan tarafından yerel olarak doğrulanamadı (o ortamda Windows/UI Automation erişimi yok) - gerçek doğrulaması, bu deponun kendi WinForms demo uygulamasını derleyen, başlatan, gerçek UI ağacını yakalayan ve `automation-sandbox-heal`'in bu gerçek veriden oluşturulan sentetik bir eski locator'ı iyileştirdiğini doğrulayan bir CI job'udur (`Desktop Capture (Python)`).

Kendi araçlarınız zaten aynı şekilde bir dict üretiyorsa, minimal bir Python taslağı:

```python
import json, subprocess

result = subprocess.run(
    ["automation-sandbox-heal", "--expected", "expected.json", "--tree", "tree.json", "--profile", "balanced"],
    capture_output=True, text=True,
)
heal_result = json.loads(result.stdout)
if result.returncode == 0:
    print(f"Healed: use {heal_result['Matched']['AutomationId']} instead")
```

### Neden CLI, Sadece Kütüphane Değil

`AutomationSandbox.SelfHealing`/`AutomationSandbox.UiModel`, kendi `UiElementInfo` ağaçlarını zaten yakalayan (`Discovery`, `WebDiscovery`, veya elle yazılmış bir adaptör ile) ve resolver'ı süreç içinde çağırmak isteyen bir .NET projesi için doğru seçimdir. CLI, geri kalan her şey içindir: .NET dışı bir test paketi, bir CI pipeline'ındaki bir shell script, veya bir JSON ağacı üretebilen ama sadece bir healing kararı almak için .NET bağımlılığı almasına gerek olmayan herhangi bir araç.

### Bilinen Sınırlamalar

- Yayınlanmış paket şu anda `0.x` beta serisi boyunca yalnızca `--prerelease` sürüm olarak gelir.
- JSON Schema elle doğrulanır, CI tarafından zorunlu kılınmaz (bu deponun kendi araç seti C#/PowerShell ile sınırlı kalır) - `UiElementInfo` bir özellik kazanır veya yeniden adlandırılırsa, şemanın sessizce uyumsuzlaşmasını şu anda hiçbir şey yakalamaz.
- JS/Playwright yakalama örneği bilinçli olarak minimaldir ve canlı bir tarayıcıya karşı CI'a bağlanmamıştır.
- Python/pywinauto masaüstü örneği `windows-latest` üzerinde CI ile doğrulanmıştır, ama bunu yazan asistan tarafından yerel olarak hiç çalıştırılmadı - o ortamda Windows/UI Automation erişimi yok.
