# 🤖 LLM Providers Guide / Yapay Zeka Sağlayıcıları Rehberi

Automation Sandbox includes 4 built-in AI providers for low-confidence healing fallback ($Score < 50\%$).

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Guide](#-english-guide)
> - [🇹🇷 Türkçe Kılavuz](#-türkçe-kılavuz)

---

## 🇬🇧 English Guide

### 📊 Provider Comparison Matrix

| Provider | Default Model | Setup Method | Cost | Privacy |
| :--- | :--- | :--- | :---: | :---: |
| **Gemini** | `gemini-3.6-flash` | `GEMINI_API_KEY` | Very Low | Cloud |
| **Claude** | `claude-haiku-4-5-20251001` | `ANTHROPIC_API_KEY` | Very Low | Cloud |
| **OpenAI** | `gpt-4o-mini` | `OPENAI_API_KEY` | Very Low | Cloud |
| **Ollama** | `llama3.2` | `OLLAMA_HOST` / `OLLAMA_MODEL` | **100% Free ($0)** | **100% Local (Offline)** |

### Setting Up Free Offline AI (Ollama)
1. Download & install [Ollama](https://ollama.com).
2. Open terminal and pull the lightweight Llama model:
   ```bash
   ollama run llama3.2
   ```
3. Pass `OllamaHealingProvider` in C# code:
   ```csharp
   var provider = new OllamaHealingProvider(host: "http://localhost:11434");
   ```

### ⏱️ Timeouts & Fallback Behavior

All LLM providers support configurable timeouts via their constructors (`TimeSpan? timeout = null`). When a timeout expires, the provider fails fast without throwing unhandled exceptions, enabling `SelfHealingResolver` to safely fall back to the heuristic match or other providers.

| Provider | Default Timeout | Rationale |
| :--- | :---: | :--- |
| **Claude / Gemini / OpenAI** | `15s` | Cloud APIs process small structured pick prompts (~500 tokens) in 1–4s; 15s is ample buffer while preventing test runner stalls. |
| **Ollama** | `30s` | Local models running on CPU/GPU may encounter cold-start model load latency. |

```csharp
// Example: Custom 5-second timeout for fast cloud resolution
var provider = new ClaudeHealingProvider(timeout: TimeSpan.FromSeconds(5));
```

---

## 🇹🇷 Türkçe Kılavuz

### 📊 Yapay Zeka Karşılaştırma Tablosu

| Sağlayıcı | Varsayılan Model | Kurulum | Maliyet | Gizlilik |
| :--- | :--- | :--- | :---: | :---: |
| **Gemini** | `gemini-3.6-flash` | `GEMINI_API_KEY` | Çok Düşük | Bulut |
| **Claude** | `claude-haiku-4-5-20251001` | `ANTHROPIC_API_KEY` | Çok Düşük | Bulut |
| **OpenAI** | `gpt-4o-mini` | `OPENAI_API_KEY` | Çok Düşük | Bulut |
| **Ollama** | `llama3.2` | `OLLAMA_HOST` / `OLLAMA_MODEL` | **100% Ücretsiz ($0)** | **100% Yerel (Offline)** |

### ⏱️ Zaman Aşımı (Timeout) ve Fallback Davranışı

Tüm LLM sağlayıcıları kurucuları üzerinden yapılandırılabilir zaman aşımı desteği sunar (`TimeSpan? timeout = null`). Bir sağlayıcı zaman aşımına uğradığında test yürütmesini kilitlemeden hızlıca başarısızlık döner (`Success = false`) ve `SelfHealingResolver`'ın güvenle sezgisel (heuristic) sonuca düşmesini sağlar.

| Sağlayıcı | Varsayılan Timeout | Gerekçe |
| :--- | :---: | :--- |
| **Claude / Gemini / OpenAI** | `15s` | Bulut API'leri ~500 token'lık küçük pick prompt'larını 1-4 saniyede tamamlar; 15s test kilitlenmelerini önlemek için idealdir. |
| **Ollama** | `30s` | Yerel CPU/GPU üzerinde çalışan modeller soğuk başlangıç (cold start) model yükleme gecikmesi yaşayabilir. |

```csharp
// Örnek: Hızlı bulut çözümlemesi için 5 saniyelik özel zaman aşımı
var provider = new ClaudeHealingProvider(timeout: TimeSpan.FromSeconds(5));
```

### 0 TL Maliyetli Çevrimdışı Yapay Zeka (Ollama) Kurulumu
1. [Ollama Resmi Sitesinden](https://ollama.com) Ollama'yı indirip kurun.
2. Terminal açıp hafif Llama modelini indirin:
   ```bash
   ollama run llama3.2
   ```
3. C# kodunuzda `OllamaHealingProvider` nesnesini verin:
   ```csharp
   var provider = new OllamaHealingProvider(host: "http://localhost:11434");
   ```

