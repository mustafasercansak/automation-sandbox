using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.ContentAnalysis
{
    // Self-contained single-provider HTTP call, deliberately not depending on the LlmHealing
    // package's multi-provider consensus machinery: that's built for picking one candidate from a
    // shortlist, a different problem. This mirrors IntentAutomation.LlmIntentPlanner, which made
    // the same call for the same reason. Unlike that planner, a failure here has nothing
    // structural to fall back to - the heuristic checks already ran independently in
    // ContentAnalyzer - so this class degrades to an empty result on any failure rather than
    // threading per-failure diagnostics through a fallback result.

    /// <summary>Claude-backed <see cref="IContentAnalysisProvider" />. Degrades to an empty result - never
    /// throws - when no API key is configured or the call fails after retries.</summary>
    public sealed class ClaudeContentAnalysisProvider : IContentAnalysisProvider
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string DefaultModel = "claude-haiku-4-5-20251001";

        /// <summary>Default deadline for one HTTP attempt.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
        /// <summary>Default number of retries after the initial attempt.</summary>
        public static readonly int DefaultMaxRetries = 2;

        private static readonly HttpClient SharedHttpClient = new();

        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string _model;
        private readonly TimeSpan _timeout;
        private readonly int _maxRetries;

        /// <summary>Transforms passage text before it is disclosed in a prompt; the default redacts common
        /// secrets and personal data (<see cref="AutomationSandbox.UiModel.SensitiveDataSanitizer.Default" />).</summary>
        public Func<string, string>? TextSanitizer { get; set; }

        /// <summary>Whether an API key is configured; this does not probe service reachability or quota.</summary>
        public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

        /// <summary>Configures the provider from explicit values or, when omitted, the
        /// <c>ANTHROPIC_API_KEY</c>/<c>ANTHROPIC_MODEL</c> environment variables.</summary>
        public ClaudeContentAnalysisProvider(
            HttpClient? httpClient = null,
            string? apiKey = null,
            string? model = null,
            TimeSpan? timeout = null,
            int? maxRetries = null)
        {
            if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
            }

            if (maxRetries.HasValue && maxRetries.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRetries), "MaxRetries must be non-negative.");
            }

            _httpClient = httpClient ?? SharedHttpClient;
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            _model = !string.IsNullOrEmpty(model) ? model! :
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")) ? Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")! :
                DefaultModel;
            _timeout = timeout ?? DefaultTimeout;
            _maxRetries = maxRetries ?? DefaultMaxRetries;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ContentIssue>> AnalyzeAsync(IReadOnlyList<ContentPassage> passages, CancellationToken cancellationToken = default)
        {
            if (passages == null)
            {
                throw new ArgumentNullException(nameof(passages));
            }

            if (!IsAvailable || passages.Count == 0)
            {
                return Array.Empty<ContentIssue>();
            }

            var prompt = ContentAnalysisPrompt.Build(passages, TextSanitizer);
            var requestBody = new
            {
                model = _model,
                max_tokens = 2048,
                thinking = new { type = "disabled" },
                messages = new[] { new { role = "user", content = prompt } },
            };
            var requestJson = JsonSerializer.Serialize(requestBody);

            var totalAttemptsAllowed = _maxRetries + 1;
            for (var attempt = 1; attempt <= totalAttemptsAllowed; attempt++)
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(_timeout);

                try
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
                    {
                        Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
                    };
                    httpRequest.Headers.Add("x-api-key", _apiKey);
                    httpRequest.Headers.Add("anthropic-version", "2023-06-01");

                    using var response = await _httpClient.SendAsync(httpRequest, attemptCts.Token).ConfigureAwait(false);
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        return ContentAnalysisPrompt.ParseIssues(ExtractText(body), passages);
                    }

                    var isTransient = (int)response.StatusCode is 429 or 500 or 502 or 503 or 504;
                    if (!isTransient || attempt == totalAttemptsAllowed)
                    {
                        return Array.Empty<ContentIssue>();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Array.Empty<ContentIssue>();
                }
                catch when (attempt == totalAttemptsAllowed)
                {
                    return Array.Empty<ContentIssue>();
                }
                catch
                {
                    // Fall through to the backoff delay below and retry.
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1)), cancellationToken).ConfigureAwait(false);
            }

            return Array.Empty<ContentIssue>();
        }

        // Finds the first text block rather than assuming content[0] is text - a thinking block
        // (when thinking isn't disabled) would otherwise come first. Same approach as
        // LlmIntentPlanner.ExtractText.
        private static string ExtractText(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);
            foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "text" &&
                    block.TryGetProperty("text", out var textProp))
                {
                    return textProp.GetString() ?? "";
                }
            }

            return "";
        }
    }
}
