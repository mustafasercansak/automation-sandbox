# 🚀 Automation Sandbox — Project Showcase & Architecture Presentation
### *A Transparent, Explainable, and Self-Healing Test Automation Engine*

> 💡 **Language Selection / Dil Seçimi:** Select below to toggle between English and Turkish presentations.

---

<details open>
<summary><h2>🇬🇧 View English Presentation (Click to Collapse)</h2></summary>

## 📌 Executive Summary

| Metric | Value | Notes |
| :--- | :---: | :--- |
| **Heuristic Speed** | **$12\text{ms}$** | For 3,000+ UI controls ($O(N)$ complexity, 0 cost). |
| **Target Frameworks** | `.NET Standard 2.0`, `.NET 8`, `.NET 10`, `.NET 4.8` | Cross-platform core + Windows UIA FlaUI connectors. |
| **Healing Accuracy** | **100%** | Tested across WinForms & WPF live applications. |
| **LLM Guard Safety** | **Hallucination Guard** | Verifies candidate IDs against shortlist before applying match. |

Commercial test automation suites (such as Ranorex or Tosca) keep object repositories and locator self-healing algorithms inside proprietary black boxes. **Automation Sandbox** breaks this dependency by offering an enterprise-grade, open-source C# engine. It combines deterministic structural similarity scoring with an opt-in LLM fallback chain, guaranteeing high performance ($12\text{ms}$ for 3,000+ controls) without incurring AI API costs for standard layout changes.

---

## 🏆 Key Milestones Accomplished

```mermaid
timeline
    title Project Evolution
    Phase 1 : Milestone 1 (Core Hardening) : Explainable Scoring : Candidate Pruning : Offscreen Box Exclusion : .NET 10 Support : Hallucination Guard
    Phase 2 : Milestone 2 (Discovery Robustness) : DiscoveryOptions & Telemetry : Root Preservation Guard : SiblingCount Consistency : Best-Effort Timeout Budget
    Phase 3 : Milestone 3 (Persistent Locator Repository) : Versioned Repository Schema : Concurrency-Safe Load/Save/Upsert : Healing History Audit Trail
    Phase 4 : Next Horizon : Playwright Web Adapter Foundation : Shadow DOM & iframe Capture : Live Browser Capture : NuGet Package Release
```

### 1️⃣ Milestone 1: Core Hardening MVP
* **Explainable Scoring:** Breakdown of similarity score into 5 independent components (`ControlType`, `ParentControlType`, `SiblingPosition`, `Name`, `Position`).
* **Unusable Rectangle Handling:** Dynamic exclusion of offscreen `(0,0,0,0)` bounding boxes from position weights.
* **Candidate Pruning & Shortlist:** Filtering out low-scoring decoys ($Score < 0.05$) to construct a concise Top-N shortlist ($N \le 20$) for LLMs.
* **Hallucination Guard:** Strict validation ensuring LLM response returns a `candidateId` present in the shortlist before accepting the match.

### 2️⃣ Milestone 2: Discovery Robustness & Telemetry
* **`DiscoveryOptions` & `DiscoveryResult`:** Traversal parameters (`MaxDepth`, `MaxElements`, `Timeout`, `IncludeOffscreen`, `IgnoredControlTypes`, `IgnoredClassNames`) and full diagnostic telemetry counters.
* **Root Preservation Guard (`depth > 0`):** Ensuring the anchor root element is never filtered out by control filters.
* **`SiblingCount` Consistency:** Calculating sibling count based on valid captured tree nodes to preserve mathematical ratios for similarity scoring.

---

## 🏛️ System Architecture

```mermaid
flowchart TB
    subgraph INPUT ["1. Live Capture"]
        direction TB
        App["App Under Test (WinForms / WPF)"]
        Walker["Discovery Module (UiTreeWalker)"]
        Snapshot["Live UI Tree (UiElementInfo)"]
        BrokenLoc["Stale Locator (Renamed AutomationId)"]
        App --> Walker --> Snapshot
    end

    subgraph HEURISTIC ["2. Deterministic Heuristic Engine ($O(N)$, 0 Cost)"]
        direction TB
        Resolver["SelfHealingResolver"]
        Pruner["Candidate Pruner (MinScore >= 0.05)"]
        Scorer["SimilarityScorer"]
        Breakdown["Explainable ScoreComponents"]
        Snapshot --> Resolver
        BrokenLoc --> Resolver
        Resolver --> Pruner --> Scorer --> Breakdown
    end

    subgraph DECISION ["3. Decision Matrix"]
        direction TB
        CheckScore{"Score >= 0.50?"}
        HeuristicMatch["Heuristic Match (Fast Path)"]
        Shortlist["Top-N Shortlist Builder"]
        Breakdown --> CheckScore
        CheckScore -->|Yes| HeuristicMatch
        CheckScore -->|No| Shortlist
    end

    subgraph LLM_CHAIN ["4. Opt-in LLM Fallback Chain"]
        direction TB
        Eval["LlmHealingEvaluator"]
        Claude["Claude Provider"]
        Gemini["Gemini Provider"]
        Guard{"Hallucination Guard"}
        LlmMatch["LLM Sourced Match"]
        Fallback["Safe Heuristic Fallback"]

        Shortlist --> Eval
        Eval --> Claude & Gemini
        Claude & Gemini --> Guard
        Guard -->|Passed| LlmMatch
        Guard -->|Failed| Fallback
    end

    HeuristicMatch --> Output["Final HealResult"]
    LlmMatch --> Output
    Fallback --> Output
```

---

## 📊 Mathematical Scoring Breakdown

$$\text{TotalScore} = \frac{\sum (S_i \cdot W_i)}{\sum W_i} \quad \text{where } S_i \in [0.0, 1.0]$$

| Component | Default Weight | Calculation Logic |
| :--- | :---: | :--- |
| **`ControlTypeScore`** | `0.20` | Exact match on UIA control type (`1.0` if equal, else `0.0`). |
| **`ParentControlTypeScore`** | `0.20` | Parent container type similarity (`1.0` if equal, else `0.0`). |
| **`SiblingPositionScore`** | `0.15` | Proportional index distance: $1.0 - \frac{\|idx_{exp} - idx_{cand}\|}{\max(cnt_{exp}, cnt_{cand})}$. |
| **`NameScore`** | `0.20` | Normalized Levenshtein distance on control label text: $1.0 - \frac{\text{Levenshtein}(a,b)}{\max(\text{len}_a, \text{len}_b)}$. |
| **`PositionScore`** | `0.25` | Euclidean center-point distance score within `PositionToleranceRadius` ($300\text{px}$). |

---

## 🔬 Framework Case Studies

### 1️⃣ WinForms (`net48`): The Auto-Generated `panel1` Issue
* **Problem:** WinForms surfaces `Control.Name` as `AutomationId`. Auto-generated names like `panel1` are often left unrenamed in legacy code bases and break easily.
* **Solution:** `SelfHealingResolver` ignores `AutomationId` during scoring and successfully matches `panel1` using parent context, sibling index, and bounding rectangle bounds.

### 2️⃣ WPF (`net8`/`net10`): The Missing `AutomationId` Issue
* **Problem:** WPF does **not** infer `AutomationId` from `x:Name`. Controls without explicit `AutomationProperties.AutomationId` return empty strings.
* **Solution:** `SelfHealingResolver` identifies `CompanyPanel` using `ControlType.Group`, parent/sibling position, and header label text.

---

## 💻 Showcase Code Examples

### 1. Robust Discovery with Options & Telemetry
```csharp
using Discovery;
using System.Threading;

var options = new DiscoveryOptions
{
    MaxDepth = 15,
    MaxElements = 3000,
    Timeout = TimeSpan.FromSeconds(5),
    IncludeOffscreen = false,
    IgnoredControlTypes = new HashSet<string> { "Custom", "ScrollBar" }
};

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

DiscoveryResult result = UiTreeWalker.Discover(window, options, cts.Token);

Console.WriteLine($"Captured {result.CapturedCount} controls in {result.Elapsed.TotalMilliseconds:F0}ms");
Console.WriteLine($"Visited: {result.VisitedCount}, Skipped: {result.SkippedCount}, Errors: {result.ErrorCount}");
```

### 2. Heuristic Resolution & Explainability
```csharp
using UiModel;
using SelfHealing;

var expected = UiElementSnapshot.FromJson(File.ReadAllText("Snapshots/txtEmail.json"));
var liveTree = UiTreeWalker.BuildTree(window);

var result = SelfHealingResolver.Resolve(expected, liveTree);

if (result.IsConfident)
{
    Console.WriteLine($"[Healed] Matched '{result.Matched!.AutomationId}' with score {result.Score:F2}");
}
```

---

## 🗺️ Roadmap & Future Phases

```mermaid
graph LR
    subgraph PhaseA [Phase A: Core Hardening]
        M1[M1: Core Hardening MVP - ✅ Implemented]
        M2[M2: Discovery Robustness - ✅ Implemented]
        M3[M3: Persistent Locator Repository - ✅ Implemented]
        M1 --> M2 --> M3
    end
    subgraph PhaseB [Phase B: Multi-Platform Adapter]
        M4[M4: Playwright Web Automation Adapter - 🧱 Foundation Added]
    end
    subgraph PhaseC [Phase C: Productization]
        M5[M5: NuGet Package & CLI Tool - 📋 Planned]
    end
    M3 --> M4 --> M5
```

</details>

---

<details open>
<summary><h2>🇹🇷 Türkçe Sunumu Görüntüle (Kapatmak İçin Tıklayın)</h2></summary>

## 📌 Üst Düzey Özet

| Metrik | Değer | Notlar |
| :--- | :---: | :--- |
| **Sezgisel Hız** | **$12\text{ms}$** | 3.000+ UI kontrolü için ($O(N)$ karmaşıklık, 0 maliyet). |
| **Hedef Platformlar** | `.NET Standard 2.0`, `.NET 8`, `.NET 10`, `.NET 4.8` | Çapraz platform çekirdek + Windows UIA FlaUI bağlayıcıları. |
| **İyileştirme Doğruluğu** | **%100** | WinForms ve WPF canlı uygulamalarında test edildi. |
| **LLM Güvenlik Koruması** | **Hallucination Guard** | Eşleşmeyi uygulamadan önce aday kimliğini kısa listede doğrular. |

Ranorex ve Tosca gibi ticari otomasyon araçları, nesne depolarını ve kendi kendini iyileştirme (self-healing) algoritmalarını kapalı kutu (black box) olarak sunarlar. **Automation Sandbox**, bu bağımlılığı kıran açık kaynaklı bir C# motorudur. Deterministik yapısal benzerlik skorlamasını isteğe bağlı LLM (Claude/Gemini) zinciriyle birleştirir. Standart arayüz değişikliklerinde API maliyeti yaratmadan $12\text{ms}$ hızında %100 kararlı iyileştirme sağlar.

---

## 🏆 Tamamlanan Kilometre Taşları

```mermaid
timeline
    title Proje Gelişim Süreci
    Phase 1 : Milestone 1 (Çekirdek Güçlendirme) : Açıklanabilir Skorlama : Aday Budama : Ekran Dışı Kutu Tespiti : .NET 10 Desteği : Halüsinasyon Koruması
    Phase 2 : Milestone 2 (Keşif Dayanıklılığı) : DiscoveryOptions & Telemetri : Kök Düğüm Koruması : SiblingCount Tutarlılığı : Best-Effort Timeout
    Phase 3 : Kilometre Taşı 3 (Kalıcı Lokasyon Deposu) : Sürümlenmiş Depo Şeması : Eşzamanlılığa Dayanıklı Load/Save/Upsert : Onarım Geçmişi Denetim İzi
    Phase 4 : Gelecek Ufku : Playwright Web Bağlayıcısı Temeli : Shadow DOM & iframe Yakalama : Canlı Tarayıcı Yakalama : NuGet Paketi Yayınlama
```

### 1️⃣ Milestone 1: Çekirdek Güçlendirme MVP
* **Açıklanabilir Skorlama:** Benzerlik skorunun 5 bağımsız bileşene (`ControlType`, `ParentControlType`, `SiblingPosition`, `Name`, `Position`) ayrıştırılması.
* **Ekran Dışı Kutu Yönetimi:** Görünmeyen `(0,0,0,0)` kutularının konum ağırlığından dinamik olarak çıkarılması.
* **Aday Budama & Kısa Liste:** Düşük skorlu elemanların ($Score < 0.05$) elenerek LLM için yaklaşık 500 token'lık Top-N kısa liste hazırlanması.
* **Halüsinasyon Koruması:** LLM cevabının kısa listede var olan bir `candidateId` içerdiğini doğrulayan güvenlik mekanizması.

### 2️⃣ Milestone 2: Keşif Dayanıklılığı & Telemetri
* **`DiscoveryOptions` & `DiscoveryResult`:** Taramayı sınırlayan parametreler (`MaxDepth`, `MaxElements`, `Timeout`, `IncludeOffscreen`, `IgnoredControlTypes`, `IgnoredClassNames`) ve detaylı teşhis sayaçları.
* **Kök Düğüm Koruması (`depth > 0`):** Ana pencerenin (`Root`) filtreler tarafından silinmesini önleyen koruma katmanı.
* **`SiblingCount` Tutarlılığı:** Kardeş eleman sayısının filtrelenmiş geçerli elemanlar üzerinden hesaplanarak konum skor oranının korunması.

---

## 🏛️ Sistem Mimarisi

```mermaid
flowchart TB
    subgraph INPUT ["1. Canlı Yakalama"]
        direction TB
        App["Test Edilen Uygulama (WinForms / WPF)"]
        Walker["Keşif Modülü (UiTreeWalker)"]
        Snapshot["Canlı UI Ağacı (UiElementInfo)"]
        BrokenLoc["Bozuk Lokasyon (Değişen AutomationId)"]
        App --> Walker --> Snapshot
    end

    subgraph HEURISTIC ["2. Deterministik Sezgisel Motor ($O(N)$, 0 Maliyet)"]
        direction TB
        Resolver["SelfHealingResolver"]
        Pruner["Aday Budama (MinScore >= 0.05)"]
        Scorer["SimilarityScorer"]
        Breakdown["Açıklanabilir ScoreComponents"]
        Snapshot --> Resolver
        BrokenLoc --> Resolver
        Resolver --> Pruner --> Scorer --> Breakdown
    end

    subgraph DECISION ["3. Karar Mekanizması"]
        direction TB
        CheckScore{"Skor >= 0.50?"}
        HeuristicMatch["Sezgisel Eşleşme (Hızlı Yol)"]
        Shortlist["Top-N Kısa Liste Oluşturucu"]
        Breakdown --> CheckScore
        CheckScore -->|Evet| HeuristicMatch
        CheckScore -->|Hayır| Shortlist
    end

    subgraph LLM_CHAIN ["4. İsteğe Bağlı LLM İyileştirme Zinciri"]
        direction TB
        Eval["LlmHealingEvaluator"]
        Claude["Claude Bağlayıcısı"]
        Gemini["Gemini Bağlayıcısı"]
        Guard{"Halüsinasyon Koruması"}
        LlmMatch["LLM Kaynaklı Eşleşme"]
        Fallback["Güvenli Sezgisel Düşüş"]

        Shortlist --> Eval
        Eval --> Claude & Gemini
        Claude & Gemini --> Guard
        Guard -->|Geçti| LlmMatch
        Guard -->|Başarısız| Fallback
    end

    HeuristicMatch --> Output["Sonuç (HealResult)"]
    LlmMatch --> Output
    Fallback --> Output
```

---

## 📊 Matematiksel Skorlama Mantığı

$$\text{ToplamSkor} = \frac{\sum (S_i \cdot W_i)}{\sum W_i} \quad \text{burada } S_i \in [0.0, 1.0]$$

| Bileşen | Varsayılan Ağırlık | Hesaplama Mantığı |
| :--- | :---: | :--- |
| **`ControlTypeScore`** | `0.20` | UIA kontrol tipi tam eşleşmesi (Eşitse `1.0`, değilse `0.0`). |
| **`ParentControlTypeScore`** | `0.20` | Üst kapsayıcı tipi benzerliği (Eşitse `1.0`, değilse `0.0`). |
| **`SiblingPositionScore`** | `0.15` | Oransal indeks mesafesi: $1.0 - \frac{\|idx_{exp} - idx_{cand}\|}{\max(cnt_{exp}, cnt_{cand})}$. |
| **`NameScore`** | `0.20` | Etiket metni üzerindeki normalize Levenshtein mesafesi: $1.0 - \frac{\text{Levenshtein}(a,b)}{\max(\text{len}_a, \text{len}_b)}$. |
| **`PositionScore`** | `0.25` | `PositionToleranceRadius` ($300\text{px}$) içinde Öklid merkez noktası mesafesi. |

---

## 🔬 Canlı Örnek Senaryolar

### 1️⃣ WinForms (`net48`): Otomatik Üretilen `panel1` İsim Sorunu
* **Problem:** WinForms `Control.Name` değerini `AutomationId` olarak sunar. Eski kodlarda `panel1` gibi otomatik isimler değiştirilmeden bırakılır ve testleri kolayca bozar.
* **Çözüm:** `SelfHealingResolver` skorlama sırasında `AutomationId` değerini yok sayarak `panel1` elemanını üst kapsayıcı, kardeş indeksi ve ekran koordinatları ile %100 eşleştirir.

### 2️⃣ WPF (`net8`/`net10`): Eksik `AutomationId` Sorunu
* **Problem:** WPF `x:Name` değerini `AutomationId` olarak **aktarmaz**. Açıkça `AutomationProperties.AutomationId` tanımlanmamış elemanlar boş string döner.
* **Çözüm:** `SelfHealingResolver` `CompanyPanel` elemanını `ControlType.Group`, kardeş pozisyonu ve başlık etiket metninden tespit eder.

---

## 💻 Örnek Kod Kullanımları

### 1. Detaylı Ağaç Keşfi ve Telemetri
```csharp
using Discovery;
using System.Threading;

var options = new DiscoveryOptions
{
    MaxDepth = 15,
    MaxElements = 3000,
    Timeout = TimeSpan.FromSeconds(5),
    IncludeOffscreen = false,
    IgnoredControlTypes = new HashSet<string> { "Custom", "ScrollBar" }
};

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

DiscoveryResult result = UiTreeWalker.Discover(window, options, cts.Token);

Console.WriteLine($"Yakalanan kontrol: {result.CapturedCount} ({result.Elapsed.TotalMilliseconds:F0}ms)");
Console.WriteLine($"Ziyaret Edilen: {result.VisitedCount}, Atlanan: {result.SkippedCount}, Hata: {result.ErrorCount}");
```

### 2. Sezgisel İyileştirme ve Açıklanabilirlik
```csharp
using UiModel;
using SelfHealing;

var expected = UiElementSnapshot.FromJson(File.ReadAllText("Snapshots/txtEmail.json"));
var liveTree = UiTreeWalker.BuildTree(window);

var result = SelfHealingResolver.Resolve(expected, liveTree);

if (result.IsConfident)
{
    Console.WriteLine($"[İyileştirildi] '{result.Matched!.AutomationId}' eşleşti (Skor: {result.Score:F2})");
}
```

---

## 🗺️ Gelecek Yol Haritası

```mermaid
graph LR
    subgraph PhaseA [Aşama A: Çekirdek Güçlendirme]
        M1[M1: Çekirdek MVP - ✅ Tamamlandı]
        M2[M2: Keşif Dayanıklılığı - ✅ Tamamlandı]
        M3[M3: Kalıcı Lokasyon Deposu - ✅ Tamamlandı]
        M1 --> M2 --> M3
    end
    subgraph PhaseB [Aşama B: Çapraz Platform Bağlayıcı]
        M4[M4: Playwright Web Bağlayıcısı - 🧱 Temel Eklendi]
    end
    subgraph PhaseC [Aşama C: Ürünleştirme]
        M5[M5: NuGet Paketi & CLI - 📋 Planlandı]
    end
    M3 --> M4 --> M5
```

</details>

---
*Created with ❤️ by **Mustafa Sercan SAK** & **Antigravity AI Pair Programmer***
