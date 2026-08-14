using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LlmHealing
{
    public sealed class OllamaHealingProvider : HttpLlmHealingProvider
    {
        private const string DefaultHost = "http://localhost:11434";
        private const string DefaultModel = "llama3.2";

        // Ollama runs locally on CPU/GPU where cold-start model loading can take longer
        // than lightweight cloud API roundtrips. 30s provides sufficient headroom.
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(70);
        public static readonly int DefaultMaxRetries = 2;

        private readonly string _host;
        private readonly string _model;
        private readonly bool _explicitlyConfigured;

        public override bool IsAvailable => _explicitlyConfigured ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OLLAMA_HOST")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OLLAMA_MODEL")) ||
            string.Equals(Environment.GetEnvironmentVariable("OLLAMA_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);

        protected override string UnavailableErrorMessage => "Ollama is not enabled or configured.";

        public OllamaHealingProvider(
            HttpClient? httpClient = null,
            string? host = null,
            string? model = null,
            TimeSpan? timeout = null,
            TimeSpan? totalTimeout = null,
            string? name = null,
            int? maxRetries = null,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
            : base(
                defaultName: "Ollama",
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
            _explicitlyConfigured = host != null || model != null || httpClient != null;

            // GitHub Actions substitutes an unset repo Variable with an empty string, not a
            // missing env var - a plain ?? wouldn't fall through to the defaults in that case
            // (see ClaudeHealingProvider/GeminiHealingProvider, which hit this live in CI).
            _host = (NullIfEmpty(host) ?? NullIfEmpty(Environment.GetEnvironmentVariable("OLLAMA_HOST")) ?? DefaultHost).TrimEnd('/');
            _model = NullIfEmpty(model) ?? NullIfEmpty(Environment.GetEnvironmentVariable("OLLAMA_MODEL")) ?? DefaultModel;
        }

        protected override HttpRequestMessage CreateRequest(string prompt)
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                stream = false,
            };

            var apiUrl = $"{_host}/api/chat";
            return new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
        }

        protected override string ExtractText(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("message", out var messageProp) &&
                messageProp.TryGetProperty("content", out var contentProp))
            {
                return contentProp.GetString() ?? "";
            }

            return "";
        }
    }
}
