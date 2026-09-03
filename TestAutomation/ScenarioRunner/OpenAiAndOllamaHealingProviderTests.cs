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
        public async Task OpenAiHealingProvider_RecoversAnswer_FromAReasoningModelWithTruncatedContentAndASiblingReasoningField()
        {
            // Groq's openai/gpt-oss-120b (#378): message.content is a JSON object cut off inside
            // the "reasoning" string, and message.reasoning carries the chain-of-thought. The
            // provider must fold both in and salvage candidateId/confidence from the content.
            var fakeResponse = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "{\"candidateId\":\"c0\",\"confidence\":0.85,\"reasoning\":\"c0 matches the TabItem type and a name close to the target's 'Settings & Preferences'",
                            reasoning = "We need to pick the candidate matching the target. c0: TabItem, right class, close name. Thus best is c0. Confidence ~0.85. Provide JSON.",
                        },
                    },
                },
            });

            var provider = new OpenAiHealingProvider(new HttpClient(new FakeHttpMessageHandler(fakeResponse, HttpStatusCode.OK)), apiKey: "test-openai-key");

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.True(result.Success);
            Assert.Equal("c0", result.MatchedCandidateId);
            Assert.Equal(0.85, result.Confidence, precision: 3);
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
        public void OpenAiHealingProvider_TreatsEmptyApiKey_AsUnsetRatherThanShortCircuitingTheFallbackChain()
        {
            // Regression: the key chain is `apiKey ?? OPENAI_API_KEY ?? GITHUB_TOKEN`, and `??`
            // only continues on null. GitHub Actions substitutes an unset secret with an empty
            // string rather than omitting the variable, so `llm-smoke.yml` - which declares an
            // unset OPENAI_API_KEY alongside a real GITHUB_TOKEN - produced an empty key and
            // never reached the GITHUB_TOKEN it was written to use. The workflow failed with
            // "Provider must be available when token is configured."
            //
            // An empty key must therefore behave exactly like an absent one at every link.
            // This asserts equivalence rather than a fixed value so it does not depend on
            // whichever provider variables happen to be set on the machine running it, and it
            // deliberately does not mutate process-wide environment variables: GitHubModelsSmokeTests
            // wakes up when GITHUB_TOKEN appears and would start making real API calls if a test
            // running in parallel set it. End-to-end proof of the chain belongs to `llm-smoke.yml`.
            var fromEmpty = new OpenAiHealingProvider(apiKey: "");
            var fromNull = new OpenAiHealingProvider(apiKey: null);

            Assert.Equal(fromNull.IsAvailable, fromEmpty.IsAvailable);
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
        public void OpenAiHealingProvider_DefaultsToStandardEndpoint_WhenNotSpecified()
        {
            var provider = new OpenAiHealingProvider(apiKey: "test-key");
            Assert.Equal("https://api.openai.com/v1/chat/completions", provider.ApiUrl);
        }

        [Fact]
        public void OpenAiHealingProvider_NormalizesCustomEndpoint_AndAppendsChatCompletions()
        {
            var provider1 = new OpenAiHealingProvider(apiKey: "test-key", endpoint: "https://models.github.ai/inference");
            Assert.Equal("https://models.github.ai/inference/chat/completions", provider1.ApiUrl);

            var provider2 = new OpenAiHealingProvider(apiKey: "test-key", endpoint: "https://models.github.ai/inference/chat/completions");
            Assert.Equal("https://models.github.ai/inference/chat/completions", provider2.ApiUrl);

            var provider3 = new OpenAiHealingProvider(apiKey: "test-key", endpoint: "https://models.github.ai/inference/");
            Assert.Equal("https://models.github.ai/inference/chat/completions", provider3.ApiUrl);
        }

        [Fact]
        public async Task OpenAiHealingProvider_DispatchesRequestToCustomEndpoint()
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
            var provider = new OpenAiHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "test-token",
                endpoint: "https://models.github.ai/inference");

            await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.NotNull(handler.LastRequest);
            Assert.Equal("https://models.github.ai/inference/chat/completions", handler.LastRequest!.RequestUri!.ToString());
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
            // delayAsync is stubbed out because a timed-out attempt is retried like any other
            // transient failure: with the real backoff this single test waits ~800ms.
            var provider = new OpenAiHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "test-openai-key",
                timeout: TimeSpan.FromMilliseconds(50),
                delayAsync: (_, _) => Task.CompletedTask);

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
                timeout: TimeSpan.FromMilliseconds(50),
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task OpenAiHealingProvider_RetriesOn500_AndSucceeds()
        {
            var callCount = 0;
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
                                reasoning = "Recovered after retry.",
                            }),
                        },
                    },
                },
            });

            var handler = new FakeHttpMessageHandler((_, _) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("transient internal error")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(fakeResponse, Encoding.UTF8, "application/json")
                });
            });

            var provider = new OpenAiHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "test-openai-key",
                maxRetries: 2,
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.True(result.Success);
            Assert.Equal("c0", result.MatchedCandidateId);
            Assert.Equal(2, result.AttemptCount);
            Assert.Equal(2, callCount);
        }

        [Fact]
        public void OpenAiHealingProvider_Throws_WhenParametersAreInvalid()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OpenAiHealingProvider(timeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OpenAiHealingProvider(timeout: TimeSpan.FromSeconds(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OpenAiHealingProvider(totalTimeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OpenAiHealingProvider(maxRetries: -1));
            Assert.Throws<ArgumentException>(() => new OpenAiHealingProvider(timeout: TimeSpan.FromSeconds(20), totalTimeout: TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public async Task OllamaHealingProvider_RetriesOn503_AndSucceeds()
        {
            var callCount = 0;
            var fakeResponse = JsonSerializer.Serialize(new
            {
                message = new
                {
                    role = "assistant",
                    content = JsonSerializer.Serialize(new
                    {
                        candidateId = "c0",
                        confidence = 0.88,
                        reasoning = "Recovered after retry.",
                    }),
                },
            });

            var handler = new FakeHttpMessageHandler((_, _) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("model loading")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(fakeResponse, Encoding.UTF8, "application/json")
                });
            });

            var provider = new OllamaHealingProvider(
                httpClient: new HttpClient(handler),
                host: "http://localhost:11434",
                maxRetries: 2,
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.True(result.Success);
            Assert.Equal("c0", result.MatchedCandidateId);
            Assert.Equal(2, result.AttemptCount);
            Assert.Equal(2, callCount);
        }

        [Fact]
        public void OllamaHealingProvider_Throws_WhenParametersAreInvalid()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OllamaHealingProvider(timeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OllamaHealingProvider(timeout: TimeSpan.FromSeconds(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OllamaHealingProvider(totalTimeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OllamaHealingProvider(maxRetries: -1));
            Assert.Throws<ArgumentException>(() => new OllamaHealingProvider(timeout: TimeSpan.FromSeconds(20), totalTimeout: TimeSpan.FromSeconds(10)));
        }

        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
            public HttpRequestMessage? LastRequest { get; private set; }
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
                LastRequest = request;
                LastRequestBody = request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);

                return await _responder(request, cancellationToken);
            }
        }
    }
}
