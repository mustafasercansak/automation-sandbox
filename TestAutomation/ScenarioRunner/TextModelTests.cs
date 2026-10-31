using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;

namespace ScenarioRunner
{
    // No test here needs an API key or the network: every model gets a stub HTTP handler that records the
    // request it received and replays canned responses.
    public class TextModelTests
    {
        private static readonly Func<TimeSpan, CancellationToken, Task> NoDelay = (_, _) => Task.CompletedTask;

        private sealed class RecordedRequest
        {
            public string Url { get; set; } = "";
            public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public JsonElement Body { get; set; }
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Queue<(HttpStatusCode Status, string Body)> _responses;
            private (HttpStatusCode Status, string Body) _last;

            public StubHandler(params (HttpStatusCode, string)[] responses)
            {
                _responses = new Queue<(HttpStatusCode, string)>(responses);
            }

            public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var recorded = new RecordedRequest { Url = request.RequestUri!.ToString() };
                foreach (var header in request.Headers)
                {
                    recorded.Headers[header.Key] = string.Join(",", header.Value);
                }

                var body = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                recorded.Body = JsonDocument.Parse(body).RootElement.Clone();
                Requests.Add(recorded);

                if (_responses.Count > 0)
                {
                    _last = _responses.Dequeue();
                }

                return new HttpResponseMessage(_last.Status) { Content = new StringContent(_last.Body, Encoding.UTF8, "application/json") };
            }
        }

        private static (HttpStatusCode, string) Ok(string body) => (HttpStatusCode.OK, body);

        private const string ChatAnswer = "{\"choices\":[{\"message\":{\"content\":\"  hello  \"}}]}";

        private static ChatCompletionsTextModel Chat(StubHandler handler, string? key = "k", string endpoint = "https://api.example.test/v1", int retries = 2)
        {
            return new ChatCompletionsTextModel("Example", key, "model-x", endpoint, new HttpClient(handler), maxRetries: retries, delayAsync: NoDelay);
        }

        [Fact]
        public async Task ChatCompletions_SendsTheExpectedRequest_AndParsesTheAnswer()
        {
            var handler = new StubHandler(Ok(ChatAnswer));

            var result = await Chat(handler).CompleteAsync(new TextCompletionRequest("say hi", maxOutputTokens: 300));

            Assert.True(result.Success);
            Assert.Equal("hello", result.Text);
            Assert.Equal("Example", result.ModelName);
            Assert.Equal(1, result.AttemptCount);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://api.example.test/v1/chat/completions", request.Url);
            Assert.Equal("Bearer k", request.Headers["Authorization"]);
            Assert.Equal("model-x", request.Body.GetProperty("model").GetString());
            Assert.Equal(300, request.Body.GetProperty("max_tokens").GetInt32());
            Assert.Equal(0.0, request.Body.GetProperty("temperature").GetDouble());
            var message = request.Body.GetProperty("messages")[0];
            Assert.Equal("user", message.GetProperty("role").GetString());
            Assert.Equal("say hi", message.GetProperty("content").GetString());
            Assert.False(request.Body.TryGetProperty("response_format", out _));
        }

        [Fact]
        public async Task ChatCompletions_AsksForJsonOnlyWhenTheCallerExpectsJson()
        {
            var handler = new StubHandler(Ok(ChatAnswer));

            await Chat(handler).CompleteAsync(new TextCompletionRequest("give json", expectJson: true));

            Assert.Equal("json_object", handler.Requests[0].Body.GetProperty("response_format").GetProperty("type").GetString());
        }

        [Fact]
        public async Task ChatCompletions_UsesAFullEndpointUrlAsIs()
        {
            var handler = new StubHandler(Ok(ChatAnswer));

            await Chat(handler, endpoint: "https://api.example.test/custom/chat/completions").CompleteAsync(new TextCompletionRequest("x"));

            Assert.Equal("https://api.example.test/custom/chat/completions", handler.Requests[0].Url);
        }

        [Theory]
        [InlineData("not a url")]
        [InlineData("ftp://api.example.test/v1")]
        [InlineData("http://api.example.test/v1")]
        public void ChatCompletions_RejectsAnEndpointThatCouldLeakTheKey(string endpoint)
        {
            Assert.Throws<ArgumentException>(() => new ChatCompletionsTextModel("Example", "k", "m", endpoint));
        }

        [Fact]
        public void ChatCompletions_AllowsPlainHttpOnlyOnLoopback()
        {
            var model = new ChatCompletionsTextModel("Local", "k", "m", "http://localhost:11434/v1");

            Assert.True(model.IsAvailable);
        }

        [Fact]
        public async Task Claude_SendsTheExpectedRequest_AndJoinsTextBlocksSkippingThinking()
        {
            var handler = new StubHandler(Ok("{\"content\":[{\"type\":\"thinking\",\"thinking\":\"hmm\"},{\"type\":\"text\",\"text\":\"one \"},{\"type\":\"text\",\"text\":\"two\"}]}"));
            var model = new ClaudeTextModel("key", httpClient: new HttpClient(handler), delayAsync: NoDelay);

            var result = await model.CompleteAsync(new TextCompletionRequest("draft", maxOutputTokens: 500));

            Assert.True(result.Success);
            Assert.Equal("one two", result.Text);
            Assert.Equal("Claude", result.ModelName);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://api.anthropic.com/v1/messages", request.Url);
            Assert.Equal("key", request.Headers["x-api-key"]);
            Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);
            Assert.Equal(ClaudeTextModel.DefaultModel, request.Body.GetProperty("model").GetString());
            Assert.Equal(500, request.Body.GetProperty("max_tokens").GetInt32());
            Assert.Equal("disabled", request.Body.GetProperty("thinking").GetProperty("type").GetString());
            Assert.Equal("draft", request.Body.GetProperty("messages")[0].GetProperty("content").GetString());
        }

        [Fact]
        public async Task Claude_UsesTheModelItIsGiven_AndFallsBackWhenTheModelIsBlank()
        {
            var handler = new StubHandler(Ok("{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}"));

            await new ClaudeTextModel("key", "claude-custom", httpClient: new HttpClient(handler)).CompleteAsync(new TextCompletionRequest("x"));
            await new ClaudeTextModel("key", "  ", httpClient: new HttpClient(handler)).CompleteAsync(new TextCompletionRequest("x"));

            Assert.Equal("claude-custom", handler.Requests[0].Body.GetProperty("model").GetString());
            Assert.Equal(ClaudeTextModel.DefaultModel, handler.Requests[1].Body.GetProperty("model").GetString());
        }

        [Fact]
        public async Task Gemini_SendsOnlyModelAndInput_AndReadsModelOutputSteps()
        {
            var handler = new StubHandler(Ok("{\"steps\":[{\"type\":\"thought\",\"content\":[{\"type\":\"text\",\"text\":\"skip\"}]},{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":\"answer\"}]}]}"));
            var model = new GeminiTextModel("gkey", httpClient: new HttpClient(handler), delayAsync: NoDelay);

            var result = await model.CompleteAsync(new TextCompletionRequest("question", maxOutputTokens: 100, expectJson: true));

            Assert.True(result.Success);
            Assert.Equal("answer", result.Text);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/interactions", request.Url);
            Assert.Equal("gkey", request.Headers["x-goog-api-key"]);
            Assert.Equal(GeminiTextModel.DefaultModel, request.Body.GetProperty("model").GetString());
            Assert.Equal("question", request.Body.GetProperty("input").GetString());
            Assert.Equal(2, request.Body.EnumerateObject().Count());
        }

        [Fact]
        public async Task AFailureStatus_IsReturnedNotThrown_AndANonTransientOneIsNotRetried()
        {
            var handler = new StubHandler((HttpStatusCode.Unauthorized, "{\"error\":\"bad key\"}"));

            var result = await Chat(handler).CompleteAsync(new TextCompletionRequest("x"));

            Assert.False(result.Success);
            Assert.Contains("401", result.ErrorMessage);
            Assert.Equal("", result.Text);
            Assert.Equal(1, result.AttemptCount);
        }

        [Fact]
        public async Task ATransientFailure_IsRetriedThroughTheSharedTransport()
        {
            var handler = new StubHandler((HttpStatusCode.InternalServerError, "{}"), Ok(ChatAnswer));

            var result = await Chat(handler).CompleteAsync(new TextCompletionRequest("x"));

            Assert.True(result.Success);
            Assert.Equal(2, result.AttemptCount);
            Assert.Equal(2, handler.Requests.Count);
        }

        [Fact]
        public async Task APersistentTransientFailure_EndsAsAResultAfterTheConfiguredRetries()
        {
            var handler = new StubHandler((HttpStatusCode.ServiceUnavailable, "{}"));

            var result = await Chat(handler, retries: 2).CompleteAsync(new TextCompletionRequest("x"));

            Assert.False(result.Success);
            Assert.Equal(3, result.AttemptCount);
        }

        [Theory]
        [InlineData("{\"choices\":[{\"message\":{\"content\":\"   \"}}]}", "no text")]
        [InlineData("{\"choices\":[]}", "no text")]
        [InlineData("this is not json", "could not be read")]
        [InlineData("{\"unexpected\":true}", "could not be read")]
        public async Task AnEmptyOrMalformedAnswer_IsAFailureWithAReason(string body, string expected)
        {
            var result = await Chat(new StubHandler(Ok(body))).CompleteAsync(new TextCompletionRequest("x"));

            Assert.False(result.Success);
            Assert.Contains(expected, result.ErrorMessage);
        }

        [Fact]
        public async Task AModelWithoutAKey_IsUnavailableAndSendsNothing()
        {
            var handler = new StubHandler(Ok(ChatAnswer));

            var result = await Chat(handler, key: " ").CompleteAsync(new TextCompletionRequest("x"));

            Assert.False(result.Success);
            Assert.Contains("Example", result.ErrorMessage);
            Assert.Equal(0, result.AttemptCount);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task ThePromptIsSanitisedBeforeItLeavesTheProcess_AndACustomSanitiserReplacesTheDefault()
        {
            var handler = new StubHandler(Ok(ChatAnswer));
            var model = Chat(handler);

            await model.CompleteAsync(new TextCompletionRequest("contact jane.doe@example.com today"));
            model.TextSanitizer = text => "CUSTOM:" + text;
            await model.CompleteAsync(new TextCompletionRequest("contact jane.doe@example.com today"));

            var first = handler.Requests[0].Body.GetProperty("messages")[0].GetProperty("content").GetString()!;
            var second = handler.Requests[1].Body.GetProperty("messages")[0].GetProperty("content").GetString()!;
            Assert.DoesNotContain("jane.doe@example.com", first);
            Assert.StartsWith("CUSTOM:", second);
            Assert.Contains("jane.doe@example.com", second);
        }

        [Fact]
        public async Task CancellationByTheCaller_IsThrown()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => Chat(new StubHandler(Ok(ChatAnswer))).CompleteAsync(new TextCompletionRequest("x"), cts.Token));
        }

        [Fact]
        public void ARequestIsValidatedWhenItIsCreated()
        {
            Assert.Throws<ArgumentException>(() => new TextCompletionRequest("  "));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TextCompletionRequest("x", maxOutputTokens: 0));
            Assert.Equal(TextCompletionRequest.DefaultMaxOutputTokens, new TextCompletionRequest("x").MaxOutputTokens);
        }
    }

    public class TextModelRegistryTests
    {
        private static Func<string, string?> Env(params (string, string?)[] values)
        {
            var map = values.ToDictionary(v => v.Item1, v => v.Item2);
            return key => map.TryGetValue(key, out var value) ? value : null;
        }

        [Fact]
        public void WithNoConfiguration_TheRegistryIsEmpty()
        {
            Assert.Empty(TextModelRegistry.CreateFromEnvironment(getEnv: Env()).Models);
        }

        [Fact]
        public void EveryConfiguredModelIsRegisteredInOrder_AndIsAvailable()
        {
            var registry = TextModelRegistry.CreateFromEnvironment(getEnv: Env(
                ("ANTHROPIC_API_KEY", "a"), ("GEMINI_API_KEY", "g"), ("OPENAI_API_KEY", "o"),
                ("MISTRAL_API_KEY", "m"), ("MISTRAL_MODEL", "mistral-small-latest"),
                ("NVIDIA_API_KEY", "n"), ("NVIDIA_MODEL", "some-model")));

            Assert.Equal(new[] { "Claude", "Gemini", "OpenAI", "Mistral", "Nvidia" }, registry.Models.Select(m => m.Name));
            Assert.All(registry.Models, model => Assert.True(model.IsAvailable));
        }

        [Fact]
        public void AModelWhoseRequiredModelIsMissing_IsLeftOutInsteadOfGuessed()
        {
            var registry = TextModelRegistry.CreateFromEnvironment(getEnv: Env(
                ("MISTRAL_API_KEY", "m"), ("NVIDIA_MODEL", "only-a-model")));

            Assert.Empty(registry.Models);
        }

        [Fact]
        public void BlankValuesCountAsUnset_AsTheyDoWhenAnActionsVariableIsEmpty()
        {
            var registry = TextModelRegistry.CreateFromEnvironment(getEnv: Env(("ANTHROPIC_API_KEY", "  "), ("GEMINI_API_KEY", ""), ("OPENAI_API_KEY", null)));

            Assert.Empty(registry.Models);
        }

        [Fact]
        public void LookupIgnoresCase_AndAnUnknownNameIsNull()
        {
            var registry = TextModelRegistry.CreateFromEnvironment(getEnv: Env(("GEMINI_API_KEY", "g")));

            Assert.Same(registry.Models[0], registry.Find("gEmInI"));
            Assert.Same(registry.Models[0], registry.Find("  Gemini "));
            Assert.Null(registry.Find("nobody"));
            Assert.Null(registry.Find(null!));
        }

        [Fact]
        public void ADuplicateNameIsRejectedInsteadOfReplacing()
        {
            var registry = new TextModelRegistry();
            registry.Add(new ClaudeTextModel("a"));

            Assert.Throws<ArgumentException>(() => registry.Add(new ClaudeTextModel("b", name: "claude")));
            Assert.Throws<ArgumentNullException>(() => registry.Add(null!));
            Assert.Single(registry.Models);
        }
    }
}
