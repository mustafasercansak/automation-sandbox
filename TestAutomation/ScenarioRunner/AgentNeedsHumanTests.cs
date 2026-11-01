using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.AgentCli;
using AutomationSandbox.LlmHealing;

namespace ScenarioRunner
{
    public class IssueLabelWriterTests
    {
        private const string Token = "write-token-www999";

        private static Task<IssueLabelResult> Add(ProjectBoardReaderTests.StubHandler handler, string repo = "owner/repo", int number = 7, string token = Token) =>
            IssueLabelWriter.AddNeedsHumanAsync(repo, number, token, new HttpClient(handler));

        [Fact]
        public async Task TheLabelIsAdded_WithExactlyOneLabelInTheRequest_AndTheTokenOnlyInTheHeader()
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply("[]"));

            var result = await Add(handler);

            Assert.True(result.Success);
            Assert.Null(result.Error);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://api.github.com/repos/owner/repo/issues/7/labels", request.Url);
            Assert.Equal("Bearer " + Token, request.Auth);
            Assert.DoesNotContain(Token, request.Url);
            Assert.DoesNotContain(Token, request.Body);
            using var json = JsonDocument.Parse(request.Body);
            var labels = json.RootElement.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ToArray();
            Assert.Equal(new[] { "Agent::Needs-Human" }, labels);
            Assert.Single(json.RootElement.EnumerateObject());
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, "401", true)]
        [InlineData(HttpStatusCode.Forbidden, "403", true)]
        [InlineData(HttpStatusCode.NotFound, "404", true)]
        [InlineData(HttpStatusCode.InternalServerError, "500", false)]
        [InlineData(HttpStatusCode.Found, "302", false)]
        public async Task AFailureStatus_IsAResultWithAHint_AndNeverTheToken(HttpStatusCode status, string code, bool hint)
        {
            var result = await Add(new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply("{\"message\":\"" + Token + "\"}", status)));

            Assert.False(result.Success);
            Assert.Contains("HTTP " + code, result.Error);
            Assert.Equal(hint, result.Error!.Contains("permission to write issue labels"));
            Assert.DoesNotContain(Token, result.Error);
        }

        [Fact]
        public async Task ATransportFailure_IsAResult_NotAnException_AndHidesItsMessage()
        {
            var result = await Add(new ProjectBoardReaderTests.StubHandler(_ => throw new HttpRequestException("refused " + Token)));

            Assert.False(result.Success);
            Assert.DoesNotContain(Token, result.Error);
        }

        [Theory]
        [InlineData("owner", 7, Token)]
        [InlineData("owner/repo\n", 7, Token)]
        [InlineData("../owner/repo", 7, Token)]
        [InlineData("owner/repo", 0, Token)]
        [InlineData("owner/repo", 7, "")]
        [InlineData("owner/repo", 7, "  ")]
        public async Task InvalidInput_IsRejectedBeforeAnyRequest(string repo, int number, string token)
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply("[]"));

            var result = await Add(handler, repo, number, token);

            Assert.False(result.Success);
            Assert.Empty(handler.Requests);
        }
    }

    public class AgentCommandNeedsHumanTests
    {
        private const string ProjectToken = "project-token-ppp111";
        private const string WriteToken = "write-token-www222";
        private const string MistralKey = "mistral-key-mmm333";
        private const string NvidiaKey = "nvidia-key-nnn444";
        private const string Project = "PVT_kwHOAr3aNM4BgUrT";
        private const string Good = "{\"category\":\"bug\",\"summary\":\"It crashes on start.\",\"missing_information\":[],\"suggested_labels\":[\"bug\"]}";
        private const string Ok = "{\"verdict\":\"ok\",\"issues\":[]}";
        private const string Blocks = "{\"verdict\":\"issues\",\"issues\":[\"The summary claims a version the issue never names.\"]}";
        private static readonly string[] Labels = { "Agent::Allowed", "Task::issue-triage", "Model::Mistral", "Review::Nvidia" };

        private static Dictionary<string, string?> Env(bool withWriteToken = true)
        {
            var env = new Dictionary<string, string?>
            {
                ["PROJECT_TOKEN"] = ProjectToken, ["PROJECT_ID"] = Project,
                ["MISTRAL_API_KEY"] = MistralKey, ["MISTRAL_MODEL"] = "mistral-test",
                ["NVIDIA_API_KEY"] = NvidiaKey, ["NVIDIA_MODEL"] = "nvidia-test",
            };
            if (withWriteToken)
            {
                env["GH_TOKEN"] = WriteToken;
            }

            return env;
        }

        private static string IssueJson(params string[] labels) =>
            "{\"number\":7,\"title\":\"Crash\",\"state\":\"open\",\"body\":\"It crashes.\",\"labels\":[" + string.Join(",", labels.Select(l => "{\"name\":\"" + l + "\"}")) + "]}";

        private static string Board() => ProjectBoardReaderTests.Page(new[]
        {
            "{\"content\":{\"number\":7,\"repository\":{\"nameWithOwner\":\"owner/repo\"}},\"fieldValueByName\":{\"name\":\"Ready\"}}",
        });

        private static string Chat(string content) => "{\"choices\":[{\"message\":{\"content\":" + JsonSerializer.Serialize(content) + "}}]}";

        private static Func<HttpRequestMessage, HttpResponseMessage> Reply(string body, HttpStatusCode status = HttpStatusCode.OK) => ProjectBoardReaderTests.Reply(body, status);

        private static async Task<(int Code, string Out, string Err, ProjectBoardReaderTests.StubHandler Handler)> Run(string[] args, Dictionary<string, string?> env, params Func<HttpRequestMessage, HttpResponseMessage>[] replies)
        {
            var handler = new ProjectBoardReaderTests.StubHandler(replies);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var code = await AgentCommand.RunAsync(args, key => env.TryGetValue(key, out var v) ? v : null, _ => throw new IOException(), stdout, stderr, new HttpClient(handler), CancellationToken.None);
            return (code, stdout.ToString(), stderr.ToString(), handler);
        }

        private static string[] Triage(params string[] extra) => new[] { "triage", "--repo", "owner/repo", "--issue", "7" }.Concat(extra).ToArray();

        private static bool IsLabelWrite((string Url, string? Auth, string Body) r) => r.Url.EndsWith("/labels", StringComparison.Ordinal);

        [Fact]
        public async Task ABlockedReview_AddsExactlyTheNeedsHumanLabel_AndKeepsExitCodeOne()
        {
            var result = await Run(Triage("--label-needs-human"), Env(), Reply(IssueJson(Labels)), Reply(Board()), Reply(Chat(Good)), Reply(Chat(Blocks)), Reply("[]"));

            Assert.Equal(1, result.Code);
            Assert.Equal(5, result.Handler.Requests.Count);
            var write = result.Handler.Requests[4];
            Assert.Equal("https://api.github.com/repos/owner/repo/issues/7/labels", write.Url);
            Assert.Equal("Bearer " + WriteToken, write.Auth);
            using var json = JsonDocument.Parse(write.Body);
            Assert.Equal(new[] { "Agent::Needs-Human" }, json.RootElement.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ToArray());
            Assert.Contains("Added the label Agent::Needs-Human", result.Err);
            using var report = JsonDocument.Parse(result.Out);
            Assert.Equal("Blocked", report.RootElement.GetProperty("triage").GetProperty("status").GetString());
        }

        [Fact]
        public async Task AFailedRun_AddsTheLabelToo()
        {
            var result = await Run(Triage("--label-needs-human"), Env(), Reply(IssueJson("Agent::Allowed", "Task::issue-triage", "Model::Nvidia")), Reply(Board()), Reply("{}", HttpStatusCode.NotFound), Reply("[]"));

            Assert.Equal(1, result.Code);
            Assert.Single(result.Handler.Requests, IsLabelWrite);
        }

        [Fact]
        public async Task AnAcceptedRun_AddsNothing()
        {
            var result = await Run(Triage("--label-needs-human"), Env(), Reply(IssueJson(Labels)), Reply(Board()), Reply(Chat(Good)), Reply(Chat(Ok)));

            Assert.Equal(0, result.Code);
            Assert.Equal(4, result.Handler.Requests.Count);
            Assert.DoesNotContain(result.Handler.Requests, IsLabelWrite);
            Assert.DoesNotContain("Added the label", result.Err);
        }

        [Fact]
        public async Task WithoutTheFlag_AnythingBlockedStillWritesNothing()
        {
            var result = await Run(Triage(), Env(), Reply(IssueJson(Labels)), Reply(Board()), Reply(Chat(Good)), Reply(Chat(Blocks)));

            Assert.Equal(1, result.Code);
            Assert.Equal(4, result.Handler.Requests.Count);
            Assert.DoesNotContain(result.Handler.Requests, IsLabelWrite);
        }

        [Fact]
        public async Task ANotEligibleIssue_AddsNothing_BecauseNothingWasDrafted()
        {
            var result = await Run(Triage("--label-needs-human"), Env(), Reply(IssueJson("Task::issue-triage")));

            Assert.Equal(1, result.Code);
            Assert.Single(result.Handler.Requests);
        }

        [Fact]
        public async Task ARoutingProblem_AddsNothing()
        {
            var result = await Run(Triage("--label-needs-human"), Env(), Reply(IssueJson("Agent::Allowed", "Task::issue-triage", "Model::Nope")), Reply(Board()));

            Assert.Equal(1, result.Code);
            Assert.DoesNotContain(result.Handler.Requests, IsLabelWrite);
        }

        [Fact]
        public async Task TheFlagWithoutAWriteToken_StopsBeforeAnyRequest()
        {
            var result = await Run(Triage("--label-needs-human"), Env(withWriteToken: false), Reply(IssueJson(Labels)));

            Assert.Equal(2, result.Code);
            Assert.Contains("--label-needs-human needs GH_TOKEN or GITHUB_TOKEN", result.Err);
            Assert.Empty(result.Handler.Requests);
        }

        [Fact]
        public async Task AFailedLabelWrite_ExitsTwo_WithAHint_AndNeverTheToken_ButTheReportIsStillPrinted()
        {
            var result = await Run(Triage("--label-needs-human"), Env(), Reply(IssueJson(Labels)), Reply(Board()), Reply(Chat(Good)), Reply(Chat(Blocks)), Reply("{\"message\":\"" + WriteToken + "\"}", HttpStatusCode.Forbidden));

            Assert.Equal(2, result.Code);
            Assert.Contains("Could not add the label", result.Err);
            Assert.Contains("permission to write issue labels", result.Err);
            foreach (var secret in new[] { ProjectToken, WriteToken, MistralKey, NvidiaKey })
            {
                Assert.DoesNotContain(secret, result.Err);
                Assert.DoesNotContain(secret, result.Out);
            }

            using var report = JsonDocument.Parse(result.Out);
            Assert.Equal("Completed", report.RootElement.GetProperty("stage").GetString());
        }

        [Fact]
        public async Task WithTheCommentFlagToo_BothAreWrittenAndNothingElse()
        {
            var result = await Run(Triage("--comment", "--label-needs-human"), Env(), Reply(IssueJson(Labels)), Reply(Board()), Reply(Chat(Good)), Reply(Chat(Blocks)), Reply("{}", HttpStatusCode.Created), Reply("[]"));

            Assert.Equal(1, result.Code);
            Assert.Equal(6, result.Handler.Requests.Count);
            Assert.EndsWith("/issues/7/comments", result.Handler.Requests[4].Url);
            Assert.EndsWith("/issues/7/labels", result.Handler.Requests[5].Url);
        }

        [Fact]
        public void TheOptionIsParsed_AndOffByDefault()
        {
            Assert.True(AgentOptions.Parse(Triage("--label-needs-human")).LabelNeedsHuman);
            Assert.False(AgentOptions.Parse(Triage()).LabelNeedsHuman);
        }
    }
}
