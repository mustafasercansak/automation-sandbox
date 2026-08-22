using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
        // A minimal, successful Anthropic API response for mocking.
        private const string AnthropicSuccessResponse = """
        {
          "content": [
            { "type": "text", "text": "{\"candidateId\": \"c0\", \"confidence\": 0.95, \"reasoning\": \"same control type, same position\"}" }
          ]
        }
        """;

        // A minimal, successful Gemini API response for mocking.
        private const string GeminiSuccessResponse = """
        {
          "steps": [
            { "type": "model_output", "status": "done", "content": [ { "type": "text", "text": "{\"candidateId\": \"c0\", \"confidence\": 0.7, \"reasoning\": \"structural match\"}" } ] }
          ]
        }
        """;

        private static readonly UiElementInfo Expected = new()
        {
            ControlType = "Edit",
            AutomationId = "txtEmailAddress",
            ParentControlType = "Window",
            SiblingIndex = 2,
            SiblingCount = 7,
            BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
        };

        // A single-candidate shortlist, as SelfHealingResolver.ResolveAsync would build one:
        // the real AutomationId is "txtEmail", identified in the prompt by the opaque "c0".

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
        public void Build_IncludesStructuralFieldsOfExpected_ButRedactsItsStaleAutomationId()
        {
            var prompt = LlmHealingPrompt.Build(Expected, BuildShortlist());

            // The stale AutomationId must never reach the model, not even as "ignore this" -
            // instruction-following isn't reliable enough to prevent semantic leakage (e.g. a
            // stale id like "txtEmailAddress" nudging the model toward any Name that looks
            // vaguely email-related, observed live against Gemini on a WinForms accessibility
            // quirk). Redacting the field entirely closes that vector.
            Assert.DoesNotContain("txtEmailAddress", prompt);
            Assert.Contains("\"automationId\": \"txtEmail\"", prompt);
            Assert.Contains("\"candidateId\": \"c0\"", prompt);
        }

        [Fact]
        public void Build_RespectsExplicitPlatform_WhenSpecified()
        {
            var prompt = LlmHealingPrompt.Build(Expected, BuildShortlist(), platform: "web-playwright");
            Assert.Contains("You are diagnosing a broken UI test locator for a web-playwright application.", prompt);
        }

        [Fact]
        public void Build_DefaultsToWindowsUia_WhenPlatformNotSpecified()
        {
            var prompt = LlmHealingPrompt.Build(Expected, BuildShortlist());
            Assert.Contains("You are diagnosing a broken UI test locator for a windows-uia application.", prompt);
        }

        [Fact]
        public void Build_InfersWebPlaywright_WhenElementHasShadowDomOrIframeScope()
        {
            var shadowExpected = new UiElementInfo
            {
                ControlType = "Button",
                ClassName = "custom-btn [shadow-dom]",
            };
            var prompt = LlmHealingPrompt.Build(shadowExpected, BuildShortlist());
            Assert.Contains("You are diagnosing a broken UI test locator for a web-playwright application.", prompt);
        }

        [Fact]
        public void Build_PinningTest_StandardLightDomWebButtonCannotBeInferredWithoutExplicitPlatform()
        {
            // Pinning test: light-DOM web buttons (<button class="btn btn-primary">) mapped through
            // WebElementMapper produce ControlType="Button" and ClassName="btn btn-primary" with NO
            // scope annotation. Without an explicit platform parameter, the heuristic fallback
            // defaults to "windows-uia", demonstrating why callers that know the UI context
            // must pass platform: "web-playwright" explicitly.
            var lightDomWebButton = new UiElementInfo
            {
                ControlType = "Button",
                ClassName = "btn btn-primary",
            };

            var defaultPrompt = LlmHealingPrompt.Build(lightDomWebButton, BuildShortlist());
            Assert.Contains("You are diagnosing a broken UI test locator for a windows-uia application.", defaultPrompt);

            var explicitPrompt = LlmHealingPrompt.Build(lightDomWebButton, BuildShortlist(), platform: "web-playwright");
            Assert.Contains("You are diagnosing a broken UI test locator for a web-playwright application.", explicitPrompt);
        }

        [Fact]

        public void ParseResponse_HandlesPlainJson()
        {
            var (candidateId, confidence, reasoning) = LlmHealingPrompt.ParseResponse(
                "{\"candidateId\": \"c1\", \"confidence\": 0.92, \"reasoning\": \"same position\"}");
            Assert.Equal("c1", candidateId);
            Assert.Equal(0.92, confidence, precision: 3);
            Assert.Equal("same position", reasoning);
        }

        [Fact]

        public void ParseResponse_StripsMarkdownFencesAroundJson()
        {
            var raw = "```json\n{\"candidateId\": \"c1\", \"confidence\": 0.8, \"reasoning\": \"ok\"}\n```";
            var (candidateId, confidence, _) = LlmHealingPrompt.ParseResponse(raw);
            Assert.Equal("c1", candidateId);
            Assert.Equal(0.8, confidence, precision: 3);
        }

        [Fact]

        public void ParseResponse_SkipsNonJsonBracesBeforeTheResponseObject()
        {
            var raw = "Reasoning: matched inside {Panel}\n{\"candidateId\": \"c1\", \"confidence\": 0.8, \"reasoning\": \"same parent {Panel}\"}";
            var (candidateId, confidence, reasoning) = LlmHealingPrompt.ParseResponse(raw);
            Assert.Equal("c1", candidateId);
            Assert.Equal(0.8, confidence, precision: 3);
            Assert.Equal("same parent {Panel}", reasoning);
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
            var result = await provider.ResolveAsync(Expected, BuildShortlist());
            Assert.False(result.Success);
            Assert.Equal(0, callCount);
        }

        [Fact]

        public async Task ClaudeHealingProvider_ParsesASuccessfulAnthropicShapedResponse()
        {
            const string anthropicResponseJson = """
            {
              "content": [
                { "type": "text", "text": "{\"candidateId\": \"c0\", \"confidence\": 0.95, \"reasoning\": \"same control type, same position\"}" }

              ]
            }

            """;
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(anthropicResponseJson, Encoding.UTF8, "application/json"),
            });
            var provider = new ClaudeHealingProvider(httpClient: new HttpClient(handler), apiKey: "sk-test-key");
            var result = await provider.ResolveAsync(Expected, BuildShortlist());
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("c0", result.MatchedCandidateId);
            Assert.Equal("txtEmail", result.MatchedAutomationId);
            Assert.Equal(0.95, result.Confidence, precision: 3);
            Assert.NotNull(handler.LastRequest);
            Assert.True(handler.LastRequest!.Headers.Contains("x-api-key"));
        }

        [Fact]

        public async Task ClaudeHealingProvider_UsesConfiguredModelName_InRequestBody()
        {
            // Cost control: the model is overridable (constructor param or ANTHROPIC_MODEL
            // env var) so callers aren't stuck with whatever DefaultModel is set to.
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"content":[{"type":"text","text":"{\"candidateId\":\"c0\",\"confidence\":0.9,\"reasoning\":\"x\"}"}]}""",
                    Encoding.UTF8,
                    "application/json"),
            });
            var provider = new ClaudeHealingProvider(httpClient: new HttpClient(handler), apiKey: "sk-test-key", model: "claude-haiku-4-5");
            await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.NotNull(handler.LastRequestBody);
            using var doc = JsonDocument.Parse(handler.LastRequestBody);
            Assert.Equal("claude-haiku-4-5", doc.RootElement.GetProperty("model").GetString());
        }

        [Fact]

        public async Task ClaudeHealingProvider_DefaultsToTheCheapestModel_WhenNoneIsConfigured()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"content":[{"type":"text","text":"{\"candidateId\":\"c0\",\"confidence\":0.9,\"reasoning\":\"x\"}"}]}""",
                    Encoding.UTF8,
                    "application/json"),
            });
            var provider = new ClaudeHealingProvider(httpClient: new HttpClient(handler), apiKey: "sk-test-key");
            await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.NotNull(handler.LastRequestBody);
            using var doc = JsonDocument.Parse(handler.LastRequestBody);
            Assert.Equal(ExpectedModel("ANTHROPIC_MODEL", "claude-haiku-4-5-20251001"), doc.RootElement.GetProperty("model").GetString());
        }

        // "No model configured" is only true when the ambient environment supplies none, and CI
        // passes ANTHROPIC_MODEL/GEMINI_MODEL through from repository variables. Hardcoding the
        // compiled-in default made these tests depend on that being unset: the Gemini one passed
        // only while GEMINI_MODEL was stored as a secret (invisible to `vars.`) and failed the day
        // it became a real variable. Resolving the expectation the same way the provider does
        // keeps the fallback chain pinned without depending on how the runner is configured.
        private static string ExpectedModel(string environmentVariable, string compiledDefault)
        {
            var configured = Environment.GetEnvironmentVariable(environmentVariable);
            return string.IsNullOrEmpty(configured) ? compiledDefault : configured;
        }

        [Fact]

        public async Task ClaudeHealingProvider_TreatsEmptyStringModel_AsUnsetAndUsesDefault()
        {
            // Regression: GitHub Actions substitutes an unset repo Variable with an empty
            // string, not a missing env var - ANTHROPIC_MODEL="" must fall through to
            // DefaultModel, not send model: "" to the API (that 404'd live in CI). The
            // constructor's model parameter shares the same fallback logic as the env var
            // read, so exercising it here proves the fix without mutating process-wide state.
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"content":[{"type":"text","text":"{\"candidateId\":\"c0\",\"confidence\":0.9,\"reasoning\":\"x\"}"}]}""",
                    Encoding.UTF8,
                    "application/json"),
            });
            var provider = new ClaudeHealingProvider(httpClient: new HttpClient(handler), apiKey: "sk-test-key", model: "");
            await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.NotNull(handler.LastRequestBody);
            using var doc = JsonDocument.Parse(handler.LastRequestBody);

            // The empty string must not reach the API - it continues down the chain to
            // ANTHROPIC_MODEL and then the compiled default. See ExpectedModel above for why the
            // expectation is resolved rather than hardcoded.
            var sentModel = doc.RootElement.GetProperty("model").GetString();
            Assert.False(string.IsNullOrEmpty(sentModel), "An empty model must never be sent as \"\" - the API answered \"Model '' not found\".");
            Assert.Equal(ExpectedModel("ANTHROPIC_MODEL", "claude-haiku-4-5-20251001"), sentModel);
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
                { "type": "text", "text": "{\"candidateId\": \"c0\", \"confidence\": 0.9, \"reasoning\": \"same position\"}" }

              ]
            }

            """;
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(anthropicResponseJson, Encoding.UTF8, "application/json"),
            });
            var provider = new ClaudeHealingProvider(httpClient: new HttpClient(handler), apiKey: "sk-test-key");
            var result = await provider.ResolveAsync(Expected, BuildShortlist());
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
            var result = await provider.ResolveAsync(Expected, BuildShortlist());
            Assert.False(result.Success);
            Assert.Contains("401", result.ErrorMessage);
        }

        [Fact]

        public async Task ClaudeHealingProvider_CandidateIdNotInShortlist_ReturnsNullMatchedAutomationId()
        {
            const string anthropicResponseJson = """
            {
              "content": [
                { "type": "text", "text": "{\"candidateId\": \"doesNotExist\", \"confidence\": 0.9, \"reasoning\": \"n/a\"}" }

              ]
            }

            """;
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(anthropicResponseJson, Encoding.UTF8, "application/json"),
            });
            var provider = new ClaudeHealingProvider(httpClient: new HttpClient(handler), apiKey: "sk-test-key");
            var result = await provider.ResolveAsync(Expected, BuildShortlist());
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("doesNotExist", result.MatchedCandidateId);
            Assert.Null(result.MatchedAutomationId);
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
                { "type": "model_output", "status": "done", "content": [ { "type": "text", "text": "{\"candidateId\": \"c0\", \"confidence\": 0.7, \"reasoning\": \"structural match\"}" } ] }

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
            var result = await provider.ResolveAsync(Expected, BuildShortlist());
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(0.7, result.Confidence);
            Assert.Equal("txtEmail", result.MatchedAutomationId);
        }

        [Fact]

        public async Task GeminiHealingProvider_TreatsEmptyStringModel_AsUnsetAndUsesDefault()
        {
            // Regression: GitHub Actions substitutes an unset repo Variable with an empty
            // string, not a missing env var - GEMINI_MODEL="" must fall through to
            // DefaultModel, not send model: "" to the API (that 404'd live in CI: "Model ''
            // not found"). The constructor's model parameter shares the same fallback logic
            // as the env var read, so exercising it here proves the fix without mutating
            // process-wide state.
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"steps":[{"type":"model_output","status":"done","content":[{"type":"text","text":"{\"candidateId\":\"c0\",\"confidence\":0.9,\"reasoning\":\"x\"}"}]}]}""",
                    Encoding.UTF8,
                    "application/json"),
            });
            var provider = new GeminiHealingProvider(httpClient: new HttpClient(handler), apiKey: "test-key", model: "");
            await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.NotNull(handler.LastRequestBody);
            using var doc = JsonDocument.Parse(handler.LastRequestBody);

            // The empty string must not reach the API - it continues down the chain to
            // GEMINI_MODEL and then the compiled default. See ExpectedModel above for why the
            // expectation is resolved rather than hardcoded.
            var sentModel = doc.RootElement.GetProperty("model").GetString();
            Assert.False(string.IsNullOrEmpty(sentModel), "An empty model must never be sent as \"\" - the API answered \"Model '' not found\".");
            Assert.Equal(ExpectedModel("GEMINI_MODEL", "gemini-3.6-flash"), sentModel);
        }

        [Fact]

        public async Task LlmHealingEvaluator_OnlyCallsProvidersThatReportIsAvailable()
        {
            var availableCalls = 0;
            var available = new FakeProvider("Available", isAvailable: true, onResolve: () => availableCalls++);
            var unavailable = new FakeProvider("Unavailable", isAvailable: false, onResolve: () => throw new InvalidOperationException("should never be called"));
            var results = await LlmHealingEvaluator.EvaluateAsync(new ILlmHealingProvider[] { available, unavailable }, Expected, BuildShortlist());
            Assert.Single(results);
            Assert.Equal("Available", results[0].ProviderName);
            Assert.Equal(1, availableCalls);
        }

        [Fact]
        public async Task ClaudeHealingProvider_PassesExplicitPlatformToPrompt()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(AnthropicSuccessResponse, Encoding.UTF8, "application/json"),
            });
            var provider = new ClaudeHealingProvider(httpClient: new HttpClient(handler), apiKey: "sk-test-key");
            await provider.ResolveAsync(Expected, BuildShortlist(), platform: "web-playwright");

            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("web-playwright", handler.LastRequestBody);
        }

        [Fact]
        public async Task GeminiHealingProvider_PassesExplicitPlatformToPrompt()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(GeminiSuccessResponse, Encoding.UTF8, "application/json"),
            });
            var provider = new GeminiHealingProvider(httpClient: new HttpClient(handler), apiKey: "test-key");
            await provider.ResolveAsync(Expected, BuildShortlist(), platform: "web-playwright");

            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("web-playwright", handler.LastRequestBody);
        }

        [Fact]
        public async Task ClaudeHealingProvider_TimesOut_ReturnsTimeoutErrorMessage()
        {
            var handler = new FakeHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(1000, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            // delayAsync is stubbed out because a timed-out attempt is retried like any other
            // transient failure: with the real backoff this single test waits ~800ms.
            var provider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                timeout: TimeSpan.FromMilliseconds(50),
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GeminiHealingProvider_TimesOut_ReturnsTimeoutErrorMessage()
        {
            var handler = new FakeHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(1000, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var provider = new GeminiHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "test-key",
                timeout: TimeSpan.FromMilliseconds(50),
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ClaudeHealingProvider_RetriesOnTransient503_SucceedsOnSecondAttempt()
        {
            var callCount = 0;
            var handler = new FakeHttpMessageHandler(req =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("temporarily unavailable")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(AnthropicSuccessResponse, Encoding.UTF8, "application/json")
                };
            });

            var provider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                maxRetries: 2,
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.True(result.Success);
            Assert.Equal("c0", result.MatchedCandidateId);
            Assert.Equal(2, result.AttemptCount);
            Assert.Equal(2, callCount);
        }

        [Fact]
        public async Task ClaudeHealingProvider_RetriesExhausted_ReturnsFailureWithAttemptCount()
        {
            var callCount = 0;
            var handler = new FakeHttpMessageHandler(req =>
            {
                callCount++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("server error")
                };
            });

            var provider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                maxRetries: 2,
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Equal(3, result.AttemptCount);
            Assert.Equal(3, callCount);
            Assert.Contains("500", result.ErrorMessage);
        }

        [Fact]
        public async Task ClaudeHealingProvider_NonTransient401_FailsFastWithoutRetry()
        {
            var callCount = 0;
            var handler = new FakeHttpMessageHandler(req =>
            {
                callCount++;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("invalid api key")
                };
            });

            var provider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                maxRetries: 2,
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Equal(1, result.AttemptCount);
            Assert.Equal(1, callCount);
            Assert.Contains("401", result.ErrorMessage);
        }

        [Fact]
        public async Task ClaudeHealingProvider_RetryAfterExceedsCeiling_FailsFastWithoutRetry()
        {
            var callCount = 0;
            var handler = new FakeHttpMessageHandler(req =>
            {
                callCount++;
                var response = new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("rate limited")
                };
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3600));
                return response;
            });

            var provider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                maxRetries: 2,
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Equal(1, result.AttemptCount);
            Assert.Equal(1, callCount);
            Assert.Contains("exceeds maximum delay threshold", result.ErrorMessage);
        }

        [Fact]
        public async Task RetryAfterJustAboveTheDefaultCeiling_StillFailsFast()
        {
            // #110. Groq answers with 11-13s under load - only a second or two over the 10s ceiling,
            // but the rule is a threshold, so the request is abandoned rather than waited out. This
            // pins the default behaviour so raising the ceiling elsewhere cannot silently relax it.
            var callCount = 0;
            var handler = new FakeHttpMessageHandler(_ =>
            {
                callCount++;
                var response = new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("rate limited")
                };
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
                return response;
            });

            var provider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                maxRetries: 2,
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Equal(1, callCount);
            Assert.Contains("exceeds maximum delay threshold", result.ErrorMessage);
        }

        [Fact]
        public async Task RetryAfterAboveTheDefaultCeiling_IsHonouredWhenTheCallerRaisesIt()
        {
            // A batch benchmark would rather wait twelve seconds than lose the scenario. The override
            // makes that the caller's decision instead of a constant shared with interactive healing.
            var callCount = 0;
            var delays = new List<TimeSpan>();
            var handler = new FakeHttpMessageHandler(_ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    var limited = new HttpResponseMessage((HttpStatusCode)429)
                    {
                        Content = new StringContent("rate limited")
                    };
                    limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
                    return limited;
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"candidateId\\\":\\\"c1\\\",\\\"confidence\\\":0.9,\\\"reasoning\\\":\\\"ok\\\"}\"}]}")
                };
            });

            var provider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                maxRetries: 2,
                delayAsync: (d, _) => { delays.Add(d); return Task.CompletedTask; })
            {
                MaxRetryAfterOverride = TimeSpan.FromSeconds(20),
            };

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, callCount);

            // The wait honours the server's own number rather than the exponential backoff.
            Assert.Single(delays);
            Assert.Equal(TimeSpan.FromSeconds(12), delays[0]);
        }

        [Fact]
        public async Task ClaudeHealingProvider_TotalTimeoutCeiling_TerminatesOperation()
        {
            var handler = new FakeHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(1000, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var provider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                timeout: TimeSpan.FromMilliseconds(50),
                totalTimeout: TimeSpan.FromMilliseconds(80),
                maxRetries: 3,
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Contains("timed out after", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TotalTimeoutOverride_Unset_LeavesTheInteractiveCeilingUnchanged()
        {
            // #127. Without an override, a call that would blow the constructor's total timeout
            // must still be cancelled exactly as before - the new property must not weaken the
            // default just by existing.
            var handler = new FakeHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(1000, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var provider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                timeout: TimeSpan.FromMilliseconds(50),
                totalTimeout: TimeSpan.FromMilliseconds(80),
                maxRetries: 3,
                delayAsync: (_, _) => Task.CompletedTask);

            Assert.Null(provider.TotalTimeoutOverride);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Contains("timed out after", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TotalTimeoutOverride_Set_LetsAnHonouredRetryAfterWaitComplete()
        {
            // #127. Raising MaxRetryAfterOverride alone (#110) told the transport it was allowed
            // to wait out a Retry-After - but the total timeout wrapping the whole operation was
            // still the tight interactive default, so the wait itself got cancelled before it
            // finished. The cancellation reads identically to a dead endpoint in the report, which
            // is what made the widespread "Request timed out" failures in run 32153874838
            // indistinguishable from genuine outages until this was traced.
            var callCount = 0;
            var handler = new FakeHttpMessageHandler(_ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // .NET Framework's HttpResponseMessage.Content is null until assigned, unlike
                    // .NET 8's non-null default - LlmHttpTransport reads Content unconditionally, so
                    // leaving this unset throws NullReferenceException only under net48.
                    var limited = new HttpResponseMessage((HttpStatusCode)429)
                    {
                        Content = new StringContent("rate limited"),
                    };
                    limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(150));
                    return limited;
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"candidateId\\\":\\\"c1\\\",\\\"confidence\\\":0.9,\\\"reasoning\\\":\\\"ok\\\"}\"}]}")
                };
            });

            var provider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                timeout: TimeSpan.FromMilliseconds(50),
                totalTimeout: TimeSpan.FromMilliseconds(80), // too tight for a 150ms honoured wait
                maxRetries: 2)
            {
                TotalTimeoutOverride = TimeSpan.FromMilliseconds(500),
            };

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, callCount);
        }

        [Fact]
        public async Task CloudflareParseFailure_RecordsBoundedRawResponseDiagnostic()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"content\":\"not-json-response\"}}]}"),
            });
            var provider = new OpenAiHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "test-cloudflare-key",
                name: "Cloudflare",
                maxOutputTokens: 2000);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Contains("No JSON object found", result.ErrorMessage);
            Assert.Contains("Raw response:", result.ErrorMessage);
            Assert.Contains("not-json-response", result.ErrorMessage);
        }

        [Fact]
        public async Task NonCloudflareParseFailure_RecordsBoundedRawResponseDiagnosticWithoutCredentials()
        {
            const string apiKey = "test-openai-secret";
            var malformedText = "not-json-response-" + new string('x', 5000) + "-response-tail";
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"content\":\"" + malformedText + "\"}}]}")
            });
            var provider = new OpenAiHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: apiKey,
                name: "OpenRouter");

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.False(result.Success);
            Assert.Contains("No JSON object found", result.ErrorMessage);
            Assert.Contains("Raw response:", result.ErrorMessage);
            Assert.Contains("not-json-response", result.ErrorMessage);
            Assert.Contains("...<truncated>", result.ErrorMessage);
            Assert.DoesNotContain("response-tail", result.ErrorMessage);
            Assert.DoesNotContain(apiKey, result.ErrorMessage);
        }

        [Fact]
        public async Task ClaudeHealingProvider_CallerCancellation_TakesPrecedence()
        {
            var handler = new FakeHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK));
            var provider = new ClaudeHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key");

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await provider.ResolveAsync(Expected, BuildShortlist(), cancellationToken: cts.Token);

            Assert.False(result.Success);
            Assert.Equal("Operation was canceled.", result.ErrorMessage);
        }

        [Fact]
        public void ClaudeHealingProvider_Throws_WhenParametersAreInvalid()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClaudeHealingProvider(timeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClaudeHealingProvider(timeout: TimeSpan.FromSeconds(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClaudeHealingProvider(totalTimeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClaudeHealingProvider(maxRetries: -1));
            Assert.Throws<ArgumentException>(() => new ClaudeHealingProvider(timeout: TimeSpan.FromSeconds(20), totalTimeout: TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public async Task GeminiHealingProvider_RetriesOn429_AndSucceeds()
        {
            var callCount = 0;
            var handler = new FakeHttpMessageHandler(req =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new HttpResponseMessage((HttpStatusCode)429)
                    {
                        Content = new StringContent("rate limit exceeded")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(GeminiSuccessResponse, Encoding.UTF8, "application/json")
                };
            });

            var provider = new GeminiHealingProvider(
                httpClient: new HttpClient(handler),
                apiKey: "test-key",
                maxRetries: 2,
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await provider.ResolveAsync(Expected, BuildShortlist());

            Assert.True(result.Success);
            Assert.Equal("c0", result.MatchedCandidateId);
            Assert.Equal(2, result.AttemptCount);
            Assert.Equal(2, callCount);
        }

        [Fact]
        public void GeminiHealingProvider_Throws_WhenParametersAreInvalid()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeminiHealingProvider(timeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeminiHealingProvider(timeout: TimeSpan.FromSeconds(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeminiHealingProvider(totalTimeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeminiHealingProvider(maxRetries: -1));
            Assert.Throws<ArgumentException>(() => new GeminiHealingProvider(timeout: TimeSpan.FromSeconds(20), totalTimeout: TimeSpan.FromSeconds(10)));
        }

        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
            public HttpRequestMessage? LastRequest { get; private set; }

            // Captured eagerly, while request.Content is still alive - the providers wrap
            // their request in `using var request = ...`, which disposes Content the moment
            // ResolveAsync returns, so reading it back from LastRequest afterwards throws
            // ObjectDisposedException. This plain string survives that disposal.
            public string? LastRequestBody { get; private set; }

            public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = (req, _) => Task.FromResult(responder(req));
            }

            public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
            {
                _responder = responder;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastRequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                return await _responder(request, cancellationToken);
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
            public Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                _onResolve();
                return Task.FromResult(new LlmHealingResult { ProviderName = Name, Success = true });
            }
        }
    }
}
