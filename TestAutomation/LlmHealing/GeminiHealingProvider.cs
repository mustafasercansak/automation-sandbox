using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LlmHealing
{
    // The Interactions API superseded the older per-model generateContent endpoint,
    // which the Google documentation labels legacy. Its request/response shapes and
    // x-goog-api-key header are verified by mocked contract tests and live consensus
    // evaluation (August 2026). If requests start failing, re-check the current docs:
    // this surface has already changed shape once.

    public sealed class GeminiHealingProvider : HttpLlmHealingProvider
    {
        private const string ApiUrl = "https://generativelanguage.googleapis.com/v1beta/interactions";
        private const string DefaultModel = "gemini-3.6-flash";
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(35);
        public static readonly int DefaultMaxRetries = 2;

        private readonly string? _apiKey;
        private readonly string _model;

        public override bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
        protected override string UnavailableErrorMessage => "GEMINI_API_KEY is not set.";

        public GeminiHealingProvider(
            HttpClient? httpClient = null,
            string? apiKey = null,
            string? model = null,
            TimeSpan? timeout = null,
            TimeSpan? totalTimeout = null,
            string? name = null,
            int? maxRetries = null,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
            : base(
                defaultName: "Gemini",
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
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

            // GitHub Actions substitutes an unset repo Variable with an empty string, not a
            // missing env var - a plain ?? wouldn't fall through to DefaultModel in that case
            // (confirmed live: CI sent model: "" and Gemini 404'd on it). NullIfEmpty closes that gap.
            _model = NullIfEmpty(model) ?? NullIfEmpty(Environment.GetEnvironmentVariable("GEMINI_MODEL")) ?? DefaultModel;
        }

        protected override HttpRequestMessage CreateRequest(string prompt)
        {
            var requestBody = new { model = _model, input = prompt };

            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-goog-api-key", _apiKey);
            return request;
        }

        // Mirrors the Interactions API SDKs' "output_text" convenience property:
        // find the model_output step(s) and concatenate their text content blocks.
        protected override string ExtractText(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);
            var steps = doc.RootElement.GetProperty("steps");
            var textBuilder = new StringBuilder();
            foreach (var step in steps.EnumerateArray())
            {
                if (!step.TryGetProperty("type", out var stepTypeProp) || stepTypeProp.GetString() != "model_output")
                {
                    continue;
                }

                foreach (var contentItem in step.GetProperty("content").EnumerateArray())
                {
                    if (contentItem.TryGetProperty("type", out var contentTypeProp) && contentTypeProp.GetString() == "text"
                        && contentItem.TryGetProperty("text", out var textProp))
                    {
                        textBuilder.Append(textProp.GetString());
                    }
                }
            }

            return textBuilder.ToString();
        }
    }
}
