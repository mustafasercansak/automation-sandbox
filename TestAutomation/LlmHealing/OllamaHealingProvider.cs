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
    public sealed class OllamaHealingProvider : ILlmHealingProvider
    {
        private const string DefaultHost = "http://localhost:11434";
        private const string DefaultModel = "llama3.2";

        // Ollama runs locally on CPU/GPU where cold-start model loading can take longer
        // than lightweight cloud API roundtrips. 30s provides sufficient headroom.
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
        private static readonly HttpClient SharedHttpClient = new();
        private readonly HttpClient _httpClient;
        private readonly string _host;
        private readonly string _model;
        private readonly bool _explicitlyConfigured;
        private readonly TimeSpan _timeout;

        private readonly string _name;

        public string Name => _name;

        public bool IsAvailable => _explicitlyConfigured ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OLLAMA_HOST")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OLLAMA_MODEL")) ||
            string.Equals(Environment.GetEnvironmentVariable("OLLAMA_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);

        public TimeSpan Timeout => _timeout;

        public OllamaHealingProvider(HttpClient? httpClient = null, string? host = null, string? model = null, TimeSpan? timeout = null, string? name = null)
        {
            if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
            }

            _httpClient = httpClient ?? SharedHttpClient;
            _explicitlyConfigured = host != null || model != null || httpClient != null;

            // GitHub Actions substitutes an unset repo Variable with an empty string, not a
            // missing env var - a plain ?? wouldn't fall through to the defaults in that case
            // (see ClaudeHealingProvider/GeminiHealingProvider, which hit this live in CI).
            _host = (NullIfEmpty(host) ?? NullIfEmpty(Environment.GetEnvironmentVariable("OLLAMA_HOST")) ?? DefaultHost).TrimEnd('/');
            _model = NullIfEmpty(model) ?? NullIfEmpty(Environment.GetEnvironmentVariable("OLLAMA_MODEL")) ?? DefaultModel;
            _timeout = timeout ?? DefaultTimeout;
            _name = NullIfEmpty(name?.Trim()) ?? "Ollama";
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
                return new LlmHealingResult { ProviderName = Name, Success = false, ErrorMessage = "Ollama is not enabled or configured." };
            }

            var prompt = LlmHealingPrompt.Build(expected, candidates, platform);
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
            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };

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
            if (doc.RootElement.TryGetProperty("message", out var messageProp) &&
                messageProp.TryGetProperty("content", out var contentProp))
            {
                return contentProp.GetString() ?? "";
            }

            return "";
        }
    }
}
