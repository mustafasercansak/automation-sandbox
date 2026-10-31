---
layout: default
title: LLM Providers Guide - Automation Sandbox
---

# 🤖 LLM Providers Guide / Yapay Zeka Sağlayıcıları Rehberi

Automation Sandbox includes built-in AI providers and an environment-driven factory for LLM healing fallback, triggered when the heuristic match is not confident (score, evidence coverage, or candidate margin below threshold).

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
| **Grok (xAI)** | Explicit (`GROK_MODEL`) | `GROK_API_KEY` + `GROK_MODEL` | Low | Cloud |
| **Kimi (Moonshot)** | Explicit (`KIMI_MODEL`) | `KIMI_API_KEY` + `KIMI_MODEL` | Low | Cloud |
| **Groq** | Explicit (`GROQ_MODEL`) | `GROQ_API_KEY` + `GROQ_MODEL` | Low | Cloud |
| **OpenRouter** | Explicit (`OPENROUTER_MODEL`) | `OPENROUTER_API_KEY` + `OPENROUTER_MODEL` | Varies (per routed model) | Cloud |
| **Cloudflare Workers AI** | Explicit (`CLOUDFLARE_MODEL`) | Token + account ID + model | Free daily allocation | Cloud |
| **Ollama** | `llama3.2` | `OLLAMA_HOST` / `OLLAMA_MODEL` | **100% Free (\$0)** | Local by default; host-controlled |

> [!CAUTION]
> Provider setup is also a data-disclosure decision. DOM/UI text is untrusted, and every configured provider receives the bounded prompt. Built-in redaction masks common PII/secret patterns by default but is not exhaustive and can be disabled (`SensitiveDataSanitizer.PassThrough`). Read the [LLM Healing Security Model](llm-security-model.md) before adding cloud or remote endpoints.

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
  - `GROK_API_KEY` + `GROK_MODEL` (+ optional `GROK_ENDPOINT`) $\rightarrow$ `OpenAiHealingProvider` (named `"Grok"`) — both required; there is no guessed default model
  - `KIMI_API_KEY` + `KIMI_MODEL` (+ optional `KIMI_ENDPOINT`) $\rightarrow$ `OpenAiHealingProvider` (named `"Kimi"`) — both required; there is no guessed default model
  - `GROQ_API_KEY` + `GROQ_MODEL` $\rightarrow$ `OpenAiHealingProvider` (named `"Groq"`) — both required; there is no guessed default model
  - `OPENROUTER_API_KEY` + `OPENROUTER_MODEL` $\rightarrow$ `OpenAiHealingProvider` (named `"OpenRouter"`) — both required; the model id decides which underlying model answers
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

Agreement is a necessary condition, not the only one: an LLM-consensus pick must also clear the same **per-component name and descendant/child-signature gates** a heuristic winner must clear (`SimilarityWeights.MinimumNameScoreWhenNamed`, `MinimumChildSignatureSimilarity`; see the healing pipeline description in `AGENTS.md`). Before #416, those two gates were computed only for the heuristic winner, so a name- or content-mismatched candidate agreed on by two providers could still be accepted as confident; both gates are now computed and enforced identically regardless of `HealResult.Source`. The runner-up margin gate remains heuristic-only — vote-count margins and structural-score margins are not on the same scale (#268).

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

The nightly workflow enforces this rule with a live gate on the known `Desktop_AmbiguousSiblingTabs` scenario, exercised against the whole configured provider pool (#388): at least `MinimumConsensusVotes` (2) providers must keep their raw votes inside the shortlist, name the same ground-truth `CandidateId`, and appear in `HealResult.AgreedProviders`; otherwise the workflow fails. A provider that is down with HTTP 429 or a timeout contributes no usable vote and does not fail the gate by itself, but fewer than two usable votes still fails — quorum is then not demonstrable. The broader multi-provider evaluation still runs afterward as non-gating telemetry. With no credentials the opt-in test skips cleanly, while a deliberate one-provider configuration fails before making an API call. Releases are not gated on third-party availability; this gate belongs to the nightly workflow only.

> **Reasoning models.** Some OpenAI-compatible endpoints — notably Groq's `openai/gpt-oss-120b` in the "harmony" format — return their chain-of-thought in a non-standard `message.reasoning` field and can truncate the JSON answer object in `message.content` mid-string. `OpenAiHealingProvider` folds `message.reasoning` in alongside `message.content`, and the response parser recovers `candidateId` / `confidence` from an object that was cut off inside its `reasoning` value (#378). A model that returns clean JSON is still preferable — set `GROQ_MODEL` to e.g. `llama-3.3-70b-versatile` if the recovery path shows up in your telemetry.

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

### 🛠️ Custom LLM Providers (`ILlmHealingProvider` / `HttpLlmHealingProvider`)

If you want to integrate a proprietary internal model, a cloud vendor without an OpenAI-compatible interface, or a custom heuristic-AI pipeline, you can implement `ILlmHealingProvider` directly or subclass `HttpLlmHealingProvider`.

#### 1. Option A: Subclassing `HttpLlmHealingProvider` (Recommended for REST Endpoints)

`HttpLlmHealingProvider` encapsulates constructor parameter validation, retry with exponential backoff (`LlmHttpTransport`), transient error detection (HTTP 429/500s), per-attempt and total timeout ceilings, text sanitization, prompt construction (`LlmHealingPrompt.Build`), and the **Hallucination Guard**.

You need to supply four members: `IsAvailable`, `UnavailableErrorMessage`, `CreateRequest`, and `ExtractText`:

```csharp
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AutomationSandbox.LlmHealing;

public sealed class CustomApiHealingProvider : HttpLlmHealingProvider
{
    private readonly string _endpoint;
    private readonly string? _apiKey;

    public override bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
    protected override string UnavailableErrorMessage => "CUSTOM_API_KEY environment variable is not configured.";

    public CustomApiHealingProvider(
        string endpoint = "https://ai.internal.corp/v1/heal",
        string? apiKey = null,
        string? name = null)
        : base(
            defaultName: "InternalAi",
            defaultTimeout: TimeSpan.FromSeconds(15),
            defaultTotalTimeout: TimeSpan.FromSeconds(35),
            name: name)
    {
        _endpoint = endpoint;
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("CUSTOM_API_KEY");
    }

    protected override HttpRequestMessage CreateRequest(string prompt)
    {
        var payload = JsonSerializer.Serialize(new { prompt = prompt });
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(_apiKey))
        {
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        }
        return request;
    }

    protected override string ExtractText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        // Extracts the raw JSON text containing {"candidateId": "c0", "confidence": 0.95, "reasoning": "..."}
        return doc.RootElement.GetProperty("reply").GetString() ?? string.Empty;
    }
}
```

#### 2. Option B: Direct Implementation of `ILlmHealingProvider`

For non-HTTP models, local in-process models (e.g. ONNX runtime), or custom agent frameworks, implement `ILlmHealingProvider` directly:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;
using AutomationSandbox.UiModel;

public sealed class MyLocalModelProvider : ILlmHealingProvider
{
    public string Name => "MyLocalModel";
    public bool IsAvailable => true;

    public Task<LlmHealingResult> ResolveAsync(
        UiElementInfo expected,
        IReadOnlyList<CandidateScore> candidates,
        string? platform = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Evaluate your model on the shortlist:
        var chosenId = "c0"; // e.g. from local model inference

        // 2. Hallucination Guard Contract:
        // Must match an existing CandidateId from the provided candidate shortlist!
        var validCandidate = candidates.FirstOrDefault(c => c.CandidateId == chosenId);

        if (validCandidate == null)
        {
            return Task.FromResult(new LlmHealingResult
            {
                ProviderName = Name,
                Success = false,
                ErrorMessage = "Model returned candidateId outside shortlist."
            });
        }

        return Task.FromResult(new LlmHealingResult
        {
            ProviderName = Name,
            Success = true,
            MatchedCandidateId = validCandidate.CandidateId,
            Confidence = 0.92,
            Reasoning = "Matched control type and layout position."
        });
    }
}
```

#### 3. Hallucination Guard Contract & Quorum Rules

1. **Shortlist Candidate IDs (`c0`, `c1`, ...):** The engine passes a synthetic shortlist where candidates are identified solely by opaque candidate IDs (`c0`, `c1`, ...). The provider must return one of these IDs in `MatchedCandidateId`.
2. **Invalid IDs are Discarded:** If a provider hallucinates an ID that is not present in the candidate shortlist (or returns an unparseable response), its vote is marked invalid and discarded without failing other providers' votes.
3. **Quorum Requirement:** LLM healing acceptance requires at least **two** independent providers to agree on the same `CandidateId`. A single custom provider will have its verdict recorded in reports and telemetry, but will safely fallback to the heuristic outcome unless paired with a second independent provider.

#### 4. Registering Custom Providers

Pass custom providers directly to `SelfHealingResolver.ResolveAsync`, `SelfHealingEngine`, or `SelfHealingTestFixture`:

```csharp
// Combine custom provider with built-in discovered providers:
var providers = LlmProviderFactory.CreateConfiguredProviders().ToList();
providers.Add(new CustomApiHealingProvider(apiKey: "secret-token"));

// Use in SelfHealingEngine:
var engine = new SelfHealingEngine(repository, llmProviders: providers);

// Or in SelfHealingTestFixture:
var fixture = SelfHealingTestFixture.Create(new SelfHealingTestOptions
{
    LlmProviders = providers
});
```

### 📝 Text Models Beyond Locator Healing (`ITextModel`)

The providers above exist to pick one candidate id for locator healing, so their requests are shaped for that (a small output limit, a forced JSON answer on OpenAI-compatible endpoints). `ITextModel` (#562) is a separate, plain contract for any other bounded text task, such as drafting a document, triaging an issue or reviewing a draft: one `CompleteAsync` takes a `TextCompletionRequest` (prompt, maximum output tokens, whether JSON is expected) and returns a `TextCompletionResult`. A failure of any kind is returned in the result, never thrown, except cancellation by the caller.

`ChatCompletionsTextModel` covers any OpenAI-compatible endpoint (OpenAI, Mistral, NVIDIA NIM and others; plain http is accepted only for a loopback host so a key is never sent in clear text), `ClaudeTextModel` the Anthropic messages API and `GeminiTextModel` the Gemini Interactions API, which has no verified field for an output limit or a JSON switch, so those two request settings are not sent and the caller must validate the answer. All of them reuse the retry, backoff and deadline behaviour of `LlmHttpTransport`, and sanitise the prompt with `SensitiveDataSanitizer.Default` before it leaves the process unless you set your own `TextSanitizer`. `TextModelRegistry.CreateFromEnvironment()` builds the configured models from the same environment variables as `LlmProviderFactory` (Claude, Gemini, OpenAI, Mistral and NVIDIA so far; a model whose key or required model is missing is left out), looks a model up by name ignoring case, and rejects a duplicate name instead of replacing it.

**Choosing the model by label (#569).** `TextModelRouter.Route(taskKind, labels, registry, config)` picks the drafting model from a `Model::<name>` label and an optional independent reviewer from a `Review::<name>` label (names are compared ignoring case). The task kinds are `issue-triage`, `ci-failure` and `docs`. `TextModelRoutingConfig.Parse` reads a JSON file that lists which task kinds each model may serve and, optionally, a default model and reviewer per task kind; comments and trailing commas are accepted, and an unknown task kind, a duplicate model name or a default the model is not allowed to serve is rejected with a message that says what is wrong. The router never falls back silently: it stops, and lists every reason, on an unknown task kind, no `Model::` label and no default, two different `Model::` or `Review::` labels, a name that is not configured, a model without a key, a model the file does not list or does not allow for that task kind, and a reviewer that is the same model as the drafter (including a default reviewer that matches an explicitly chosen drafter). With an empty file nothing is restricted and a label is required. Routing reads only labels, names and configuration; no model is called.

**Deciding whether a task may start (#571).** `AgentIssueGate.Evaluate(issue, boardStatus)` is a pure, fail-closed function over an `IssueSnapshot` and the issue's project board status. An issue is eligible only when it is open, is not a pull request, carries `Agent::Allowed`, carries none of `Agent::Rejected`, `Agent::Working` and `Agent::In-Review`, carries exactly one `Task::<kind>` label that names a known task kind, and its board status is exactly `Ready`; a missing or unreadable board status is not eligible. `Agent::Needs-Human` does not block: a human adding `Agent::Allowed` again is the signal to continue. One reason is reported, in this order: `rejected`, `pull-request`, `closed`, `already-working`, `in-review`, `not-allowed`, `task-kind-missing`, `task-kind-ambiguous`, `task-kind-unknown`, `board-status-unknown`, `project-not-ready`. `GitHubIssueReader.ReadAsync(repository, number, token)` reads one issue from the GitHub REST API: the host is fixed, the repository name is validated before it reaches the URL, the optional token travels only in a header and never appears in an error, a client with no redirects is used by default, the answer is validated before use, and every failure is returned as a result. Changing labels is a later part of #559.

**Reading the board and checking an issue end to end (#575).** `ProjectBoardReader.ReadStatusAsync(repository, number, projectId, token)` reads the Status of one issue's card in a GitHub Projects (v2) board through the GraphQL API and tells three outcomes apart: `Found` (with the Status, or none), `NotOnBoard`, and `Failed`. It fails closed: the project id (`PVT_...`) and the repository name are validated before any request, the token travels only in the `Authorization` header and never appears in a query or an error, pagination has a hard limit, and when the issue is not found but some cards could not be read (the token cannot see them) the read is a failure, not "not on the board", because the issue might be hidden among them. `AgentIssueIntake.CheckAsync` reads the issue, then the board, then applies the gate, and every read failure ends as "not eligible" with its reason (`issue-unreadable` or `board-status-unknown`) instead of an exception. The board is read only when the issue could pass the gate, so a rejected, running or unlabelled issue costs one request. The token for the board needs read access to the project and to the issues on it.

**The first task: issue triage (#577).** `IssueTriage.RunAsync(issue, model)` asks a model once to classify an issue (`bug`, `feature`, `question`, `docs` or `other`), summarise it, list at most five questions for the author and suggest at most five labels. An issue title and body are written by anyone who can open an issue, so the text is treated as hostile. The prompt says it is data and puts it between `BEGIN_ISSUE_DATA` and `END_ISSUE_DATA`; control characters are removed, any copy of a marker inside the text is replaced so it cannot close the data section, and the body is cut at 6,000 characters in the prompt (the reader keeps at most 20,000). The model layer then masks credential-shaped strings and e-mail addresses before the request leaves the process. The answer is accepted only if it is exactly one JSON object with four valid fields; every length and count is capped, and nothing is repaired or silently dropped: a suggested label outside the allow list (default `bug`, `enhancement`, `documentation`, `question`) rejects the whole answer, and so does any protocol label (`Agent::`, `Model::`, `Review::`, `Task::`), even when the caller's own allow list names it, so a manipulated model cannot smuggle one in. The result is a proposal for a human; it changes no label and posts nothing, and its text is still untrusted output.

**An independent review that can only block (#579).** `TriagePipeline.RunAsync(issue, routing)` takes the result of `TextModelRouter.Route` (#569): the drafting model proposes, and when a reviewer was routed it checks the proposal. The reviewer sees the issue and the proposal as two separate data sections (`BEGIN_ISSUE_DATA`/`END_ISSUE_DATA` and `BEGIN_PROPOSAL_DATA`/`END_PROPOSAL_DATA`; the proposal is untrusted too, because a model wrote it after reading a hostile issue, so control characters are removed and any copy of a marker is replaced in both) and is asked only which statements and labels the issue does not support. Its answer must be exactly one JSON object with a `verdict` of `ok` or `issues` and at most ten findings of at most 200 characters; `ok` with findings, `issues` with none, or anything else is rejected. The outcome is `Accepted` (with who drafted it and, if a review ran, who reviewed it), `Blocked` (the reviewer's findings; the proposal is kept for a human to read, never accepted) or `Failed` (with a reason). A review that cannot be completed is never approval: a routing result that is not a success, a reviewer without a key (checked before any model is called, so no quota is spent), a reviewer with the same name as the drafter, a failing reviewer call and an invalid verdict all end as `Failed`. Each model is called at most once, and the reviewer only after the drafter produced a valid proposal. The reviewer never edits the proposal. Agreement between two models is not proof of correctness (see the consensus section above); the review catches a summary or a label the issue does not support, nothing more.

**One entry point from issue to reviewed proposal (#581).** `AgentTriageRun.RunAsync(repository, number, projectId, projectToken, registry, config)` ties the parts together in a fixed order: read the issue and its board card and apply the gate, route the models from the issue's labels, run the triage pipeline. It returns an `AgentRunReport` that says how far the run got: `NotEligible` (the gate's reason and any read error), `UnsupportedTask` (the issue passed the gate but its task kind has no implementation yet; only `issue-triage` has one), `RoutingFailed` (every routing problem) or `Completed` (the triage outcome: accepted, blocked or failed). No model is asked unless the gate passed and routing succeeded; the run only reads GitHub (the issue and the board card) and changes no label, comment or card; every ordinary failure is a report, never an exception, and only cancellation is thrown; and a report holds no token and no raw model answer.

---

## 🇹🇷 Türkçe Kılavuz

### 📊 Sağlayıcı Karşılaştırma Tablosu

| Sağlayıcı | Varsayılan Model | Kurulum / Değişken | Maliyet | Gizlilik |
| :--- | :--- | :--- | :---: | :---: |
| **Gemini** | `gemini-3.6-flash` | `GEMINI_API_KEY` | Çok Düşük | Bulut |
| **Claude** | `claude-haiku-4-5-20251001` | `ANTHROPIC_API_KEY` | Çok Düşük | Bulut |
| **OpenAI** | `gpt-4o-mini` | `OPENAI_API_KEY` | Çok Düşük | Bulut |
| **Grok (xAI)** | Açıkça belirtilir (`GROK_MODEL`) | `GROK_API_KEY` + `GROK_MODEL` | Düşük | Bulut |
| **Kimi (Moonshot)** | Açıkça belirtilir (`KIMI_MODEL`) | `KIMI_API_KEY` + `KIMI_MODEL` | Düşük | Bulut |
| **Groq** | Açıkça belirtilir (`GROQ_MODEL`) | `GROQ_API_KEY` + `GROQ_MODEL` | Düşük | Bulut |
| **OpenRouter** | Açıkça belirtilir (`OPENROUTER_MODEL`) | `OPENROUTER_API_KEY` + `OPENROUTER_MODEL` | Yönlendirilen modele göre değişir | Bulut |
| **Cloudflare Workers AI** | Açıkça belirtilir (`CLOUDFLARE_MODEL`) | Token + hesap kimliği + model | Günlük ücretsiz kota | Bulut |
| **Ollama** | `llama3.2` | `OLLAMA_HOST` / `OLLAMA_MODEL` | **%100 Ücretsiz (\$0)** | Varsayılan yerel; host'a bağlı |

> [!CAUTION]
> Sağlayıcı kurulumu aynı zamanda bir veri ifşası kararıdır. DOM/UI metni güvenilmeyen girdidir ve yapılandırılmış her sağlayıcı sınırlı prompt'u alır. Yerleşik maskeleme varsayılan olarak yaygın PII/secret desenlerini maskeler, ancak kapsamlı değildir ve devre dışı bırakılabilir (`SensitiveDataSanitizer.PassThrough`). Bulut veya uzak endpoint eklemeden önce [LLM Healing Güvenlik Modelini](llm-security-model.md) okuyun.

### 🏭 Dinamik Sağlayıcı Fabrikası (`LlmProviderFactory`)

`LlmProviderFactory.CreateConfiguredProviders()` ortamdaki anahtarları otomatik keşfeder ve kod değiştirmeden sağlayıcı listesini hazırlar:

> [!IMPORTANT]
> **Hiçbir sağlayıcı zorunlu değildir; bu anahtarların hiçbiri gerekli değildir.** Bilinen bir sağlayıcı yalnızca kendi `*_API_KEY` değeri varsa kurulur, aksi halde sessizce atlanır. Geçersiz `LLM_CUSTOM_PROVIDERS` girdisi ise kimlik bilgilerini açığa çıkarmayan bir tanı mesajıyla atlanır. Uzlaşma quorum'u **iki** bağımsız sağlayıcı gerektirdiği için pratik alt sınır iki anahtardır.
>
> **Bir sağlayıcı slotunu başka bir sağlayıcının uç noktasına yönlendirmeyin.** `OpenAiHealingProvider` herhangi bir OpenAI-uyumlu adresi kabul ettiği için `OPENAI_*` slotunu başka bir sağlayıcı için yeniden kullanmak cazip gelir. Aynı modeli çağıran iki slot, iki isimli tek bir oy demektir: aynı sistem oldukları için anlaşırlar ve aralarındaki uzlaşma anlamsızdır (#19). İkinci bir görüş gerekiyorsa ikinci bir sağlayıcı kullanın. `OPENAI_*` slotu gerçek bir OpenAI kimlik bilgisine ayrılmıştır; başka bir amaçla doldurmayın, boş bırakın.

Cloudflare için `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID` ve `CLOUDFLARE_MODEL` değerlerinin üçü de zorunludur. Tam yapılandırma `"Cloudflare"` adlı bir `OpenAiHealingProvider` oluşturur; herhangi biri eksikse bozuk bir uç nokta veya tahmini model üretmek yerine sağlayıcı atlanır.

Aynı kural Grok (`GROK_API_KEY` + `GROK_MODEL` $\rightarrow$ `"Grok"`), Kimi (`KIMI_API_KEY` + `KIMI_MODEL` $\rightarrow$ `"Kimi"`), Groq (`GROQ_API_KEY` + `GROQ_MODEL` $\rightarrow$ `"Groq"`), OpenRouter (`OPENROUTER_API_KEY` + `OPENROUTER_MODEL` $\rightarrow$ `"OpenRouter"`), Mistral (`MISTRAL_API_KEY` + `MISTRAL_MODEL` $\rightarrow$ `"Mistral"`), NVIDIA NIM (`NVIDIA_API_KEY` + `NVIDIA_MODEL` $\rightarrow$ `"Nvidia"`) ve Ollama Cloud (`OLLAMA_CLOUD_API_KEY` + `OLLAMA_CLOUD_MODEL` $\rightarrow$ `"OllamaCloud"`) için de geçerlidir; hepsi OpenAI uyumludur, ayrı sağlayıcı sınıfı gerektirmez.

> [!WARNING]
> `OLLAMA_CLOUD_*` ile `OLLAMA_*` bilinçli olarak ayrıdır ve karıştırılmamalıdır. Yerel değişkenler `localhost:11434` adresini hedefleyen bir sağlayıcı kurar; CI runner'ında orada çalışan bir daemon yoktur. Dolayısıyla `OLLAMA_MODEL`'i bir bulut modeline yönlendirmek, her istekte başarısız olan ama **yine de iki sağlayıcılı mutabakat eşiğine sayılan** bir sağlayıcı üretir — sağlayıcı eklemenin amacının tam tersi.

Ek OpenAI uyumlu uç noktaları yapılandırmak için `LLM_CUSTOM_PROVIDERS` değerine bir JSON dizisi verin:

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

Uzlaşma tek başına yeterli değildir: bir LLM-uzlaşma seçimi de sezgisel kazananın geçmesi gereken aynı **isim ve descendant/child-signature per-component kapılarını** geçmelidir (`SimilarityWeights.MinimumNameScoreWhenNamed`, `MinimumChildSignatureSimilarity`; bkz. `AGENTS.md` healing pipeline bölümü). #416 öncesinde bu iki kapı yalnızca sezgisel kazanan için hesaplanıyordu; bu yüzden iki sağlayıcının anlaştığı isim veya içerik uyuşmazlığı olan bir aday yine de güvenilir kabul edilebiliyordu. Artık her iki kapı da `HealResult.Source` fark etmeksizin aynı şekilde hesaplanıp uygulanır. Runner-up marj kapısı yalnızca sezgisel kalır — oy sayısı marjları ile yapısal skor marjları aynı ölçekte değildir (#268).

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

Nightly workflow bu kuralı bilinen `Desktop_AmbiguousSiblingTabs` senaryosunda, konfigüre edilmiş sağlayıcı havuzunun tamamına karşı çalışan canlı bir gate ile uygular (#388): en az `MinimumConsensusVotes` (2) sağlayıcının ham oyları shortlist içinde kalmalı, aynı ground-truth `CandidateId` değerini seçmeli ve `HealResult.AgreedProviders` içinde görünmelidir; aksi halde workflow başarısız olur. HTTP 429 veya zaman aşımıyla devre dışı kalan bir sağlayıcı kullanılabilir oy üretmez ve gate'i tek başına başarısız kılmaz; ancak ikiden az kullanılabilir oy yine başarısızlıktır — quorum o durumda kanıtlanamaz. Daha geniş çok-sağlayıcılı değerlendirme bunun ardından gate olmayan telemetri olarak çalışmaya devam eder. Hiç credential yoksa opt-in test temiz biçimde atlanır; bilinçli tek-sağlayıcı yapılandırması ise API çağrısı yapmadan başarısız olur. Üçüncü taraf erişilebilirliği release'i engellemesin diye gate yalnızca nightly workflow'dadır.

> **Reasoning modelleri.** Bazı OpenAI-uyumlu uç noktalar — özellikle "harmony" formatındaki Groq `openai/gpt-oss-120b` — düşünce zincirini standart-dışı bir `message.reasoning` alanında döndürür ve `message.content` içindeki JSON yanıt nesnesini bir dize ortasında kesebilir. `OpenAiHealingProvider`, `message.reasoning`'i `message.content` ile birlikte katar ve yanıt ayrıştırıcı, `reasoning` değerinin içinde kesilmiş bir nesneden `candidateId` / `confidence` değerlerini kurtarır (#378). Yine de temiz JSON döndüren bir model tercih edilir — telemetrinizde kurtarma yolu görünüyorsa `GROQ_MODEL`'i örn. `llama-3.3-70b-versatile` olarak ayarlayın.

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

### 🛠️ Özel Yapay Zeka Sağlayıcıları (`ILlmHealingProvider` / `HttpLlmHealingProvider`)

Kurum içi özel bir modeli, OpenAI uyumlu olmayan bir bulut sağlayıcısını veya kendi AI/akıl yürütme motorunuzu entegre etmek istiyorsanız, doğrudan `ILlmHealingProvider` arayüzünü uygulayabilir veya `HttpLlmHealingProvider` sınıfından türetebilirsiniz.

#### 1. A Seçeneği: `HttpLlmHealingProvider` Taban Sınıfını Genişletme (REST Uç Noktaları İçin Önerilen)

`HttpLlmHealingProvider`, parametre doğrulamalarını, `LlmHttpTransport` ile üstel geri çekilmeli yeniden denemeleri (retry), geçici HTTP 429/500 hata ayrımını, zaman aşımı tavanlarını, hassas veri maskelemeyi (`TextSanitizer`), prompt üretimini (`LlmHealingPrompt.Build`) ve **Hallucination Guard (Halüsinasyon Koruması)** mantığını otomatik olarak yönetir.

Dört üyeyi tanımlamanız gerekir: `IsAvailable`, `UnavailableErrorMessage`, `CreateRequest` ve `ExtractText`:

```csharp
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AutomationSandbox.LlmHealing;

public sealed class CustomApiHealingProvider : HttpLlmHealingProvider
{
    private readonly string _endpoint;
    private readonly string? _apiKey;

    public override bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
    protected override string UnavailableErrorMessage => "CUSTOM_API_KEY ortam değişkeni tanımlı değil.";

    public CustomApiHealingProvider(
        string endpoint = "https://ai.internal.corp/v1/heal",
        string? apiKey = null,
        string? name = null)
        : base(
            defaultName: "InternalAi",
            defaultTimeout: TimeSpan.FromSeconds(15),
            defaultTotalTimeout: TimeSpan.FromSeconds(35),
            name: name)
    {
        _endpoint = endpoint;
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("CUSTOM_API_KEY");
    }

    protected override HttpRequestMessage CreateRequest(string prompt)
    {
        var payload = JsonSerializer.Serialize(new { prompt = prompt });
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(_apiKey))
        {
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        }
        return request;
    }

    protected override string ExtractText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        // {"candidateId": "c0", "confidence": 0.95, "reasoning": "..."} içeren ham JSON metnini ayıklar
        return doc.RootElement.GetProperty("reply").GetString() ?? string.Empty;
    }
}
```

#### 2. B Seçeneği: Doğrudan `ILlmHealingProvider` Arayüzünü Uygulama

HTTP dışı modeller, işlem içi yerel modeller (ör. ONNX) veya özel ajan mimarileri için `ILlmHealingProvider` arayüzünü doğrudan uygulayabilirsiniz:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;
using AutomationSandbox.UiModel;

public sealed class MyLocalModelProvider : ILlmHealingProvider
{
    public string Name => "MyLocalModel";
    public bool IsAvailable => true;

    public Task<LlmHealingResult> ResolveAsync(
        UiElementInfo expected,
        IReadOnlyList<CandidateScore> candidates,
        string? platform = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Modelinizi aday listesi üzerinde çalıştırın:
        var chosenId = "c0"; // ör. yerel model çıkarımından gelen sonuç

        // 2. Hallucination Guard (Halüsinasyon Koruması) Sözleşmesi:
        // Yalnızca aday listesinde bulunan geçerli bir CandidateId döndürülmelidir!
        var validCandidate = candidates.FirstOrDefault(c => c.CandidateId == chosenId);

        if (validCandidate == null)
        {
            return Task.FromResult(new LlmHealingResult
            {
                ProviderName = Name,
                Success = false,
                ErrorMessage = "Model aday listesi dışından geçersiz bir CandidateId döndürdü."
            });
        }

        return Task.FromResult(new LlmHealingResult
        {
            ProviderName = Name,
            Success = true,
            MatchedCandidateId = validCandidate.CandidateId,
            Confidence = 0.92,
            Reasoning = "Kontrol türü ve düzen konumu eşleşti."
        });
    }
}
```

#### 3. Hallucination Guard ve Uzlaşma (Quorum) Kuralları

1. **Aday Listesi ID'leri (`c0`, `c1`, ...):** Motor, adayları yalnızca sentetik kimliklerle (`c0`, `c1`, ...) tanımlayan sınırlandırılmış bir liste iletir. Sağlayıcı `MatchedCandidateId` alanında bu ID'lerden birini döndürmelidir.
2. **Geçersiz ID'ler Elenir:** Bir sağlayıcı listede olmayan hayali bir ID döndürürse veya yanıt ayrıştırılamazsa, o sağlayıcının oyu geçersiz sayılıp elenir; diğer sağlayıcıların oyları bundan etkilenmez.
3. **Uzlaşma Şartı:** LLM iyileştirmesinin kabul edilmesi için en az **iki** bağımsız sağlayıcının aynı `CandidateId` üzerinde anlaşması gerekir. Tek bir özel sağlayıcı telemetriye kaydedilir fakat uzlaşma için ikinci bir sağlayıcıyla eşleştirilmedikçe güvenli olarak sezgisel sonuca geri döner.

#### 4. Özel Sağlayıcıları Kaydetme ve Kullanma

Özel sağlayıcıları `SelfHealingResolver.ResolveAsync`, `SelfHealingEngine` veya `SelfHealingTestFixture` içine doğrudan parametre olarak verebilirsiniz:

```csharp
// Yerleşik sağlayıcılar ile özel sağlayıcıyı birleştirme:
var providers = LlmProviderFactory.CreateConfiguredProviders().ToList();
providers.Add(new CustomApiHealingProvider(apiKey: "secret-token"));

// SelfHealingEngine ile kullanım:
var engine = new SelfHealingEngine(repository, llmProviders: providers);

// Veya SelfHealingTestFixture ile kullanım:
var fixture = SelfHealingTestFixture.Create(new SelfHealingTestOptions
{
    LlmProviders = providers
});
```

### 📝 Locator Onarımı Dışındaki Metin Modelleri (`ITextModel`)

Yukarıdaki sağlayıcılar locator onarımı için tek bir aday kimliği seçmek üzere vardır; bu yüzden istekleri buna göre biçimlenmiştir (küçük bir çıktı sınırı, OpenAI uyumlu uç noktalarda zorunlu JSON yanıtı). `ITextModel` (#562), bir belge taslağı yazmak, bir issue'yu sınıflandırmak ya da bir taslağı gözden geçirmek gibi sınırlı her metin görevi için ayrı, sade bir sözleşmedir: tek bir `CompleteAsync`, bir `TextCompletionRequest` (istem, en fazla çıktı token'ı, JSON beklenip beklenmediği) alır ve `TextCompletionResult` döndürür. Her tür hata, çağıranın iptali dışında, fırlatılmaz; sonucun içinde döndürülür.

`ChatCompletionsTextModel` OpenAI uyumlu her uç noktayı (OpenAI, Mistral, NVIDIA NIM ve diğerleri; anahtar açık metin olarak ağda gitmesin diye düz http yalnızca döngü adresi (loopback) için kabul edilir), `ClaudeTextModel` Anthropic messages API'sini, `GeminiTextModel` ise Gemini Interactions API'sini kapsar; Gemini isteğinde çıktı sınırı veya JSON anahtarı için doğrulanmış bir alan yoktur, bu yüzden bu iki ayar gönderilmez ve çağıran yanıtı kendisi doğrulamalıdır. Hepsi `LlmHttpTransport`'un yeniden deneme, geri çekilme ve süre sınırı davranışını kullanır; kendi `TextSanitizer`'ınızı vermedikçe istemi süreçten çıkmadan önce `SensitiveDataSanitizer.Default` ile temizler. `TextModelRegistry.CreateFromEnvironment()`, yapılandırılmış modelleri `LlmProviderFactory` ile aynı ortam değişkenlerinden kurar (şimdilik Claude, Gemini, OpenAI, Mistral ve NVIDIA; anahtarı ya da zorunlu modeli eksik olan model dışarıda bırakılır), bir modeli büyük/küçük harfe bakmadan ada göre bulur ve aynı adı değiştirmek yerine reddeder.

**Modeli etiketle seçmek (#569).** `TextModelRouter.Route(taskKind, labels, registry, config)`, taslağı yazan modeli `Model::<ad>` etiketinden, isteğe bağlı bağımsız denetçiyi `Review::<ad>` etiketinden seçer (adlar büyük/küçük harfe bakılmadan karşılaştırılır). Görev türleri `issue-triage`, `ci-failure` ve `docs`'tur. `TextModelRoutingConfig.Parse`, hangi modelin hangi görev türlerine bakabileceğini ve isteğe bağlı olarak görev türü başına varsayılan model ve denetçiyi listeleyen bir JSON dosyasını okur; yorum satırları ve sondaki virgüller kabul edilir, bilinmeyen bir görev türü, aynı model adının iki kez yazılması ya da modelin bakmasına izin verilmeyen bir varsayılan, neyin yanlış olduğunu söyleyen bir iletiyle reddedilir. Yönlendirici sessizce başka modele düşmez: bilinmeyen görev türünde, `Model::` etiketi ve varsayılan olmadığında, iki farklı `Model::` ya da `Review::` etiketinde, yapılandırılmamış bir adda, anahtarı olmayan bir modelde, dosyada listelenmeyen ya da o görev türüne izin verilmeyen bir modelde ve taslağı yazanla aynı olan bir denetçide (açıkça seçilmiş bir model ile eşleşen varsayılan denetçi dahil) durur ve tüm nedenleri listeler. Dosya boşsa hiçbir şey kısıtlanmaz ve etiket zorunludur. Yönlendirme yalnızca etiketleri, adları ve yapılandırmayı okur; hiçbir model çağrılmaz.

**Bir görevin başlayıp başlayamayacağına karar vermek (#571).** `AgentIssueGate.Evaluate(issue, boardStatus)`, bir `IssueSnapshot` ve issue'nun proje panosundaki durumu üzerinde çalışan, saf ve kapalı-hata (fail-closed) bir fonksiyondur. Bir issue yalnızca şu koşulların hepsi varsa uygundur: açık, pull request değil, `Agent::Allowed` etiketi var, `Agent::Rejected`, `Agent::Working` ve `Agent::In-Review` etiketlerinden hiçbiri yok, bilinen bir görev türünü adlandıran tam olarak bir `Task::<tür>` etiketi var ve pano durumu tam olarak `Ready`; pano durumu eksik ya da okunamıyorsa uygun değildir. `Agent::Needs-Human` engellemez: bir insanın `Agent::Allowed` etiketini yeniden eklemesi devam etme işaretidir. Tek bir neden, şu sırayla raporlanır: `rejected`, `pull-request`, `closed`, `already-working`, `in-review`, `not-allowed`, `task-kind-missing`, `task-kind-ambiguous`, `task-kind-unknown`, `board-status-unknown`, `project-not-ready`. `GitHubIssueReader.ReadAsync(repository, number, token)` GitHub REST API'sinden tek bir issue okur: sunucu adresi sabittir, depo adı URL'ye girmeden doğrulanır, isteğe bağlı token yalnızca başlıkta gider ve hiçbir hata iletisinde yer almaz, varsayılan olarak yönlendirme izlemeyen bir istemci kullanılır, yanıt kullanılmadan önce doğrulanır ve her hata bir sonuç olarak döndürülür. Etiket değiştirmek, #559'un sonraki bir parçasıdır.

**Panoyu okumak ve bir issue'yu uçtan uca denetlemek (#575).** `ProjectBoardReader.ReadStatusAsync(repository, number, projectId, token)`, bir issue'nun kartının GitHub Projects (v2) panosundaki Status değerini GraphQL API ile okur ve üç sonucu birbirinden ayırır: `Found` (Status ile ya da Status'suz), `NotOnBoard` ve `Failed`. Kapalı-hata ilkesiyle çalışır: proje kimliği (`PVT_...`) ve depo adı herhangi bir istekten önce doğrulanır, token yalnızca `Authorization` başlığında gider ve hiçbir sorguda ya da hata iletisinde görünmez, sayfalamanın kesin bir sınırı vardır ve issue bulunamadığında bazı kartlar okunamadıysa (token onları göremiyorsa) okuma "panoda değil" değil, hata sayılır; çünkü issue o kartların arasında gizli olabilir. `AgentIssueIntake.CheckAsync` önce issue'yu, sonra panoyu okur, sonra kapıyı uygular ve her okuma hatası bir istisna yerine nedeniyle birlikte "uygun değil" (`issue-unreadable` ya da `board-status-unknown`) olarak biter. Pano yalnızca issue kapıdan geçebilecekse okunur; bu yüzden reddedilmiş, çalışan ya da etiketsiz bir issue tek bir istek tutar. Pano için kullanılan token'ın projeyi ve üzerindeki issue'ları okuma yetkisi olmalıdır.

**İlk görev: issue sınıflandırma (#577).** `IssueTriage.RunAsync(issue, model)`, bir modelden tek seferde bir issue'yu sınıflandırmasını (`bug`, `feature`, `question`, `docs` ya da `other`), özetlemesini, yazara en fazla beş soru sormasını ve en fazla beş etiket önermesini ister. Issue başlığı ve gövdesi, issue açabilen herkes tarafından yazıldığı için düşmanca sayılır. İstem, metnin veri olduğunu söyler ve onu `BEGIN_ISSUE_DATA` ile `END_ISSUE_DATA` arasına koyar; kontrol karakterleri silinir, metnin içindeki her işaretleyici kopyası veri bölümünü kapatamasın diye değiştirilir ve gövde istemde 6.000 karakterde kesilir (okuyucu en fazla 20.000 karakter tutar). Model katmanı, istek süreçten çıkmadan önce kimlik bilgisi biçimindeki dizeleri ve e-posta adreslerini maskeler. Yanıt, yalnızca dört geçerli alana sahip tam olarak tek bir JSON nesnesiyse kabul edilir; her uzunluk ve sayı sınırlıdır, hiçbir şey onarılmaz ya da sessizce atılmaz: izin listesinde (varsayılan `bug`, `enhancement`, `documentation`, `question`) olmayan bir etiket önerisi tüm yanıtı reddettirir; çağıranın kendi izin listesi adlandırsa bile herhangi bir protokol etiketi (`Agent::`, `Model::`, `Review::`, `Task::`) de öyle; böylece manipüle edilmiş bir model bunlardan birini içeri sokamaz. Sonuç bir insan için bir öneridir; hiçbir etiketi değiştirmez, hiçbir şey yayımlamaz ve metni hâlâ güvenilmeyen çıktıdır.

**Yalnızca engelleyebilen bağımsız bir denetim (#579).** `TriagePipeline.RunAsync(issue, routing)`, `TextModelRouter.Route` (#569) sonucunu alır: taslağı yazan model öneriyi üretir ve bir denetçi yönlendirilmişse öneriyi o kontrol eder. Denetçi, issue'yu ve öneriyi iki ayrı veri bölümü olarak görür (`BEGIN_ISSUE_DATA`/`END_ISSUE_DATA` ve `BEGIN_PROPOSAL_DATA`/`END_PROPOSAL_DATA`; öneri de güvenilmezdir, çünkü düşmanca bir issue'yu okuduktan sonra bir model yazmıştır; bu yüzden ikisinde de kontrol karakterleri silinir ve her işaretleyici kopyası değiştirilir) ve ondan yalnızca issue'nun hangi ifadeleri ve etiketleri desteklemediğini söylemesi istenir. Yanıtı, `verdict` değeri `ok` ya da `issues` olan ve en fazla 200 karakterlik en fazla on bulgu içeren tam olarak tek bir JSON nesnesi olmalıdır; bulgulu `ok`, bulgusuz `issues` ya da başka her şey reddedilir. Sonuç `Accepted` (kim yazdı ve denetim yapıldıysa kim denetledi bilgisiyle), `Blocked` (denetçinin bulguları; öneri bir insanın okuması için saklanır, asla kabul edilmez) ya da `Failed` (nedeniyle) olur. Tamamlanamayan bir denetim asla onay sayılmaz: başarılı olmayan bir yönlendirme sonucu, anahtarı olmayan bir denetçi (hiçbir model çağrılmadan önce kontrol edilir, böylece kota harcanmaz), taslağı yazanla aynı adlı bir denetçi, başarısız bir denetçi çağrısı ve geçersiz bir karar `Failed` ile biter. Her model en fazla bir kez çağrılır ve denetçi yalnızca taslak yazan geçerli bir öneri ürettikten sonra çağrılır. Denetçi öneriyi asla düzenlemez. İki modelin hemfikir olması doğruluğun kanıtı değildir (yukarıdaki uzlaşma bölümüne bakın); denetim, issue'nun desteklemediği bir özeti ya da etiketi yakalar, fazlasını değil.

**Issue'dan denetlenmiş öneriye tek giriş noktası (#581).** `AgentTriageRun.RunAsync(repository, number, projectId, projectToken, registry, config)` parçaları sabit bir sırayla birbirine bağlar: issue'yu ve pano kartını oku ve kapıyı uygula, modelleri issue'nun etiketlerinden yönlendir, sınıflandırma hattını çalıştır. Çalışmanın ne kadar ilerlediğini söyleyen bir `AgentRunReport` döndürür: `NotEligible` (kapının nedeni ve varsa okuma hatası), `UnsupportedTask` (issue kapıdan geçti ama görev türünün henüz bir uygulaması yok; yalnızca `issue-triage` var), `RoutingFailed` (tüm yönlendirme sorunları) ya da `Completed` (sınıflandırma sonucu: kabul, engellendi ya da başarısız). Kapı geçilmedikçe ve yönlendirme başarılı olmadıkça hiçbir model çağrılmaz; çalışma GitHub'ı yalnızca okur (issue ve pano kartı) ve hiçbir etiketi, yorumu ya da kartı değiştirmez; her olağan hata bir istisna değil bir rapordur, yalnızca iptal fırlatılır; ve bir rapor ne token ne de ham model yanıtı içerir.
