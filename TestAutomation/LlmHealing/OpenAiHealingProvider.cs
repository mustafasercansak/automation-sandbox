using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LlmHealing
{
    public sealed class OpenAiHealingProvider : HttpLlmHealingProvider
    {
        private const string DefaultApiUrl = "https://api.openai.com/v1/chat/completions";
        private const string DefaultModel = "gpt-4o-mini";
        private const int DefaultMaxOutputTokens = 1024;
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(35);
        public static readonly int DefaultMaxRetries = 2;

        private readonly string? _apiKey;
        private readonly string _model;
        private readonly string _apiUrl;
        private readonly bool _requestJsonResponse;
        private readonly int _maxOutputTokens;

        public override bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
        protected override string UnavailableErrorMessage => "OPENAI_API_KEY is not set.";
        public string ApiUrl => _apiUrl;

        // name matters more here than on the other providers: this one talks to any
        // OpenAI-compatible endpoint, so a consensus run can legitimately hold several
        // instances of it (Groq, Cerebras, OpenRouter). Leaving them all called "OpenAI"
        // would make the votes in HealResult.AgreedProviders indistinguishable.
        public OpenAiHealingProvider(
            HttpClient? httpClient = null,
            string? apiKey = null,
            string? model = null,
            TimeSpan? timeout = null,
            TimeSpan? totalTimeout = null,
            string? endpoint = null,
            string? name = null,
            bool requestJsonResponse = false,
            int? maxOutputTokens = null,
            int? maxRetries = null,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
            : base(
                defaultName: "OpenAI",
                defaultTimeout: DefaultTimeout,
                defaultTotalTimeout: DefaultTotalTimeout,
                defaultMaxRetries: DefaultMaxRetries,
                httpClient: httpClient,
                timeout: timeout,
                totalTimeout: totalTimeout,
                name: name,
                maxRetries: maxRetries,
                delayAsync: delayAsync)
        {
            // NullIfEmpty on every link, not just the last: GitHub Actions substitutes an unset
            // secret with an empty string rather than omitting the variable, and a plain ?? only
            // falls through on null. `llm-smoke.yml` declares OPENAI_API_KEY (unset, so "") and
            // GITHUB_TOKEN (set), which left _apiKey = "" and made IsAvailable false - the
            // GITHUB_TOKEN fallback was unreachable in exactly the workflow written to use it.
            // The same trap is handled below for the model; the key chain had been missed.
            _apiKey = NullIfEmpty(apiKey)
                ?? NullIfEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                ?? NullIfEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN"));

            // GitHub Actions substitutes an unset repo Variable with an empty string, not a
            // missing env var - a plain ?? wouldn't fall through to DefaultModel in that case
            // (see ClaudeHealingProvider/GeminiHealingProvider, which hit this live in CI).
            _model = NullIfEmpty(model) ?? NullIfEmpty(Environment.GetEnvironmentVariable("OPENAI_MODEL")) ?? DefaultModel;
            _requestJsonResponse = requestJsonResponse;
            _maxOutputTokens = maxOutputTokens ?? DefaultMaxOutputTokens;

            var rawEndpoint = NullIfEmpty(endpoint)
                ?? NullIfEmpty(Environment.GetEnvironmentVariable("OPENAI_ENDPOINT"))
                ?? NullIfEmpty(Environment.GetEnvironmentVariable("OPENAI_BASE_URL"))
                ?? DefaultApiUrl;
            _apiUrl = NormalizeEndpoint(rawEndpoint);
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            var trimmed = endpoint.Trim().TrimEnd('/');
            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return $"{trimmed}/chat/completions";
        }

        protected override HttpRequestMessage CreateRequest(string prompt)
        {
            object requestBody = _requestJsonResponse
                ? new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.0,
                    max_tokens = _maxOutputTokens,
                    response_format = new { type = "json_object" },
                }
                : new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.0,
                    max_tokens = _maxOutputTokens,
                };

            var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            return request;
        }

        protected override string ExtractText(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0 || !choices[0].TryGetProperty("message", out var messageProp))
            {
                return "";
            }

            var content = messageProp.TryGetProperty("content", out var contentProp)
                ? contentProp.GetString() ?? ""
                : "";

            // Reasoning models (e.g. Groq's openai/gpt-oss-120b in the harmony format, #378) can
            // emit a truncated JSON object in `content` and carry the usable answer - or its own
            // JSON block - in a non-standard sibling `reasoning` field. Fold both in so the
            // response scanner and the truncated-object repair see whatever is actually present.
            var reasoning = messageProp.TryGetProperty("reasoning", out var reasoningProp)
                            && reasoningProp.ValueKind == JsonValueKind.String
                ? reasoningProp.GetString() ?? ""
                : "";

            if (string.IsNullOrEmpty(reasoning))
            {
                return content;
            }

            return string.IsNullOrEmpty(content) ? reasoning : content + "\n" + reasoning;
        }
    }
}
