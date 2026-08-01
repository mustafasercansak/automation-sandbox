using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using LlmHealing;
using UiModel;

namespace ScenarioRunner
{
    // Pure-logic tests for the LlmHealing prompt/parsing/provider plumbing, run
    // against a fake HttpMessageHandler so they need no API key and no network -
    // unlike LlmHealingEvaluationTests, these are part of the required test suite.
    public class LlmHealingProviderTests
    {
        private static readonly UiElementInfo Expected = new()
        {
            ControlType = "Edit",
            AutomationId = "txtEmailAddress",
            ParentControlType = "Window",
            SiblingIndex = 2,
            SiblingCount = 7,
            BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
        };

        private static UiElementInfo BuildCurrentTree()
        {
            var tree = new UiElementInfo { ControlType = "Window", AutomationId = "MainForm" };
            tree.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "txtEmail",
                ParentControlType = "Window",
                SiblingIndex = 2,
                SiblingCount = 7,
                BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
            });
            return tree;
        }

        [Fact]
        public void Build_IncludesBothTheStaleLocatorAndTheCurrentTree()
        {
            var prompt = LlmHealingPrompt.Build(Expected, BuildCurrentTree());

            Assert.Contains("txtEmailAddress", prompt);
            Assert.Contains("\"AutomationId\": \"txtEmail\"", prompt);
            Assert.Contains("automationId", prompt);
        }

        [Fact]
        public void ParseResponse_HandlesPlainJson()
        {
            var (id, confidence, reasoning) = LlmHealingPrompt.ParseResponse(
                "{\"automationId\": \"txtEmail\", \"confidence\": 0.92, \"reasoning\": \"same position\"}");

            Assert.Equal("txtEmail", id);
            Assert.Equal(0.92, confidence, precision: 3);
            Assert.Equal("same position", reasoning);
        }

        [Fact]
        public void ParseResponse_StripsMarkdownFencesAroundJson()
        {
            var raw = "```json\n{\"automationId\": \"txtEmail\", \"confidence\": 0.8, \"reasoning\": \"ok\"}\n```";

            var (id, confidence, _) = LlmHealingPrompt.ParseResponse(raw);

            Assert.Equal("txtEmail", id);
            Assert.Equal(0.8, confidence, precision: 3);
        }

        [Fact]
        public void ParseResponse_ThrowsFormatException_WhenThereIsNoJsonObject()
        {
            Assert.Throws<FormatException>(() => LlmHealingPrompt.ParseResponse("I don't know which element matches."));
        }

        [Fact]
        public async Task ClaudeHealingProvider_WithoutApiKey_SkipsTheHttpCallEntirely()
        {
            var callCount = 0;
            var handler = new FakeHttpMessageHandler(_ => { callCount++; return new HttpResponseMessage(HttpStatusCode.OK); });
            // Explicit empty string, not null: null falls back to the real ANTHROPIC_API_KEY
            // environment variable, which would make this test depend on ambient CI/local config.
            var provider = new ClaudeHealingProvider(httpClient: new HttpClient(handler), apiKey: "");

            Assert.False(provider.IsAvailable);

            var result = await provider.ResolveAsync(Expected, BuildCurrentTree());

            Assert.False(result.Success);
            Assert.Equal(0, callCount);
        }

        [Fact]
        public async Task ClaudeHealingProvider_ParsesASuccessfulAnthropicShapedResponse()
        {
            const string anthropicResponseJson = """
            {
              "content": [
                { "type": "text", "text": "{\"automationId\": \"txtEmail\", \"confidence\": 0.95, \"reasoning\": \"same control type, same position\"}" }
              ]
            }
            """;
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(anthropicResponseJson, Encoding.UTF8, "application/json"),
            });
            var provider = new ClaudeHealingProvider(httpClient: new HttpClient(handler), apiKey: "sk-test-key");

            var result = await provider.ResolveAsync(Expected, BuildCurrentTree());

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("txtEmail", result.MatchedAutomationId);
            Assert.Equal(0.95, result.Confidence, precision: 3);
            Assert.NotNull(handler.LastRequest);
            Assert.True(handler.LastRequest!.Headers.Contains("x-api-key"));
        }

        [Fact]
        public async Task ClaudeHealingProvider_SkipsAThinkingBlockAndFindsTheTextBlock()
        {
            // Regression test: Claude Opus 5 thinks by default, which would put a
            // thinking block (no "text" property) before the text block if the
            // provider blindly read content[0].
            const string anthropicResponseJson = """
            {
              "content": [
                { "type": "thinking", "thinking": "Comparing structural properties...", "signature": "abc" },
                { "type": "text", "text": "{\"automationId\": \"txtEmail\", \"confidence\": 0.9, \"reasoning\": \"same position\"}" }
              ]
            }
            """;
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(anthropicResponseJson, Encoding.UTF8, "application/json"),
            });
            var provider = new ClaudeHealingProvider(httpClient: new HttpClient(handler), apiKey: "sk-test-key");

            var result = await provider.ResolveAsync(Expected, BuildCurrentTree());

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("txtEmail", result.MatchedAutomationId);
        }

        [Fact]
        public async Task ClaudeHealingProvider_SurfacesHttpErrorStatus_WithoutThrowing()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":{\"message\":\"invalid x-api-key\"}}"),
            });
            var provider = new ClaudeHealingProvider(httpClient: new HttpClient(handler), apiKey: "bad-key");

            var result = await provider.ResolveAsync(Expected, BuildCurrentTree());

            Assert.False(result.Success);
            Assert.Contains("401", result.ErrorMessage);
        }

        [Fact]
        public async Task GeminiHealingProvider_ParsesASuccessfulInteractionsApiShapedResponse()
        {
            const string geminiResponseJson = """
            {
              "id": "int_123",
              "status": "completed",
              "steps": [
                { "type": "user_input", "status": "done", "content": [ { "type": "text", "text": "..." } ] },
                { "type": "model_output", "status": "done", "content": [ { "type": "text", "text": "{\"automationId\": \"txtEmail\", \"confidence\": 0.7, \"reasoning\": \"structural match\"}" } ] }
              ]
            }
            """;
            var handler = new FakeHttpMessageHandler(req =>
            {
                Assert.True(req.Headers.Contains("x-goog-api-key"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(geminiResponseJson, Encoding.UTF8, "application/json"),
                };
            });
            var provider = new GeminiHealingProvider(httpClient: new HttpClient(handler), apiKey: "test-key");

            var result = await provider.ResolveAsync(Expected, BuildCurrentTree());

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("txtEmail", result.MatchedAutomationId);
        }

        [Fact]
        public async Task LlmHealingEvaluator_OnlyCallsProvidersThatReportIsAvailable()
        {
            var availableCalls = 0;
            var available = new FakeProvider("Available", isAvailable: true, onResolve: () => availableCalls++);
            var unavailable = new FakeProvider("Unavailable", isAvailable: false, onResolve: () => throw new InvalidOperationException("should never be called"));

            var results = await LlmHealingEvaluator.EvaluateAsync(new ILlmHealingProvider[] { available, unavailable }, Expected, BuildCurrentTree());

            Assert.Single(results);
            Assert.Equal("Available", results[0].ProviderName);
            Assert.Equal(1, availableCalls);
        }

        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
            public HttpRequestMessage? LastRequest { get; private set; }

            public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(_responder(request));
            }
        }

        private sealed class FakeProvider : ILlmHealingProvider
        {
            private readonly Action _onResolve;

            public FakeProvider(string name, bool isAvailable, Action onResolve)
            {
                Name = name;
                IsAvailable = isAvailable;
                _onResolve = onResolve;
            }

            public string Name { get; }
            public bool IsAvailable { get; }

            public Task<LlmHealingResult> ResolveAsync(UiElementInfo expected, UiElementInfo currentTree, CancellationToken cancellationToken = default)
            {
                _onResolve();
                return Task.FromResult(new LlmHealingResult { ProviderName = Name, Success = true });
            }
        }
    }
}
