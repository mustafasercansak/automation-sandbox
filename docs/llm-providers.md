# 🤖 LLM Providers Guide / Yapay Zeka Sağlayıcıları Rehberi

Automation Sandbox includes built-in AI providers and an environment-driven factory for low-confidence healing fallback ($Score < 50\%$).

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
| **Grok (xAI)** | `grok-2-latest` | `GROK_API_KEY` / `XAI_API_KEY` | Low | Cloud |
| **Kimi (Moonshot)** | `moonshot-v1-8k` | `KIMI_API_KEY` / `MOONSHOT_API_KEY` | Low | Cloud |
| **Cloudflare Workers AI** | Explicit (`CLOUDFLARE_MODEL`) | Token + account ID + model | Free daily allocation | Cloud |
| **Ollama** | `llama3.2` | `OLLAMA_HOST` / `OLLAMA_MODEL` | **100% Free (\$0)** | **100% Local (Offline)** |

### 🏭 Dynamic Environment Provider Factory (`LlmProviderFactory`)

`LlmProviderFactory.CreateConfiguredProviders()` automatically discovers and instantiates available providers based on active environment variables without needing code changes:

- **Well-known auto-discovery**:
  - `ANTHROPIC_API_KEY` (+ optional `ANTHROPIC_MODEL`) $\rightarrow$ `ClaudeHealingProvider`
  - `GEMINI_API_KEY` (+ optional `GEMINI_MODEL`) $\rightarrow$ `GeminiHealingProvider`
  - `OPENAI_API_KEY` (+ optional `OPENAI_MODEL`, `OPENAI_ENDPOINT`) $\rightarrow$ `OpenAiHealingProvider`
  - `GROK_API_KEY` (+ optional `GROK_MODEL`, `GROK_ENDPOINT`) $\rightarrow$ `OpenAiHealingProvider` (named `"Grok"`)
  - `KIMI_API_KEY` (+ optional `KIMI_MODEL`, `KIMI_ENDPOINT`) $\rightarrow$ `OpenAiHealingProvider` (named `"Kimi"`)
  - `CLOUDFLARE_API_TOKEN` + `CLOUDFLARE_ACCOUNT_ID` + `CLOUDFLARE_MODEL` $\rightarrow$ `OpenAiHealingProvider` (named `"Cloudflare"`)
  - `OLLAMA_ENABLED=true` or `OLLAMA_HOST` $\rightarrow$ `OllamaHealingProvider`

- **Arbitrary Custom Endpoints (`LLM_CUSTOM_PROVIDERS`)**:
  Provide a JSON array string to configure additional OpenAI-compatible endpoints:
  ```json
  [
    {
      "name": "DeepSeek",
      "endpoint": "https://api.deepseek.com/v1",
      "model": "deepseek-chat",
      "apiKeyEnvVar": "DEEPSEEK_API_KEY",
      "timeoutSeconds": 20
    }
  ]
  ```

```csharp
// Discover all available providers dynamically:
var providers = LlmProviderFactory.CreateConfiguredProviders();

var result = await SelfHealingResolver.ResolveAsync(expected, treeRoot, llmProviders: providers);
```

### ⏱️ Timeouts & Resilience Patterns

All LLM providers derive from `HttpLlmHealingProvider` and support configurable per-attempt timeouts, overall total operation timeouts, and automatic retry with exponential backoff:

| Setting | Default (Cloud) | Default (Ollama) | Description |
| :--- | :---: | :---: | :--- |
| **`Timeout`** | `15s` | `30s` | Per-attempt HTTP timeout. Prevents any single hanging request from blocking the pipeline. |
| **`TotalTimeout`** | `35s` | `70s` | Overall operation ceiling across all retries + backoffs. |
| **`MaxRetries`** | `2` | `2` | Number of retry attempts on transient errors (total up to 3 attempts). |

#### Retry Rules:
- **Transient Errors Retried**: HTTP 429 (Rate Limited), HTTP 500, 502, 503, 504, and `HttpRequestException` (transient network drops) are automatically retried with exponential backoff and jitter.
- **Fail-Fast on Permanent Errors**: HTTP 400, 401, 403, and 404 fail immediately without wasting retry attempts.
- **`Retry-After` Header & Quota Guard**: If a response includes a `Retry-After` header $\le 10\text{s}$, the transport pauses for the requested delay. If `Retry-After` $> 10\text{s}$ (e.g. daily quota exhaustion), the provider fails fast immediately.

### 🤝 Consensus Acceptance

An LLM pick is accepted only when **at least two providers independently name the same candidate**. Self-reported confidence is recorded but never compared or thresholded: Claude's `0.72` and Gemini's `0.95` do not live on the same scale.

| Situation | Outcome |
| :--- | :--- |
| Two or more providers name the same candidate | Accepted; the voters are recorded in `HealResult.AgreedProviders` |
| Only one provider is configured | **Never accepted** — one provider cannot agree with itself |
| Every provider names a different candidate | Not accepted — reported as split vote / disagreement |
| Two candidates tie for the most votes | Not accepted — a tie is disagreement |
| A provider fails or times out | Vote is discarded; remaining valid votes determine consensus |

> **Independence is the point.** Two `OpenAiHealingProvider` instances pointed at the same endpoint and model are the same model voting twice, not a consensus. Prefer providers backed by genuinely different models.

The nightly workflow enforces this rule with a live gate using Gemini and Groq/Llama on the known `Desktop_AmbiguousSiblingTabs` scenario. Both raw votes must remain inside the shortlist, name the same ground-truth `CandidateId`, and appear in `HealResult.AgreedProviders`; otherwise the workflow fails. The broader multi-provider evaluation still runs afterward as non-gating telemetry. With no credentials the opt-in test skips cleanly, while a deliberate one-provider configuration fails before making an API call. Releases are not gated on third-party availability; this gate belongs to the nightly workflow only.

#### Naming providers

`HealResult.AgreedProviders` identifies voters by `ILlmHealingProvider.Name`, so names must be unique within a run — `LlmProviderFactory` throws on a duplicate rather than producing an unreadable report. Because `OpenAiHealingProvider` speaks to any OpenAI-compatible endpoint, several instances of it can legitimately be configured at once. The factory handles the well-known ones for you; construct them by hand and each needs a `name`:

```csharp
var providers = new ILlmHealingProvider[]
{
    new OpenAiHealingProvider(name: "Groq",     endpoint: "https://api.groq.com/openai/v1",  apiKey: groqKey),
    new OpenAiHealingProvider(name: "Cerebras", endpoint: "https://api.cerebras.ai/v1",      apiKey: cerebrasKey),
};
```

Without `name`, both would report `"OpenAI"` and their votes would be indistinguishable in the report.

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

On modest hardware, prefer a smaller model (`llama3.2:1b`, `qwen2.5:0.5b`) — set it with `OLLAMA_MODEL` or the `model:` parameter. Ollama alone cannot satisfy consensus: it is one provider, so pair it with a second one if you want LLM picks accepted rather than only recorded.

### 🌐 Free Cloud AI with Cloudflare Workers AI

Cloudflare Workers AI exposes an account-scoped OpenAI-compatible endpoint and includes a daily free allocation. Configure all three values; the factory deliberately skips Cloudflare when any one is missing because neither the account path nor a currently available model can be guessed safely:

```text
CLOUDFLARE_API_TOKEN=<repository secret>
CLOUDFLARE_ACCOUNT_ID=<repository variable>
CLOUDFLARE_MODEL=@cf/zai-org/glm-4.7-flash
```

The resulting provider is named `Cloudflare` and calls `https://api.cloudflare.com/client/v4/accounts/{account-id}/ai/v1/chat/completions`. Model availability and free-plan eligibility can change; run `provider-diagnostics.yml` before selecting a model and consult [Workers AI pricing](https://developers.cloudflare.com/workers-ai/platform/pricing/). Keep model ids in repository variables rather than secrets.

Choose a model family different from the other voters. Two endpoints serving the same underlying model do not provide independent consensus. The free allocation is appropriate for low-volume nightly or manual evaluation, not a guaranteed per-PR release gate.

`OpenAiHealingProvider` also supports other OpenAI-compatible endpoints such as Azure OpenAI, vLLM, and LM Studio. GitHub Models is not an option: its inference API was fully retired on July 30, 2026.

---

## 🇹🇷 Türkçe Kılavuz

### 📊 Sağlayıcı Karşılaştırma Tablosu

| Sağlayıcı | Varsayılan Model | Kurulum / Değişken | Maliyet | Gizlilik |
| :--- | :--- | :--- | :---: | :---: |
| **Gemini** | `gemini-3.6-flash` | `GEMINI_API_KEY` | Çok Düşük | Bulut |
| **Claude** | `claude-haiku-4-5-20251001` | `ANTHROPIC_API_KEY` | Çok Düşük | Bulut |
| **OpenAI** | `gpt-4o-mini` | `OPENAI_API_KEY` | Çok Düşük | Bulut |
| **Grok (xAI)** | `grok-2-latest` | `GROK_API_KEY` / `XAI_API_KEY` | Düşük | Bulut |
| **Kimi (Moonshot)** | `moonshot-v1-8k` | `KIMI_API_KEY` / `MOONSHOT_API_KEY` | Düşük | Bulut |
| **Cloudflare Workers AI** | Açıkça belirtilir (`CLOUDFLARE_MODEL`) | Token + hesap kimliği + model | Günlük ücretsiz kota | Bulut |
| **Ollama** | `llama3.2` | `OLLAMA_HOST` / `OLLAMA_MODEL` | **%100 Ücretsiz (\$0)** | **%100 Yerel (Çevrimdışı)** |

### 🏭 Dinamik Sağlayıcı Fabrikası (`LlmProviderFactory`)

`LlmProviderFactory.CreateConfiguredProviders()` ortamdaki anahtarları otomatik keşfeder ve kod değiştirmeden sağlayıcı listesini hazırlar:

Cloudflare için `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID` ve `CLOUDFLARE_MODEL` değerlerinin üçü de zorunludur. Tam yapılandırma `"Cloudflare"` adlı bir `OpenAiHealingProvider` oluşturur; herhangi biri eksikse bozuk bir uç nokta veya tahmini model üretmek yerine sağlayıcı atlanır.

```csharp
// Ortamdaki tüm geçerli sağlayıcıları otomatik al:
var providers = LlmProviderFactory.CreateConfiguredProviders();

var result = await SelfHealingResolver.ResolveAsync(expected, treeRoot, llmProviders: providers);
```

### ⏱️ Zaman Aşımı (Timeout) ve Dayanıklılık (Resilience)

Tüm sağlayıcılar `HttpLlmHealingProvider` tabanından türer; deneme başına zaman aşımı, toplam işlem tavanı ve üstel geri çekilmeli (exponential backoff) otomatik yeniden deneme destekler:

| Ayar | Varsayılan (Bulut) | Varsayılan (Ollama) | Açıklama |
| :--- | :---: | :---: | :--- |
| **`Timeout`** | `15s` | `30s` | Deneme başına HTTP zaman aşımı. Tek bir asılı isteğin akışı kilitlemesini önler. |
| **`TotalTimeout`** | `35s` | `70s` | Tüm denemeler ve bekleme süreleri dahil toplam işlem tavanı. |
| **`MaxRetries`** | `2` | `2` | Geçici hatalarda yeniden deneme sayısı (toplam en fazla 3 deneme). |

#### Yeniden Deneme (Retry) Kuralları:
- **Yeniden denenen geçici hatalar**: HTTP 429 (kota aşımı), 500, 502, 503, 504 ve `HttpRequestException` (geçici ağ kopmaları) üstel geri çekilme ve jitter ile otomatik olarak yeniden denenir.
- **Kalıcı hatalarda hızlı başarısızlık**: HTTP 400, 401, 403 ve 404 yeniden deneme hakkı harcamadan anında başarısız döner.
- **`Retry-After` Başlığı ve Kota Koruması**: Yanıtta `Retry-After` başlığı $\le 10\text{s}$ ise belirtilen süre kadar beklenir. $> 10\text{s}$ ise (ör. günlük kota tükenmesi) boşuna beklenmez, doğrudan başarısız dönülür.

### 🤝 Mutabakat (Consensus) Kabul Kuralı

Bir LLM seçimi yalnızca **en az iki bağımsız sağlayıcı aynı adayı seçtiğinde** kabul edilir. Modellerin kendi beyan ettiği güven puanları karşılaştırılmaz.

| Durum | Sonuç |
| :--- | :--- |
| İki veya daha fazla sağlayıcı aynı adayı seçer | Kabul edilir; oy verenler `HealResult.AgreedProviders` içine yazılır |
| Yalnızca tek sağlayıcı yapılandırılmış | **Asla kabul edilmez** — tek sağlayıcı kendisiyle mutabakat sağlayamaz |
| Her sağlayıcı farklı bir aday seçer | Kabul edilmez — ayrık oy (split vote) olarak raporlanır |
| İki aday en yüksek oyda berabere kalır | Kabul edilmez — beraberlik anlaşmazlıktır |
| Bir sağlayıcı hata verir veya zaman aşımına uğrar | O oy elenir; kalan geçerli oylar mutabakatı belirler |

> **Asıl mesele bağımsızlık.** Aynı uç noktaya ve aynı modele bakan iki `OpenAiHealingProvider` örneği, mutabakat değil aynı modelin iki kez oy vermesidir. Gerçekten farklı modellere dayanan sağlayıcıları tercih edin.

Nightly workflow bu kuralı bilinen `Desktop_AmbiguousSiblingTabs` senaryosunda Gemini ve Groq/Llama kullanan canlı bir gate ile uygular. İki ham oyun da shortlist içinde kalması, aynı ground-truth `CandidateId` değerini seçmesi ve `HealResult.AgreedProviders` içinde görünmesi gerekir; aksi halde workflow başarısız olur. Daha geniş çok-sağlayıcılı değerlendirme bunun ardından gate olmayan telemetri olarak çalışmaya devam eder. Hiç credential yoksa opt-in test temiz biçimde atlanır; bilinçli tek-sağlayıcı yapılandırması ise API çağrısı yapmadan başarısız olur. Üçüncü taraf erişilebilirliği release'i engellemesin diye gate yalnızca nightly workflow'dadır.

#### Sağlayıcılara isim verme

`HealResult.AgreedProviders` oy verenleri `ILlmHealingProvider.Name` ile tanımlar; bu yüzden isimler bir çalıştırma içinde benzersiz olmalıdır — `LlmProviderFactory` yinelenen bir isimde okunmaz bir rapor üretmek yerine hata fırlatır. `OpenAiHealingProvider` OpenAI uyumlu her uç noktayla konuştuğu için aynı anda birden çok örneği meşru biçimde yapılandırılabilir. Bilinen sağlayıcıları fabrika sizin için kurar; elle kuruyorsanız her birine `name` verin:

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

Bilgisayarınız zayıfsa daha küçük bir model tercih edin (`llama3.2:1b`, `qwen2.5:0.5b`) — `OLLAMA_MODEL` ile veya `model:` parametresiyle ayarlanır. Ollama tek başına mutabakatı sağlayamaz: tek sağlayıcıdır, dolayısıyla LLM seçimlerinin yalnız kaydedilmesi değil kabul edilmesi isteniyorsa yanına ikinci bir sağlayıcı gerekir.

### 🌐 Cloudflare Workers AI ile Ücretsiz Bulut Yapay Zekası

Cloudflare Workers AI hesap kapsamlı, OpenAI uyumlu bir uç nokta ve günlük ücretsiz kota sunar. Üç değerin tamamını yapılandırın:

```text
CLOUDFLARE_API_TOKEN=<repository secret>
CLOUDFLARE_ACCOUNT_ID=<repository variable>
CLOUDFLARE_MODEL=@cf/zai-org/glm-4.7-flash
```

Oluşan sağlayıcının adı `Cloudflare`, uç noktası `https://api.cloudflare.com/client/v4/accounts/{account-id}/ai/v1/chat/completions` olur. Model erişilebilirliği ve ücretsiz plan uygunluğu değişebilir; model seçmeden önce `provider-diagnostics.yml` çalıştırın ve [Workers AI fiyatlandırmasını](https://developers.cloudflare.com/workers-ai/platform/pricing/) kontrol edin. Model kimlikleri secret değil repository variable olarak tutulmalıdır.

Diğer oy verenlerden farklı bir model ailesi seçin. Aynı temel modeli sunan iki uç nokta bağımsız mutabakat oluşturmaz. Ücretsiz kota düşük hacimli nightly veya manuel değerlendirmeye uygundur; her PR için garantili release gate olarak kullanılmamalıdır.

`OpenAiHealingProvider`, Azure OpenAI, vLLM ve LM Studio gibi diğer OpenAI uyumlu uç noktaları da destekler. GitHub Models artık seçenek değildir: inference API 30 Temmuz 2026 tarihinde tamamen kapatılmıştır.
