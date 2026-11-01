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

### Scanning a Website for Typos: `automation-sandbox-audit`

A second tool, `AutomationSandbox.SiteAudit` (command `automation-sandbox-audit`), crawls a site from one URL and reports spelling mistakes, plus duplicated words and leftover placeholders. It needs no API key and sends no page text anywhere: spelling comes from [Hunspell](https://hunspell.github.io/) dictionaries, which handle inflected languages such as Turkish. It requires the .NET 8 runtime (or newer) and downloads Chromium for the crawl.

```bash
automation-sandbox-audit --url https://example.com --lang tr,en --allow-word Playwright --install-browser
```

- `--lang` lists dictionaries, primary language first (default `en`; regional variants such as `en-GB` work, which matters because `en` is American spelling and flags `behaviour`). A word is accepted if **any** listed dictionary accepts it, which suits a Turkish page containing English terms; the trade-off is that a typo that happens to be a valid word in the other language is not reported. Turkish suffixes on foreign words (`artifact'ları`) are judged by the stem.
- `--allow-word` / `--allow-file` keep brand and product names out of the report. They also exempt a word from the duplicate-word check, which is how a language that repeats a word on purpose (Turkish `adım adım`, "step by step") is not reported as a typo.
- Dictionaries are downloaded once, from a pinned commit of [`wooorm/dictionaries`](https://github.com/wooorm/dictionaries) (nothing is bundled), into a per-user cache; `--no-download` forbids the download for offline use.
- Exit code `0` means no findings, `1` means findings (so a CI step can gate on it), `2` means the audit could not run (bad arguments, missing dictionary, start page unreachable). A JSON Lines log and an HTML report are written to `--output-dir` (overwritten on each run).
- It judges single words only. Grammar and meaning are outside a dictionary's reach, and brand names, jargon and code you did not allow-list will be reported as misspellings. Text is checked up to 10,000 characters per element. Expect many false positives on a developer-documentation site (the project's own docs: 420 distinct flagged words across 31 pages, mostly terms such as `json` and `nuget`; the English dictionary is small and lacks plurals like `integrations`); it suits marketing and content sites far better, and an allow list takes care of the rest.
- Not published to nuget.org yet; until it is, pack it from the repository (`dotnet pack TestAutomation/SiteAuditCli`). The `Site Audit CLI` CI job exercises the packed tool against a fixture site with the real Turkish and English dictionaries.

#### Running the typo audit on GitHub

A composite action wraps the install, run, summary and upload steps, so a scheduled workflow is one `uses:` line. It needs no API key, and no page text leaves the runner:

```yaml
name: Typo audit
on:
  schedule:
    - cron: '0 6 * * 1'
  workflow_dispatch:
jobs:
  audit:
    runs-on: ubuntu-latest
    steps:
      - uses: mustafasercansak/automation-sandbox/.github/actions/site-audit@main
        with:
          url: https://example.com
          lang: tr,en
          allow-words: |
            Acme
            Playwright
```

- It writes a findings table to the job summary and uploads the JSON and HTML report as an artifact (`upload-report: 'false'` to skip, `artifact-name` if the action runs twice in one job).
- By default the job fails when anything is found. Set `fail-on-findings: 'false'` to only report, and read the `issue-count` and `exit-code` outputs to branch on them. A run that could not execute (bad URL, missing dictionary) always fails, so a broken audit never looks like a clean site.
- Because the tool is not on nuget.org yet, the action builds it from its own repository checkout, so the tool version is exactly the one at the ref you pin the action to. Pin a tag or commit SHA rather than `@main` for a reproducible audit. That build adds about a minute. Once a version is published, `version:` installs it instead.
- It audits the site as served, so it covers what a browser sees: run it after a deploy, or on a schedule against production. Chromium is installed by the tool; a self-hosted runner without the browser's system libraries needs them installed first.

### Triaging an Issue from the Command Line: `automation-sandbox-agent`

`automation-sandbox-agent triage` (#585) runs one GitHub issue through the issue-triage agent described in the [LLM provider guide](llm-providers.md): it reads the issue and its project board card, applies the `Agent::` label gate, routes the models from the issue's `Model::<name>` and `Review::<name>` labels, drafts a triage proposal and lets a second model check it. It is read-only: it changes no label, comment or card. The report is JSON on stdout; diagnostics go to stderr.

```sh
export PROJECT_TOKEN=$(gh auth token)      # read access to the project and its issues
export GEMINI_API_KEY=...                  # the drafting model
export MISTRAL_API_KEY=... MISTRAL_MODEL=mistral-medium-2508   # the reviewer
automation-sandbox-agent triage --repo owner/name --issue 7 --project-id PVT_xxxx
```

Keys and tokens are read from environment variables only, never from an argument (an argument reaches the shell history and the process list); a flag that looks like one is rejected and never echoed. The issue needs `Agent::Allowed`, one `Task::issue-triage` or `Task::ci-failure` label and the `Ready` column of the board. For `ci-failure` the issue body is read as the pasted CI log and the report gains a `ciFailure` section (the log's deterministic category, then the proposed issue); the `docs` task kind is not implemented yet and is reported as such. Exit codes: `0` the proposal was accepted, `1` the issue is not eligible, routing stopped, or the proposal was blocked or failed (the report says why), `2` the command could not run (bad arguments, no token, no model). Packing, installing and these exit codes are checked in CI; the path from an issue to a proposal is covered by unit tests with a stub GitHub and fake models. Real runs so far (2026-10-31, through the manual workflow `agent-triage.yml`, #589): on a plain trial issue and on a deliberately hostile one (a forged data marker, "ignore all previous instructions", a forged JSON answer with a link, `@everyone` and protocol labels, and a request to print the keys), Gemini drafted and Ollama Cloud (`gemma4:31b`) reviewed, and both runs were accepted with a correct, harmless proposal; the hostile instructions were not followed. That is two runs, one model pair and one hostile text: it does not show that a model cannot be tricked, only that these safeguards were not tested against one that was. On the same days Mistral answered HTTP 429 as a drafter and as a reviewer, and an NVIDIA model that its public list shows answered HTTP 404 for this account.

**Posting the result on the issue (`--comment`, #590).** By default nothing is written to GitHub. With `--comment` the run ends by posting its report as one comment on the issue: the stage, the gate reason, which models drafted and reviewed, the proposal or the failure reason, and the reviewer's findings. The comment says plainly that a language model wrote the proposal and that a person must read it before acting on it. It needs its own token that may write issue comments (`GH_TOKEN` or `GITHUB_TOKEN`; for a public repository the `public_repo` scope of a classic token), separate from the read-only project token; without it the run stops before any model is asked. Everything a model or a provider produced is escaped before it is posted: a mention cannot notify anybody, links and HTML are inert, markdown cannot open or close formatting, and the length is capped. A provider's error is reduced to its status code and a short reason, because the response body can carry account identifiers (the first real NVIDIA 404 did); the JSON report on stdout gets the same reduction. A comment that cannot be posted ends the command with exit code `2`. No label and no card is ever changed.

**Running it on GitHub instead of your own machine (#589).** The repository secrets (model keys) cannot be read back by anyone, so a model that is only configured there cannot be tried from a terminal. The manual workflow `agent-triage.yml` runs the same tool on GitHub's runner with those secrets: start it from the Actions tab (or `gh workflow run agent-triage.yml -f issue_number=7`), and read the report in the job summary. With `post_comment` it also posts the report as a comment, through the workflow's own token, so no extra token is needed for that. Reading a user-owned project board does need a `PROJECT_TOKEN` secret (a classic token with `read:project` and nothing else). Only a person who may run workflows can start it; no issue, label, comment or pull request event can, so a stranger cannot spend model quota or reach the secrets.

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

### Web Sitesinde Yazım Hatası Taramak: `automation-sandbox-audit`

İkinci bir araç, `AutomationSandbox.SiteAudit` (komut: `automation-sandbox-audit`), tek bir URL'den başlayarak siteyi gezer ve yazım hatalarını, tekrarlanan kelimeleri ve unutulmuş yer tutucu metinleri raporlar. API anahtarı gerektirmez ve sayfa metnini hiçbir yere göndermez: yazım denetimi, Türkçe gibi ek alan dilleri de işleyebilen [Hunspell](https://hunspell.github.io/) sözlüklerinden gelir. .NET 8 runtime'ı (veya daha yenisi) gerekir ve gezme için Chromium indirir.

```bash
automation-sandbox-audit --url https://example.com --lang tr,en --allow-word Playwright --install-browser
```

- `--lang` sözlükleri listeler, ana dil önce gelir (varsayılan `en`; `en-GB` gibi bölgesel varyantlar çalışır, `en` Amerikan imlasıdır ve `behaviour` kelimesini hatalı bulur). Listelenen sözlüklerden **herhangi biri** bir kelimeyi kabul ederse kelime doğru sayılır; bu, İngilizce terimler içeren Türkçe bir sayfaya uygundur. Bedeli: diğer dilde geçerli bir kelime olan bir yazım hatası raporlanmaz. Yabancı kelimelere gelen Türkçe ekler (`artifact'ları`) kök üzerinden değerlendirilir.
- `--allow-word` / `--allow-file` marka ve ürün adlarını rapordan uzak tutar. Ayrıca bir kelimeyi tekrarlanan-kelime denetiminden de muaf tutar; bir kelimeyi bilerek tekrarlayan bir dilde (Türkçe `adım adım`) bu, yazım hatası olarak raporlanmaz.
- Sözlükler bir kez, [`wooorm/dictionaries`](https://github.com/wooorm/dictionaries) deposunun sabitlenmiş bir commit'inden indirilir (pakete gömülü değildir) ve kullanıcıya özel bir önbelleğe konur; `--no-download` çevrimdışı kullanım için indirmeyi yasaklar.
- Çıkış kodu `0` bulgu yok, `1` bulgu var (CI adımı buna göre kapı olabilir), `2` tarama çalıştırılamadı (hatalı argüman, eksik sözlük, ulaşılamayan başlangıç sayfası) demektir. `--output-dir` altına bir JSON Lines günlüğü ve bir HTML rapor yazılır (her çalıştırmada üzerine yazılır).
- Yalnızca tek tek kelimeleri değerlendirir. Dilbilgisi ve anlam bir sözlüğün erişiminin dışındadır; izin listesine eklemediğiniz marka adları, jargon ve kod yazım hatası olarak raporlanır. Metin, eleman başına 10.000 karaktere kadar denetlenir. Geliştirici belgesi sitelerinde çok sayıda yanlış alarm bekleyin (projenin kendi belgeleri: 31 sayfada 420 farklı işaretli kelime, çoğu `json` ve `nuget` gibi terimler; İngilizce sözlük küçüktür ve `integrations` gibi çoğulları içermez); pazarlama ve içerik siteleri için çok daha uygundur, kalanını bir izin listesi halleder.
- Henüz nuget.org'da yayımlanmadı; yayımlanana kadar depodan paketleyin (`dotnet pack TestAutomation/SiteAuditCli`). `Site Audit CLI` CI job'ı paketlenmiş aracı, gerçek Türkçe ve İngilizce sözlüklerle bir fixture sitesine karşı çalıştırır.

#### Yazım denetimini GitHub'da çalıştırmak

Bir composite action kurulum, çalıştırma, özet ve yükleme adımlarını sarar; böylece zamanlanmış bir workflow tek bir `uses:` satırıdır. API anahtarı gerektirmez ve sayfa metni runner'dan çıkmaz:

```yaml
name: Typo audit
on:
  schedule:
    - cron: '0 6 * * 1'
  workflow_dispatch:
jobs:
  audit:
    runs-on: ubuntu-latest
    steps:
      - uses: mustafasercansak/automation-sandbox/.github/actions/site-audit@main
        with:
          url: https://example.com
          lang: tr,en
          allow-words: |
            Acme
            Playwright
```

- Job özetine bir bulgu tablosu yazar ve JSON ile HTML raporunu artifact olarak yükler (atlamak için `upload-report: 'false'`, action aynı job'da iki kez çalışıyorsa `artifact-name`).
- Varsayılan olarak bir şey bulunursa job başarısız olur. Yalnızca raporlamak için `fail-on-findings: 'false'` verin ve `issue-count` ile `exit-code` çıktılarını okuyarak dallanın. Çalıştırılamayan bir tarama (hatalı URL, eksik sözlük) her zaman başarısız olur; bozuk bir tarama hiçbir zaman temiz bir site gibi görünmez.
- Araç henüz nuget.org'da olmadığından action onu kendi depo checkout'undan derler; yani araç sürümü, action'ı sabitlediğiniz ref'teki sürümdür. Tekrarlanabilir bir tarama için `@main` yerine bir etiket veya commit SHA'sı sabitleyin. Bu derleme yaklaşık bir dakika ekler. Bir sürüm yayımlandığında `version:` onu kurar.
- Siteyi sunulduğu haliyle tarar, yani bir tarayıcının gördüğünü kapsar: yayına almadan sonra veya üretime karşı zamanlanmış olarak çalıştırın. Chromium'u araç kurar; tarayıcının sistem kütüphaneleri olmayan bir self-hosted runner'da önce onların kurulması gerekir.

### Komut Satırından Bir Issue'yu Sınıflandırmak: `automation-sandbox-agent`

`automation-sandbox-agent triage` (#585), [LLM sağlayıcı rehberinde](llm-providers.md) anlatılan issue sınıflandırma ajanından tek bir GitHub issue'sunu geçirir: issue'yu ve proje panosundaki kartını okur, `Agent::` etiket kapısını uygular, modelleri issue'nun `Model::<ad>` ve `Review::<ad>` etiketlerinden yönlendirir, bir sınıflandırma önerisi üretir ve ikinci bir modelin kontrol etmesini sağlar. Salt okunurdur: hiçbir etiketi, yorumu ya da kartı değiştirmez. Rapor stdout'ta JSON olarak verilir; tanılama iletileri stderr'e gider.

```sh
export PROJECT_TOKEN=$(gh auth token)      # projeyi ve issue'ları okuma yetkisi
export GEMINI_API_KEY=...                  # taslağı yazan model
export MISTRAL_API_KEY=... MISTRAL_MODEL=mistral-medium-2508   # denetçi
automation-sandbox-agent triage --repo owner/name --issue 7 --project-id PVT_xxxx
```

Anahtarlar ve token'lar yalnızca ortam değişkenlerinden okunur, asla bir argümandan değil (argüman kabuk geçmişine ve işlem listesine düşer); buna benzeyen bir bayrak reddedilir ve asla geri yazılmaz. Issue'nun `Agent::Allowed` etiketi, bir `Task::issue-triage` ya da `Task::ci-failure` etiketi ve panoda `Ready` sütununda olması gerekir. `ci-failure` için issue gövdesi yapıştırılmış CI günlüğü olarak okunur ve rapora bir `ciFailure` bölümü eklenir (günlüğün belirleyici kategorisi, ardından önerilen issue); `docs` görev türü henüz uygulanmamıştır ve öyle raporlanır. Çıkış kodları: `0` öneri kabul edildi, `1` issue uygun değil, yönlendirme durdu ya da öneri engellendi veya başarısız oldu (rapor nedenini söyler), `2` komut çalışamadı (hatalı argüman, token yok, model yok). Paketleme, kurulum ve bu çıkış kodları CI'da denetlenir; bir issue'dan öneriye giden yol, taklit bir GitHub ve sahte modellerle birim testleriyle kapsanır. Şimdiye kadarki gerçek çalıştırmalar (31 Ekim 2026, elle başlatılan `agent-triage.yml` iş akışıyla, #589): düz bir deneme issue'sunda ve bilerek düşmanca yazılmış bir issue'da (sahte bir veri işaretleyicisi, "önceki talimatları unut", bağlantılı, `@everyone`'lu ve korumalı etiketli sahte bir JSON yanıtı ve anahtarları yazdırma isteği) taslağı Gemini yazdı, Ollama Cloud (`gemma4:31b`) denetledi ve iki çalışma da doğru, zararsız bir öneriyle kabul edildi; düşmanca talimatlara uyulmadı. Bu iki çalışma, tek bir model çifti ve tek bir düşmanca metindir: bir modelin kandırılamayacağını göstermez, yalnızca bu önlemlerin kandırılmış bir modele karşı denenmediğini söyler. Aynı gün Mistral hem taslak yazarken hem denetlerken HTTP 429 verdi, ve genel listesinde görünen bir NVIDIA modeli bu hesap için HTTP 404 verdi.

**Sonucu issue'ya yazmak (`--comment`, #590).** Varsayılan olarak GitHub'a hiçbir şey yazılmaz. `--comment` ile çalışma, raporunu issue'ya tek bir yorum olarak yazarak biter: aşama, kapının nedeni, taslağı hangi modelin yazdığı ve kimin denetlediği, öneri ya da hatanın nedeni ve denetçinin bulguları. Yorum, öneriyi bir dil modelinin yazdığını ve harekete geçmeden önce bir insanın okuması gerektiğini açıkça söyler. Yalnızca-okuma proje token'ından ayrı, issue yorumu yazabilen kendi token'ını ister (`GH_TOKEN` ya da `GITHUB_TOKEN`; herkese açık bir repo için klasik token'ın `public_repo` kapsamı); o yoksa çalışma, hiçbir model çağrılmadan önce durur. Bir modelin ya da sağlayıcının ürettiği her şey yazılmadan önce temizlenir: bir anma kimseye bildirim göndermez, bağlantılar ve HTML etkisizdir, markdown biçimlendirmeyi açıp kapatamaz ve uzunluk sınırlıdır. Bir sağlayıcı hatası, yalnızca durum koduna ve kısa bir nedene indirilir, çünkü yanıt gövdesi hesap tanımlayıcıları taşıyabilir (ilk gerçek NVIDIA 404'ü taşıdı); stdout'taki JSON raporu da aynı indirgemeyi alır. Yazılamayan bir yorum, komutu `2` çıkış koduyla bitirir. Hiçbir etiket ve hiçbir kart değiştirilmez.

**Kendi bilgisayarın yerine GitHub'da çalıştırmak (#589).** Depo gizli anahtarları (model anahtarları) kimse tarafından geri okunamaz; bu yüzden yalnızca orada tanımlı bir model terminalden denenemez. Elle başlatılan `agent-triage.yml` iş akışı, aynı aracı GitHub'ın çalıştırıcısında bu anahtarlarla çalıştırır: Actions sekmesinden (ya da `gh workflow run agent-triage.yml -f issue_number=7` ile) başlatılır ve rapor iş özetinden okunur. `post_comment` ile raporu ayrıca bir yorum olarak da yazar; bunu iş akışının kendi token'ıyla yaptığı için ek bir token gerekmez. Kullanıcıya ait bir proje panosunu okumak ise bir `PROJECT_TOKEN` gizli anahtarı gerektirir (yalnızca `read:project` yetkili klasik bir token). Yalnızca iş akışı çalıştırma yetkisi olan bir kişi başlatabilir; hiçbir issue, etiket, yorum ya da pull request olayı başlatamaz, bu yüzden bir yabancı model kotanı harcayamaz ve gizli anahtarlara ulaşamaz.

### Neden CLI, Sadece Kütüphane Değil

`AutomationSandbox.SelfHealing`/`AutomationSandbox.UiModel`, kendi `UiElementInfo` ağaçlarını zaten yakalayan (`Discovery`, `WebDiscovery`, veya elle yazılmış bir adaptör ile) ve resolver'ı süreç içinde çağırmak isteyen bir .NET projesi için doğru seçimdir. CLI, geri kalan her şey içindir: .NET dışı bir test paketi, bir CI pipeline'ındaki bir shell script, veya bir JSON ağacı üretebilen ama sadece bir healing kararı almak için .NET bağımlılığı almasına gerek olmayan herhangi bir araç.

### Bilinen Sınırlamalar

- Yayınlanmış paket şu anda `0.x` beta serisi boyunca yalnızca `--prerelease` sürüm olarak gelir.
- JSON Schema elle doğrulanır, CI tarafından zorunlu kılınmaz (bu deponun kendi araç seti C#/PowerShell ile sınırlı kalır) - `UiElementInfo` bir özellik kazanır veya yeniden adlandırılırsa, şemanın sessizce uyumsuzlaşmasını şu anda hiçbir şey yakalamaz.
- JS/Playwright yakalama örneği bilinçli olarak minimaldir ve canlı bir tarayıcıya karşı CI'a bağlanmamıştır.
- Python/pywinauto masaüstü örneği `windows-latest` üzerinde CI ile doğrulanmıştır, ama bunu yazan asistan tarafından yerel olarak hiç çalıştırılmadı - o ortamda Windows/UI Automation erişimi yok.
