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
        private static readonly HttpClient SharedHttpClient = new();
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string _model;
        private readonly IIntentPlanner _fallback;

        public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

        public LlmIntentPlanner(HttpClient? httpClient = null, string? apiKey = null, string? model = null, IIntentPlanner? fallback = null)
        {
            _httpClient = httpClient ?? SharedHttpClient;
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            _model = NullIfEmpty(model) ?? NullIfEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")) ?? DefaultModel;
            _fallback = fallback ?? new DeterministicIntentPlanner();
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
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            httpRequest.Headers.Add("x-api-key", _apiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");

            try
            {
                using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return Degrade(request, $"LLM planning HTTP {(int)response.StatusCode} after {stopwatch.Elapsed.TotalMilliseconds:F0}ms; degraded to DeterministicIntentPlanner.");
                }

                var text = ExtractText(responseBody);
                var scenario = LlmIntentPlanningPrompt.ParseScenario(text, request);
                return new IntentPlanningResult { Scenario = scenario };
            }

            catch (Exception ex)
            {
                return Degrade(request, $"LLM planning failed ({ex.Message}); degraded to DeterministicIntentPlanner.");
            }
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
