using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UiModel;

namespace LlmHealing
{
    public sealed class OpenAiHealingProvider : ILlmHealingProvider
    {
        private const string DefaultApiUrl = "https://api.openai.com/v1/chat/completions";
        private const string DefaultModel = "gpt-4o-mini";
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
        private static readonly HttpClient SharedHttpClient = new();
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string _model;
        private readonly string _apiUrl;
        private readonly TimeSpan _timeout;

        public string Name => "OpenAI";
        public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
        public TimeSpan Timeout => _timeout;
        public string ApiUrl => _apiUrl;

        public OpenAiHealingProvider(
            HttpClient? httpClient = null,
            string? apiKey = null,
            string? model = null,
            TimeSpan? timeout = null,
            string? endpoint = null)
        {
            if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
            }

            _httpClient = httpClient ?? SharedHttpClient;
            _apiKey = apiKey
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");

            // GitHub Actions substitutes an unset repo Variable with an empty string, not a
            // missing env var - a plain ?? wouldn't fall through to DefaultModel in that case
            // (see ClaudeHealingProvider/GeminiHealingProvider, which hit this live in CI).
            _model = NullIfEmpty(model) ?? NullIfEmpty(Environment.GetEnvironmentVariable("OPENAI_MODEL")) ?? DefaultModel;
            _timeout = timeout ?? DefaultTimeout;

            var rawEndpoint = NullIfEmpty(endpoint)
                ?? NullIfEmpty(Environment.GetEnvironmentVariable("OPENAI_ENDPOINT"))
                ?? NullIfEmpty(Environment.GetEnvironmentVariable("OPENAI_BASE_URL"))
                ?? DefaultApiUrl;
            _apiUrl = NormalizeEndpoint(rawEndpoint);
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private static string NormalizeEndpoint(string endpoint)
        {
            var trimmed = endpoint.Trim().TrimEnd('/');
            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return $"{trimmed}/chat/completions";
        }

        public async Task<LlmHealingResult> ResolveAsync(
            UiElementInfo expected,
            IReadOnlyList<CandidateScore> candidates,
            string? platform = null,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            if (!IsAvailable)
            {
                return new LlmHealingResult { ProviderName = Name, Success = false, ErrorMessage = "OPENAI_API_KEY is not set." };
            }

            var prompt = LlmHealingPrompt.Build(expected, candidates, platform);
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.0,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

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

                var text = ExtractText(responseBody);
                var (candidateId, confidence, reasoning) = LlmHealingPrompt.ParseResponse(text);

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

        private static string ExtractText(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var messageProp) &&
                    messageProp.TryGetProperty("content", out var contentProp))
                {
                    return contentProp.GetString() ?? "";
                }
            }

            return "";
        }
    }
}
