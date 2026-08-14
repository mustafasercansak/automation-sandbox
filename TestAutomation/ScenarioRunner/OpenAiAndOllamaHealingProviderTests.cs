using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LlmHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class OpenAiAndOllamaHealingProviderTests
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

        private static List<CandidateScore> BuildShortlist()
        {
            var candidate = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "txtEmail",
                ParentControlType = "Window",
                SiblingIndex = 2,
                SiblingCount = 7,
                BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
            };

            return new List<CandidateScore>
            {
                new()
                {
                    CandidateId = "c0",
                    Candidate = candidate,
                    TotalScore = 0.9,
                    Components = new ScoreComponents
                    {
                        ControlTypeScore = 1.0,
                        ParentControlTypeScore = 1.0,
                        SiblingPositionScore = 1.0,
                        NameScore = 1.0,
                        PositionScore = 1.0,
                    },
                },
            };
        }

        [Fact]
        public async Task OpenAiHealingProvider_ParsesValidResponse_AndMatchesShortlistCandidate()
        {
            var fakeResponse = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = JsonSerializer.Serialize(new
                            {
                                candidateId = "c0",
                                confidence = 0.95,
                                reasoning = "The candidate has exact matching properties.",
                            }),
                        },
                    },
                },
            });

            var handler = new FakeHttpMessageHandler(fakeResponse, HttpStatusCode.OK);
            var httpClient = new HttpClient(handler);
            var provider = new OpenAiHealingProvider(httpClient, apiKey: "test-openai-key");

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.True(result.Success);
            Assert.Equal("OpenAI", result.ProviderName);
            Assert.Equal("c0", result.MatchedCandidateId);
            Assert.Equal("txtEmail", result.MatchedAutomationId);
            Assert.Equal(0.95, result.Confidence);
        }

        [Fact]
        public async Task OpenAiHealingProvider_WhenApiKeyMissing_ReturnsUnavailable()
        {
            var provider = new OpenAiHealingProvider(apiKey: "");
            Assert.False(provider.IsAvailable);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());
            Assert.False(result.Success);
            Assert.Contains("OPENAI_API_KEY is not set", result.ErrorMessage);
        }

        [Fact]
        public async Task OllamaHealingProvider_ParsesValidResponse_AndMatchesShortlistCandidate()
        {
            var fakeResponse = JsonSerializer.Serialize(new
            {
                message = new
                {
                    role = "assistant",
                    content = JsonSerializer.Serialize(new
                    {
                        candidateId = "c0",
                        confidence = 0.88,
                        reasoning = "Matched edit field.",
                    }),
                },
            });

            var handler = new FakeHttpMessageHandler(fakeResponse, HttpStatusCode.OK);
            var httpClient = new HttpClient(handler);
            var provider = new OllamaHealingProvider(httpClient, host: "http://localhost:11434");

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.True(result.Success);
            Assert.Equal("Ollama", result.ProviderName);
            Assert.Equal("c0", result.MatchedCandidateId);
            Assert.Equal("txtEmail", result.MatchedAutomationId);
            Assert.Equal(0.88, result.Confidence);
        }

        [Fact]
        public async Task OpenAiHealingProvider_PassesExplicitPlatformToPrompt()
        {
            var fakeResponse = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "{\"candidateId\": \"c0\", \"confidence\": 0.9, \"reasoning\": \"ok\"}",
                        },
                    },
                },
            });

            var handler = new FakeHttpMessageHandler(fakeResponse, HttpStatusCode.OK);
            var provider = new OpenAiHealingProvider(new HttpClient(handler), apiKey: "test-openai-key");

            await provider.ResolveAsync(Expected, BuildShortlist(), platform: "web-playwright");

            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("web-playwright", handler.LastRequestBody);
        }

        [Fact]
        public async Task OllamaHealingProvider_PassesExplicitPlatformToPrompt()
        {
            var fakeResponse = JsonSerializer.Serialize(new
            {
                message = new
                {
                    role = "assistant",
                    content = "{\"candidateId\": \"c0\", \"confidence\": 0.9, \"reasoning\": \"ok\"}",
                },
            });

            var handler = new FakeHttpMessageHandler(fakeResponse, HttpStatusCode.OK);
            var provider = new OllamaHealingProvider(new HttpClient(handler), host: "http://localhost:11434");

            await provider.ResolveAsync(Expected, BuildShortlist(), platform: "web-playwright");

            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("web-playwright", handler.LastRequestBody);
        }

        [Fact]
        public async Task OpenAiHealingProvider_TimesOut_ReturnsTimeoutErrorMessage()
        {
            var handler = new FakeHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(1000, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var provider = new OpenAiHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "test-openai-key",
                timeout: TimeSpan.FromMilliseconds(50));

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task OllamaHealingProvider_TimesOut_ReturnsTimeoutErrorMessage()
        {
            var handler = new FakeHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(1000, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var provider = new OllamaHealingProvider(
                httpClient: new HttpClient(handler),
                host: "http://localhost:11434",
                timeout: TimeSpan.FromMilliseconds(50));

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void OpenAiHealingProvider_Throws_WhenTimeoutIsZeroOrNegative()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OpenAiHealingProvider(timeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OpenAiHealingProvider(timeout: TimeSpan.FromSeconds(-1)));
        }

        [Fact]
        public void OllamaHealingProvider_Throws_WhenTimeoutIsZeroOrNegative()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OllamaHealingProvider(timeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OllamaHealingProvider(timeout: TimeSpan.FromSeconds(-1)));
        }

        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
            public string? LastRequestBody { get; private set; }

            public FakeHttpMessageHandler(string responseContent, HttpStatusCode statusCode)
            {
                _responder = (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(responseContent, Encoding.UTF8, "application/json"),
                });
            }

            public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
            {
                _responder = responder;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestBody = request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);

                return await _responder(request, cancellationToken);
            }
        }
    }
}
