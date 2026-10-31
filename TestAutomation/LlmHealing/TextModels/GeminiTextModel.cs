using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>A Gemini model through the Interactions API, the same request shape
    /// <see cref="GeminiHealingProvider" /> uses. That request has no verified field for an output-token limit or a
    /// JSON switch, so <see cref="TextCompletionRequest.MaxOutputTokens" /> and
    /// <see cref="TextCompletionRequest.ExpectJson" /> are not sent; a caller must validate the answer either way.</summary>
    public sealed class GeminiTextModel : HttpTextModel
    {
        private const string ApiUrl = "https://generativelanguage.googleapis.com/v1beta/interactions";

        /// <summary>Model used when none is given.</summary>
        public const string DefaultModel = "gemini-3.6-flash";

        private readonly string _apiKey;
        private readonly string _model;

        /// <summary>Creates a model. An empty <paramref name="model" /> falls back to <see cref="DefaultModel" />.</summary>
        public GeminiTextModel(
            string? apiKey,
            string? model = null,
            string name = "Gemini",
            HttpClient? httpClient = null,
            TimeSpan? timeout = null,
            TimeSpan? totalTimeout = null,
            int maxRetries = 2,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
            : base(name, httpClient, timeout, totalTimeout, maxRetries, delayAsync)
        {
            _apiKey = apiKey?.Trim() ?? "";
            _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model!.Trim();
        }

        /// <inheritdoc />
        public override bool IsAvailable => _apiKey.Length > 0;

        /// <inheritdoc />
        protected override string UnavailableErrorMessage => $"{Name}: no API key is configured.";

        /// <inheritdoc />
        protected override HttpRequestMessage CreateRequest(string sanitizedPrompt, TextCompletionRequest request)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { model = _model, input = sanitizedPrompt }), Encoding.UTF8, "application/json"),
            };
            message.Headers.Add("x-goog-api-key", _apiKey);
            return message;
        }

        /// <inheritdoc />
        protected override string ExtractText(string responseBody)
        {
            using var document = JsonDocument.Parse(responseBody);
            var text = new StringBuilder();
            foreach (var step in document.RootElement.GetProperty("steps").EnumerateArray())
            {
                if (!step.TryGetProperty("type", out var stepType) || stepType.GetString() != "model_output")
                {
                    continue;
                }

                foreach (var item in step.GetProperty("content").EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                        item.TryGetProperty("text", out var value))
                    {
                        text.Append(value.GetString());
                    }
                }
            }

            return text.ToString();
        }
    }
}
