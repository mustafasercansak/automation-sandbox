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

### 🤝 Consensus Acceptance

An LLM pick is accepted only when **at least two providers independently name the same candidate**. Self-reported confidence is recorded but never compared or thresholded: Claude's `0.72` and Gemini's `0.95` do not live on the same scale, so treating the larger number as the better answer just rewards whichever model is most optimistic.

What follows from that rule:

| Situation | Outcome |
| :--- | :--- |
| Two or more providers name the same candidate | Accepted; the voters are recorded in `HealResult.AgreedProviders` |
| Only one provider is configured | **Never accepted** — one provider cannot agree with itself, no matter how confident it claims to be |
| Every provider names a different candidate | Not accepted — this is the LLM layer reporting "we do not know" |
| Two candidates tie for the most votes | Not accepted — a tie is disagreement; breaking it by confidence would reinstate the comparison this rule removes |
| A provider fails, times out, or names a candidate that was not on its shortlist | That vote is discarded before counting; the remaining valid votes still stand on their own |

When a pick is not accepted the resolver returns the heuristic result unchanged, exactly as it does when no provider is configured at all.

```csharp
// Consensus needs at least two independent providers.
var result = await SelfHealingResolver.ResolveAsync(
    expected,
    currentTreeRoot,
    new ILlmHealingProvider[] { new ClaudeHealingProvider(), new GeminiHealingProvider() });

// result.AgreedProviders => ["Claude", "Gemini"] when they converged.
```

The quorum is tunable through `SimilarityWeights.MinimumConsensusVotes` (default `2`); values below 2 are rejected by `Validate()`.

> **Independence is the point.** Two `OpenAiHealingProvider` instances pointed at the same endpoint and model are the same model voting twice, not a consensus. Prefer providers backed by genuinely different models.

#### Naming providers

`HealResult.AgreedProviders` identifies voters by `ILlmHealingProvider.Name`, so names must be unique within a run. Because `OpenAiHealingProvider` speaks to any OpenAI-compatible endpoint, several instances of it can legitimately be configured at once — give each one a `name`:

```csharp
var providers = new ILlmHealingProvider[]
{
    new OpenAiHealingProvider(name: "Groq",     endpoint: "https://api.groq.com/openai/v1",  apiKey: groqKey),
    new OpenAiHealingProvider(name: "Cerebras", endpoint: "https://api.cerebras.ai/v1",      apiKey: cerebrasKey),
};
```

Without `name`, both would report `"OpenAI"` and their votes would be indistinguishable in the report.

### 🌐 Free Cloud AI via GitHub Models & Custom Endpoints
`OpenAiHealingProvider` supports custom OpenAI-compatible endpoints (such as GitHub Models, Azure OpenAI, vLLM, LM Studio).

In GitHub Actions, you can use **GitHub Models** (`https://models.github.ai/inference`) with the built-in `GITHUB_TOKEN` and `permissions: models: read`:

```csharp
// Example: Connect to GitHub Models using GITHUB_TOKEN
var provider = new OpenAiHealingProvider(
    endpoint: "https://models.github.ai/inference",
    model: "gpt-4o-mini");
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

### 🤝 Mutabakatla Kabul (Consensus)

Bir LLM seçimi, ancak **en az iki sağlayıcı birbirinden bağımsız olarak aynı adayı işaret ederse** kabul edilir. Modellerin kendi bildirdiği güven skoru kaydedilir ama asla karşılaştırılmaz veya bir eşiğe sokulmaz: Claude'un `0.72`'si ile Gemini'nin `0.95`'i aynı ölçekte değildir; büyük sayıyı doğru cevap saymak yalnızca en iyimser modeli ödüllendirir.

Bu kuralın sonuçları:

| Durum | Sonuç |
| :--- | :--- |
| İki veya daha fazla sağlayıcı aynı adayı seçer | Kabul edilir; oy verenler `HealResult.AgreedProviders` içine yazılır |
| Yalnızca tek sağlayıcı yapılandırılmış | **Asla kabul edilmez** — bir sağlayıcı kendisiyle mutabık olamaz, ne kadar emin olduğunu söylerse söylesin |
| Her sağlayıcı farklı bir aday seçer | Kabul edilmez — bu, LLM katmanının "bilmiyoruz" demesidir |
| İki aday en yüksek oyda berabere kalır | Kabul edilmez — beraberlik anlaşmazlıktır; güvene göre çözmek, kaldırılan karşılaştırmayı geri getirir |
| Bir sağlayıcı hata verir, zaman aşımına uğrar veya kısa listede olmayan bir aday söyler | O oy sayımdan önce elenir; kalan geçerli oylar kendi başına geçerliliğini korur |

Seçim kabul edilmediğinde çözümleyici, hiç sağlayıcı yapılandırılmamış gibi sezgisel (heuristic) sonucu değiştirmeden döndürür.

```csharp
// Mutabakat için en az iki bağımsız sağlayıcı gerekir.
var result = await SelfHealingResolver.ResolveAsync(
    expected,
    currentTreeRoot,
    new ILlmHealingProvider[] { new ClaudeHealingProvider(), new GeminiHealingProvider() });

// Anlaştıklarında result.AgreedProviders => ["Claude", "Gemini"]
```

Yeter sayı `SimilarityWeights.MinimumConsensusVotes` ile ayarlanabilir (varsayılan `2`); 2'nin altındaki değerler `Validate()` tarafından reddedilir.

> **Asıl mesele bağımsızlık.** Aynı uç noktaya ve aynı modele bakan iki `OpenAiHealingProvider` örneği, mutabakat değil aynı modelin iki kez oy vermesidir. Gerçekten farklı modellere dayanan sağlayıcıları tercih edin.

#### Sağlayıcılara isim verme

`HealResult.AgreedProviders` oy verenleri `ILlmHealingProvider.Name` ile tanımlar; bu yüzden isimler bir çalıştırma içinde benzersiz olmalıdır. `OpenAiHealingProvider` OpenAI uyumlu her uç noktayla konuştuğu için aynı anda birden çok örneği meşru biçimde yapılandırılabilir — her birine `name` verin:

```csharp
var providers = new ILlmHealingProvider[]
{
    new OpenAiHealingProvider(name: "Groq",     endpoint: "https://api.groq.com/openai/v1",  apiKey: groqKey),
    new OpenAiHealingProvider(name: "Cerebras", endpoint: "https://api.cerebras.ai/v1",      apiKey: cerebrasKey),
};
```

`name` verilmezse ikisi de `"OpenAI"` olarak raporlanır ve oyları raporda birbirinden ayırt edilemez.

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

### 🌐 GitHub Models ve Özel OpenAI Uç Noktaları
`OpenAiHealingProvider` özel OpenAI uyumlu uç noktaları (GitHub Models, Azure OpenAI, vLLM, LM Studio) destekler.

GitHub Actions içerisinde dahili `GITHUB_TOKEN` ve `permissions: models: read` izni ile **GitHub Models** (`https://models.github.ai/inference`) kullanılabilir:

```csharp
// Örnek: GITHUB_TOKEN ile GitHub Models'e bağlanma
var provider = new OpenAiHealingProvider(
    endpoint: "https://models.github.ai/inference",
    model: "gpt-4o-mini");
```


