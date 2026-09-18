using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.UiModel;

namespace AutomationSandbox.LlmHealing
{
    // Abstract base class for HTTP-based LLM healing providers.
    // Encapsulates shared fields, constructor validations, retry/timeout orchestration,
    // and candidate matching, while keeping vendor-specific request building and response
    // parsing in derived classes.
    /// <summary>Abstract base class for HTTP-based LLM healing providers. Encapsulates shared fields, constructor validations, retry/timeout orchestration, and candidate matching, while keeping vendor-specific request building and response parsing in derived classes.</summary>
    public abstract class HttpLlmHealingProvider : ILlmHealingProvider
    {
        private static readonly HttpClient SharedHttpClient = new();
        private readonly HttpClient _httpClient;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _totalTimeout;
        private readonly int _maxRetries;
        private readonly Func<TimeSpan, CancellationToken, Task>? _delayAsync;
        private readonly string _name;

        /// <summary>Unique provider name used to associate independent votes and diagnostics within an evaluation.</summary>
        public string Name => _name;
        /// <summary>Whether required local provider configuration is present; this does not probe service reachability or quota.</summary>
        public abstract bool IsAvailable { get; }
        // #110: raises the Retry-After fail-fast ceiling for this provider. Null keeps
        // LlmHttpTransport.MaxRetryAfter, which is what every interactive caller wants. A batch
        // benchmark sets it because waiting out a 12s rate limit is cheaper than losing the run.
        // Settable rather than a constructor argument so the seven provider subclasses keep their
        // signatures - the override is a property of how a provider is being used, not of what it is.
        /// <summary>#110: raises the Retry-After fail-fast ceiling for this provider. Null keeps LlmHttpTransport.MaxRetryAfter, which is what every interactive caller wants. A batch benchmark sets it because waiting out a 12s rate limit is cheaper than losing the run. Settable rather than a constructor argument so the seven provider subclasses keep their signatures - the override is a property of how a provider is being used, not of what it is.</summary>
        public TimeSpan? MaxRetryAfterOverride { get; set; }

        // #127: MaxRetryAfterOverride alone was not enough. Widening how long a Retry-After is
        // honoured does nothing if the total operation still gets cancelled before the wait
        // completes - attempt(<=15s) + honoured wait(<=30s) + retry(<=15s) can reach 60s against
        // the interactive 35s default, and the resulting cancellation prints the same "Request
        // timed out" message a genuinely dead endpoint would, making the two indistinguishable in
        // a report. Null keeps the interactive default; a batch caller raising one ceiling must
        // raise this one too.
        /// <summary>#127: MaxRetryAfterOverride alone was not enough. Widening how long a Retry-After is honoured does nothing if the total operation still gets cancelled before the wait completes - attempt(&lt;=15s) + honoured wait(&lt;=30s) + retry(&lt;=15s) can reach 60s against the interactive 35s default, and the resulting cancellation prints the same &quot;Request timed out&quot; message a genuinely dead endpoint would, making the two indistinguishable in a report. Null keeps the interactive default; a batch caller raising one ceiling must raise this one too.</summary>
        public TimeSpan? TotalTimeoutOverride { get; set; }
        /// <summary>Transforms text before it is disclosed in an LLM prompt; the default redacts common secrets and personal data.</summary>
        public Func<string, string>? TextSanitizer { get; set; }

        /// <summary>Deadline for one provider HTTP attempt.</summary>
        public TimeSpan Timeout => _timeout;
        /// <summary>Deadline for the complete provider operation, including retry delays.</summary>
        public TimeSpan TotalTimeout => _totalTimeout;
        /// <summary>Maximum number of retries after the initial attempt; zero disables retries.</summary>
        public int MaxRetries => _maxRetries;

        /// <summary>Configures shared provider transport, deadlines, retry limits, and identity; vendor subclasses supply request and response formats.</summary>
        protected HttpLlmHealingProvider(
            string defaultName,
            TimeSpan defaultTimeout,
            TimeSpan defaultTotalTimeout,
            int defaultMaxRetries = 2,
            HttpClient? httpClient = null,
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
            _timeout = timeout ?? defaultTimeout;
            _totalTimeout = totalTimeout ?? (timeout.HasValue ? TimeSpan.FromSeconds(Math.Max(defaultTotalTimeout.TotalSeconds, _timeout.TotalSeconds * 2.5)) : defaultTotalTimeout);
            if (_totalTimeout < _timeout)
            {
                throw new ArgumentException("TotalTimeout cannot be less than per-attempt Timeout.", nameof(totalTimeout));
            }

            _maxRetries = maxRetries ?? defaultMaxRetries;
            _delayAsync = delayAsync;
            _name = NullIfEmpty(name?.Trim()) ?? defaultName;
        }

        /// <summary>Returns null for null, empty, or whitespace-only configuration values; otherwise preserves the supplied value.</summary>
        protected static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>Diagnostic returned when the provider lacks required local configuration.</summary>
        protected abstract string UnavailableErrorMessage { get; }

        /// <summary>Creates one authenticated vendor request for the supplied prompt. The transport owns and disposes the returned request.</summary>
        protected abstract HttpRequestMessage CreateRequest(string prompt);

        /// <summary>Extracts model-generated text from the vendor response envelope for guarded response parsing.</summary>
        protected abstract string ExtractText(string responseBody);

        /// <summary>Builds a bounded prompt, calls the retry transport, and parses a candidate proposal with timing and attempt telemetry. The resolver independently validates votes.</summary>
        public async Task<LlmHealingResult> ResolveAsync(
            UiElementInfo expected,
            IReadOnlyList<CandidateScore> candidates,
            string? platform = null,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            if (!IsAvailable)
            {
                return new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = false,
                    ErrorMessage = UnavailableErrorMessage,
                    Elapsed = stopwatch.Elapsed,
                    AttemptCount = 0,
                };
            }

            var prompt = LlmHealingPrompt.Build(expected, candidates, platform, TextSanitizer);

            var httpResponse = await LlmHttpTransport.SendWithRetryAsync(
                _httpClient,
                () => CreateRequest(prompt),
                _timeout,
                TotalTimeoutOverride ?? _totalTimeout,
                _maxRetries,
                _delayAsync,
                cancellationToken,
                MaxRetryAfterOverride).ConfigureAwait(false);

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
                var diagnostic = $" Raw response: {TruncateForDiagnostics(httpResponse.Body)}";
                return new LlmHealingResult
                {
                    ProviderName = Name,
                    Success = false,
                    ErrorMessage = ex.Message + diagnostic,
                    Elapsed = stopwatch.Elapsed,
                    AttemptCount = httpResponse.AttemptsMade,
                };
            }
        }

        private static string TruncateForDiagnostics(string? body)
        {
            const int maxLength = 4096;
            var value = body ?? "<empty>";
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...<truncated>";
        }
    }
}
