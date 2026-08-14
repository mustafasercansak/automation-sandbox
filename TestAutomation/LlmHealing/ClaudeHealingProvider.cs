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

        // Cheapest/fastest Claude tier by default - this is a small structured-pick task,
        // not one that benefits from Opus-level reasoning. Override with the model
        // constructor parameter or ANTHROPIC_MODEL if a stronger model is ever warranted.
        private const string DefaultModel = "claude-haiku-4-5-20251001";
        private static readonly HttpClient SharedHttpClient = new();
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string _model;
        public string Name => "Claude";
        public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
        public ClaudeHealingProvider(HttpClient? httpClient = null, string? apiKey = null, string? model = null)
        {
            _httpClient = httpClient ?? SharedHttpClient;
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

            // GitHub Actions substitutes an unset repo Variable with an empty string, not a
            // missing env var - a plain ?? wouldn't fall through to DefaultModel in that case
            // (confirmed live: CI sent model: "" and the API 404'd). NullIfEmpty closes that gap.
            _model = NullIfEmpty(model) ?? NullIfEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")) ?? DefaultModel;
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        public async Task<LlmHealingResult> ResolveAsync(
            UiElementInfo expected,
            IReadOnlyList<CandidateScore> candidates,
            string? platform = null,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            if (!IsAvailable)
            {
                return new LlmHealingResult { ProviderName = Name, Success = false, ErrorMessage = "ANTHROPIC_API_KEY is not set." };
            }

            var prompt = LlmHealingPrompt.Build(expected, candidates, platform);
            var requestBody = new
            {
                model = _model,
                max_tokens = 1024,

                // Some Claude models (e.g. Opus 5, if a caller overrides DefaultModel to it)
                // think by default even with no "thinking" field set, which would put a
                // thinking block before the text block in the response - ExtractText already
                // skips non-text blocks, but disabling it here avoids paying for reasoning
                // this small structured-pick task doesn't need, on any model.
                thinking = new { type = "disabled" },
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

                var text = ExtractText(responseBody);
                var (candidateId, confidence, reasoning) = LlmHealingPrompt.ParseResponse(text);

                // MatchedAutomationId is informational only (may be null/empty even on a
                // legitimate match) - the resolver looks the candidate up by MatchedCandidateId.
                var matched = candidates.FirstOrDefault(c => c.CandidateId == candidateId);
                return new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = true,
                    MatchedCandidateId = candidateId,
                    MatchedAutomationId = matched?.Candidate.AutomationId,
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

        // Finds the first text block in the response rather than assuming content[0]
        // is text - a thinking block (when thinking isn't disabled) or a
        // server-tool-use block would otherwise come first.

        private static string ExtractText(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);
            foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "text"
                    && block.TryGetProperty("text", out var textProp))
                {
                    return textProp.GetString() ?? "";
                }
            }

            return "";
        }
    }
}
