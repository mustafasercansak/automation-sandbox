using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UiModel;

namespace LlmHealing
{
    // Unlike ClaudeHealingProvider, this has not been verified against a live Gemini
    // API key. The request/response shape matches Google's documented
    // generateContent REST endpoint, but the default model name can drift faster
    // than this file gets updated - override it via the constructor or the
    // GEMINI_MODEL environment variable if requests start failing.
    public sealed class GeminiHealingProvider : ILlmHealingProvider
    {
        private const string DefaultModel = "gemini-2.0-flash";

        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string _model;

        public string Name => "Gemini";

        public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

        public GeminiHealingProvider(HttpClient? httpClient = null, string? apiKey = null, string? model = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            _model = model ?? Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? DefaultModel;
        }

        public async Task<LlmHealingResult> ResolveAsync(UiElementInfo expected, UiElementInfo currentTree, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            if (!IsAvailable)
            {
                return new LlmHealingResult { ProviderName = Name, Success = false, ErrorMessage = "GEMINI_API_KEY is not set." };
            }

            var prompt = LlmHealingPrompt.Build(expected, currentTree);
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { responseMimeType = "application/json" },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return new LlmHealingResult
                    {
                        ProviderName = Name,
                        Success = false,
                        ErrorMessage = $"HTTP {(int)response.StatusCode}: {responseBody}",
                        Elapsed = stopwatch.Elapsed,
                    };
                }

                using var doc = JsonDocument.Parse(responseBody);
                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? "";

                var (automationId, confidence, reasoning) = LlmHealingPrompt.ParseResponse(text);

                return new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = true,
                    MatchedAutomationId = automationId,
                    Confidence = confidence,
                    Reasoning = reasoning,
                    Elapsed = stopwatch.Elapsed,
                };
            }
            catch (Exception ex)
            {
                return new LlmHealingResult { ProviderName = Name, Success = false, ErrorMessage = ex.Message, Elapsed = stopwatch.Elapsed };
            }
        }
    }
}
