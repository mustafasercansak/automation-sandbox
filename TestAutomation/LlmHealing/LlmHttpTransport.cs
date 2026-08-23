using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LlmHealing
{
    // Shared resilience transport for LLM HTTP calls:
    // 1. Automatic retry with exponential backoff and thread-safe jitter for transient HTTP errors (429, 500, 502, 503, 504) and HttpRequestException.
    // 2. Immediate fail-fast on non-transient errors (400, 401, 403, 404).
    // 3. Retry-After header parsing with a 10s ceiling: Retry-After > 10s indicates quota exhaustion and fails fast.
    // 4. Dual timeout: per-attempt timeout (_timeout) and total ceiling (_totalTimeout).
    // 5. Injectable delayAsync hook for sub-millisecond unit testing.
    public static class LlmHttpTransport
    {
        public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMilliseconds(200);

        [ThreadStatic]
        private static Random? _threadRandom;
        private static Random GetRandom() => _threadRandom ??= new Random();

        public static bool IsTransient(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return code == 429 || code == 500 || code == 502 || code == 503 || code == 504;
        }

        public static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter is null)
            {
                return null;
            }

            if (retryAfter.Delta.HasValue)
            {
                return retryAfter.Delta.Value;
            }

            if (retryAfter.Date.HasValue)
            {
                var diff = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
            }

            return null;
        }

        public static async Task<LlmHttpResponse> SendWithRetryAsync(
            HttpClient httpClient,
            Func<HttpRequestMessage> requestFactory,
            TimeSpan perAttemptTimeout,
            TimeSpan totalTimeout,
            int maxRetries,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
            CancellationToken cancellationToken = default,
            TimeSpan? maxRetryAfter = null)
        {
            delayAsync ??= Task.Delay;
            // #110: the 10s default encodes an interactive tradeoff - a UI test must not stall inside
            // a locator resolution, so a long Retry-After is read as an exhausted quota. A batch
            // benchmark has the opposite requirement: Groq answers with 11-13s under load, and
            // refusing to wait there costs the entire measurement to save twelve seconds.
            var retryAfterCeiling = maxRetryAfter ?? MaxRetryAfter;
            var totalAttemptsAllowed = maxRetries + 1;
            var attemptsMade = 0;

            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overallCts.CancelAfter(totalTimeout);

            // Only the status code outlives an attempt - the body is already copied out as a
            // string. Keeping the HttpResponseMessage itself would mean holding an undisposed
            // response (and its content stream) across the retry loop.
            HttpStatusCode? lastStatusCode = null;
            string? lastResponseBody = null;
            string? lastExceptionMessage = null;

            for (var attempt = 1; attempt <= totalAttemptsAllowed; attempt++)
            {
                if (overallCts.IsCancellationRequested)
                {
                    break;
                }

                // Counted only once the attempt is actually going to be made: incrementing
                // before the check above would report a request that was never sent, and
                // AttemptsMade feeds the per-provider flakiness telemetry.
                attemptsMade = attempt;

                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
                attemptCts.CancelAfter(perAttemptTimeout);

                try
                {
                    using var request = requestFactory();
                    using var response = await httpClient.SendAsync(request, attemptCts.Token).ConfigureAwait(false);
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        return new LlmHttpResponse
                        {
                            IsSuccess = true,
                            StatusCode = response.StatusCode,
                            Body = responseBody,
                            AttemptsMade = attemptsMade,
                        };
                    }

                    lastStatusCode = response.StatusCode;
                    lastResponseBody = responseBody;

                    // Non-transient errors (400, 401, 403, 404) fail fast without retrying
                    if (!IsTransient(response.StatusCode))
                    {
                        var truncatedBody = TruncateErrorBody(responseBody);
                        return new LlmHttpResponse
                        {
                            IsSuccess = false,
                            StatusCode = response.StatusCode,
                            Body = truncatedBody,
                            ErrorMessage = $"HTTP {(int)response.StatusCode}: {truncatedBody}",
                            AttemptsMade = attemptsMade,
                        };
                    }

                    // Transient status code: check Retry-After header
                    var retryAfter = ParseRetryAfter(response);
                    if (retryAfter.HasValue && retryAfter.Value > retryAfterCeiling)
                    {
                        var truncatedBody = TruncateErrorBody(responseBody);
                        return new LlmHttpResponse
                        {
                            IsSuccess = false,
                            StatusCode = response.StatusCode,
                            Body = truncatedBody,
                            ErrorMessage = $"HTTP {(int)response.StatusCode}: Retry-After of {retryAfter.Value.TotalSeconds:F0}s exceeds maximum delay threshold ({retryAfterCeiling.TotalSeconds:F0}s).",
                            AttemptsMade = attemptsMade,
                        };
                    }

                    // If more attempts remain, delay and retry
                    if (attempt < totalAttemptsAllowed)
                    {
                        var backoff = retryAfter ?? CalculateExponentialBackoff(attempt);
                        await delayAsync(backoff, overallCts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Caller cancellation takes precedence over timeout
                    return new LlmHttpResponse
                    {
                        IsSuccess = false,
                        IsCanceled = true,
                        ErrorMessage = "Operation was canceled.",
                        AttemptsMade = attemptsMade,
                    };
                }
                catch (OperationCanceledException) when (overallCts.IsCancellationRequested)
                {
                    // Total timeout ceiling reached
                    return new LlmHttpResponse
                    {
                        IsSuccess = false,
                        IsTimedOut = true,
                        ErrorMessage = $"Request timed out after {totalTimeout.TotalSeconds:F0}s.",
                        AttemptsMade = attemptsMade,
                    };
                }
                catch (OperationCanceledException) when (attemptCts.IsCancellationRequested)
                {
                    // Single attempt timed out
                    if (attempt < totalAttemptsAllowed && !overallCts.IsCancellationRequested)
                    {
                        var backoff = CalculateExponentialBackoff(attempt);
                        try
                        {
                            await delayAsync(backoff, overallCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return new LlmHttpResponse
                            {
                                IsSuccess = false,
                                IsTimedOut = true,
                                ErrorMessage = $"Request timed out after {totalTimeout.TotalSeconds:F0}s.",
                                AttemptsMade = attemptsMade,
                            };
                        }
                    }
                    else
                    {
                        return new LlmHttpResponse
                        {
                            IsSuccess = false,
                            IsTimedOut = true,
                            ErrorMessage = $"Request timed out after {perAttemptTimeout.TotalSeconds:F0}s.",
                            AttemptsMade = attemptsMade,
                        };
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
                            return new LlmHttpResponse
                            {
                                IsSuccess = false,
                                IsTimedOut = true,
                                ErrorMessage = $"Request timed out after {totalTimeout.TotalSeconds:F0}s.",
                                AttemptsMade = attemptsMade,
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    return new LlmHttpResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = ex.Message,
                        AttemptsMade = attemptsMade,
                    };
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return new LlmHttpResponse
                {
                    IsSuccess = false,
                    IsCanceled = true,
                    ErrorMessage = "Operation was canceled.",
                    AttemptsMade = attemptsMade,
                };
            }

            if (overallCts.IsCancellationRequested)
            {
                return new LlmHttpResponse
                {
                    IsSuccess = false,
                    IsTimedOut = true,
                    ErrorMessage = $"Request timed out after {totalTimeout.TotalSeconds:F0}s.",
                    AttemptsMade = attemptsMade,
                };
            }

            if (lastStatusCode.HasValue)
            {
                var truncatedBody = TruncateErrorBody(lastResponseBody);
                return new LlmHttpResponse
                {
                    IsSuccess = false,
                    StatusCode = lastStatusCode,
                    Body = truncatedBody,
                    ErrorMessage = $"HTTP {(int)lastStatusCode.Value}: {truncatedBody}",
                    AttemptsMade = attemptsMade,
                };
            }

            return new LlmHttpResponse
            {
                IsSuccess = false,
                ErrorMessage = lastExceptionMessage ?? "Request failed after maximum retries.",
                AttemptsMade = attemptsMade,
            };
        }

        public const int MaxCapturedErrorBodyLength = 1024;

        public static string TruncateErrorBody(string? body, int maxLength = MaxCapturedErrorBodyLength)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return "";
            }

            var trimmed = body!.Trim();
            if (trimmed.Length <= maxLength)
            {
                return trimmed;
            }

            return trimmed.Substring(0, maxLength) + " [truncated]";
        }

        private static TimeSpan CalculateExponentialBackoff(int attempt)
        {
            var baseMs = DefaultInitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            var jitterMs = GetRandom().Next(0, 50);
            var totalMs = Math.Min(2000, baseMs + jitterMs);
            return TimeSpan.FromMilliseconds(totalMs);
        }
    }

    public sealed class LlmHttpResponse
    {
        public bool IsSuccess { get; set; }
        public bool IsTimedOut { get; set; }
        public bool IsCanceled { get; set; }
        public HttpStatusCode? StatusCode { get; set; }
        public string? Body { get; set; }
        public string? ErrorMessage { get; set; }
        public int AttemptsMade { get; set; }
    }
}
