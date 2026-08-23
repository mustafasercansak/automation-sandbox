using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IntentAutomation;
using LlmHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class TextSanitizerTests
    {
        [Fact]
        public void LlmHealingPrompt_WithoutTextSanitizer_SendsTextUnchanged()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Submit sensitive-card-4111",
                ClassName = "CardForm_secret-class",
                TestIntent = "Click button with sensitive-token-999",
            };
            var candidates = new List<CandidateScore>
            {
                new CandidateScore
                {
                    CandidateId = "c0",
                    Candidate = new UiElementInfo
                    {
                        AutomationId = "btn-sensitive-token",
                        ControlType = "Button",
                        Name = "Submit card-4111",
                        ClassName = "CardForm_secret-class",
                    },
                    TotalScore = 0.95,
                    Components = new ScoreComponents(),
                },
            };

            var prompt = LlmHealingPrompt.Build(expected, candidates);

            Assert.Contains("sensitive-card-4111", prompt);
            Assert.Contains("CardForm_secret-class", prompt);
            Assert.Contains("sensitive-token-999", prompt);
            Assert.Contains("btn-sensitive-token", prompt);
        }

        [Fact]
        public void LlmHealingPrompt_WithTextSanitizer_RedactsSensitiveFields()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Submit sensitive-card-4111",
                ClassName = "CardForm_secret-class",
                TestIntent = "Click button with sensitive-token-999",
            };
            var candidates = new List<CandidateScore>
            {
                new CandidateScore
                {
                    CandidateId = "c0",
                    Candidate = new UiElementInfo
                    {
                        AutomationId = "btn-sensitive-token",
                        ControlType = "Button",
                        Name = "Submit card-4111",
                        ClassName = "CardForm_secret-class",
                    },
                    TotalScore = 0.95,
                    Components = new ScoreComponents(),
                },
            };

            Func<string, string> sanitizer = text => text
                .Replace("sensitive-card-4111", "[REDACTED_CARD]")
                .Replace("secret-class", "[REDACTED_CLASS]")
                .Replace("sensitive-token-999", "[REDACTED_TOKEN]")
                .Replace("btn-sensitive-token", "[REDACTED_ID]");

            var prompt = LlmHealingPrompt.Build(expected, candidates, platform: "windows-uia", textSanitizer: sanitizer);

            Assert.DoesNotContain("sensitive-card-4111", prompt);
            Assert.DoesNotContain("secret-class", prompt);
            Assert.DoesNotContain("sensitive-token-999", prompt);
            Assert.DoesNotContain("btn-sensitive-token", prompt);

            Assert.Contains("[REDACTED_CARD]", prompt);
            Assert.Contains("[REDACTED_CLASS]", prompt);
            Assert.Contains("[REDACTED_TOKEN]", prompt);
            Assert.Contains("[REDACTED_ID]", prompt);
        }

        [Fact]
        public async Task HttpLlmHealingProvider_WithTextSanitizer_SendsSanitizedPromptInHttpBody()
        {
            string? capturedRequestBody = null;
            var handler = new MockHttpMessageHandler((request, _) =>
            {
                capturedRequestBody = request.Content?.ReadAsStringAsync().Result;
                var anthropicResponse = "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"candidateId\\\":\\\"c0\\\",\\\"confidence\\\":0.9,\\\"reasoning\\\":\\\"Matched\\\"}\"}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(anthropicResponse),
                });
            });

            using var httpClient = new HttpClient(handler);
            var provider = new ClaudeHealingProvider(
                apiKey: "test-api-key",
                httpClient: httpClient,
                delayAsync: (_, _) => Task.CompletedTask)
            {
                TextSanitizer = text => text.Replace("john.doe@secret-corp.com", "[EMAIL_REDACTED]"),
            };

            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "john.doe@secret-corp.com",
            };
            var candidates = new List<CandidateScore>
            {
                new CandidateScore
                {
                    CandidateId = "c0",
                    Candidate = new UiElementInfo { ControlType = "Edit", Name = "Email: john.doe@secret-corp.com" },
                    TotalScore = 0.85,
                    Components = new ScoreComponents(),
                },
            };

            var result = await provider.ResolveAsync(expected, candidates);

            Assert.True(result.Success);
            Assert.NotNull(capturedRequestBody);
            Assert.DoesNotContain("john.doe@secret-corp.com", capturedRequestBody);
            Assert.Contains("[EMAIL_REDACTED]", capturedRequestBody);
        }

        [Fact]
        public void LlmIntentPlanningPrompt_WithoutTextSanitizer_SendsDataUnchanged()
        {
            var request = new IntentPlanningRequest
            {
                Goal = "Log in as admin@corp.local with secret password P@ssw0rd123",
                TargetUrl = "https://internal.corp.local/secret-portal",
                TestData = new Dictionary<string, string>
                {
                    { "Password", "P@ssw0rd123" },
                    { "ApiKey", "sk-123456789" },
                },
            };

            var prompt = LlmIntentPlanningPrompt.Build(request);

            Assert.Contains("admin@corp.local", prompt);
            Assert.Contains("P@ssw0rd123", prompt);
            Assert.Contains("https://internal.corp.local/secret-portal", prompt);
            Assert.Contains("sk-123456789", prompt);
        }

        [Fact]
        public void LlmIntentPlanningPrompt_WithTextSanitizer_SanitizesGoalUrlAndTestData()
        {
            var request = new IntentPlanningRequest
            {
                Goal = "Log in as admin@corp.local with secret password P@ssw0rd123",
                TargetUrl = "https://internal.corp.local/secret-portal",
                TestData = new Dictionary<string, string>
                {
                    { "Password", "P@ssw0rd123" },
                    { "ApiKey", "sk-123456789" },
                },
            };

            Func<string, string> sanitizer = text => text
                .Replace("admin@corp.local", "[USER]")
                .Replace("P@ssw0rd123", "[REDACTED_PW]")
                .Replace("https://internal.corp.local/secret-portal", "https://example.com/app")
                .Replace("sk-123456789", "[REDACTED_KEY]");

            var prompt = LlmIntentPlanningPrompt.Build(request, sanitizer);

            Assert.DoesNotContain("admin@corp.local", prompt);
            Assert.DoesNotContain("P@ssw0rd123", prompt);
            Assert.DoesNotContain("https://internal.corp.local/secret-portal", prompt);
            Assert.DoesNotContain("sk-123456789", prompt);

            Assert.Contains("[USER]", prompt);
            Assert.Contains("[REDACTED_PW]", prompt);
            Assert.Contains("https://example.com/app", prompt);
            Assert.Contains("[REDACTED_KEY]", prompt);
        }

        [Fact]
        public async Task LlmIntentPlanner_WithTextSanitizer_SendsSanitizedPromptInHttpBody()
        {
            string? capturedRequestBody = null;
            var handler = new MockHttpMessageHandler((request, _) =>
            {
                capturedRequestBody = request.Content?.ReadAsStringAsync().Result;
                var mockResponse = "{\"steps\":[{\"actionType\":\"Fill\",\"targetDescription\":\"Email input\",\"value\":\"[EMAIL]\"}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"content\":[{\"type\":\"text\",\"text\":" + System.Text.Json.JsonSerializer.Serialize(mockResponse) + "}]}"),
                });
            });

            using var httpClient = new HttpClient(handler);
            var planner = new LlmIntentPlanner(
                httpClient: httpClient,
                apiKey: "test-api-key",
                delayAsync: (_, _) => Task.CompletedTask)
            {
                TextSanitizer = text => text.Replace("confidential-order-987", "[MASKED_ORDER]"),
            };

            var request = new IntentPlanningRequest
            {
                Goal = "Cancel order confidential-order-987",
            };

            var result = await planner.PlanAsync(request);

            Assert.NotNull(capturedRequestBody);
            Assert.DoesNotContain("confidential-order-987", capturedRequestBody);
            Assert.Contains("[MASKED_ORDER]", capturedRequestBody);
        }

        private sealed class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _handler(request, cancellationToken);
            }
        }
    }
}
