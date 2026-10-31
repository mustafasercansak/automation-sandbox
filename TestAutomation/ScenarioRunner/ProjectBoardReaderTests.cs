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
    public class ProjectBoardReaderTests
    {
        private const string Project = "PVT_kwHOAr3aNM4BgUrT";
        private const string Token = "project-token-xyz";

        internal sealed class StubHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;
            private Func<HttpRequestMessage, HttpResponseMessage>? _last;

            public StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
            {
                _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
            }

            public List<(string Url, string? Auth, string Body)> Requests { get; } = new List<(string, string?, string)>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
                if (_responses.Count > 0)
                {
                    _last = _responses.Dequeue();
                }

                return _last!(request);
            }
        }

        internal static Func<HttpRequestMessage, HttpResponseMessage> Reply(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            _ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static string Card(int number, string? repo, string? status)
        {
            var content = repo == null ? "null" : $"{{\"number\":{number},\"repository\":{{\"nameWithOwner\":\"{repo}\"}}}}";
            var field = status == null ? "null" : $"{{\"name\":\"{status}\"}}";
            return $"{{\"content\":{content},\"fieldValueByName\":{field}}}";
        }

        internal static string Page(string[] cards, bool hasNext = false, string? cursor = null) =>
            "{\"data\":{\"node\":{\"items\":{\"nodes\":[" + string.Join(",", cards) + "],\"pageInfo\":{\"hasNextPage\":" +
            (hasNext ? "true" : "false") + ",\"endCursor\":" + (cursor == null ? "null" : "\"" + cursor + "\"") + "}}}}}";

        private static Task<BoardStatusResult> Read(StubHandler handler, string repo = "owner/repo", int number = 7, string project = Project, string token = Token) =>
            ProjectBoardReader.ReadStatusAsync(repo, number, project, token, new HttpClient(handler));

        [Fact]
        public async Task ACardWithAStatus_IsFound_AndTheTokenTravelsOnlyInTheAuthorizationHeader()
        {
            var handler = new StubHandler(Reply(Page(new[] { Card(3, "owner/repo", "Backlog"), Card(7, "owner/repo", "Ready") })));

            var result = await Read(handler);

            Assert.Equal(BoardReadOutcome.Found, result.Outcome);
            Assert.Equal("Ready", result.Status);
            Assert.Null(result.Error);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://api.github.com/graphql", request.Url);
            Assert.Equal("Bearer " + Token, request.Auth);
            Assert.DoesNotContain(Token, request.Url);
            Assert.DoesNotContain(Token, request.Body);
            using var body = JsonDocument.Parse(request.Body);
            Assert.Equal(Project, body.RootElement.GetProperty("variables").GetProperty("project").GetString());
            Assert.Contains("fieldValueByName(name: \"Status\")", body.RootElement.GetProperty("query").GetString());
        }

        [Fact]
        public async Task TheRepositoryIsComparedIgnoringCase_AndAnotherRepositorysCardIsNotMistakenForTheIssue()
        {
            var handler = new StubHandler(Reply(Page(new[] { Card(7, "other/repo", "Ready"), Card(7, "OWNER/Repo", "In progress") })));

            var result = await Read(handler);

            Assert.Equal("In progress", result.Status);
        }

        [Fact]
        public async Task ACardWithNoStatusValue_IsFoundWithANullStatus()
        {
            var result = await Read(new StubHandler(Reply(Page(new[] { Card(7, "owner/repo", null) }))));

            Assert.Equal(BoardReadOutcome.Found, result.Outcome);
            Assert.Null(result.Status);
        }

        [Fact]
        public async Task AnIssueThatIsNotOnTheBoard_IsReportedAsSuch_WhenEveryCardWasReadable()
        {
            var result = await Read(new StubHandler(Reply(Page(new[] { Card(1, "owner/repo", "Ready"), "{\"content\":{},\"fieldValueByName\":null}" }))));

            Assert.Equal(BoardReadOutcome.NotOnBoard, result.Outcome);
            Assert.Null(result.Error);
        }

        [Fact]
        public async Task AnUnreadableCard_HidingTheIssue_IsAFailureNotNotOnBoard()
        {
            var result = await Read(new StubHandler(Reply(Page(new[] { Card(1, "owner/repo", "Ready"), Card(0, null, "Ready") }))));

            Assert.Equal(BoardReadOutcome.Failed, result.Outcome);
            Assert.Contains("cannot read", result.Error);
        }

        [Fact]
        public async Task AnUnreadableCard_DoesNotMatter_WhenTheIssueIsFoundAnyway()
        {
            var result = await Read(new StubHandler(Reply(Page(new[] { Card(0, null, "Ready"), Card(7, "owner/repo", "Ready") }))));

            Assert.Equal(BoardReadOutcome.Found, result.Outcome);
        }

        [Fact]
        public async Task SeveralPages_AreFollowed_WithTheCursorSentOnTheNextRequest()
        {
            var handler = new StubHandler(
                Reply(Page(new[] { Card(1, "owner/repo", "Ready") }, hasNext: true, cursor: "CURSOR1")),
                Reply(Page(new[] { Card(7, "owner/repo", "In review") })));

            var result = await Read(handler);

            Assert.Equal("In review", result.Status);
            Assert.Equal(2, handler.Requests.Count);
            using var first = JsonDocument.Parse(handler.Requests[0].Body);
            using var second = JsonDocument.Parse(handler.Requests[1].Body);
            Assert.Equal(JsonValueKind.Null, first.RootElement.GetProperty("variables").GetProperty("cursor").ValueKind);
            Assert.Equal("CURSOR1", second.RootElement.GetProperty("variables").GetProperty("cursor").GetString());
        }

        [Fact]
        public async Task APageThatDoesNotAdvance_IsAFailure()
        {
            var handler = new StubHandler(Reply(Page(new[] { Card(1, "owner/repo", "Ready") }, hasNext: true, cursor: null)));

            var result = await Read(handler);

            Assert.Equal(BoardReadOutcome.Failed, result.Outcome);
            Assert.Contains("did not advance", result.Error);
        }

        [Fact]
        public async Task ABoardWithMorePagesThanTheLimit_IsAFailure_NotAnEndlessLoop()
        {
            var counter = 0;
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Page(new[] { Card(1, "owner/repo", "Ready") }, hasNext: true, cursor: "C" + (++counter)), Encoding.UTF8, "application/json"),
            });

            var result = await Read(handler);

            Assert.Equal(BoardReadOutcome.Failed, result.Outcome);
            Assert.Contains("more cards than the reader", result.Error);
            Assert.Equal(100, handler.Requests.Count);
        }

        [Theory]
        [InlineData("{\"errors\":[{\"message\":\"boom\"}],\"data\":null}")]
        [InlineData("{\"data\":{\"node\":null}}")]
        [InlineData("{\"data\":{}}")]
        [InlineData("{}")]
        [InlineData("[]")]
        [InlineData("not json")]
        [InlineData("{\"data\":{\"node\":{\"items\":{\"nodes\":[],\"pageInfo\":{}}}}}")]
        [InlineData("{\"data\":{\"node\":{\"items\":{\"nodes\":{},\"pageInfo\":{\"hasNextPage\":false}}}}}")]
        public async Task AnErrorListOrAMalformedAnswer_IsAFailure(string body)
        {
            var result = await Read(new StubHandler(Reply(body)));

            Assert.Equal(BoardReadOutcome.Failed, result.Outcome);
            Assert.False(string.IsNullOrEmpty(result.Error));
            Assert.DoesNotContain("boom", result.Error);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, "401")]
        [InlineData(HttpStatusCode.Forbidden, "403")]
        [InlineData(HttpStatusCode.BadGateway, "502")]
        [InlineData(HttpStatusCode.Found, "302")]
        public async Task ANonSuccessStatus_IsAFailureWithTheStatus_AndNeverTheToken(HttpStatusCode status, string expected)
        {
            var result = await Read(new StubHandler(Reply("{}", status)));

            Assert.Equal(BoardReadOutcome.Failed, result.Outcome);
            Assert.Contains(expected, result.Error);
            Assert.DoesNotContain(Token, result.Error);
        }

        [Fact]
        public async Task ATransportFailure_IsAFailureNotAnException()
        {
            var result = await Read(new StubHandler(_ => throw new HttpRequestException("refused: " + Token)));

            Assert.Equal(BoardReadOutcome.Failed, result.Outcome);
            Assert.DoesNotContain(Token, result.Error);
        }

        [Theory]
        [InlineData("owner", 7, Project, Token)]
        [InlineData("owner/repo\n", 7, Project, Token)]
        [InlineData("owner/repo", 0, Project, Token)]
        [InlineData("owner/repo", 7, "", Token)]
        [InlineData("owner/repo", 7, "PVT_x", Token)]
        [InlineData("owner/repo", 7, "NOT_A_PROJECT_ID", Token)]
        [InlineData("owner/repo", 7, "PVT_kwHOAr3aNM4BgUrT\n", Token)]
        [InlineData("owner/repo", 7, Project, "")]
        [InlineData("owner/repo", 7, Project, "   ")]
        public async Task InvalidInput_IsRejectedBeforeAnyRequest(string repo, int number, string project, string token)
        {
            var handler = new StubHandler(Reply(Page(new string[0])));

            var result = await Read(handler, repo, number, project, token);

            Assert.Equal(BoardReadOutcome.Failed, result.Outcome);
            Assert.Empty(handler.Requests);
        }
    }

    public class AgentIssueIntakeTests
    {
        private static string IssueJson(params string[] labels) =>
            "{\"number\":7,\"title\":\"Docs\",\"state\":\"open\",\"labels\":[" + string.Join(",", labels.Select(l => "{\"name\":\"" + l + "\"}")) + "]}";

        // The first reply answers the issue request, the second the board request.
        private static Task<AgentIssueIntakeResult> Check(ProjectBoardReaderTests.StubHandler handler) =>
            AgentIssueIntake.CheckAsync("owner/repo", 7, "PVT_kwHOAr3aNM4BgUrT", "project-token", "issue-token", new HttpClient(handler));

        private static string Board(string? status) => ProjectBoardReaderTests.Page(new[]
        {
            "{\"content\":{\"number\":7,\"repository\":{\"nameWithOwner\":\"owner/repo\"}},\"fieldValueByName\":" + (status == null ? "null" : "{\"name\":\"" + status + "\"}") + "}",
        });

        [Fact]
        public async Task AnOpenAllowedIssueInReady_IsEligible()
        {
            var handler = new ProjectBoardReaderTests.StubHandler(
                ProjectBoardReaderTests.Reply(IssueJson("Agent::Allowed", "Task::docs")),
                ProjectBoardReaderTests.Reply(Board("Ready")));

            var result = await Check(handler);

            Assert.True(result.Decision.Eligible);
            Assert.Equal("docs", result.Decision.TaskKind);
            Assert.Null(result.Error);
            Assert.Equal(2, handler.Requests.Count);
            Assert.EndsWith("/repos/owner/repo/issues/7", handler.Requests[0].Url);
            Assert.Equal("Bearer issue-token", handler.Requests[0].Auth);
            Assert.Equal("Bearer project-token", handler.Requests[1].Auth);
        }

        [Theory]
        [InlineData("Backlog", GateReasons.ProjectNotReady, null)]
        [InlineData("In progress", GateReasons.ProjectNotReady, null)]
        [InlineData(null, GateReasons.BoardStatusUnknown, "no Status")]
        public async Task ACardOutsideReady_IsNotEligible(string? status, string reason, string? note)
        {
            var handler = new ProjectBoardReaderTests.StubHandler(
                ProjectBoardReaderTests.Reply(IssueJson("Agent::Allowed", "Task::docs")),
                ProjectBoardReaderTests.Reply(Board(status)));

            var result = await Check(handler);

            Assert.False(result.Decision.Eligible);
            Assert.Equal(reason, result.Decision.Reason);
            if (note == null)
            {
                Assert.Null(result.Error);
            }
            else
            {
                Assert.Contains(note, result.Error);
            }
        }

        [Fact]
        public async Task AnIssueNotOnTheBoard_IsNotEligible_WithTheReason()
        {
            var handler = new ProjectBoardReaderTests.StubHandler(
                ProjectBoardReaderTests.Reply(IssueJson("Agent::Allowed", "Task::docs")),
                ProjectBoardReaderTests.Reply(ProjectBoardReaderTests.Page(new string[0])));

            var result = await Check(handler);

            Assert.Equal(GateReasons.BoardStatusUnknown, result.Decision.Reason);
            Assert.Contains("not on the project board", result.Error);
        }

        [Fact]
        public async Task ABoardThatCannotBeRead_IsNotEligible_NeverAnException()
        {
            var handler = new ProjectBoardReaderTests.StubHandler(
                ProjectBoardReaderTests.Reply(IssueJson("Agent::Allowed", "Task::docs")),
                ProjectBoardReaderTests.Reply("{}", HttpStatusCode.Forbidden));

            var result = await Check(handler);

            Assert.False(result.Decision.Eligible);
            Assert.Equal(GateReasons.BoardStatusUnknown, result.Decision.Reason);
            Assert.Contains("403", result.Error);
            Assert.NotNull(result.Issue);
        }

        [Fact]
        public async Task AnIssueThatCannotBeRead_IsNotEligible_AndTheBoardIsNeverAsked()
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply("{}", HttpStatusCode.NotFound));

            var result = await Check(handler);

            Assert.Equal(GateReasons.IssueUnreadable, result.Decision.Reason);
            Assert.Null(result.Issue);
            Assert.Contains("404", result.Error);
            Assert.Single(handler.Requests);
        }

        [Theory]
        [InlineData(GateReasons.Rejected, new[] { "Agent::Allowed", "Task::docs", "Agent::Rejected" })]
        [InlineData(GateReasons.AlreadyWorking, new[] { "Agent::Allowed", "Task::docs", "Agent::Working" })]
        [InlineData(GateReasons.NotAllowed, new[] { "Task::docs" })]
        [InlineData(GateReasons.TaskKindMissing, new[] { "Agent::Allowed" })]
        public async Task AnIssueThatCannotPassTheGateAnyway_CostsOneRequest_NotAPagedBoardRead(string reason, string[] labels)
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(labels)));

            var result = await Check(handler);

            Assert.Equal(reason, result.Decision.Reason);
            Assert.Single(handler.Requests);
        }
    }
}
