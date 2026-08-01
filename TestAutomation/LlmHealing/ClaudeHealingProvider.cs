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
    // Raw HTTP against the Messages API rather than the official Anthropic SDK:
    // this project targets netstandard2.0 (so it can also be referenced by
    // net48 test projects), and raw HTTP keeps this provider symmetric with
    // GeminiHealingProvider, which has no first-party .NET SDK to reach for.
    public sealed class ClaudeHealingProvider : ILlmHealingProvider
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string DefaultModel = "claude-opus-5";

        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string _model;

        public string Name => "Claude";

        public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

        public ClaudeHealingProvider(HttpClient? httpClient = null, string? apiKey = null, string? model = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            _model = model ?? DefaultModel;
        }

        public async Task<LlmHealingResult> ResolveAsync(UiElementInfo expected, UiElementInfo currentTree, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            if (!IsAvailable)
            {
                return new LlmHealingResult { ProviderName = Name, Success = false, ErrorMessage = "ANTHROPIC_API_KEY is not set." };
            }

            var prompt = LlmHealingPrompt.Build(expected, currentTree);

            var requestBody = new
            {
                model = _model,
                max_tokens = 1024,
                output_config = new { effort = "low" },
                messages = new[] { new { role = "user", content = prompt } },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

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
                var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
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
