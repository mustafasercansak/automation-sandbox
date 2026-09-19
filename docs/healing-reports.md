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

`SelfHealingEngine` emits an append-only **JSON Lines** report (`healing-report.json` by convention, though `.jsonl` describes it more honestly) and an **HTML Dashboard** (`healing-report.html`) automatically. Schema v8 records accepted heals and declined or failed attempts, including opt-in batch ownership conflicts, so the report no longer implies a 100% success rate by construction.

For `ExecuteWithHealingAsync`, an accepted event is written only after the action retry with the proposed element succeeds. If that retry fails, the proposal is not reported as accepted and the locator repository remains unchanged.

> [!IMPORTANT]
> **Format change (#424):** the JSON report is now **JSON Lines** - one `HealingReportEntry` object per line - instead of a single JSON array document. `HealingReportFileSink.Record()` appends exactly one line per call without reading or re-serializing prior history, so recording N events costs O(N) total instead of the old O(N²). Use `HealingReportFileSink.LoadReport()` to read the file back as a `HealingReportDocument`; parsing the whole file with a single `JsonSerializer.Deserialize<HealingReportDocument>()` call no longer works, because the file is not one JSON value. A pre-existing `healing-report.json` written by a version of this library before #424 is in the old single-array format and is **not** auto-migrated - archive or delete it before upgrading, since the sink starts a fresh JSON Lines file at the same path.

`HealingReportFileSink.Record()` appends the new entry with a real filesystem append -
never touching bytes already written - guarded by the same cross-process lock as before. A
crash or thrown exception mid-write can at most leave a truncated trailing line; every entry
committed before it stays intact and readable. When an HTML path is configured, the
dashboard is still re-rendered from the full history after each append and committed with
the same adjacent-temp-file-plus-atomic-replace strategy as before - it is derived output and
can always be rebuilt from the JSON Lines file via `LoadReport()`.

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
- **`Outcome`** (schema v7+): The resolution result: `accepted`, `accepted-unverified`, `retry-failed`, `observed`, `manual-review`, `fail-closed`, `ambiguous`, `ownership-conflict`, `low-evidence`, `low-confidence`, `no-candidates`, `no-consensus`, `provider-error`, or `unspecified`. `observed` and `manual-review` represent non-mutating evaluations in `Observe` and `Review` healing modes. `fail-closed` represents execution in `FailClosed` mode where discovery was skipped. `ownership-conflict` means an independently accepted claim lost or tied a one-to-one ownership decision — either against a `ResolveBatch` peer, or (when `SelfHealingEngine.ReconcileAgainstRepository` is enabled) against another authored locator in the repository that already resolves onto the same candidate. `unspecified` exposes a missing decision classification without miscounting it as measured low confidence. `null` on upgraded legacy entries means the older build did not record an outcome; pre-v7 reports contained accepted heals only.
- **`Platform`** (schema v7+): The caller-provided platform identifier, such as `web-playwright` or `windows-uia`.
- **`CandidateIdentity` & `ReconciliationDisposition`** (schema v8+): Nullable ownership telemetry, written by `ResolveBatch` and by engine repository reconciliation. The identity is an opaque path within one captured tree, not a reusable locator; `null` means reconciliation was not run for that entry.
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

### 📈 Aggregate Summary for CI Gates

`HealingReportSummary.Summarize(document)` computes the numbers a CI gate or dashboard usually wants — accepted/declined counts, an `AcceptanceRate`, counts grouped by `Outcome`, how many accepted heals were LLM-assisted or diverged from the heuristic winner, and which `LocatorKey`s hit a provider error — in a single pass, without parsing the HTML report or hand-writing LINQ over `Events`:

```csharp
var document = HealingReportFileSink.LoadReport(reportPath);
var summary = HealingReportSummary.Summarize(document);

Console.WriteLine($"{summary.AcceptedCount}/{summary.TotalEvents} accepted ({summary.AcceptanceRate:P1})");
if (summary.LocatorsWithProviderErrors.Count > 0)
{
    Console.WriteLine($"Provider errors on: {string.Join(", ", summary.LocatorsWithProviderErrors)}");
}
```

Reports can contain captured UI text, automation IDs, model reasoning, and provider error
details (including a bounded raw response when parsing fails). Treat both JSON and HTML as
sensitive test artifacts; see the [LLM Healing Security Model](llm-security-model.md#local-telemetry-is-sensitive-too).

> [!WARNING]
> **Report size:** the candidate list is intentionally **unpruned**. On very large UI trees a single event can add ~1 MB of JSON (≈1.3 MB measured on a 3,001-node tree). As of #424, `Record()` itself is O(1) per call and no longer rewrites the file; but when an HTML path is configured the dashboard is still re-rendered from the full history on every call, so its cost still grows with event count. Enable file reports (`SELF_HEALING_REPORT_PATH`) on CI/diagnostic runs, not on every local run, and consider `HealingReportFileSink(path, htmlFilePath: null)` for a pure audit trail without the per-call HTML cost.

---

## 🇹🇷 Türkçe Kılavuz

### 💡 Genel Bakış
CI/CD süreçlerinde (GitHub Actions, Azure DevOps vb.) testleriniz çalışırken **hangi elemanların iyileştirildiği**, **neye dönüştüğü** ve **yapay zekanın devreye girip girmediği** raporlanmalıdır.

`SelfHealingEngine` motoru çözüm denemelerini otomatik olarak eklemeli (append-only) bir
**JSON Lines** raporu (`healing-report.json`, dosya adı geleneksel olarak böyle kalsa da
artık `.jsonl` daha doğru bir betimleme olurdu) ve bir **HTML Görsel Gösterge Paneli**
(`healing-report.html`) olarak kaydeder. Şema v8, isteğe bağlı batch sahiplik çakışmaları
dahil kabul edilen iyileştirmeleri ve reddedilen veya başarısız denemeleri kaydeder; böylece
rapor yapısı gereği %100 başarı izlenimi vermez.

`ExecuteWithHealingAsync` kullanıldığında kabul edilmiş bir olay, yalnızca önerilen elemanla yapılan eylem tekrarı başarılı olduktan sonra yazılır. Bu tekrar başarısız olursa öneri kabul edilmiş olarak raporlanmaz ve locator repository değişmeden kalır.

> [!IMPORTANT]
> **Format değişikliği (#424):** JSON raporu artık tek bir JSON dizisi yerine **JSON Lines** biçimindedir - her satırda bir `HealingReportEntry` nesnesi. `HealingReportFileSink.Record()`, önceki geçmişi okumadan veya yeniden serileştirmeden yalnızca tek bir satır ekler; böylece N olay kaydetmenin toplam maliyeti eski O(N²) yerine O(N) olur. Dosyayı bir `HealingReportDocument` olarak geri okumak için `HealingReportFileSink.LoadReport()` kullanın - tek bir `JsonSerializer.Deserialize<HealingReportDocument>()` çağrısıyla tüm dosyayı ayrıştırmak artık çalışmaz, çünkü dosya tek bir JSON değeri değildir. #424 öncesi bir sürümün yazdığı mevcut bir `healing-report.json` eski tek-dizi biçimindedir ve **otomatik olarak taşınmaz** - yükseltmeden önce arşivleyin veya silin; sink aynı yolda sıfırdan yeni bir JSON Lines dosyasına başlar.

`HealingReportFileSink.Record()`, önceden yazılmış baytlara hiç dokunmadan gerçek bir dosya
sistemi ekleme (append) işlemiyle yeni satırı ekler; bu işlem öncekiyle aynı süreçler-arası
kilitle korunur. Yazma sırasında bir çökme veya fırlatılan exception, olsa olsa son satırın
yarım kalmasına yol açar; ondan önce commit edilmiş her olay bozulmadan okunabilir kalır. Bir
HTML yolu yapılandırıldığında, gösterge paneli her ekleme sonrası yine tüm geçmişten yeniden
üretilir ve öncekiyle aynı bitişik-geçici-dosya-artı-atomik-değiştirme stratejisiyle commit
edilir - bu türetilmiş bir çıktıdır ve `LoadReport()` ile JSON Lines dosyasından her zaman
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
- **`Outcome`** (şema v7+): Çözüm sonucu: `accepted`, `accepted-unverified`, `retry-failed`, `observed`, `manual-review`, `fail-closed`, `ambiguous`, `ownership-conflict`, `low-evidence`, `low-confidence`, `no-candidates`, `no-consensus`, `provider-error` veya `unspecified`. `observed` ve `manual-review`, `Observe` ve `Review` modlarındaki değişiklik yapmayan değerlendirmeleri belirtir. `fail-closed`, keşif adımının çalıştırılmadığı `FailClosed` modunu temsil eder. `ownership-conflict`, bağımsız kabul edilen bir talebin bire bir sahiplik kararını kaybettiğini veya berabere kaldığını gösterir — ya bir `ResolveBatch` eşine karşı, ya da (`SelfHealingEngine.ReconcileAgainstRepository` etkinken) depoda aynı adaya çözülen başka bir özgün locator'a karşı. `unspecified`, eksik karar sınıflandırmasını ölçülmüş `low-confidence` verisi gibi saymadan görünür kılar. Eski raporlardan yükseltilen girdilerde `null`, önceki build'in sonucu kaydetmediği anlamına gelir; v7 öncesi raporlar yalnızca kabul edilen iyileştirmeleri içeriyordu.
- **`Platform`** (şema v7+): Çağıranın verdiği `web-playwright` veya `windows-uia` gibi platform kimliği.
- **`CandidateIdentity` & `ReconciliationDisposition`** (şema v8+): Nullable sahiplik telemetrisi; `ResolveBatch` ve motor depo uzlaştırması tarafından yazılır. Kimlik tek yakalanmış ağaç içindeki opak yoldur, yeniden kullanılabilir locator değildir; `null`, o girdi için uzlaştırma çalıştırılmadığı anlamına gelir.
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

### 📈 CI Kapıları İçin Toplu Özet

`HealingReportSummary.Summarize(document)`, bir CI kapısının veya panelin genelde ihtiyaç duyduğu sayıları — kabul/red sayıları, `AcceptanceRate`, `Outcome`'a göre gruplanmış sayımlar, kabul edilen iyileştirmelerin kaçının LLM destekli olduğu veya sezgisel kazanandan saptığı, hangi `LocatorKey`'lerin sağlayıcı hatasına takıldığı — HTML raporunu ayrıştırmadan veya `Events` üzerinde elle LINQ yazmadan, tek geçişte hesaplar:

```csharp
var document = HealingReportFileSink.LoadReport(reportPath);
var summary = HealingReportSummary.Summarize(document);

Console.WriteLine($"{summary.AcceptedCount}/{summary.TotalEvents} kabul edildi ({summary.AcceptanceRate:P1})");
if (summary.LocatorsWithProviderErrors.Count > 0)
{
    Console.WriteLine($"Sağlayıcı hatası olanlar: {string.Join(", ", summary.LocatorsWithProviderErrors)}");
}
```

Raporlar yakalanmış UI metni, automation ID, model reasoning'i ve sağlayıcı hata ayrıntıları
(parse başarısız olduğunda sınırlı bir ham yanıt dahil) içerebilir. JSON ve HTML'yi hassas
test artifact'ları olarak kabul edin; [LLM Healing Güvenlik Modeline](llm-security-model.md#yerel-telemetri-de-hassastır)
bakın.

> [!WARNING]
> **Rapor boyutu:** aday listesi bilinçli olarak **budanmamıştır**. Çok büyük UI ağaçlarında tek bir olay ~1 MB JSON ekleyebilir (3.001 düğümlü ağaçta ≈1,3 MB ölçüldü). #424 itibarıyla `Record()`'un kendisi çağrı başına O(1)'dir ve artık dosyanın tamamını yeniden yazmaz; ancak bir HTML yolu yapılandırıldığında gösterge paneli her çağrıda yine tüm geçmişten yeniden üretilir, dolayısıyla onun maliyeti olay sayısıyla büyümeye devam eder. Dosya raporlarını (`SELF_HEALING_REPORT_PATH`) her yerel çalıştırmada değil, CI/teşhis çalıştırmalarında açın; yalnızca eklemeli denetim izi isteyip HTML'in çağrı başına maliyetinden kaçınmak için `HealingReportFileSink(path, htmlFilePath: null)` kullanmayı düşünün.
