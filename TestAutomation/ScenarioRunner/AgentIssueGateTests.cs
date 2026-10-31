using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;

namespace ScenarioRunner
{
    public class AgentIssueGateTests
    {
        private static IssueSnapshot Issue(IEnumerable<string> labels, bool open = true, bool pullRequest = false)
        {
            return new IssueSnapshot(7, "A title", open, pullRequest, labels);
        }

        private static readonly string[] Ready = { "Agent::Allowed", "Task::docs" };

        [Fact]
        public void AnOpenAllowedIssueWithOneKnownTaskKindInReady_IsEligible()
        {
            var decision = AgentIssueGate.Evaluate(Issue(Ready), "Ready");

            Assert.True(decision.Eligible);
            Assert.Equal(GateReasons.Eligible, decision.Reason);
            Assert.Equal("docs", decision.TaskKind);
        }

        [Fact]
        public void LabelNamesAreComparedIgnoringCase_AndTheTaskKindIsNormalised()
        {
            var decision = AgentIssueGate.Evaluate(Issue(new[] { "agent::ALLOWED", "task::Issue-Triage" }), "Ready");

            Assert.True(decision.Eligible);
            Assert.Equal("issue-triage", decision.TaskKind);
        }

        [Fact]
        public void TheSameTaskLabelWrittenTwice_IsNotAmbiguous()
        {
            Assert.True(AgentIssueGate.Evaluate(Issue(new[] { "Agent::Allowed", "Task::docs", "task::DOCS" }), "Ready").Eligible);
        }

        [Fact]
        public void NeedsHumanDoesNotBlock_ADecisionToContinueIsAHumanReAddingAllowed()
        {
            Assert.True(AgentIssueGate.Evaluate(Issue(Ready.Concat(new[] { "Agent::Needs-Human" })), "Ready").Eligible);
        }

        [Theory]
        [InlineData(GateReasons.Rejected, new[] { "Agent::Allowed", "Task::docs", "Agent::Rejected" }, true, false, "Ready")]
        [InlineData(GateReasons.PullRequest, new[] { "Agent::Allowed", "Task::docs" }, true, true, "Ready")]
        [InlineData(GateReasons.Closed, new[] { "Agent::Allowed", "Task::docs" }, false, false, "Ready")]
        [InlineData(GateReasons.AlreadyWorking, new[] { "Agent::Allowed", "Task::docs", "Agent::Working" }, true, false, "Ready")]
        [InlineData(GateReasons.InReview, new[] { "Agent::Allowed", "Task::docs", "Agent::In-Review" }, true, false, "Ready")]
        [InlineData(GateReasons.NotAllowed, new[] { "Task::docs" }, true, false, "Ready")]
        [InlineData(GateReasons.TaskKindMissing, new[] { "Agent::Allowed" }, true, false, "Ready")]
        [InlineData(GateReasons.TaskKindAmbiguous, new[] { "Agent::Allowed", "Task::docs", "Task::ci-failure" }, true, false, "Ready")]
        [InlineData(GateReasons.TaskKindUnknown, new[] { "Agent::Allowed", "Task::cook" }, true, false, "Ready")]
        [InlineData(GateReasons.TaskKindUnknown, new[] { "Agent::Allowed", "Task::" }, true, false, "Ready")]
        [InlineData(GateReasons.BoardStatusUnknown, new[] { "Agent::Allowed", "Task::docs" }, true, false, null)]
        [InlineData(GateReasons.ProjectNotReady, new[] { "Agent::Allowed", "Task::docs" }, true, false, "Backlog")]
        [InlineData(GateReasons.ProjectNotReady, new[] { "Agent::Allowed", "Task::docs" }, true, false, "In progress")]
        [InlineData(GateReasons.ProjectNotReady, new[] { "Agent::Allowed", "Task::docs" }, true, false, "ready")]
        [InlineData(GateReasons.ProjectNotReady, new[] { "Agent::Allowed", "Task::docs" }, true, false, "")]
        public void EachConditionStopsTheIssue_WithItsOwnReason(string reason, string[] labels, bool open, bool pullRequest, string? board)
        {
            var decision = AgentIssueGate.Evaluate(Issue(labels, open, pullRequest), board);

            Assert.False(decision.Eligible);
            Assert.Equal(reason, decision.Reason);
            Assert.Null(decision.TaskKind);
        }

        [Theory]
        // Rejected wins over everything else, even a closed pull request that is also running.
        [InlineData(GateReasons.Rejected, new[] { "Agent::Rejected", "Agent::Working", "Agent::In-Review" }, false, true, null)]
        [InlineData(GateReasons.PullRequest, new[] { "Agent::Working" }, false, true, null)]
        [InlineData(GateReasons.Closed, new[] { "Agent::Working", "Agent::In-Review" }, false, false, null)]
        [InlineData(GateReasons.AlreadyWorking, new[] { "Agent::Working", "Agent::In-Review" }, true, false, null)]
        [InlineData(GateReasons.InReview, new[] { "Agent::In-Review" }, true, false, null)]
        // The task label is checked before the board, so a missing task is reported even when the card is not ready.
        [InlineData(GateReasons.TaskKindMissing, new[] { "Agent::Allowed" }, true, false, "Backlog")]
        [InlineData(GateReasons.NotAllowed, new[] { "Task::docs" }, true, false, "Backlog")]
        public void SeveralProblemsAtOnce_ReportTheFirstInTheDocumentedOrder(string reason, string[] labels, bool open, bool pullRequest, string? board)
        {
            Assert.Equal(reason, AgentIssueGate.Evaluate(Issue(labels, open, pullRequest), board).Reason);
        }

        [Fact]
        public void ArgumentsAreValidated()
        {
            Assert.Throws<ArgumentNullException>(() => AgentIssueGate.Evaluate(null!, "Ready"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IssueSnapshot(0, "t", true, false, new string[0]));
            Assert.Throws<ArgumentNullException>(() => new IssueSnapshot(1, "t", true, false, null!));
            Assert.Equal(new[] { "a" }, new IssueSnapshot(1, "t", true, false, new string[] { "a", null! }).Labels);
        }
    }

    public class GitHubIssueReaderTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(_respond(request));
            }
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private const string Valid = "{\"number\":7,\"title\":\"Fix the docs\",\"state\":\"open\",\"labels\":[{\"name\":\"Agent::Allowed\"},{\"name\":\"Task::docs\"}]}";

        [Fact]
        public async Task AValidIssue_IsReadWithTheExpectedRequest()
        {
            var handler = new StubHandler(_ => Json(Valid));

            var result = await GitHubIssueReader.ReadAsync("owner/repo.name", 7, httpClient: new HttpClient(handler));

            Assert.True(result.Success);
            Assert.Null(result.Error);
            Assert.Equal(7, result.Issue!.Number);
            Assert.Equal("Fix the docs", result.Issue.Title);
            Assert.True(result.Issue.IsOpen);
            Assert.False(result.Issue.IsPullRequest);
            Assert.Equal(new[] { "Agent::Allowed", "Task::docs" }, result.Issue.Labels);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://api.github.com/repos/owner/repo.name/issues/7", request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString());
            Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
            Assert.Null(request.Headers.Authorization);
        }

        [Fact]
        public async Task ATokenTravelsOnlyAsABearerHeader_AndNeverAppearsInAnError()
        {
            var handler = new StubHandler(_ => Json("{}", HttpStatusCode.Unauthorized));

            var result = await GitHubIssueReader.ReadAsync("owner/repo", 7, "secret-token-123", new HttpClient(handler));

            var request = Assert.Single(handler.Requests);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("secret-token-123", request.Headers.Authorization.Parameter);
            Assert.DoesNotContain("secret-token-123", request.RequestUri!.ToString());
            Assert.False(result.Success);
            Assert.DoesNotContain("secret-token-123", result.Error);
            Assert.Contains("401", result.Error);
        }

        [Fact]
        public async Task APullRequestMarker_IsReported()
        {
            var body = "{\"number\":8,\"title\":\"A PR\",\"state\":\"closed\",\"labels\":[],\"pull_request\":{\"url\":\"x\"}}";

            var result = await GitHubIssueReader.ReadAsync("owner/repo", 8, httpClient: new HttpClient(new StubHandler(_ => Json(body))));

            Assert.True(result.Success);
            Assert.True(result.Issue!.IsPullRequest);
            Assert.False(result.Issue.IsOpen);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("{\"number\":0,\"title\":\"t\",\"state\":\"open\",\"labels\":[]}")]
        [InlineData("{\"number\":\"7\",\"title\":\"t\",\"state\":\"open\",\"labels\":[]}")]
        [InlineData("{\"number\":7,\"state\":\"open\",\"labels\":[]}")]
        [InlineData("{\"number\":7,\"title\":\"t\",\"state\":\"open\"}")]
        [InlineData("{\"number\":7,\"title\":\"t\",\"state\":\"open\",\"labels\":[\"Agent::Allowed\"]}")]
        [InlineData("{\"number\":7,\"title\":\"t\",\"state\":\"open\",\"labels\":[{\"colour\":\"red\"}]}")]
        public async Task AMalformedAnswer_IsAFailureNotAnException(string body)
        {
            var result = await GitHubIssueReader.ReadAsync("owner/repo", 7, httpClient: new HttpClient(new StubHandler(_ => Json(body))));

            Assert.False(result.Success);
            Assert.Null(result.Issue);
            Assert.False(string.IsNullOrEmpty(result.Error));
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, "404")]
        [InlineData(HttpStatusCode.Forbidden, "403")]
        [InlineData(HttpStatusCode.Found, "302")]
        [InlineData(HttpStatusCode.InternalServerError, "500")]
        public async Task ANonSuccessStatus_IsAFailureWithTheStatus(HttpStatusCode status, string expected)
        {
            var result = await GitHubIssueReader.ReadAsync("owner/repo", 7, httpClient: new HttpClient(new StubHandler(_ => Json("{}", status))));

            Assert.False(result.Success);
            Assert.Contains(expected, result.Error);
        }

        [Fact]
        public async Task ATransportFailure_IsAFailureNotAnException()
        {
            var handler = new StubHandler(_ => throw new HttpRequestException("connection refused: secret details"));

            var result = await GitHubIssueReader.ReadAsync("owner/repo", 7, httpClient: new HttpClient(handler));

            Assert.False(result.Success);
            Assert.DoesNotContain("secret details", result.Error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("owner")]
        [InlineData("owner/")]
        [InlineData("/repo")]
        [InlineData("a/b/c")]
        [InlineData("owner/..")]
        [InlineData("owner/.")]
        [InlineData("owner/re po")]
        [InlineData("../owner/repo")]
        [InlineData("owner/repo?x=1")]
        [InlineData("owner/repo#frag")]
        [InlineData("owner/repo\n")]
        public async Task ARepositoryThatCouldChangeTheRequestPath_IsRejectedBeforeAnyRequest(string? repository)
        {
            var handler = new StubHandler(_ => Json(Valid));

            var result = await GitHubIssueReader.ReadAsync(repository!, 7, httpClient: new HttpClient(handler));

            Assert.False(result.Success);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task ANonPositiveNumber_IsRejectedBeforeAnyRequest()
        {
            var handler = new StubHandler(_ => Json(Valid));

            Assert.False((await GitHubIssueReader.ReadAsync("owner/repo", 0, httpClient: new HttpClient(handler))).Success);
            Assert.False((await GitHubIssueReader.ReadAsync("owner/repo", -3, httpClient: new HttpClient(handler))).Success);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task TheReaderAndTheGateWorkTogether()
        {
            var result = await GitHubIssueReader.ReadAsync("owner/repo", 7, httpClient: new HttpClient(new StubHandler(_ => Json(Valid))));

            Assert.True(AgentIssueGate.Evaluate(result.Issue!, "Ready").Eligible);
            Assert.Equal(GateReasons.ProjectNotReady, AgentIssueGate.Evaluate(result.Issue!, "Backlog").Reason);
        }
    }
}
