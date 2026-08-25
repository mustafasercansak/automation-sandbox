---
layout: default
title: Healing Reports & Dashboard - Automation Sandbox
---

# 📊 Self-Healing Reports & Dashboard / İyileştirme Raporları ve Görsel Panel

This guide explains how **Automation Sandbox** generates JSON and HTML visual report artifacts whenever locator resolution is attempted.

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Guide](#-english-guide)
> - [🇹🇷 Türkçe Kılavuz](#-türkçe-kılavuz)

---

## 🇬🇧 English Guide

### 💡 Overview
When running automated tests in CI/CD pipelines (e.g. GitHub Actions, Azure DevOps, Jenkins), knowing **which locators healed**, **what changed**, and **whether AI was used** is essential for test maintenance.

`SelfHealingEngine` emits append-only **JSON** (`healing-report.json`) and **HTML Dashboard** (`healing-report.html`) report artifacts automatically. Schema v8 records accepted heals and declined or failed attempts, including opt-in batch ownership conflicts, so the report no longer implies a 100% success rate by construction.

For `ExecuteWithHealingAsync`, an accepted event is written only after the action retry with the proposed element succeeds. If that retry fails, the proposal is not reported as accepted and the locator repository remains unchanged.

`HealingReportFileSink` writes the next JSON document to an adjacent temporary file and
commits it with one atomic replace operation when a report already exists. It never
deletes the previous JSON before the replacement. If serialization or the commit fails,
the existing history remains readable and the temporary file is cleaned up during normal
exception handling. A process or power loss before the atomic commit can leave a harmless
temporary file, but not remove the previous report. The HTML dashboard is derived output
written after the JSON commit and can be regenerated from that JSON.

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
- **`Outcome`** (schema v7+): The resolution result: `accepted`, `accepted-unverified`, `retry-failed`, `observed`, `manual-review`, `fail-closed`, `ambiguous`, `ownership-conflict`, `low-evidence`, `low-confidence`, `no-candidates`, `no-consensus`, `provider-error`, or `unspecified`. `observed` and `manual-review` represent non-mutating evaluations in `Observe` and `Review` healing modes. `fail-closed` represents execution in `FailClosed` mode where discovery was skipped. `ownership-conflict` means an independently accepted batch claim lost or tied the one-to-one ownership decision. `unspecified` exposes a missing decision classification without miscounting it as measured low confidence. `null` on upgraded legacy entries means the older build did not record an outcome; pre-v7 reports contained accepted heals only.
- **`Platform`** (schema v7+): The caller-provided platform identifier, such as `web-playwright` or `windows-uia`.
- **`CandidateIdentity` & `ReconciliationDisposition`** (schema v8+): Nullable batch-only ownership telemetry. The identity is an opaque path within one captured tree, not a reusable locator; `null` on older or single-locator entries means reconciliation was not observed by that writer.
- **`ReviewStatus`**:
  - `accepted`: High-confidence heuristic match ($\ge 50\%$).
  - `accepted-with-llm`: Matches resolved via an LLM provider (e.g. Gemini, Claude, OpenAI, Ollama, or any other configured provider).
  - `manual-review`: Borderline matches requiring QA engineer review.
- **`PreviousSnapshot`, `AcceptedSnapshot` & `ProposedSnapshot`**: The previous locator, an accepted replacement, or the unaccepted candidate involved in a decline/failed retry.
- **`ProviderErrors`** (schema v7+): Provider names and failure details. It remains available even when other providers reach consensus successfully.
- **`ProviderAttempts`** (schema v6+): Attempt count for every evaluated provider.
- **`ScoreBreakdown`**: Component breakdown (`ControlType`, `Parent`, `Sibling`, `Name`, `Position`). A component is `null` when that signal had no evidence on either side (missing == missing is never a perfect match).
- **`EvidenceCoverage`**: Fraction of the total signal weight backed by non-null evidence (schema v2+). `null` on entries upgraded from v1 reports means "unknown", not "no evidence".
- **`RunnerUpScore`** (schema v3+): Second-best candidate score at decision time (`null` when there was no runner-up) — the margin gate's input, persisted for offline audit.
- **`Candidates`** (schema v2+): Every scored candidate — not just the winner — with `TotalScore`, `Components`, and `EvidenceCoverage`, so thresholds can be re-tuned offline against recorded reports.

`HealingReportDocument.Events` contains every attempt. Consumers that need the pre-v7 accepted-only view can use `HealingReportDocument.AcceptedEvents`; it includes `accepted`, `accepted-unverified`, and all legacy entries without requiring hand-written filtering.

Reports can contain captured UI text, automation IDs, model reasoning, and provider error
details (including a bounded raw response when parsing fails). Treat both JSON and HTML as
sensitive test artifacts; see the [LLM Healing Security Model](llm-security-model.md#local-telemetry-is-sensitive-too).

> [!WARNING]
> **Report size:** the candidate list is intentionally **unpruned**. On very large UI trees a single event can add ~1 MB of JSON (≈1.3 MB measured on a 3,001-node tree), and each `Record()` call rewrites the whole file — cost grows quadratically with event count. Enable file reports (`SELF_HEALING_REPORT_PATH`) on CI/diagnostic runs, not on every local run.

---

## 🇹🇷 Türkçe Kılavuz

### 💡 Genel Bakış
CI/CD süreçlerinde (GitHub Actions, Azure DevOps vb.) testleriniz çalışırken **hangi elemanların iyileştirildiği**, **neye dönüştüğü** ve **yapay zekanın devreye girip girmediği** raporlanmalıdır.

`SelfHealingEngine` motoru çözüm denemelerini anlık olarak **JSON** (`healing-report.json`) ve **HTML Görsel Gösterge Paneli** (`healing-report.html`) olarak otomatik kaydeder. Şema v8, isteğe bağlı batch sahiplik çakışmaları dahil kabul edilen iyileştirmeleri ve reddedilen veya başarısız denemeleri kaydeder; böylece rapor yapısı gereği %100 başarı izlenimi vermez.

`ExecuteWithHealingAsync` kullanıldığında kabul edilmiş bir olay, yalnızca önerilen elemanla yapılan eylem tekrarı başarılı olduktan sonra yazılır. Bu tekrar başarısız olursa öneri kabul edilmiş olarak raporlanmaz ve locator repository değişmeden kalır.

`HealingReportFileSink`, sonraki JSON belgesini hedefle aynı dizindeki geçici dosyaya
yazar ve mevcut raporu tek bir atomik değiştirme işlemiyle günceller. Önceki JSON dosyası
değiştirmeden önce hiçbir zaman silinmez. Serileştirme veya commit başarısız olursa mevcut
geçmiş okunabilir kalır ve normal exception işleyişinde geçici dosya temizlenir. Atomik
commit'ten önce süreç ya da güç kesilirse zararsız bir geçici dosya kalabilir, ancak önceki
rapor kaybolmaz. HTML paneli JSON commit'inden sonra yazılan türetilmiş çıktıdır ve JSON'dan
yeniden üretilebilir.

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
- **`Outcome`** (şema v7+): Çözüm sonucu: `accepted`, `accepted-unverified`, `retry-failed`, `observed`, `manual-review`, `fail-closed`, `ambiguous`, `ownership-conflict`, `low-evidence`, `low-confidence`, `no-candidates`, `no-consensus`, `provider-error` veya `unspecified`. `observed` ve `manual-review`, `Observe` ve `Review` modlarındaki değişiklik yapmayan değerlendirmeleri belirtir. `fail-closed`, keşif adımının çalıştırılmadığı `FailClosed` modunu temsil eder. `ownership-conflict`, bağımsız kabul edilen bir batch talebinin bire bir sahiplik kararını kaybettiğini veya berabere kaldığını gösterir. `unspecified`, eksik karar sınıflandırmasını ölçülmüş `low-confidence` verisi gibi saymadan görünür kılar. Eski raporlardan yükseltilen girdilerde `null`, önceki build'in sonucu kaydetmediği anlamına gelir; v7 öncesi raporlar yalnızca kabul edilen iyileştirmeleri içeriyordu.
- **`Platform`** (şema v7+): Çağıranın verdiği `web-playwright` veya `windows-uia` gibi platform kimliği.
- **`CandidateIdentity` & `ReconciliationDisposition`** (şema v8+): Nullable ve yalnızca batch sahiplik telemetrisi. Kimlik tek yakalanmış ağaç içindeki opak yoldur, yeniden kullanılabilir locator değildir; eski veya tek-locator girdilerindeki `null`, yazan sürümün uzlaştırma gözlemlemediğini belirtir.
- **`ReviewStatus` (İnceleme Durumu):**
  - `accepted`: Yüksek güvenli sezgisel eşleşme ($\ge \%50$).
  - `accepted-with-llm`: Bir LLM sağlayıcısıyla (örn. Gemini, Claude, OpenAI, Ollama veya yapılandırılmış başka bir sağlayıcı) çözülen eşleşme.
  - `manual-review`: Sınırda kalan ve QA mühendisi onayı gerektiren eşleşme.
- **`PreviousSnapshot`, `AcceptedSnapshot` & `ProposedSnapshot`**: Önceki locator, kabul edilen yeni locator veya reddedilen/başarısız retry'daki önerilen aday.
- **`ProviderErrors`** (şema v7+): Sağlayıcı adları ve hata ayrıntıları. Diğer sağlayıcılar başarıyla uzlaşsa bile bu bilgi korunur.
- **`ProviderAttempts`** (şema v6+): Değerlendirilen her sağlayıcının deneme sayısı.
- **`ScoreBreakdown`**: Bileşen dökümü (`ControlType`, `Parent`, `Sibling`, `Name`, `Position`). Bir sinyal iki tarafta da yoksa ilgili bileşen `null` olur (eksik == eksik asla tam eşleşme sayılmaz).
- **`EvidenceCoverage`**: Boş olmayan kanıtla desteklenen toplam sinyal ağırlığının oranı (şema v2+). v1'den yükseltilen girdilerde `null` değeri "kanıt yok" değil "bilinmiyor" demektir.
- **`RunnerUpScore`** (şema v3+): Karar anındaki ikinci en iyi aday skoru (ikinci aday yoksa `null`) — margin kapısının girdisi, çevrimdışı denetim için saklanır.
- **`Candidates`** (şema v2+): Yalnızca kazanan değil, skorlanan **tüm** adaylar — `TotalScore`, `Components` ve `EvidenceCoverage` ile birlikte; eşiklerin çevrimdışı yeniden ayarlanabilmesi için.

`HealingReportDocument.Events` tüm denemeleri içerir. v7 öncesindeki yalnızca-kabul-edilen görünümüne ihtiyaç duyan tüketiciler elle filtre yazmadan `HealingReportDocument.AcceptedEvents` kullanabilir; bu görünüm `accepted`, `accepted-unverified` ve tüm eski girdileri kapsar.

Raporlar yakalanmış UI metni, automation ID, model reasoning'i ve sağlayıcı hata ayrıntıları
(parse başarısız olduğunda sınırlı bir ham yanıt dahil) içerebilir. JSON ve HTML'yi hassas
test artifact'ları olarak kabul edin; [LLM Healing Güvenlik Modeline](llm-security-model.md#yerel-telemetri-de-hassastır)
bakın.

> [!WARNING]
> **Rapor boyutu:** aday listesi bilinçli olarak **budanmamıştır**. Çok büyük UI ağaçlarında tek bir olay ~1 MB JSON ekleyebilir (3.001 düğümlü ağaçta ≈1,3 MB ölçüldü) ve her `Record()` çağrısı dosyanın tamamını yeniden yazar — maliyet olay sayısıyla karesel büyür. Dosya raporlarını (`SELF_HEALING_REPORT_PATH`) her yerel çalıştırmada değil, CI/teşhis çalıştırmalarında açın.
