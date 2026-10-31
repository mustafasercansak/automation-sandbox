using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>A Claude model through the Anthropic messages API. Extended thinking is switched off, so the answer is
    /// the first text block and no tokens are spent on reasoning the task does not need.</summary>
    public sealed class ClaudeTextModel : HttpTextModel
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";

        /// <summary>Model used when none is given.</summary>
        public const string DefaultModel = "claude-haiku-4-5-20251001";

        private readonly string _apiKey;
        private readonly string _model;

        /// <summary>Creates a model. An empty <paramref name="model" /> falls back to <see cref="DefaultModel" />.</summary>
        public ClaudeTextModel(
            string? apiKey,
            string? model = null,
            string name = "Claude",
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
            var body = new
            {
                model = _model,
                max_tokens = request.MaxOutputTokens,
                thinking = new { type = "disabled" },
                messages = new[] { new { role = "user", content = sanitizedPrompt } },
            };

            var message = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            message.Headers.Add("x-api-key", _apiKey);
            message.Headers.Add("anthropic-version", "2023-06-01");
            return message;
        }

        /// <inheritdoc />
        protected override string ExtractText(string responseBody)
        {
            using var document = JsonDocument.Parse(responseBody);
            var text = new StringBuilder();
            foreach (var block in document.RootElement.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                    block.TryGetProperty("text", out var value))
                {
                    text.Append(value.GetString());
                }
            }

            return text.ToString();
        }
    }
}
