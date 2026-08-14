# 🚀 Automation Sandbox Documentation / Dökümantasyon

Welcome to **Automation Sandbox**! / **Automation Sandbox** projesine hoş geldiniz!

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Documentation](#-english-overview)
> - [🇹🇷 Türkçe Dökümantasyon](#-türkçe-genel-bakış)

---

## 🇬🇧 English Overview

### 💡 What is Automation Sandbox? (Simple Explanation)
Imagine you write an automated test that clicks a button called `"Submit"`. One day, the developers rename that button to `"Complete Registration"`. Standard test tools (like Selenium or Appium) will crash because they can no longer find `"Submit"`.

**Automation Sandbox is like a smart GPS for your software tests:**
1. It remembers what the button looked like (size, location, parent window, role).
2. When the ID or name breaks, it calculates a **similarity score** ($0\% - 100\%$) across all elements on the screen.
3. If it is confident ($\ge 50\%$), it automatically picks the right element and heals your test **without any AI cost** (pure heuristic, ~$12\text{ms}$ for 3,000 controls on developer hardware).
4. If it is unsure, it asks an AI model (Gemini, Claude, OpenAI, or local Ollama) to pick the element safely.

---

### 📚 Documentation Index

| Guide | Description |
| :--- | :--- |
| 🚀 [**Getting Started**](getting-started.md) | Step-by-step installation, core concepts, and copy-paste code examples. |
| 💻 [**Desktop Automation**](desktop-automation.md) | How to test Windows desktop applications (WinForms, WPF, WinUI). |
| 🌐 [**Web Automation**](web-automation.md) | How to test Web applications with Playwright (Shadow DOM & iframe support). |
| 🎯 [**Intent-Aware Healing**](intent-aware-healing.md) | Using `TestIntent` to explain *why* a test step is being performed. |
| 📊 [**Healing Reports & Dashboard**](healing-reports.md) | JSON and HTML visual report artifacts for accepted healing events. |
| 🧭 [**Intent-Driven Automation**](intent-driven-automation.md) | M6: intent planning (deterministic + LLM-backed), DOM/desktop matching, locator recording, Playwright/FlaUI test generation, intent flow reports, pipeline orchestration, and live page exploration. |
| 🤖 [**LLM Providers**](llm-providers.md) | Setting up AI providers (Gemini, Claude, OpenAI, and 100% free local Ollama). |
| 📄 [**JSON Schema Reference**](json-schema.md) | Field-by-field breakdown of the `.locator.json` repository file. |
| 📦 [**NuGet Packaging**](nuget-packaging.md) | Creating preview `.nupkg` artifacts and preparing a publish checklist. |

---

## 🇹🇷 Türkçe Genel Bakış

### 💡 Automation Sandbox Nedir? (En Basit Anlatım)
Yazılım testinizde `"Kaydet"` adlı bir butona tıklayan otomatik bir test yazdığınızı hayal edin. Bir gün yazılımcılar bu butonun adını `"Değişiklikleri Onayla"` olarak değiştirdi. Standart test araçları (Selenium, Appium vb.) eski butonu bulamadığı için test **hata verir ve çöker**.

**Automation Sandbox, testleriniz için akıllı bir navigasyon (GPS) gibidir:**
1. Butonun eski halini (boyutunu, ekrandaki yerini, penceresini, türünü) hafızasına kaydeder.
2. Adı veya ID'si değiştiğinde, ekrandaki tüm elemanları inceleyerek bir **benzerlik skoru** ($\%0 - \%100$) hesaplar.
3. Eminse ($\ge \%50$), doğru butonu otomatik bulur ve testinizi **yapay zeka maliyeti olmadan** iyileştirir (heal eder) — saf sezgisel; geliştirici donanımında 3.000 kontrol için ~12 milisaniye.
4. Kararsız kalırsa, yapay zekaya (Gemini, Claude, OpenAI veya bilgisayarınızdaki yerel Ollama'ya) danışarak doğru elemanı güvenle seçer.

---

### 📚 Döküman Haritası

| Rehber | Açıklama |
| :--- | :--- |
| 🚀 [**Başlangıç Rehberi**](getting-started.md) | Adım adım kurulum, temel mantık ve kopyala-yapıştır kod örnekleri. |
| 💻 [**Masaüstü Testleri**](desktop-automation.md) | Windows masaüstü (WinForms, WPF) uygulamalarını test etme. |
| 🌐 [**Web Testleri**](web-automation.md) | Playwright ile web sitelerini test etme (Shadow DOM ve iframe dahil). |
| 🎯 [**Intent-Aware Healing**](intent-aware-healing.md) | `TestIntent` ile test adımının amacını yapay zekaya anlatma. |
| 📊 [**İyileştirme Raporları & Panel**](healing-reports.md) | İyileştirilen elemanlar için otomatik JSON ve HTML görsel raporları. |
| 🧭 [**Intent Tabanlı Otomasyon**](intent-driven-automation.md) | M6: intent planlama (deterministic + LLM destekli), DOM/masaüstü eşleştirme, locator kaydı, Playwright/FlaUI test üretimi, intent raporu, pipeline orkestrasyonu ve canlı sayfa keşfi. |
| 🤖 [**Yapay Zeka Kurulumu**](llm-providers.md) | Gemini, Claude, OpenAI ve 0 TL maliyetli yerel Ollama kurulumu. |
| 📄 [**JSON Şema Rehberi**](json-schema.md) | `.locator.json` kayıt dosyasının alan alan detaylı açıklaması. |
| 📦 [**NuGet Paketleme**](nuget-packaging.md) | Preview `.nupkg` artifact üretimi ve yayın kontrol listesi. |

---

## 🏛️ System Architecture Diagram / Mimari Şema

```mermaid
flowchart TB
    subgraph INPUT ["1. Live Target / Canlı Ekran"]
        A1["Windows App (WinForms / WPF)"]
        A2["Web Page (Playwright DOM)"]
    end

    subgraph ENGINE ["2. Heuristic Engine / Sezgisel Motor (Pure Heuristic)"]
        B1["SimilarityScorer (5 Mathematical Components)"]
        B2{"Score ≥ 50%?"}
        B3["✅ Auto-Heal (Free / 0 Cost)"]
        B4["⚡ Send Top-20 Candidates to AI"]
    end

    subgraph AI ["3. AI Fallback / Yapay Zeka Desteği"]
        C1["Google Gemini"]
        C2["Anthropic Claude"]
        C3["OpenAI GPT-4o-mini"]
        C4["Local Ollama (100% Free / Offline)"]
        C5{"Hallucination Guard Verification"}
    end

    subgraph REPO ["4. Locator Repository / Kalıcı Depo"]
        D1[".locator.json (Thread-Safe Persistence)"]
        D2["Healing History Audit Log"]
    end

    A1 & A2 --> B1
    B1 --> B2
    B2 -- Yes / Evet --> B3
    B2 -- Low Confidence / Kararsız --> B4
    B4 --> C1 & C2 & C3 & C4
    C1 & C2 & C3 & C4 --> C5
    C5 -- Valid Match --> D1 & D2
    B3 --> D1 & D2
```
