using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntentAutomation
{
    // Natural-language-to-full-scenario planning: DeterministicIntentPlanner only
    // recognizes a fixed vocabulary of verbs (save/submit/create/...), so a goal phrased
    // differently ("finish the order", "kaydı tamamla") produces an incomplete plan and
    // sets RequiresReview instead of guessing. LlmIntentPlanner asks a model to read the
    // goal directly, but never trusts its output blindly: any structurally invalid
    // response (bad ActionType, empty steps) - same as no API key or an HTTP failure -
    // degrades to the deterministic planner's result rather than surfacing malformed
    // steps to the pipeline.

    public sealed class LlmIntentPlanner : IIntentPlanner
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";

        // Same cheapest/fastest tier as ClaudeHealingProvider - this is a small
        // structured-planning task, not one that benefits from a flagship model.
        private const string DefaultModel = "claude-haiku-4-5-20251001";
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(35);
        public static readonly int DefaultMaxRetries = 2;
        public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMilliseconds(200);

        [ThreadStatic]
        private static Random? _threadRandom;
        private static Random GetRandom() => _threadRandom ??= new Random();

        private static readonly HttpClient SharedHttpClient = new();
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string _model;
        private readonly IIntentPlanner _fallback;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _totalTimeout;
        private readonly int _maxRetries;
        private readonly Func<TimeSpan, CancellationToken, Task>? _delayAsync;

        public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
        public TimeSpan Timeout => _timeout;
        public TimeSpan TotalTimeout => _totalTimeout;
        public int MaxRetries => _maxRetries;

        public LlmIntentPlanner(
            HttpClient? httpClient = null,
            string? apiKey = null,
            string? model = null,
            IIntentPlanner? fallback = null,
            TimeSpan? timeout = null,
            TimeSpan? totalTimeout = null,
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
            _model = NullIfEmpty(model) ?? NullIfEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")) ?? DefaultModel;
            _fallback = fallback ?? new DeterministicIntentPlanner();
            _timeout = timeout ?? DefaultTimeout;
            _totalTimeout = totalTimeout ?? (timeout.HasValue ? TimeSpan.FromSeconds(Math.Max(DefaultTotalTimeout.TotalSeconds, _timeout.TotalSeconds * 2.5)) : DefaultTotalTimeout);
            if (_totalTimeout < _timeout)
            {
                throw new ArgumentException("TotalTimeout cannot be less than per-attempt Timeout.", nameof(totalTimeout));
            }

            _maxRetries = maxRetries ?? DefaultMaxRetries;
            _delayAsync = delayAsync;
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        // Synchronous IIntentPlanner conformance for drop-in use with IntentAutomationPipeline.Run.
        // Safe to block on here: this project has no sync-context (ASP.NET classic style) callers.
        public IntentPlanningResult Plan(IntentPlanningRequest request) => PlanAsync(request).GetAwaiter().GetResult();

        public async Task<IntentPlanningResult> PlanAsync(IntentPlanningRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Goal))
            {
                throw new ArgumentException("Goal must not be empty.", nameof(request));
            }

            if (!IsAvailable)
            {
                return Degrade(request, "ANTHROPIC_API_KEY is not set; degraded to DeterministicIntentPlanner.");
            }

            var stopwatch = Stopwatch.StartNew();
            var prompt = LlmIntentPlanningPrompt.Build(request);
            var requestBody = new
            {
                model = _model,
                max_tokens = 2048,
                thinking = new { type = "disabled" },
                output_config = new { effort = "low" },
                messages = new[] { new { role = "user", content = prompt } },
            };

            var delayAsync = _delayAsync ?? Task.Delay;
            var totalAttemptsAllowed = _maxRetries + 1;
            var attemptsMade = 0;

            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overallCts.CancelAfter(_totalTimeout);

            // Only the status code outlives an attempt - the body is already copied out as a
            // string. Keeping the HttpResponseMessage itself would mean holding an undisposed
            // response (and its content stream) across the retry loop.
            System.Net.HttpStatusCode? lastStatusCode = null;
            string? lastResponseBody = null;
            string? lastExceptionMessage = null;

            for (var attempt = 1; attempt <= totalAttemptsAllowed; attempt++)
            {
                if (overallCts.IsCancellationRequested)
                {
                    break;
                }

                // Counted only once the attempt is actually going to be made: incrementing
                // before the check above would report a request that was never sent.
                attemptsMade = attempt;

                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
                attemptCts.CancelAfter(_timeout);

                try
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
                    };
                    httpRequest.Headers.Add("x-api-key", _apiKey);
                    httpRequest.Headers.Add("anthropic-version", "2023-06-01");

                    using var response = await _httpClient.SendAsync(httpRequest, attemptCts.Token).ConfigureAwait(false);
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var text = ExtractText(responseBody);
                        var scenario = LlmIntentPlanningPrompt.ParseScenario(text, request);
                        return new IntentPlanningResult { Scenario = scenario };
                    }

                    lastStatusCode = response.StatusCode;
                    lastResponseBody = responseBody;

                    // Non-transient errors (400, 401, 403, 404) fail fast without retrying
                    if (!IsTransient(response.StatusCode))
                    {
                        return Degrade(request, $"LLM planning HTTP {(int)response.StatusCode} after {attemptsMade} attempt(s) ({stopwatch.Elapsed.TotalMilliseconds:F0}ms); degraded to DeterministicIntentPlanner.");
                    }

                    // Transient status code: check Retry-After header
                    var retryAfter = ParseRetryAfter(response);
                    if (retryAfter.HasValue && retryAfter.Value > MaxRetryAfter)
                    {
                        return Degrade(request, $"LLM planning HTTP {(int)response.StatusCode}: Retry-After of {retryAfter.Value.TotalSeconds:F0}s exceeds maximum delay threshold ({MaxRetryAfter.TotalSeconds:F0}s); degraded to DeterministicIntentPlanner.");
                    }

                    if (attempt < totalAttemptsAllowed)
                    {
                        var backoff = retryAfter ?? CalculateExponentialBackoff(attempt);
                        await delayAsync(backoff, overallCts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Degrade(request, "LLM planning operation was canceled; degraded to DeterministicIntentPlanner.");
                }
                catch (OperationCanceledException) when (overallCts.IsCancellationRequested)
                {
                    return Degrade(request, $"LLM planning timed out after {_totalTimeout.TotalSeconds:F0}s; degraded to DeterministicIntentPlanner.");
                }
                catch (OperationCanceledException) when (attemptCts.IsCancellationRequested)
                {
                    if (attempt < totalAttemptsAllowed && !overallCts.IsCancellationRequested)
                    {
                        var backoff = CalculateExponentialBackoff(attempt);
                        try
                        {
                            await delayAsync(backoff, overallCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return Degrade(request, $"LLM planning timed out after {_totalTimeout.TotalSeconds:F0}s; degraded to DeterministicIntentPlanner.");
                        }
                    }
                    else
                    {
                        return Degrade(request, $"LLM planning timed out after {_timeout.TotalSeconds:F0}s; degraded to DeterministicIntentPlanner.");
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastExceptionMessage = ex.Message;
                    if (attempt < totalAttemptsAllowed && !overallCts.IsCancellationRequested)
                    {
                        var backoff = CalculateExponentialBackoff(attempt);
                        try
                        {
                            await delayAsync(backoff, overallCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return Degrade(request, $"LLM planning timed out after {_totalTimeout.TotalSeconds:F0}s; degraded to DeterministicIntentPlanner.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Degrade(request, $"LLM planning failed ({ex.Message}); degraded to DeterministicIntentPlanner.");
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Degrade(request, "LLM planning operation was canceled; degraded to DeterministicIntentPlanner.");
            }

            if (overallCts.IsCancellationRequested)
            {
                return Degrade(request, $"LLM planning timed out after {_totalTimeout.TotalSeconds:F0}s; degraded to DeterministicIntentPlanner.");
            }

            if (lastStatusCode.HasValue)
            {
                return Degrade(request, $"LLM planning HTTP {(int)lastStatusCode.Value} after {attemptsMade} attempt(s) ({stopwatch.Elapsed.TotalMilliseconds:F0}ms); degraded to DeterministicIntentPlanner.");
            }

            return Degrade(request, $"LLM planning failed ({lastExceptionMessage ?? "maximum retries exceeded"}); degraded to DeterministicIntentPlanner.");
        }

        private static bool IsTransient(System.Net.HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return code == 429 || code == 500 || code == 502 || code == 503 || code == 504;
        }

        private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter is null) return null;
            if (retryAfter.Delta.HasValue) return retryAfter.Delta.Value;
            if (retryAfter.Date.HasValue)
            {
                var diff = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
            }
            return null;
        }

        private static TimeSpan CalculateExponentialBackoff(int attempt)
        {
            var baseMs = DefaultInitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            var jitterMs = GetRandom().Next(0, 50);
            var totalMs = Math.Min(2000, baseMs + jitterMs);
            return TimeSpan.FromMilliseconds(totalMs);
        }

        private IntentPlanningResult Degrade(IntentPlanningRequest request, string diagnostic)
        {
            var fallbackResult = _fallback.Plan(request);
            fallbackResult.Diagnostics.Insert(0, diagnostic);
            return fallbackResult;
        }

        // Finds the first text block in the response rather than assuming content[0] is
        // text - a thinking block (when thinking isn't disabled) would otherwise come first.
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
