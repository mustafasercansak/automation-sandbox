# 📊 Self-Healing Reports & Dashboard / İyileştirme Raporları ve Görsel Panel

This guide explains how **Automation Sandbox** generates JSON and HTML visual report artifacts whenever locator healing events occur.

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Guide](#-english-guide)
> - [🇹🇷 Türkçe Kılavuz](#-türkçe-kılavuz)

---

## 🇬🇧 English Guide

### 💡 Overview
When running automated tests in CI/CD pipelines (e.g. GitHub Actions, Azure DevOps, Jenkins), knowing **which locators healed**, **what changed**, and **whether AI was used** is essential for test maintenance.

`SelfHealingEngine` emits append-only **JSON** (`healing-report.json`) and **HTML Dashboard** (`healing-report.html`) report artifacts automatically!

---

### ⚙️ Enabling Reports via Environment Variables

You can enable automatic report generation without modifying your test code by setting environment variables:

```bash
# Set report file output path
export SELF_HEALING_REPORT_PATH="TestResults/healing-report.json"

# Run your test suite as usual
dotnet test TestAutomation/ScenarioRunner/ScenarioRunner.csproj
```

By default, an interactive **HTML Dashboard** (`healing-report.html`) is written alongside the JSON file!

---

### 🔍 Report Content & Review Status

Each event in the report contains:
- **`LocatorKey`**: The test locator key (e.g. `RegistrationPage.SubmitButton`).
- **`ReviewStatus`**:
  - `accepted`: High-confidence heuristic match ($\ge 50\%$).
  - `accepted-with-llm`: Matches resolved via Gemini, Claude, OpenAI, or Ollama.
  - `manual-review`: Borderline matches requiring QA engineer review.
- **`PreviousSnapshot` & `AcceptedSnapshot`**: Full before/after element comparison.
- **`ScoreBreakdown`**: Component breakdown (`ControlType`, `Parent`, `Sibling`, `Name`, `Position`). A component is `null` when that signal had no evidence on either side (missing == missing is never a perfect match).
- **`EvidenceCoverage`**: Fraction of the total signal weight backed by non-null evidence (schema v2+). `null` on entries upgraded from v1 reports means "unknown", not "no evidence".
- **`RunnerUpScore`** (schema v3+): Second-best candidate score at decision time (`null` when there was no runner-up) — the margin gate's input, persisted for offline audit.
- **`Candidates`** (schema v2+): Every scored candidate — not just the winner — with `TotalScore`, `Components`, and `EvidenceCoverage`, so thresholds can be re-tuned offline against recorded reports.

> [!WARNING]
> **Report size:** the candidate list is intentionally **unpruned**. On very large UI trees a single event can add ~1 MB of JSON (≈1.3 MB measured on a 3,001-node tree), and each `Record()` call rewrites the whole file — cost grows quadratically with event count. Enable file reports (`SELF_HEALING_REPORT_PATH`) on CI/diagnostic runs, not on every local run.

---

## 🇹🇷 Türkçe Kılavuz

### 💡 Genel Bakış
CI/CD süreçlerinde (GitHub Actions, Azure DevOps vb.) testleriniz çalışırken **hangi elemanların iyileştirildiği**, **neye dönüştüğü** ve **yapay zekanın devreye girip girmediği** raporlanmalıdır.

`SelfHealingEngine` motoru kabul edilen tüm iyileştirme olaylarını anlık olarak **JSON** (`healing-report.json`) ve **HTML Görsel Gösterge Paneli** (`healing-report.html`) olarak otomatik kaydeder!

---

### ⚙️ Çevre Değişkenleri İle Raporlamayı Etkinleştirme

Test kodlarınızı değiştirmeden, yalnızca ortam değişkeni tanımlayarak raporlamayı açabilirsiniz:

```bash
# Rapor dosyasının yazılacağı konumu belirleyin
export SELF_HEALING_REPORT_PATH="TestResults/healing-report.json"

# Testlerinizi her zamanki gibi çalıştırın
dotnet test TestAutomation/ScenarioRunner/ScenarioRunner.csproj
```

JSON dosyası oluştuğunda yanında **etkileşimli HTML Rapor Paneli** (`healing-report.html`) otomatik üretilir!

---

### 🔍 Rapor İçeriği ve İnceleme Durumları

Rapordaki her olay şu bilgileri içerir:
- **`LocatorKey`**: Test elemanının anahtarı (örn: `KayitFormu.GonderButonu`).
- **`ReviewStatus` (İnceleme Durumu):**
  - `accepted`: Yüksek güvenli sezgisel eşleşme ($\ge \%50$).
  - `accepted-with-llm`: Yapay zeka (Gemini, Claude, OpenAI, Ollama) ile çözülen eşleşme.
  - `manual-review`: Sınırda kalan ve QA mühendisi onayı gerektiren eşleşme.
- **`PreviousSnapshot` & `AcceptedSnapshot`**: Elemanın iyileştirme öncesi ve sonrası tüm özellikleri.
- **`ScoreBreakdown`**: Bileşen dökümü (`ControlType`, `Parent`, `Sibling`, `Name`, `Position`). Bir sinyal iki tarafta da yoksa ilgili bileşen `null` olur (eksik == eksik asla tam eşleşme sayılmaz).
- **`EvidenceCoverage`**: Boş olmayan kanıtla desteklenen toplam sinyal ağırlığının oranı (şema v2+). v1'den yükseltilen girdilerde `null` değeri "kanıt yok" değil "bilinmiyor" demektir.
- **`RunnerUpScore`** (şema v3+): Karar anındaki ikinci en iyi aday skoru (ikinci aday yoksa `null`) — margin kapısının girdisi, çevrimdışı denetim için saklanır.
- **`Candidates`** (şema v2+): Yalnızca kazanan değil, skorlanan **tüm** adaylar — `TotalScore`, `Components` ve `EvidenceCoverage` ile birlikte; eşiklerin çevrimdışı yeniden ayarlanabilmesi için.

> [!WARNING]
> **Rapor boyutu:** aday listesi bilinçli olarak **budanmamıştır**. Çok büyük UI ağaçlarında tek bir olay ~1 MB JSON ekleyebilir (3.001 düğümlü ağaçta ≈1,3 MB ölçüldü) ve her `Record()` çağrısı dosyanın tamamını yeniden yazar — maliyet olay sayısıyla karesel büyür. Dosya raporlarını (`SELF_HEALING_REPORT_PATH`) her yerel çalıştırmada değil, CI/teşhis çalıştırmalarında açın.
