using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.UiModel;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>Base class for <see cref="ITextModel" /> implementations that call an HTTP API. It owns the shared
    /// parts: the sanitiser, deadlines and retries through <see cref="LlmHttpTransport" />, and turning every
    /// failure into a result. Subclasses supply only the vendor request and response formats.</summary>
    public abstract class HttpTextModel : ITextModel
    {
        private static readonly HttpClient SharedHttpClient = new HttpClient();
        private readonly HttpClient _httpClient;
        private readonly Func<TimeSpan, CancellationToken, Task>? _delayAsync;

        /// <summary>Default deadline for one HTTP attempt. Longer than the healing providers use, because drafting a
        /// document takes longer than choosing one candidate.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        /// <summary>Default deadline for the whole call, retries included.</summary>
        public static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(150);

        /// <summary>Configures the shared transport settings; vendor subclasses supply request and response formats.</summary>
        protected HttpTextModel(
            string name,
            HttpClient? httpClient = null,
            TimeSpan? timeout = null,
            TimeSpan? totalTimeout = null,
            int maxRetries = 2,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("name must not be null or empty.", nameof(name));
            }

            Timeout = timeout ?? DefaultTimeout;
            TotalTimeout = totalTimeout ?? DefaultTotalTimeout;
            if (Timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
            }

            if (TotalTimeout < Timeout)
            {
                throw new ArgumentException("TotalTimeout cannot be less than per-attempt Timeout.", nameof(totalTimeout));
            }

            if (maxRetries < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRetries), "MaxRetries must be non-negative.");
            }

            Name = name.Trim();
            MaxRetries = maxRetries;
            _httpClient = httpClient ?? SharedHttpClient;
            _delayAsync = delayAsync;
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public abstract bool IsAvailable { get; }

        /// <summary>Deadline for one HTTP attempt.</summary>
        public TimeSpan Timeout { get; }

        /// <summary>Deadline for the whole call, retries included.</summary>
        public TimeSpan TotalTimeout { get; }

        /// <summary>Retries after the first attempt; zero disables retries.</summary>
        public int MaxRetries { get; }

        /// <summary>Transforms the prompt before it leaves the process. Null uses
        /// <see cref="SensitiveDataSanitizer.Default" />, which redacts common secrets and personal data.</summary>
        public Func<string, string>? TextSanitizer { get; set; }

        /// <summary>Diagnostic returned when the model lacks required local configuration.</summary>
        protected abstract string UnavailableErrorMessage { get; }

        /// <summary>Creates one authenticated vendor request for the (already sanitised) prompt. The transport owns and
        /// disposes the returned request.</summary>
        protected abstract HttpRequestMessage CreateRequest(string sanitizedPrompt, TextCompletionRequest request);

        /// <summary>Extracts the generated text from the vendor response body; may throw on a malformed body.</summary>
        protected abstract string ExtractText(string responseBody);

        /// <inheritdoc />
        public async Task<TextCompletionResult> CompleteAsync(TextCompletionRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var stopwatch = Stopwatch.StartNew();
            if (!IsAvailable)
            {
                return Failure(UnavailableErrorMessage, stopwatch, 0);
            }

            var sanitized = (TextSanitizer ?? SensitiveDataSanitizer.Default)(request.Prompt);
            var response = await LlmHttpTransport.SendWithRetryAsync(
                _httpClient,
                () => CreateRequest(sanitized, request),
                Timeout,
                TotalTimeout,
                MaxRetries,
                _delayAsync,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Failure(response.ErrorMessage ?? "Request failed.", stopwatch, response.AttemptsMade);
            }

            string text;
            try
            {
                text = ExtractText(response.Body ?? "");
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException || ex is InvalidOperationException || ex is System.Collections.Generic.KeyNotFoundException)
            {
                return Failure("The response could not be read: " + ex.Message, stopwatch, response.AttemptsMade);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return Failure("The model returned no text.", stopwatch, response.AttemptsMade);
            }

            return new TextCompletionResult
            {
                ModelName = Name,
                Success = true,
                Text = text.Trim(),
                Elapsed = stopwatch.Elapsed,
                AttemptCount = response.AttemptsMade,
            };
        }

        private TextCompletionResult Failure(string message, Stopwatch stopwatch, int attempts)
        {
            return new TextCompletionResult
            {
                ModelName = Name,
                Success = false,
                ErrorMessage = message,
                Elapsed = stopwatch.Elapsed,
                AttemptCount = attempts,
            };
        }
    }
}
