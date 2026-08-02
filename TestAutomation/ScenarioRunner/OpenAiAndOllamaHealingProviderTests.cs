using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
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
            var fakeResponse = @"{
                ""choices"": [
                    {
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""{\""candidateId\"":\""c0\"",\""confidence\"":0.95,\""reasoning\"":\""The candidate has exact matching properties.\""}""
                        }
                    }
                ]
            }";

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
            var fakeResponse = @"{
                ""message"": {
                    ""role"": ""assistant"",
                    ""content"": ""{\""candidateId\"":\""c0\"",\""confidence\"":0.88,\""reasoning\"":\""Matched edit field.\""}""
                }
            }";

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

        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly string _responseContent;
            private readonly HttpStatusCode _statusCode;

            public FakeHttpMessageHandler(string responseContent, HttpStatusCode statusCode)
            {
                _responseContent = responseContent;
                _statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_responseContent, Encoding.UTF8, "application/json"),
                };
                return Task.FromResult(response);
            }
        }
    }
}
