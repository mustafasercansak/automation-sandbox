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
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(35);
        public static readonly int DefaultMaxRetries = 2;
        private static readonly HttpClient SharedHttpClient = new();
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string _model;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _totalTimeout;
        private readonly int _maxRetries;
        private readonly Func<TimeSpan, CancellationToken, Task>? _delayAsync;
        private readonly string _name;
        public string Name => _name;
        public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
        public TimeSpan Timeout => _timeout;
        public TimeSpan TotalTimeout => _totalTimeout;
        public int MaxRetries => _maxRetries;

        public ClaudeHealingProvider(
            HttpClient? httpClient = null,
            string? apiKey = null,
            string? model = null,
            TimeSpan? timeout = null,
            TimeSpan? totalTimeout = null,
            string? name = null,
            int? maxRetries = null,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
        {
            if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
            }

            if (totalTimeout.HasValue && totalTimeout.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(totalTimeout), "TotalTimeout must be greater than zero.");
            }

            if (maxRetries.HasValue && maxRetries.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRetries), "MaxRetries must be non-negative.");
            }

            _httpClient = httpClient ?? SharedHttpClient;
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

            // GitHub Actions substitutes an unset repo Variable with an empty string, not a
            // missing env var - a plain ?? wouldn't fall through to DefaultModel in that case
            // (confirmed live: CI sent model: "" and the API 404'd). NullIfEmpty closes that gap.
            _model = NullIfEmpty(model) ?? NullIfEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")) ?? DefaultModel;
            _timeout = timeout ?? DefaultTimeout;
            _totalTimeout = totalTimeout ?? (timeout.HasValue ? TimeSpan.FromSeconds(Math.Max(DefaultTotalTimeout.TotalSeconds, _timeout.TotalSeconds * 2.5)) : DefaultTotalTimeout);
            if (_totalTimeout < _timeout)
            {
                throw new ArgumentException("TotalTimeout cannot be less than per-attempt Timeout.", nameof(totalTimeout));
            }

            _maxRetries = maxRetries ?? DefaultMaxRetries;
            _delayAsync = delayAsync;
            _name = NullIfEmpty(name?.Trim()) ?? "Claude";
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
                return new LlmHealingResult { ProviderName = Name, Success = false, ErrorMessage = "ANTHROPIC_API_KEY is not set.", AttemptCount = 0 };
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

            HttpRequestMessage CreateRequest()
            {
                var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("x-api-key", _apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
                return request;
            }

            var httpResponse = await LlmHttpTransport.SendWithRetryAsync(
                _httpClient,
                CreateRequest,
                _timeout,
                _totalTimeout,
                _maxRetries,
                _delayAsync,
                cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccess)
            {
                return new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = false,
                    ErrorMessage = httpResponse.ErrorMessage,
                    Elapsed = stopwatch.Elapsed,
                    AttemptCount = httpResponse.AttemptsMade,
                };
            }

            try
            {
                var text = ExtractText(httpResponse.Body ?? "");
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
                    AttemptCount = httpResponse.AttemptsMade,
                };
            }
            catch (Exception ex)
            {
                return new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Elapsed = stopwatch.Elapsed,
                    AttemptCount = httpResponse.AttemptsMade,
                };
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
