using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntentAutomation;
using Xunit;
namespace ScenarioRunner
{
    // Pure-logic tests for LlmIntentPlanner/LlmIntentPlanningPrompt, run against a fake
    // HttpMessageHandler so they need no API key and no network - part of the required
    // test suite, unlike LlmHealingEvaluationTests' live provider comparison.

    public class LlmIntentPlannerTests
    {
        private static IntentPlanningRequest BuildRequest() => new()
        {
            Name = "Create customer",
            Goal = "Create a customer record with a valid email",
            TargetUrl = "https://example.test/customers",
            TestData = new Dictionary<string, string> { ["email"] = "jane.doe@example.com" },
        };

        [Fact]
        public async Task WithoutApiKey_DegradesToDeterministicPlanner_WithoutCallingHttp()
        {
            var callCount = 0;
            var handler = new FakeHttpMessageHandler(_ => { callCount++; return new HttpResponseMessage(HttpStatusCode.OK); });
            var planner = new LlmIntentPlanner(httpClient: new HttpClient(handler), apiKey: "");

            Assert.False(planner.IsAvailable);
            var result = await planner.PlanAsync(BuildRequest());

            Assert.Equal(0, callCount);
            Assert.Contains(result.Diagnostics, d => d.Contains("ANTHROPIC_API_KEY"));
            Assert.Contains(result.Scenario.Steps, step => step.ActionType == IntentActionType.Click && step.LocatorKey == "Action.PrimarySubmit");
        }

        [Fact]
        public async Task ParsesAWellFormedScenario_FromTheModelResponse()
        {
            const string anthropicResponseJson = """
            {
              "content": [
                { "type": "text", "text": "{\"steps\": [{\"actionType\": \"Navigate\", \"targetDescription\": \"target page\", \"value\": \"https://example.test/customers\", \"testIntent\": \"open the page\", \"expectedOutcome\": \"page loaded\", \"locatorKey\": \"Navigation.TargetPage\"}, {\"actionType\": \"Fill\", \"targetDescription\": \"email field\", \"value\": \"jane.doe@example.com\", \"testIntent\": \"enter email\", \"expectedOutcome\": \"email filled\", \"locatorKey\": \"Field.Email\"}, {\"actionType\": \"Click\", \"targetDescription\": \"save button\", \"value\": \"\", \"testIntent\": \"submit\", \"expectedOutcome\": \"record created\", \"locatorKey\": \"Action.PrimarySubmit\"}]}" }
              ]
            }
            """;
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(anthropicResponseJson, Encoding.UTF8, "application/json"),
            });
            var planner = new LlmIntentPlanner(httpClient: new HttpClient(handler), apiKey: "sk-test-key");

            var result = await planner.PlanAsync(BuildRequest());

            Assert.Empty(result.Diagnostics);
            Assert.Equal(3, result.Scenario.Steps.Count);
            Assert.Equal(IntentActionType.Navigate, result.Scenario.Steps[0].ActionType);
            Assert.Equal(IntentActionType.Fill, result.Scenario.Steps[1].ActionType);
            Assert.Equal(IntentActionType.Click, result.Scenario.Steps[2].ActionType);
            Assert.Equal(new[] { 1, 2, 3 }, result.Scenario.Steps.ConvertAll(step => step.Order));
        }

        [Fact]
        public async Task ParsesStructuredAssertion_FromModelResponse()
        {
            const string anthropicResponseJson = """
            {
              "content": [
                { "type": "text", "text": "{\"steps\": [{\"actionType\": \"Assert\", \"targetDescription\": \"order total\", \"value\": \"\", \"testIntent\": \"verify total\", \"expectedOutcome\": \"Order total is $125\", \"locatorKey\": \"Assert.OrderTotal\", \"assertionKind\": \"TextEquals\", \"expectedValue\": \"$125\"}]}" }
              ]
            }
            """;
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(anthropicResponseJson, Encoding.UTF8, "application/json"),
            });
            var planner = new LlmIntentPlanner(httpClient: new HttpClient(handler), apiKey: "sk-test-key");

            var result = await planner.PlanAsync(BuildRequest());

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.Scenario.Steps);
            var step = result.Scenario.Steps[0];
            Assert.Equal(IntentActionType.Assert, step.ActionType);
            Assert.Equal(AssertionKind.TextEquals, step.AssertionKind);
            Assert.Equal("$125", step.ExpectedValue);
        }

        [Fact]
        public async Task InvalidActionType_DegradesToDeterministicPlanner()
        {
            const string anthropicResponseJson = """
            {
              "content": [
                { "type": "text", "text": "{\"steps\": [{\"actionType\": \"Teleport\", \"targetDescription\": \"email field\", \"value\": \"x\", \"testIntent\": \"x\", \"expectedOutcome\": \"x\", \"locatorKey\": \"Field.Email\"}]}" }
              ]
            }
            """;
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(anthropicResponseJson, Encoding.UTF8, "application/json"),
            });
            var planner = new LlmIntentPlanner(httpClient: new HttpClient(handler), apiKey: "sk-test-key");

            var result = await planner.PlanAsync(BuildRequest());

            Assert.Contains(result.Diagnostics, d => d.Contains("LLM planning failed"));
            // Falls back to the deterministic planner's own well-formed result.
            Assert.Contains(result.Scenario.Steps, step => step.ActionType == IntentActionType.Fill && step.LocatorKey == "Field.Email");
        }

        [Fact]
        public async Task EmptyStepsArray_DegradesToDeterministicPlanner()
        {
            const string anthropicResponseJson = """
            {
              "content": [
                { "type": "text", "text": "{\"steps\": []}" }
              ]
            }
            """;
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(anthropicResponseJson, Encoding.UTF8, "application/json"),
            });
            var planner = new LlmIntentPlanner(httpClient: new HttpClient(handler), apiKey: "sk-test-key");

            var result = await planner.PlanAsync(BuildRequest());

            Assert.Contains(result.Diagnostics, d => d.Contains("LLM planning failed"));
            Assert.NotEmpty(result.Scenario.Steps);
        }

        [Fact]
        public async Task HttpErrorStatus_DegradesToDeterministicPlanner_WithoutThrowing()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":{\"message\":\"invalid x-api-key\"}}"),
            });
            var planner = new LlmIntentPlanner(httpClient: new HttpClient(handler), apiKey: "bad-key");

            var result = await planner.PlanAsync(BuildRequest());

            Assert.Contains(result.Diagnostics, d => d.Contains("HTTP 401"));
            Assert.NotEmpty(result.Scenario.Steps);
        }

        [Fact]
        public void Plan_BlocksSynchronously_ForIIntentPlannerConformance()
        {
            var handler = new FakeHttpMessageHandler(_ => { throw new InvalidOperationException("should not be called without an API key"); });
            IIntentPlanner planner = new LlmIntentPlanner(httpClient: new HttpClient(handler), apiKey: "");

            var result = planner.Plan(BuildRequest());

            Assert.NotEmpty(result.Scenario.Steps);
        }

        [Fact]
        public async Task RejectsEmptyGoal_BeforeCallingHttp()
        {
            var callCount = 0;
            var handler = new FakeHttpMessageHandler(_ => { callCount++; return new HttpResponseMessage(HttpStatusCode.OK); });
            var planner = new LlmIntentPlanner(httpClient: new HttpClient(handler), apiKey: "sk-test-key");

            await Assert.ThrowsAsync<ArgumentException>(() => planner.PlanAsync(new IntentPlanningRequest { Goal = " " }));
            Assert.Equal(0, callCount);
        }

        [Fact]
        public async Task LlmIntentPlanner_TimesOut_DegradesToDeterministicPlanner()
        {
            var handler = new FakeHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(1000, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            // delayAsync is stubbed out because a timed-out attempt is retried like any other
            // transient failure: with the real backoff this single test waits ~800ms.
            var planner = new LlmIntentPlanner(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                timeout: TimeSpan.FromMilliseconds(50),
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await planner.PlanAsync(BuildRequest());

            Assert.NotEmpty(result.Scenario.Steps);
            Assert.Contains(result.Diagnostics, d => d.Contains("timed out", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task LlmIntentPlanner_RetriesOn503_AndSucceeds()
        {
            const string anthropicResponseJson = """
            {
              "content": [
                { "type": "text", "text": "{\"steps\": [{\"actionType\": \"Click\", \"targetDescription\": \"save\", \"value\": \"\", \"testIntent\": \"submit\", \"expectedOutcome\": \"saved\", \"locatorKey\": \"Action.Save\"}]}" }
              ]
            }
            """;
            var callCount = 0;
            var handler = new FakeHttpMessageHandler((_, _) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("temporarily unavailable")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(anthropicResponseJson, Encoding.UTF8, "application/json")
                });
            });

            var planner = new LlmIntentPlanner(
                httpClient: new HttpClient(handler),
                apiKey: "sk-test-key",
                maxRetries: 2,
                delayAsync: (_, _) => Task.CompletedTask);

            var result = await planner.PlanAsync(BuildRequest());

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.Scenario.Steps);
            Assert.Equal(2, callCount);
        }

        [Fact]
        public void LlmIntentPlanner_Throws_WhenParametersAreInvalid()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LlmIntentPlanner(timeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LlmIntentPlanner(timeout: TimeSpan.FromSeconds(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LlmIntentPlanner(totalTimeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LlmIntentPlanner(maxRetries: -1));
            Assert.Throws<ArgumentException>(() => new LlmIntentPlanner(timeout: TimeSpan.FromSeconds(20), totalTimeout: TimeSpan.FromSeconds(10)));
        }

        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

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
                return await _responder(request, cancellationToken);
            }
        }
    }
}
