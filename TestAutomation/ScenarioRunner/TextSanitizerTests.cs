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
        [Theory]
        [InlineData("Contact user@example.com for info", "Contact [REDACTED_EMAIL] for info")]
        [InlineData("admin.service+dev@sub.company.org", "[REDACTED_EMAIL]")]
        [InlineData("Card: 4111-2222-3333-4444", "Card: [REDACTED_CARD]")]
        [InlineData("Amex 3782 822463 10005 exp", "Amex [REDACTED_CARD] exp")]
        [InlineData("CardNum=4111222233334444", "CardNum=[REDACTED_CARD]")]
        [InlineData("SSN: 123-45-6789", "SSN: [REDACTED_SSN]")]
        [InlineData("Authorization: Bearer sk-proj-1234567890abcdef12345", "Authorization: Bearer [REDACTED_SECRET]")]
        [InlineData("GitHub token: ghp_1234567890abcdef1234567890", "GitHub token: [REDACTED_SECRET]")]
        [InlineData("JWT: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.doNotLeakThisSignature123", "JWT: [REDACTED_SECRET]")]
        [InlineData("password: SuperSecretPassword123!", "password:[REDACTED_SECRET]")]
        [InlineData("api_key = secret-key-value-999", "api_key=[REDACTED_SECRET]")]
        [InlineData("access_token: token123456", "access_token:[REDACTED_SECRET]")]
        public void SensitiveDataSanitizer_RedactsExpectedPatterns(string input, string expected)
        {
            var sanitized = SensitiveDataSanitizer.Redact(input);
            Assert.Equal(expected, sanitized);
        }

        [Fact]
        public void SensitiveDataSanitizer_NullOrEmpty_ReturnsAsIs()
        {
            Assert.Null(SensitiveDataSanitizer.Redact(null));
            Assert.Equal("", SensitiveDataSanitizer.Redact(""));
        }

        [Fact]
        public void LlmHealingPrompt_Default_RedactsCommonSensitivePatterns()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Submit card 4111-2222-3333-4444 for user@example.com",
                ClassName = "Form_password:secretPassword123",
                TestIntent = "Click button with Bearer abcdef1234567890abcdef",
            };
            var candidates = new List<CandidateScore>
            {
                new()
                {
                    CandidateId = "c0",
                    Candidate = new UiElementInfo
                    {
                        AutomationId = "btn-sk-12345678901234567890",
                        ControlType = "Button",
                        Name = "Submit user@example.com",
                        ClassName = "Form_card_4111-2222-3333-4444",
                    },
                    TotalScore = 0.95,
                    Components = new ScoreComponents(),
                },
            };

            var prompt = LlmHealingPrompt.Build(expected, candidates);

            // Sensitive data is redacted by default
            Assert.DoesNotContain("4111-2222-3333-4444", prompt);
            Assert.DoesNotContain("user@example.com", prompt);
            Assert.DoesNotContain("secretPassword123", prompt);
            Assert.DoesNotContain("abcdef1234567890abcdef", prompt);
            Assert.DoesNotContain("sk-12345678901234567890", prompt);

            // Redaction tokens are present
            Assert.Contains("[REDACTED_CARD]", prompt);
            Assert.Contains("[REDACTED_EMAIL]", prompt);
            Assert.Contains("[REDACTED_SECRET]", prompt);
        }

        [Fact]
        public void LlmHealingPrompt_WithPassThroughSanitizer_SendsTextUnchanged()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Submit sensitive-card-4111-2222-3333-4444",
                ClassName = "CardForm_secret-class",
                TestIntent = "Click button with sensitive-token-999",
            };
            var candidates = new List<CandidateScore>
            {
                new()
                {
                    CandidateId = "c0",
                    Candidate = new UiElementInfo
                    {
                        AutomationId = "btn-sensitive-token",
                        ControlType = "Button",
                        Name = "Submit user@example.com",
                        ClassName = "CardForm_secret-class",
                    },
                    TotalScore = 0.95,
                    Components = new ScoreComponents(),
                },
            };

            var prompt = LlmHealingPrompt.Build(expected, candidates, textSanitizer: SensitiveDataSanitizer.PassThrough);

            Assert.Contains("sensitive-card-4111-2222-3333-4444", prompt);
            Assert.Contains("CardForm_secret-class", prompt);
            Assert.Contains("sensitive-token-999", prompt);
            Assert.Contains("btn-sensitive-token", prompt);
            Assert.Contains("user@example.com", prompt);
        }

        [Fact]
        public void LlmHealingPrompt_WithCustomSanitizer_AppliesCustomSanitizer()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Custom sensitive text 123",
            };
            var candidates = new List<CandidateScore>
            {
                new()
                {
                    CandidateId = "c0",
                    Candidate = new UiElementInfo
                    {
                        ControlType = "Button",
                        Name = "Custom sensitive text 123",
                    },
                    TotalScore = 0.9,
                    Components = new ScoreComponents(),
                },
            };

            Func<string, string> customSanitizer = text => text.Replace("sensitive text 123", "[CUSTOM_MASK]");

            var prompt = LlmHealingPrompt.Build(expected, candidates, textSanitizer: customSanitizer);

            Assert.DoesNotContain("sensitive text 123", prompt);
            Assert.Contains("[CUSTOM_MASK]", prompt);
        }

        [Fact]
        public async Task HttpLlmHealingProvider_Default_SendsSanitizedPromptInHttpBody()
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
                delayAsync: (_, _) => Task.CompletedTask);

            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "john.doe@secret-corp.com",
            };
            var candidates = new List<CandidateScore>
            {
                new()
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
            Assert.Contains("[REDACTED_EMAIL]", capturedRequestBody);
        }

        [Fact]
        public void LlmIntentPlanningPrompt_Default_RedactsCommonSensitivePatterns()
        {
            var request = new IntentPlanningRequest
            {
                Goal = "Log in as admin@corp.local with secret password: P@ssw0rd123",
                TargetUrl = "https://internal.corp.local/secret-portal?key=ghp_1234567890abcdef12345",
                TestData = new Dictionary<string, string>
                {
                    { "Password", "password: P@ssw0rd123" },
                    { "ApiKey", "sk-12345678901234567890" },
                    { "CardNumber", "4111-2222-3333-4444" },
                },
            };

            var prompt = LlmIntentPlanningPrompt.Build(request);

            Assert.DoesNotContain("admin@corp.local", prompt);
            Assert.DoesNotContain("P@ssw0rd123", prompt);
            Assert.DoesNotContain("ghp_1234567890abcdef12345", prompt);
            Assert.DoesNotContain("sk-12345678901234567890", prompt);
            Assert.DoesNotContain("4111-2222-3333-4444", prompt);

            Assert.Contains("[REDACTED_EMAIL]", prompt);
            Assert.Contains("[REDACTED_SECRET]", prompt);
            Assert.Contains("[REDACTED_CARD]", prompt);
        }

        [Fact]
        public void LlmIntentPlanningPrompt_WithPassThroughSanitizer_SendsDataUnchanged()
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

            var prompt = LlmIntentPlanningPrompt.Build(request, textSanitizer: SensitiveDataSanitizer.PassThrough);

            Assert.Contains("admin@corp.local", prompt);
            Assert.Contains("P@ssw0rd123", prompt);
            Assert.Contains("https://internal.corp.local/secret-portal", prompt);
            Assert.Contains("sk-123456789", prompt);
        }

        [Fact]
        public async Task LlmIntentPlanner_Default_SendsSanitizedPromptInHttpBody()
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
                delayAsync: (_, _) => Task.CompletedTask);

            var request = new IntentPlanningRequest
            {
                Goal = "Log in with user user@corp.com and card 4111-2222-3333-4444",
            };

            var result = await planner.PlanAsync(request);

            Assert.NotNull(capturedRequestBody);
            Assert.DoesNotContain("user@corp.com", capturedRequestBody);
            Assert.DoesNotContain("4111-2222-3333-4444", capturedRequestBody);
            Assert.Contains("[REDACTED_EMAIL]", capturedRequestBody);
            Assert.Contains("[REDACTED_CARD]", capturedRequestBody);
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
