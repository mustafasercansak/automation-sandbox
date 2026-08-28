---
layout: default
title: LLM Security Model - Automation Sandbox
---

# LLM Healing Security Model / LLM Healing Güvenlik Modeli

## English

### Scope and trust boundary

LLM healing is opt-in. It runs only when heuristic resolution is not confident and at
least one configured provider is available. The resolver sends each available provider
the same bounded shortlist in parallel; retries send the same prompt to that provider
again. Configuring several providers therefore discloses the prompt to several separate
data processors—it is not one shared private computation.

Treat every value captured from a DOM or desktop accessibility tree as **untrusted
input**. Element text can contain personal data, secrets, customer content, or deliberate
prompt-injection instructions. `TestIntent` is application-authored but may carry the
same kinds of sensitive data. Neither source becomes trusted merely because it is
serialized into a structured prompt.

The deterministic heuristic path does not call an LLM provider. To guarantee that UI
content does not leave the process through this feature, do not configure LLM providers
and do not pass provider instances to `ResolveAsync`.

### What reaches a provider

The provider does **not** receive a screenshot or the full DOM/UI tree. The prompt
contains:

- the target platform;
- the last known element's `ControlType`, `Name`, `ClassName`, bounding rectangle,
  parent control type, sibling index/count, and `TestIntent`;
- up to `MaxCandidatesForLlm` candidates (20 by default), including each candidate's
  synthetic `CandidateId`, `AutomationId`, `ControlType`, `Name`, total heuristic score,
  and component scores; and
- instructions requesting a candidate ID, confidence value, and one-sentence reasoning.

The stale target `AutomationId` is deliberately omitted to reduce semantic anchoring.
Candidate `AutomationId` values are still included. `TestIntent` appears in both the
serialized target snapshot and a human-readable intent block when it is non-empty.

By default, a built-in redactor masks common sensitive patterns (see "PII, secrets, and
operator controls" below) in this text before transmission. There is no classifier,
allow-list, or secret scanner beyond that pattern-based redaction, and it can be disabled
per caller. Bounding the shortlist limits volume and cost; neither the redaction pass nor
the bounding makes the remaining content anonymous or safe to disclose.

### Named risks, current mitigations, and residual risk

| Risk | Current mitigation | What it does **not** mitigate |
| :--- | :--- | :--- |
| A model invents an element that was not offered | Synthetic candidate IDs form a closed shortlist; the hallucination guard discards votes whose ID is outside it before counting agreement. | Malicious or mistaken choice *inside* the shortlist, sensitive-data disclosure, or a false heal. |
| Prompt injection in DOM/UI text (e.g. "ignore previous instructions") attempts to hijack locator choice | Structural XML boundary tags (`<target_element>`, `<candidate_shortlist>`, `<test_intent>`), explicit system security directive instructing the model to treat element content as passive data only, JSON attribute escaping, synthetic candidateId closed set, hallucination guard, and multi-provider consensus quorum. | A subtle in-context jailbreak that successfully deceives all consensus providers into picking an incorrect in-shortlist candidate. |
| The stale target ID semantically anchors the model | The target's old `AutomationId` is omitted from the prompt. | Sensitive target `Name`, `ClassName`, `TestIntent`, candidate names/IDs, or adversarial text in any of those fields. |
| An unexpectedly large tree causes unbounded disclosure | Only the scored Top-N shortlist is sent; no screenshot or full tree is sent. | PII or secrets contained in those selected elements, disclosure to every configured provider, or repeated disclosure during retries. |
| One provider returns an arbitrary high-confidence answer | At least `MinimumConsensusVotes` independent provider votes must name the same in-shortlist candidate; self-reported confidence does not gate acceptance. | Correctness or security. Providers can share the same bad assumption, and agreement is explicitly not evidence that an element still exists. |
| A provider stalls or rate-limits a run | Bounded retries and per-attempt/total timeouts limit how long the call can occupy the resolver. See “Timeouts & Resilience Patterns” in the [provider guide](llm-providers.md). | Confidentiality. Every retry resends the prompt, and these controls do not change provider retention. |

The candidate-ID protocol, prompt boundary tags, and security directives form an **instruction/data separation and output-integrity boundary**. Untrusted DOM text is enclosed within `<target_element>`, `<candidate_shortlist>`, and `<test_intent>` XML tags, and guarded with explicit security directives prohibiting the LLM from executing or following instructions embedded inside element names or attributes. Furthermore, the Hallucination Guard strictly enforces that returned candidate IDs must belong to the pre-scored shortlist, and the multi-model consensus quorum requires independent agreement across multiple provider architectures.

This boundary reduces the risk that embedded UI text hijacks locator selection, but it is
not a disclosure control: every field sent in `<target_element>` and `<candidate_shortlist>`
is still readable by each configured provider. Do not use LLM healing on screens whose
captured fields cannot be disclosed to every configured provider.

Independent agreement also remains a locator-selection quorum—not a security review,
fact check, or correctness guarantee. The measured limitations are documented in the
[benchmark guide](benchmark-calibration.md#6-multi-provider-llm-consensus-as-an-absence-detector-97).

### PII, secrets, and operator controls

By default, the library applies a built-in sensitive data redaction pass (`SensitiveDataSanitizer.Redact`) across all DOM/UI-tree and test intent text before constructing LLM prompts (`LlmHealingPrompt.Build` and `LlmIntentPlanningPrompt.Build`). Common patterns such as email addresses, credit/debit card numbers, bearer tokens, prefixed API keys/JWTs (including Stripe `sk_live_`/`pk_test_`/`rk_live_` keys and Google/GCP `AIza` keys), key-value secrets (e.g. `password: ...`, `api_key = ...`), and US Social Security Numbers are automatically masked with standard replacement tokens (`[REDACTED_EMAIL]`, `[REDACTED_CARD]`, `[REDACTED_SECRET]`, `[REDACTED_SSN]`).

Redaction is **opt-out**: operators who require raw, unmasked text can pass `SensitiveDataSanitizer.PassThrough` (or `text => text`) to `HttpLlmHealingProvider.TextSanitizer`, `LlmHealingPrompt.Build`, or `LlmIntentPlanner.TextSanitizer`. Custom sanitizers can also be supplied via the same hook (`Func<string, string>`).

> [!CAUTION]
> Pattern-based sanitization is a defence-in-depth layer, not a substitute for data isolation or access controls. It cannot detect proprietary, unstructured, or domain-specific confidential information. Do not send production or highly sensitive screens to third-party LLM providers.

Suitable controls include:

1. Rely on default built-in redaction, or configure a custom `TextSanitizer` delegate (e.g. domain-specific proprietary ID masking) on provider instances or intent planners.
2. Use `SensitiveDataSanitizer.PassThrough` only in isolated, synthetic test environments where masking is undesirable.
3. Use heuristic-only resolution for authenticated, financial, health, production-data,
   or other sensitive screens.
4. Capture synthetic or scrubbed test data, and keep secrets out of element names,
   accessibility labels, IDs, and `TestIntent`.
5. Configure only providers and regions approved by the application's data owner. A
   multi-provider quorum requires disclosure to multiple providers.
6. If local processing is required, use an Ollama daemon on a controlled host and verify
   `OLLAMA_HOST`. Ollama Cloud is remote, and a non-local `OLLAMA_HOST` is remote even
   though the provider class is named `OllamaHealingProvider`.
7. Treat custom OpenAI-compatible endpoints as separate processors. Their transport,
   logging, retention, subprocessors, and training policies are entirely operator-owned.

### Provider telemetry and retention

Automation Sandbox cannot enforce provider-side logging, training, regional processing,
abuse monitoring, or deletion. These policies can differ by account tier, endpoint,
region, feature, and contract, and they can change independently of this repository.
Review the terms that apply to the exact account and endpoint before enabling it:

| Provider path supported by the factory | Official policy or data-control starting point |
| :--- | :--- |
| Anthropic Claude | [Anthropic API data retention](https://privacy.anthropic.com/en/articles/7996866-how-long-do-you-store-my-organization-s-data) |
| Google Gemini | [Gemini API terms](https://ai.google.dev/gemini-api/terms) and [Zero Data Retention](https://ai.google.dev/gemini-api/docs/zdr) |
| OpenAI | [OpenAI API data controls](https://developers.openai.com/api/docs/guides/your-data#default-usage-policies-by-endpoint) |
| xAI Grok | [xAI security FAQ](https://docs.x.ai/developers/faq/security) |
| Groq | [Groq: Your Data](https://console.groq.com/docs/your-data) |
| OpenRouter | [Data collection](https://openrouter.ai/docs/guides/privacy/data-collection) and [Zero Data Retention routing](https://openrouter.ai/docs/guides/features/zdr) |
| Cloudflare Workers AI | [Workers AI data usage](https://developers.cloudflare.com/workers-ai/platform/data-usage/) |
| Mistral | [Mistral legal terms](https://legal.mistral.ai/) |
| NVIDIA NIM API | [NVIDIA API trial terms](https://assets.ngc.nvidia.com/products/api-catalog/legal/NVIDIA%20API%20Trial%20Terms%20of%20Service.pdf) |
| Kimi/Moonshot | Start with the [Moonshot platform documentation](https://platform.moonshot.ai/docs) and verify the current service terms, privacy notice, account controls, and contract for the exact endpoint. |
| Ollama Cloud or a local/remote Ollama daemon | [Ollama privacy policy](https://ollama.com/privacy); also verify where `OLLAMA_HOST` actually routes requests. |
| Custom endpoints | Verify the endpoint operator's current service terms, privacy notice, account controls, and contract. No retention assumption is encoded in this library. |

For example, the linked OpenAI documentation distinguishes abuse-monitoring retention
from application-state storage and describes account eligibility for modified or zero
data retention. Do not generalize one provider's or one endpoint's policy to another.
No retention assumption for Kimi/Moonshot, Ollama Cloud, or custom endpoints is encoded
in this library either.

### Local telemetry is sensitive too

When healing reports are enabled, JSON and rendered HTML can persist captured snapshots,
candidate names and automation IDs, model reasoning, provider names and attempt counts,
and provider error details. A response parse failure appends up to 4096 characters of the
raw HTTP response body to the provider error. A model may echo prompt content there.

Healing reports are not encrypted or automatically expired by the library. Store them as
sensitive test artifacts, restrict access, define a deletion period, and review them
before attaching them to issues or CI artifacts. See the [healing report guide](healing-reports.md).

Availability and retry design is tracked separately in issues
[#109](https://github.com/mustafasercansak/automation-sandbox/issues/109),
[#110](https://github.com/mustafasercansak/automation-sandbox/issues/110),
[#127](https://github.com/mustafasercansak/automation-sandbox/issues/127), and
[#129](https://github.com/mustafasercansak/automation-sandbox/issues/129). Those controls
limit operational failure; they do not change this confidentiality model.

---

## Türkçe

### Kapsam ve güven sınırı

LLM healing isteğe bağlıdır. Yalnızca sezgisel çözüm yeterince güvenli olmadığında ve en
az bir yapılandırılmış sağlayıcı bulunduğunda çalışır. Resolver aynı sınırlı aday listesini
kullanılabilir tüm sağlayıcılara paralel gönderir; retry aynı prompt'u o sağlayıcıya yeniden
gönderir. Bu nedenle birden fazla sağlayıcı yapılandırmak, prompt'u tek bir ortak özel
hesaplamaya değil birden fazla ayrı veri işleyene açıklamak demektir.

DOM veya masaüstü erişilebilirlik ağacından yakalanan her değeri **güvenilmeyen girdi**
olarak kabul edin. Eleman metni kişisel veri, sır, müşteri içeriği veya kasıtlı prompt
injection talimatı içerebilir. `TestIntent` uygulama tarafından yazılır fakat aynı tür
hassas verileri taşıyabilir. Bu kaynaklar yapılandırılmış bir prompt içine serialize
edilince güvenilir hâle gelmez.

Deterministik sezgisel yol hiçbir LLM sağlayıcısını çağırmaz. UI içeriğinin bu özellik
üzerinden süreç dışına çıkmamasını garanti etmek için LLM sağlayıcısı yapılandırmayın ve
`ResolveAsync` metoduna sağlayıcı örneği vermeyin.

### Sağlayıcıya hangi veriler gider

Sağlayıcı ekran görüntüsünü veya DOM/UI ağacının tamamını almaz. Prompt şunları içerir:

- hedef platform;
- son bilinen elemanın `ControlType`, `Name`, `ClassName`, bounding rectangle, üst eleman
  türü, kardeş indeksi/sayısı ve `TestIntent` değerleri;
- en fazla `MaxCandidatesForLlm` aday (varsayılan 20); her adayın sentetik `CandidateId`,
  `AutomationId`, `ControlType`, `Name`, toplam sezgisel skor ve bileşen skorları; ve
- aday kimliği, confidence değeri ve tek cümlelik reasoning isteyen talimatlar.

Eski hedef `AutomationId`, semantik çapalamayı azaltmak için bilerek çıkarılır. Adayların
`AutomationId` değerleri ise gönderilir. Boş değilse `TestIntent` hem serialize edilmiş
hedef snapshot'ında hem de okunabilir intent bloğunda yer alır.

Varsayılan olarak yerleşik bir redactor, gönderim öncesinde bu metin içindeki yaygın hassas
verileri maskeler (aşağıdaki "PII, sırlar ve operatör kontrolleri" bölümüne bakın). Bu desen
tabanlı maskeleme dışında bir sınıflandırıcı, allow-list veya secret scanner yoktur ve çağıran
taraf bunu devre dışı bırakabilir. Aday listesini sınırlamak hacmi ve maliyeti sınırlar; ne
maskeleme ne de bu sınırlama kalan içeriği anonim veya açıklanması güvenli hâle getirir.

### Adlandırılmış riskler, mevcut önlemler ve kalan risk

| Risk | Mevcut önlem | **Azaltmadığı** risk |
| :--- | :--- | :--- |
| Model sunulmayan bir eleman uydurur | Sentetik aday kimlikleri kapalı bir liste oluşturur; hallucination guard liste dışı kimliğe verilen oyu uzlaşma sayımından önce atar. | Liste içindeki kötü niyetli veya hatalı seçim, hassas veri ifşası ya da yanlış healing. |
| DOM/UI metnindeki prompt injection (ör. "önceki talimatları yok say") locator seçimini ele geçirmeye çalışır | Yapısal XML sınır etiketleri (`<target_element>`, `<candidate_shortlist>`, `<test_intent>`), modelin eleman içeriğini yalnızca pasif veri olarak değerlendirmesini emreden açık sistem güvenlik direktifi, JSON öznitelik kaçışı, sentetik candidateId kapalı kümesi, hallucination guard ve çoklu sağlayıcı uzlaşma quorum'u. | Liste içindeki yanlış bir adayı seçmesi için tüm uzlaşma sağlayıcılarını başarıyla aldatan incelikli bir in-context jailbreak. |
| Eski hedef kimliği modeli semantik olarak çapalar | Hedefin eski `AutomationId` değeri prompt'tan çıkarılır. | Hassas hedef `Name`, `ClassName`, `TestIntent`, aday adları/kimlikleri veya bu alanlardaki saldırgan metin. |
| Beklenmedik büyüklükteki ağaç sınırsız veri ifşasına yol açar | Yalnızca skorlanmış Top-N aday listesi gönderilir; ekran görüntüsü ve tam ağaç gönderilmez. | Seçilen elemanlardaki PII/sırlar, tüm yapılandırılmış sağlayıcılara ifşa veya retry sırasında tekrarlanan ifşa. |
| Tek sağlayıcı keyfî ve yüksek-confidence bir cevap verir | En az `MinimumConsensusVotes` bağımsız sağlayıcı oyu aynı liste içi adayı göstermelidir; modelin confidence değeri kabulü belirlemez. | Doğruluk veya güvenlik. Sağlayıcılar aynı hatalı varsayımı paylaşabilir; uzlaşma elemanın hâlâ var olduğunu kanıtlamaz. |
| Sağlayıcı çalışmayı bekletir veya rate limit uygular | Sınırlı retry ile deneme/operasyon timeout'ları çağrının resolver'ı ne kadar süre meşgul edeceğini sınırlar. [Sağlayıcı rehberindeki](llm-providers.md) “Zaman Aşımı ve Dayanıklılık” bölümüne bakın. | Gizlilik. Her retry prompt'u yeniden gönderir ve bu kontroller sağlayıcı retention politikasını değiştirmez. |

Aday-kimliği protokolü, prompt sınır etiketleri ve güvenlik direktifleri bir **talimat/veri ayrımı ve çıktı bütünlüğü sınırı** oluşturur. Güvenilmeyen DOM metni `<target_element>`, `<candidate_shortlist>` ve `<test_intent>` XML etiketleri içine alınır ve LLM'in eleman adları veya öznitelikleri içine gömülmüş talimatları yürütmesini ya da takip etmesini engelleyen açık güvenlik direktifleriyle korunur. Ayrıca Hallucination Guard, döndürülen aday kimliklerinin önceden skorlanmış aday listesine ait olmasını zorunlu kılar ve çoklu model uzlaşma quorum'u birden fazla sağlayıcı mimarisinde bağımsız anlaşma gerektirir.

Bu sınır, gömülü UI metninin locator seçimini ele geçirme riskini azaltır; ancak bir ifşa
kontrolü değildir: `<target_element>` ve `<candidate_shortlist>` içinde gönderilen her alan
yapılandırılmış her sağlayıcı tarafından hâlâ okunabilir. Yakalanan alanları tüm
yapılandırılmış sağlayıcılara açıklayamayacağınız ekranlarda LLM healing kullanmayın.

Bağımsız uzlaşma da yalnızca locator seçimi quorum'udur; güvenlik incelemesi, doğrulama
veya doğruluk garantisi değildir. Ölçülmüş sınırlar [benchmark rehberinde](benchmark-calibration.md#6-multi-provider-llm-consensus-as-an-absence-detector-97)
belgelenmiştir.

### PII, sırlar ve operatör kontrolleri

Varsayılan olarak kütüphane, LLM prompt'ları (`LlmHealingPrompt.Build` ve `LlmIntentPlanningPrompt.Build`) oluşturulmadan önce tüm DOM/UI ağacı ve test intent metinleri üzerinde yerleşik bir hassas veri maskeleme adımı (`SensitiveDataSanitizer.Redact`) uygular. E-posta adresleri, kredi/banka kartı numaraları, bearer token'ları, ön ekli API anahtarları/JWT'ler (Stripe `sk_live_`/`pk_test_`/`rk_live_` ve Google/GCP `AIza` anahtarları dahil), anahtar-değer sırları (ör. `password: ...`, `api_key = ...`) ve ABD Sosyal Güvenlik Numaraları standart maskeleme belirteçleriyle (`[REDACTED_EMAIL]`, `[REDACTED_CARD]`, `[REDACTED_SECRET]`, `[REDACTED_SSN]`) otomatik olarak maskelenir.

Maskeleme **opt-out** (varsayılan olarak açık) olarak çalışır: ham metnin filtrelenmeden iletilmesini isteyen operatörler `HttpLlmHealingProvider.TextSanitizer`, `LlmHealingPrompt.Build` veya `LlmIntentPlanner.TextSanitizer` üzerine `SensitiveDataSanitizer.PassThrough` (veya `text => text`) geçebilir. Özel temizleyiciler de aynı kanca (`Func<string, string>`) üzerinden sağlanabilir.

> [!CAUTION]
> Desen tabanlı maskeleme bir derinlemesine savunma (defence-in-depth) katmanıdır; veri yalıtımı veya erişim kontrollerinin yerine geçmez. Yapılandırılmamış, tescilli veya sektöre özel gizli bilgileri tespit edemez. Üretim ortamlarını veya yüksek derecede hassas ekranları harici LLM sağlayıcılarına göndermeyin.

Uygun kontroller şunları içerir:

1. Varsayılan yerleşik maskelemeyi kullanın veya sağlayıcılar / intent planner üzerinde özel bir `TextSanitizer` delegesi yapılandırın.
2. `SensitiveDataSanitizer.PassThrough` delegesini yalnızca maskelemenin istenmediği izole sentetik test ortamlarında kullanın.
3. Kimlik doğrulamalı, finansal, sağlık, production-data veya başka hassas ekranlarda
   yalnızca sezgisel çözümü kullanın.
4. Sentetik ya da temizlenmiş test verisi yakalayın; eleman adları, accessibility label,
   kimlik ve `TestIntent` içine sır koymayın.
5. Yalnızca veri sahibinin onayladığı sağlayıcıları ve bölgeleri yapılandırın. Çoklu
   sağlayıcı quorum'u verinin birden fazla sağlayıcıya açıklanmasını gerektirir.
6. Yerel işleme gerekiyorsa denetlenen bir host'taki Ollama daemon'unu kullanın ve
   `OLLAMA_HOST` değerini doğrulayın. Ollama Cloud uzaktır; yerel olmayan bir `OLLAMA_HOST`
   da sınıf adı `OllamaHealingProvider` olsa bile uzaktır.
7. Özel OpenAI-uyumlu endpoint'leri ayrı veri işleyenler olarak kabul edin. Transport,
   loglama, retention, alt işleyen ve eğitim politikaları tamamen operatör sorumluluğundadır.

### Sağlayıcı telemetrisi ve retention

Automation Sandbox sağlayıcı tarafındaki loglama, eğitim, bölgesel işleme, abuse
monitoring veya silmeyi zorlayamaz. Politikalar hesap paketi, endpoint, bölge, özellik ve
sözleşmeye göre farklılaşabilir ve bu depodan bağımsız değişebilir. Bir sağlayıcıyı
etkinleştirmeden önce tam olarak kullandığınız hesap ve endpoint için geçerli koşulları
inceleyin:

| Factory'nin desteklediği sağlayıcı yolu | Resmî politika veya veri kontrolü başlangıç noktası |
| :--- | :--- |
| Anthropic Claude | [Anthropic API veri saklama](https://privacy.anthropic.com/en/articles/7996866-how-long-do-you-store-my-organization-s-data) |
| Google Gemini | [Gemini API koşulları](https://ai.google.dev/gemini-api/terms) ve [Zero Data Retention](https://ai.google.dev/gemini-api/docs/zdr) |
| OpenAI | [OpenAI API veri kontrolleri](https://developers.openai.com/api/docs/guides/your-data#default-usage-policies-by-endpoint) |
| xAI Grok | [xAI güvenlik SSS](https://docs.x.ai/developers/faq/security) |
| Groq | [Groq: Your Data](https://console.groq.com/docs/your-data) |
| OpenRouter | [Veri toplama](https://openrouter.ai/docs/guides/privacy/data-collection) ve [Zero Data Retention routing](https://openrouter.ai/docs/guides/features/zdr) |
| Cloudflare Workers AI | [Workers AI veri kullanımı](https://developers.cloudflare.com/workers-ai/platform/data-usage/) |
| Mistral | [Mistral yasal koşulları](https://legal.mistral.ai/) |
| NVIDIA NIM API | [NVIDIA API deneme koşulları](https://assets.ngc.nvidia.com/products/api-catalog/legal/NVIDIA%20API%20Trial%20Terms%20of%20Service.pdf) |
| Kimi/Moonshot | [Moonshot platform dokümanından](https://platform.moonshot.ai/docs) başlayın; tam endpoint için güncel hizmet koşulları, gizlilik bildirimi, hesap kontrolleri ve sözleşmeyi doğrulayın. |
| Ollama Cloud veya yerel/uzak Ollama daemon'u | [Ollama gizlilik politikası](https://ollama.com/privacy); ayrıca `OLLAMA_HOST` değerinin istekleri gerçekte nereye yönlendirdiğini doğrulayın. |
| Özel endpoint'ler | Endpoint operatörünün güncel hizmet koşulları, gizlilik bildirimi, hesap kontrolleri ve sözleşmesini doğrulayın. Bu kütüphanede hiçbir retention varsayımı kodlanmaz. |

Örneğin bağlantısı verilen OpenAI dokümanı abuse-monitoring retention ile uygulama-durumu
saklamasını ayırır ve değiştirilmiş veya sıfır veri saklamaya hesap uygunluğunu açıklar.
Bir sağlayıcının veya endpoint'in politikasını diğerine genellemeyin. Kimi/Moonshot,
Ollama Cloud ve özel endpoint'ler için bu kütüphanede hiçbir retention varsayımı kodlanmaz.

### Yerel telemetri de hassastır

Healing raporları etkinse JSON ve oluşturulan HTML; yakalanmış snapshot'ları, aday adları
ve automation ID'leri, model reasoning'ini, sağlayıcı adları ve deneme sayılarını, ayrıca
sağlayıcı hata ayrıntılarını kalıcılaştırabilir. Yanıt parse edilemezse ham HTTP response
body'nin en fazla 4096 karakteri sağlayıcı hatasına eklenir. Model burada prompt içeriğini
tekrarlayabilir.

Healing raporları kütüphane tarafından şifrelenmez veya otomatik süresi doldurulmaz.
Bunları hassas test artifact'ları olarak saklayın, erişimi kısıtlayın, silme süresi
belirleyin ve issue/CI artifact'ına eklemeden önce inceleyin. [Healing report rehberine](healing-reports.md)
bakın.

Kullanılabilirlik ve retry tasarımı ayrıca
[#109](https://github.com/mustafasercansak/automation-sandbox/issues/109),
[#110](https://github.com/mustafasercansak/automation-sandbox/issues/110),
[#127](https://github.com/mustafasercansak/automation-sandbox/issues/127) ve
[#129](https://github.com/mustafasercansak/automation-sandbox/issues/129) issue'larında
izlenir. Bu kontroller operasyonel arızayı sınırlar; gizlilik modelini değiştirmez.
