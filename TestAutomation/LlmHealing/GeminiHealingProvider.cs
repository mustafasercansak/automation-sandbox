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
    // Verified against Google's Gemini API documentation as of August 2026 (via
    // WebFetch, not a live call): the Interactions API superseded the older
    // per-model generateContent endpoint, which the docs now label legacy. Request
    // shape, response shape, and the x-goog-api-key auth header below reflect that
    // migration. Still not exercised against a live key - if requests start
    // failing, re-check the current docs before assuming this file is stale in the
    // usual way, since this surface has already changed shape once.

    public sealed class GeminiHealingProvider : ILlmHealingProvider
    {
        private const string ApiUrl = "https://generativelanguage.googleapis.com/v1beta/interactions";
        private const string DefaultModel = "gemini-3.6-flash";
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
        private static readonly HttpClient SharedHttpClient = new();
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string _model;
        private readonly TimeSpan _timeout;
        public string Name => "Gemini";
        public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
        public TimeSpan Timeout => _timeout;

        public GeminiHealingProvider(HttpClient? httpClient = null, string? apiKey = null, string? model = null, TimeSpan? timeout = null)
        {
            if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
            }

            _httpClient = httpClient ?? SharedHttpClient;
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

            // GitHub Actions substitutes an unset repo Variable with an empty string, not a
            // missing env var - a plain ?? wouldn't fall through to DefaultModel in that case
            // (confirmed live: CI sent model: "" and Gemini 404'd on it). NullIfEmpty closes that gap.
            _model = NullIfEmpty(model) ?? NullIfEmpty(Environment.GetEnvironmentVariable("GEMINI_MODEL")) ?? DefaultModel;
            _timeout = timeout ?? DefaultTimeout;
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
                return new LlmHealingResult { ProviderName = Name, Success = false, ErrorMessage = "GEMINI_API_KEY is not set." };
            }

            var prompt = LlmHealingPrompt.Build(expected, candidates, platform);
            var requestBody = new { model = _model, input = prompt };
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-goog-api-key", _apiKey);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);

            try
            {
                using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
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

                var text = ExtractOutputText(responseBody);
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = false,
                    ErrorMessage = $"Request timed out after {_timeout.TotalSeconds:F0}s.",
                    Elapsed = stopwatch.Elapsed,
                };
            }
            catch (OperationCanceledException)
            {
                return new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = false,
                    ErrorMessage = "Operation was canceled.",
                    Elapsed = stopwatch.Elapsed,
                };
            }
            catch (Exception ex)
            {
                return new LlmHealingResult { ProviderName = Name, Success = false, ErrorMessage = ex.Message, Elapsed = stopwatch.Elapsed };
            }
        }

        // Mirrors the Interactions API SDKs' "output_text" convenience property:
        // find the model_output step(s) and concatenate their text content blocks.

        private static string ExtractOutputText(string responseBody)
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
