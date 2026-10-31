using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>A model behind an OpenAI-compatible <c>chat/completions</c> endpoint: OpenAI itself, Mistral, NVIDIA
    /// NIM, Ollama Cloud, a local server, or any other service that speaks the same wire format. One class covers
    /// them all; only the name, endpoint, key and model differ.</summary>
    public sealed class ChatCompletionsTextModel : HttpTextModel
    {
        private readonly string _apiKey;
        private readonly string _model;
        private readonly Uri _url;

        /// <summary>Creates a model. <paramref name="endpoint" /> is either the base URL (<c>/chat/completions</c> is
        /// appended) or the full URL. Plain http is accepted only for a loopback host, so a key is never sent in
        /// clear text across a network.</summary>
        public ChatCompletionsTextModel(
            string name,
            string? apiKey,
            string model,
            string endpoint = "https://api.openai.com/v1",
            HttpClient? httpClient = null,
            TimeSpan? timeout = null,
            TimeSpan? totalTimeout = null,
            int maxRetries = 2,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
            : base(name, httpClient, timeout, totalTimeout, maxRetries, delayAsync)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new ArgumentException("model must not be null or empty.", nameof(model));
            }

            _apiKey = apiKey?.Trim() ?? "";
            _model = model.Trim();
            _url = BuildUrl(endpoint);
        }

        /// <inheritdoc />
        public override bool IsAvailable => _apiKey.Length > 0;

        /// <inheritdoc />
        protected override string UnavailableErrorMessage => $"{Name}: no API key is configured.";

        private static Uri BuildUrl(string endpoint)
        {
            if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("endpoint must be an absolute http or https URL.", nameof(endpoint));
            }

            if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            {
                throw new ArgumentException("endpoint must use https unless it is a loopback address.", nameof(endpoint));
            }

            var text = uri.ToString().TrimEnd('/');
            return text.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? new Uri(text)
                : new Uri(text + "/chat/completions");
        }

        /// <inheritdoc />
        protected override HttpRequestMessage CreateRequest(string sanitizedPrompt, TextCompletionRequest request)
        {
            object body = request.ExpectJson
                ? new
                {
                    model = _model,
                    messages = new[] { new { role = "user", content = sanitizedPrompt } },
                    temperature = 0.0,
                    max_tokens = request.MaxOutputTokens,
                    response_format = new { type = "json_object" },
                }
                : new
                {
                    model = _model,
                    messages = new[] { new { role = "user", content = sanitizedPrompt } },
                    temperature = 0.0,
                    max_tokens = request.MaxOutputTokens,
                };

            var message = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            return message;
        }

        /// <inheritdoc />
        protected override string ExtractText(string responseBody)
        {
            using var document = JsonDocument.Parse(responseBody);
            var choices = document.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0 || !choices[0].TryGetProperty("message", out var message))
            {
                return "";
            }

            return message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                ? content.GetString() ?? ""
                : "";
        }
    }
}
