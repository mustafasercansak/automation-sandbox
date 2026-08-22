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
| **Ollama** | `llama3.2` | `OLLAMA_HOST` / `OLLAMA_MODEL` | **100% Free (\$0)** | Local by default; host-controlled |

> [!CAUTION]
> Provider setup is also a data-disclosure decision. DOM/UI text is untrusted, there is no automatic PII/secret redaction, and every configured provider receives the bounded prompt. Read the [LLM Healing Security Model](llm-security-model.md) before adding cloud or remote endpoints.

### 🏭 Dynamic Environment Provider Factory (`LlmProviderFactory`)

`LlmProviderFactory.CreateConfiguredProviders()` automatically discovers and instantiates available providers based on active environment variables without needing code changes:

> [!IMPORTANT]
> **No provider is required, and none of these keys is mandatory.** A well-known provider exists only when its own `*_API_KEY` is present; everything else is skipped silently. Invalid `LLM_CUSTOM_PROVIDERS` input is skipped with a credential-safe diagnostic instead. The agreement quorum needs **two** independent providers, so two keys is the practical minimum.
>
> **Never point one provider slot at another provider's endpoint.** `OpenAiHealingProvider` accepts any OpenAI-compatible URL, which makes it tempting to reuse the `OPENAI_*` slot for a different vendor. Two slots proxying the same model are one voter with two names: their agreement is not independent and the quorum becomes meaningless (#19). If you need a second opinion, use a second vendor. The `OPENAI_*` slot is reserved for a genuine OpenAI credential; leave it unset otherwise.


- **Well-known auto-discovery**:
  - `ANTHROPIC_API_KEY` (+ optional `ANTHROPIC_MODEL`) $\rightarrow$ `ClaudeHealingProvider`
  - `GEMINI_API_KEY` (+ optional `GEMINI_MODEL`) $\rightarrow$ `GeminiHealingProvider`
  - `OPENAI_API_KEY` (+ optional `OPENAI_MODEL`, `OPENAI_ENDPOINT`) $\rightarrow$ `OpenAiHealingProvider`
  - `GROK_API_KEY` (+ optional `GROK_MODEL`, `GROK_ENDPOINT`) $\rightarrow$ `OpenAiHealingProvider` (named `"Grok"`)
  - `KIMI_API_KEY` (+ optional `KIMI_MODEL`, `KIMI_ENDPOINT`) $\rightarrow$ `OpenAiHealingProvider` (named `"Kimi"`)
  - `CLOUDFLARE_API_TOKEN` + `CLOUDFLARE_ACCOUNT_ID` + `CLOUDFLARE_MODEL` $\rightarrow$ `OpenAiHealingProvider` (named `"Cloudflare"`)
  - `MISTRAL_API_KEY` + `MISTRAL_MODEL` $\rightarrow$ `OpenAiHealingProvider` (named `"Mistral"`)
  - `NVIDIA_API_KEY` + `NVIDIA_MODEL` $\rightarrow$ `OpenAiHealingProvider` (named `"Nvidia"`)
  - `OLLAMA_CLOUD_API_KEY` + `OLLAMA_CLOUD_MODEL` $\rightarrow$ `OpenAiHealingProvider` (named `"OllamaCloud"`)
  - `OLLAMA_ENABLED=true` or `OLLAMA_HOST` $\rightarrow$ `OllamaHealingProvider` (local daemon on `localhost:11434`)

> [!WARNING]
> `OLLAMA_CLOUD_*` and `OLLAMA_*` are deliberately separate and must never be conflated. The local variables build a provider aimed at `localhost:11434`, where no daemon exists on a CI runner. Pointing `OLLAMA_MODEL` at a cloud model therefore produces a provider that fails every request **while still counting toward the two-provider agreement quorum**, which is the opposite of what adding a provider is meant to achieve.

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

  Every entry requires a non-empty `name`, `endpoint`, `model`, and either `apiKey`
  or a resolvable `apiKeyEnvVar`. `endpoint` and `model` are never inherited from the
  `OPENAI_*` variables: an entry missing either value is skipped so it cannot silently
  send a custom credential to OpenAI or add a mislabeled vote to the agreement quorum. Malformed
  JSON skips the custom array without discarding already discovered built-in providers.
  If the array itself is valid but one entry has the wrong JSON shape, only that entry
  is skipped and valid custom siblings are still constructed.
  Diagnostics never echo the JSON or API key. They go to standard error by default, or
  can be routed to the application's logger:

  ```csharp
  var providers = LlmProviderFactory.CreateConfiguredProviders(
      httpClient: null,
      getEnv: null,
      log: message => logger.LogWarning("{Message}", message));
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

If an HTTP provider returns a successful response that cannot be parsed, its provider error
includes the raw response body, bounded to 4096 characters. This applies uniformly to Claude,
Gemini, Ollama, and every OpenAI-compatible endpoint. Only the response body is captured;
request headers and configured API credentials are not appended to the diagnostic.

### 🤝 Independent Model Agreement (`Consensus` API)

An LLM pick is accepted only when **at least two providers independently name the same candidate**. The public API calls this `MinimumConsensusVotes`, and reports use `no-consensus`; these names describe the quorum mechanism, not a correctness guarantee. Self-reported confidence is recorded but never compared or thresholded: Claude's `0.72` and Gemini's `0.95` do not live on the same scale.

> [!WARNING]
> **Agreement is an additional signal, not proof that the chosen element is correct.** Across four live runs, providers unanimously agreed in 34 deleted-element scenarios and all 34 verdicts were false heals, including cases where three independently sourced model families chose the same decoy. The measured separation came from providers disagreeing more often when an element was absent, not from agreement establishing correctness. See the [formal finding](benchmark-calibration.md#6-multi-provider-llm-consensus-as-an-absence-detector-97).

| Situation | Outcome |
| :--- | :--- |
| Two or more providers name the same candidate | Accepted; the voters are recorded in `HealResult.AgreedProviders` |
| Only one provider is configured | **Never accepted** — one provider cannot agree with itself |
| Every provider names a different candidate | Not accepted — reported as split vote / disagreement |
| Two candidates tie for the most votes | Not accepted — a tie is disagreement |
| A provider fails or times out | Vote is discarded; remaining valid votes determine whether the quorum is met |

> **Independence is the point.** Two `OpenAiHealingProvider` instances pointed at the same endpoint and model are the same model voting twice, not independent agreement. Prefer providers backed by genuinely different models. Even genuinely independent agreement remains a quorum signal, not a correctness guarantee.

The nightly workflow enforces this rule with a live gate using Groq and Mistral on the known `Desktop_AmbiguousSiblingTabs` scenario. Both raw votes must remain inside the shortlist, name the same ground-truth `CandidateId`, and appear in `HealResult.AgreedProviders`; otherwise the workflow fails. The broader multi-provider evaluation still runs afterward as non-gating telemetry. With no credentials the opt-in test skips cleanly, while a deliberate one-provider configuration fails before making an API call. Releases are not gated on third-party availability; this gate belongs to the nightly workflow only.

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

On modest hardware, prefer a smaller model (`llama3.2:1b`, `qwen2.5:0.5b`) — set it with `OLLAMA_MODEL` or the `model:` parameter. Ollama alone cannot satisfy the agreement quorum: it is one provider, so pair it with a second one if you want LLM picks accepted rather than only recorded.

### 🌐 Free Cloud AI with Cloudflare Workers AI

Cloudflare Workers AI exposes an account-scoped OpenAI-compatible endpoint and includes a daily free allocation. Configure all three values; the factory deliberately skips Cloudflare when any one is missing because neither the account path nor a currently available model can be guessed safely:

```text
CLOUDFLARE_API_TOKEN=<repository secret>
CLOUDFLARE_ACCOUNT_ID=<repository variable>
CLOUDFLARE_MODEL=@cf/zai-org/glm-4.7-flash
```

The resulting provider is named `Cloudflare` and calls `https://api.cloudflare.com/client/v4/accounts/{account-id}/ai/v1/chat/completions`. The Cloudflare request uses `response_format: { "type": "json_object" }` and `max_tokens: 2000`, leaving room for Qwen reasoning plus the complete JSON response without depending on a provider-specific output default. Model availability and free-plan eligibility can change; run `provider-diagnostics.yml` before selecting a model and consult [Workers AI pricing](https://developers.cloudflare.com/workers-ai/platform/pricing/). Keep model ids in repository variables rather than secrets.

Choose a model family different from the other voters. Two endpoints serving the same underlying model do not provide independent agreement. The free allocation is appropriate for low-volume nightly or manual evaluation, not a guaranteed per-PR release gate.

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
| **Ollama** | `llama3.2` | `OLLAMA_HOST` / `OLLAMA_MODEL` | **%100 Ücretsiz (\$0)** | Varsayılan yerel; host'a bağlı |

> [!CAUTION]
> Sağlayıcı kurulumu aynı zamanda bir veri ifşası kararıdır. DOM/UI metni güvenilmeyen girdidir, otomatik PII/secret redaction yoktur ve yapılandırılmış her sağlayıcı sınırlı prompt'u alır. Bulut veya uzak endpoint eklemeden önce [LLM Healing Güvenlik Modelini](llm-security-model.md) okuyun.

### 🏭 Dinamik Sağlayıcı Fabrikası (`LlmProviderFactory`)

`LlmProviderFactory.CreateConfiguredProviders()` ortamdaki anahtarları otomatik keşfeder ve kod değiştirmeden sağlayıcı listesini hazırlar:

> [!IMPORTANT]
> **Hiçbir sağlayıcı zorunlu değildir; bu anahtarların hiçbiri gerekli değildir.** Bilinen bir sağlayıcı yalnızca kendi `*_API_KEY` değeri varsa kurulur, aksi halde sessizce atlanır. Geçersiz `LLM_CUSTOM_PROVIDERS` girdisi ise kimlik bilgilerini açığa çıkarmayan bir tanı mesajıyla atlanır. Uzlaşma quorum'u **iki** bağımsız sağlayıcı gerektirdiği için pratik alt sınır iki anahtardır.
>
> **Bir sağlayıcı slotunu başka bir sağlayıcının uç noktasına yönlendirmeyin.** `OpenAiHealingProvider` herhangi bir OpenAI-uyumlu adresi kabul ettiği için `OPENAI_*` slotunu başka bir sağlayıcı için yeniden kullanmak cazip gelir. Aynı modeli çağıran iki slot, iki isimli tek bir oy demektir: aynı sistem oldukları için anlaşırlar ve aralarındaki uzlaşma anlamsızdır (#19). İkinci bir görüş gerekiyorsa ikinci bir sağlayıcı kullanın. `OPENAI_*` slotu gerçek bir OpenAI kimlik bilgisine ayrılmıştır; başka bir amaçla doldurmayın, boş bırakın.

Cloudflare için `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID` ve `CLOUDFLARE_MODEL` değerlerinin üçü de zorunludur. Tam yapılandırma `"Cloudflare"` adlı bir `OpenAiHealingProvider` oluşturur; herhangi biri eksikse bozuk bir uç nokta veya tahmini model üretmek yerine sağlayıcı atlanır.

Aynı kural Mistral (`MISTRAL_API_KEY` + `MISTRAL_MODEL` $\rightarrow$ `"Mistral"`), NVIDIA NIM (`NVIDIA_API_KEY` + `NVIDIA_MODEL` $\rightarrow$ `"Nvidia"`) ve Ollama Cloud (`OLLAMA_CLOUD_API_KEY` + `OLLAMA_CLOUD_MODEL` $\rightarrow$ `"OllamaCloud"`) için de geçerlidir; ikisi de OpenAI uyumludur, ayrı sağlayıcı sınıfı gerektirmez.

> [!WARNING]
> `OLLAMA_CLOUD_*` ile `OLLAMA_*` bilinçli olarak ayrıdır ve karıştırılmamalıdır. Yerel değişkenler `localhost:11434` adresini hedefleyen bir sağlayıcı kurar; CI runner'ında orada çalışan bir daemon yoktur. Dolayısıyla `OLLAMA_MODEL`'i bir bulut modeline yönlendirmek, her istekte başarısız olan ama **yine de iki sağlayıcılı mutabakat eşiğine sayılan** bir sağlayıcı üretir — sağlayıcı eklemenin amacının tam tersi.

`LLM_CUSTOM_PROVIDERS` dizisindeki her giriş boş olmayan `name`, `endpoint`, `model`
ve doğrudan `apiKey` ya da çözümlenebilir bir `apiKeyEnvVar` içermelidir. `endpoint`
ve `model`, `OPENAI_*` değişkenlerinden devralınmaz. Bu alanlardan biri eksikse özel
kimlik bilgisinin yanlışlıkla OpenAI'a gönderilmemesi ve mutabakata yanlış etiketli bir
oy eklenmemesi için yalnızca ilgili giriş atlanır. JSON bütünüyle bozuksa daha önce
keşfedilmiş yerleşik sağlayıcılar korunur. Dizinin kendisi geçerli olup bir elemanın JSON
şekli bozuksa yalnızca o eleman atlanır ve geçerli özel sağlayıcı kardeşleri yine oluşturulur.
Tanı mesajları ham JSON'u ve API anahtarını içermez; varsayılan olarak standart hataya
yazılır veya uygulamanın logger'ına yönlendirilebilir:

```csharp
var providers = LlmProviderFactory.CreateConfiguredProviders(
    httpClient: null,
    getEnv: null,
    log: message => logger.LogWarning("{Message}", message));
```

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

Bir HTTP sağlayıcısı ayrıştırılamayan başarılı bir yanıt döndürürse sağlayıcı hatası, 4096
karakterle sınırlandırılmış ham yanıt gövdesini içerir. Bu davranış Claude, Gemini, Ollama ve
tüm OpenAI uyumlu uç noktalara aynı şekilde uygulanır. Yalnızca yanıt gövdesi yakalanır;
istek başlıkları ve yapılandırılmış API kimlik bilgileri tanı mesajına eklenmez.

### 🤝 Bağımsız Model Uzlaşması (`Consensus` API)

Bir LLM seçimi yalnızca **en az iki bağımsız sağlayıcı aynı adayı seçtiğinde** kabul edilir. Public API bu eşiği `MinimumConsensusVotes`, raporlar ise başarısız sonucu `no-consensus` olarak adlandırır; bu adlar doğruluk garantisini değil quorum mekanizmasını ifade eder. Modellerin kendi beyan ettiği güven puanları karşılaştırılmaz.

> [!WARNING]
> **Uzlaşma ek bir sinyaldir; seçilen elemanın doğru olduğunun kanıtı değildir.** Dört canlı koşuda sağlayıcılar 34 silinmiş-eleman senaryosunda oybirliğine ulaştı ve 34 kararın tamamı yanlış iyileştirmeydi; üç bağımsız kaynaklı model ailesinin aynı yanlış komşuyu seçtiği vakalar da buna dahildi. Ölçülen ayrışma, eleman yokken sağlayıcıların daha sık anlaşamamasından doğdu; anlaşmaları doğruluğu kanıtlamadı. [Resmi bulguya](benchmark-calibration.md#6-multi-provider-llm-consensus-as-an-absence-detector-97) bakın.

| Durum | Sonuç |
| :--- | :--- |
| İki veya daha fazla sağlayıcı aynı adayı seçer | Kabul edilir; oy verenler `HealResult.AgreedProviders` içine yazılır |
| Yalnızca tek sağlayıcı yapılandırılmış | **Asla kabul edilmez** — tek sağlayıcı kendisiyle mutabakat sağlayamaz |
| Her sağlayıcı farklı bir aday seçer | Kabul edilmez — ayrık oy (split vote) olarak raporlanır |
| İki aday en yüksek oyda berabere kalır | Kabul edilmez — beraberlik anlaşmazlıktır |
| Bir sağlayıcı hata verir veya zaman aşımına uğrar | O oy elenir; kalan geçerli oylar quorum sağlanıp sağlanmadığını belirler |

> **Asıl mesele bağımsızlık.** Aynı uç noktaya ve aynı modele bakan iki `OpenAiHealingProvider` örneği, bağımsız uzlaşma değil aynı modelin iki kez oy vermesidir. Gerçekten farklı modellere dayanan sağlayıcıları tercih edin. Gerçekten bağımsız uzlaşma bile bir quorum sinyalidir; doğruluk garantisi değildir.

Nightly workflow bu kuralı bilinen `Desktop_AmbiguousSiblingTabs` senaryosunda Groq ve Mistral kullanan canlı bir gate ile uygular. İki ham oyun da shortlist içinde kalması, aynı ground-truth `CandidateId` değerini seçmesi ve `HealResult.AgreedProviders` içinde görünmesi gerekir; aksi halde workflow başarısız olur. Daha geniş çok-sağlayıcılı değerlendirme bunun ardından gate olmayan telemetri olarak çalışmaya devam eder. Hiç credential yoksa opt-in test temiz biçimde atlanır; bilinçli tek-sağlayıcı yapılandırması ise API çağrısı yapmadan başarısız olur. Üçüncü taraf erişilebilirliği release'i engellemesin diye gate yalnızca nightly workflow'dadır.

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

Bilgisayarınız zayıfsa daha küçük bir model tercih edin (`llama3.2:1b`, `qwen2.5:0.5b`) — `OLLAMA_MODEL` ile veya `model:` parametresiyle ayarlanır. Ollama tek başına uzlaşma quorum'unu sağlayamaz: tek sağlayıcıdır, dolayısıyla LLM seçimlerinin yalnız kaydedilmesi değil kabul edilmesi isteniyorsa yanına ikinci bir sağlayıcı gerekir.

### 🌐 Cloudflare Workers AI ile Ücretsiz Bulut Yapay Zekası

Cloudflare Workers AI hesap kapsamlı, OpenAI uyumlu bir uç nokta ve günlük ücretsiz kota sunar. Üç değerin tamamını yapılandırın:

```text
CLOUDFLARE_API_TOKEN=<repository secret>
CLOUDFLARE_ACCOUNT_ID=<repository variable>
CLOUDFLARE_MODEL=@cf/zai-org/glm-4.7-flash
```

Oluşan sağlayıcının adı `Cloudflare`, uç noktası `https://api.cloudflare.com/client/v4/accounts/{account-id}/ai/v1/chat/completions` olur. Cloudflare isteği `response_format: { "type": "json_object" }` ve `max_tokens: 2000` gönderir; böylece Qwen reasoning çıktısı ve tamamlanmış JSON yanıtı için yeterli alan bırakılır, çıktı sağlayıcı varsayılanına bırakılmaz. Model erişilebilirliği ve ücretsiz plan uygunluğu değişebilir; model seçmeden önce `provider-diagnostics.yml` çalıştırın ve [Workers AI fiyatlandırmasını](https://developers.cloudflare.com/workers-ai/platform/pricing/) kontrol edin. Model kimlikleri secret değil repository variable olarak tutulmalıdır.

Diğer oy verenlerden farklı bir model ailesi seçin. Aynı temel modeli sunan iki uç nokta bağımsız mutabakat oluşturmaz. Ücretsiz kota düşük hacimli nightly veya manuel değerlendirmeye uygundur; her PR için garantili release gate olarak kullanılmamalıdır.

`OpenAiHealingProvider`, Azure OpenAI, vLLM ve LM Studio gibi diğer OpenAI uyumlu uç noktaları da destekler. GitHub Models artık seçenek değildir: inference API 30 Temmuz 2026 tarihinde tamamen kapatılmıştır.
