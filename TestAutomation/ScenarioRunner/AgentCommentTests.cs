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
    public class AgentReportCommentTests
    {
        // Ordinal on purpose: string.Contains is culture-sensitive and ignores the zero-width space the escaping inserts, so
        // it would still "find" a mention or a link that has been broken.
        internal static void NotIn(string text, string needle) =>
            Assert.True(text.IndexOf(needle, StringComparison.Ordinal) < 0, "'" + needle + "' must not appear in: " + text);

        internal static void In(string text, string needle) =>
            Assert.True(text.IndexOf(needle, StringComparison.Ordinal) >= 0, "'" + needle + "' must appear in: " + text);

        // The error the first real NVIDIA run returned, including the account identifier it carried.
        private const string RealNotFound = "The model \"Nvidia\" failed: HTTP 404: {\"status\":404,\"title\":\"Not Found\",\"detail\":\"Function '9b96341b-9791-4db9-a00d-4e43aa192a39': Not found for account 'KTtt0iQ1ITfYAId-8qT7RldwvalumlD12KrQq4hjDB0'\"}";

        [Fact]
        public void AProviderErrorBody_IsReducedToTheStatusAndAShortReason()
        {
            var summary = AgentReportComment.ErrorSummary(RealNotFound);

            Assert.Equal("The model \"Nvidia\" failed: HTTP 404 (not found; the model or endpoint may not be available for this account)", summary);
            Assert.DoesNotContain("KTtt0", summary);
            Assert.DoesNotContain("9b96341b", summary);
            Assert.DoesNotContain("{", summary);
        }

        [Theory]
        [InlineData("failed: HTTP 429: {\"message\":\"Rate limit exceeded\"}", "HTTP 429 (rate limited)")]
        [InlineData("failed: HTTP 401: {\"x\":1}", "HTTP 401 (not authorised)")]
        [InlineData("failed: HTTP 403: nope", "HTTP 403 (forbidden)")]
        [InlineData("failed: HTTP 500: <html>boom</html>", "HTTP 500 (provider error)")]
        [InlineData("failed: HTTP 504", "HTTP 504 (timed out)")]
        [InlineData("failed: HTTP 418: teapot", "HTTP 418 (request failed)")]
        public void EveryStatusGetsAPlainDescription_AndNothingAfterIt(string error, string expected)
        {
            var summary = AgentReportComment.ErrorSummary(error);

            Assert.EndsWith(expected, summary);
            Assert.DoesNotContain("boom", summary);
            Assert.DoesNotContain("teapot", summary);
            Assert.DoesNotContain("nope", summary);
        }

        [Fact]
        public void AnErrorWithoutAStatus_LosesAnyJsonObject_IsOneLine_AndIsCapped()
        {
            Assert.Equal("The model returned no text.", AgentReportComment.ErrorSummary("The model returned no text."));
            Assert.Equal("Bad answer", AgentReportComment.ErrorSummary("Bad {\"secret\":{\"id\":\"abc\"}} answer"));
            Assert.DoesNotContain("abc", AgentReportComment.ErrorSummary("Bad {\"secret\":{\"id\":\"abc\"}} answer"));
            Assert.DoesNotContain("\n", AgentReportComment.ErrorSummary("one\ntwo\r\nthree"));
            Assert.True(AgentReportComment.ErrorSummary(new string('x', 5000)).Length <= 243);
            Assert.Equal("", AgentReportComment.ErrorSummary(null));
            Assert.Equal("", AgentReportComment.ErrorSummary("   "));
        }

        [Fact]
        public void HostileText_IsMadeInert()
        {
            var text = AgentReportComment.EscapeInline("Ping @octocat and @everyone, see https://evil.example/x and www.evil.example and ftp://x.y, <script>alert(1)</script> [click](http://evil.example) `code` **bold** _it_ | cell | ~strike~ #123 ![img](x)\nnext\u0000line");

            NotIn(text, "@octocat");
            In(text, "@​octocat");
            In(text, "@​everyone");
            NotIn(text, "https://");
            NotIn(text, "http://");
            NotIn(text, "ftp://");
            NotIn(text, "www.");
            NotIn(text, "<script>");
            In(text, "\\<script\\>");
            In(text, "\\[click\\]");
            In(text, "\\`code\\`");
            In(text, "\\*\\*bold\\*\\*");
            In(text, "\\#123");
            In(text, "\\!\\[img\\]");
            NotIn(text, "\n");
            Assert.DoesNotContain(text, c => c == '\u0000');
        }

        [Fact]
        public void ALongField_IsCut()
        {
            Assert.True(AgentReportComment.EscapeInline(new string('a', 5000)).Length <= 403);
            Assert.Equal("", AgentReportComment.EscapeInline(null));
        }
    }

    public class IssueCommentWriterTests
    {
        private const string Token = "write-token-www999";

        private static Task<IssueCommentResult> Post(ProjectBoardReaderTests.StubHandler handler, string repo = "owner/repo", int number = 7, string token = Token, string body = "Hello") =>
            IssueCommentWriter.PostAsync(repo, number, token, body, new HttpClient(handler));

        [Fact]
        public async Task ACommentIsPosted_WithTheExpectedRequest_AndTheTokenOnlyInTheHeader()
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply("{}", HttpStatusCode.Created));

            var result = await Post(handler, body: "Hello <b>there</b>");

            Assert.True(result.Success);
            Assert.Null(result.Error);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://api.github.com/repos/owner/repo/issues/7/comments", request.Url);
            Assert.Equal("Bearer " + Token, request.Auth);
            Assert.DoesNotContain(Token, request.Url);
            Assert.DoesNotContain(Token, request.Body);
            using var json = JsonDocument.Parse(request.Body);
            Assert.Equal("Hello <b>there</b>", json.RootElement.GetProperty("body").GetString());
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, "401", true)]
        [InlineData(HttpStatusCode.Forbidden, "403", true)]
        [InlineData(HttpStatusCode.NotFound, "404", true)]
        [InlineData(HttpStatusCode.InternalServerError, "500", false)]
        [InlineData(HttpStatusCode.Found, "302", false)]
        public async Task AFailureStatus_IsAResultWithAHint_AndNeverTheToken(HttpStatusCode status, string code, bool hint)
        {
            var result = await Post(new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply("{\"message\":\"" + Token + "\"}", status)));

            Assert.False(result.Success);
            Assert.Contains("HTTP " + code, result.Error);
            Assert.Equal(hint, result.Error!.Contains("permission to write issue comments"));
            Assert.DoesNotContain(Token, result.Error);
        }

        [Fact]
        public async Task ATransportFailure_IsAResult_NotAnException_AndHidesItsMessage()
        {
            var result = await Post(new ProjectBoardReaderTests.StubHandler(_ => throw new HttpRequestException("refused " + Token)));

            Assert.False(result.Success);
            Assert.DoesNotContain(Token, result.Error);
        }

        [Theory]
        [InlineData("owner", 7, Token, "Hello")]
        [InlineData("owner/repo\n", 7, Token, "Hello")]
        [InlineData("../owner/repo", 7, Token, "Hello")]
        [InlineData("owner/repo", 0, Token, "Hello")]
        [InlineData("owner/repo", 7, "", "Hello")]
        [InlineData("owner/repo", 7, "  ", "Hello")]
        [InlineData("owner/repo", 7, Token, "")]
        [InlineData("owner/repo", 7, Token, "   ")]
        public async Task InvalidInput_IsRejectedBeforeAnyRequest(string repo, int number, string token, string body)
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply("{}", HttpStatusCode.Created));

            var result = await Post(handler, repo, number, token, body);

            Assert.False(result.Success);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task ATooLongComment_IsRejectedBeforeAnyRequest()
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply("{}", HttpStatusCode.Created));

            var result = await Post(handler, body: new string('a', 65001));

            Assert.False(result.Success);
            Assert.Empty(handler.Requests);
        }
    }

    public class AgentCommandCommentTests
    {
        private const string ProjectToken = "project-token-ppp111";
        private const string WriteToken = "write-token-www222";
        private const string MistralKey = "mistral-key-mmm333";
        private const string NvidiaKey = "nvidia-key-nnn444";
        private const string Project = "PVT_kwHOAr3aNM4BgUrT";
        private const string RealNotFound = "{\"status\":404,\"title\":\"Not Found\",\"detail\":\"Function '9b96341b': Not found for account 'KTtt0iQ1ITfYAId'\"}";
        private const string Good = "{\"category\":\"bug\",\"summary\":\"Ping @octocat see https://evil.example now.\",\"missing_information\":[\"Which version?\"],\"suggested_labels\":[\"bug\"]}";
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

        private static async Task<(int Code, string Out, string Err, ProjectBoardReaderTests.StubHandler Handler)> Run(string[] args, Dictionary<string, string?> env, params Func<HttpRequestMessage, HttpResponseMessage>[] replies)
        {
            var handler = new ProjectBoardReaderTests.StubHandler(replies);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var code = await AgentCommand.RunAsync(args, key => env.TryGetValue(key, out var v) ? v : null, _ => throw new IOException(), stdout, stderr, new HttpClient(handler), CancellationToken.None);
            return (code, stdout.ToString(), stderr.ToString(), handler);
        }

        private static string[] Triage(params string[] extra) => new[] { "triage", "--repo", "owner/repo", "--issue", "7" }.Concat(extra).ToArray();

        private static Func<HttpRequestMessage, HttpResponseMessage> Ok(string body, HttpStatusCode status = HttpStatusCode.OK) => ProjectBoardReaderTests.Reply(body, status);

        [Fact]
        public async Task WithoutTheFlag_NothingIsPosted()
        {
            var result = await Run(Triage(), Env(), Ok(IssueJson(Labels)), Ok(Board()), Ok(Chat(Good)), Ok(Chat("{\"verdict\":\"ok\",\"issues\":[]}")));

            Assert.Equal(0, result.Code);
            Assert.Equal(4, result.Handler.Requests.Count);
            Assert.DoesNotContain(result.Handler.Requests, r => r.Url.EndsWith("/comments"));
            Assert.DoesNotContain("Posted", result.Err);
        }

        [Fact]
        public async Task TheFlagWithoutAWriteToken_StopsBeforeAnyRequest()
        {
            var result = await Run(Triage("--comment"), Env(withWriteToken: false), Ok(IssueJson(Labels)));

            Assert.Equal(2, result.Code);
            Assert.Contains("--comment needs GH_TOKEN or GITHUB_TOKEN", result.Err);
            Assert.Empty(result.Handler.Requests);
        }

        [Fact]
        public async Task AnAcceptedRun_PostsOneEscapedComment_AndKeepsItsExitCode()
        {
            var result = await Run(Triage("--comment"), Env(), Ok(IssueJson(Labels)), Ok(Board()), Ok(Chat(Good)), Ok(Chat("{\"verdict\":\"ok\",\"issues\":[]}")), Ok("{}", HttpStatusCode.Created));

            Assert.Equal(0, result.Code);
            Assert.Equal(5, result.Handler.Requests.Count);
            var post = result.Handler.Requests[4];
            Assert.Equal("https://api.github.com/repos/owner/repo/issues/7/comments", post.Url);
            Assert.Equal("Bearer " + WriteToken, post.Auth);
            using var json = JsonDocument.Parse(post.Body);
            var body = json.RootElement.GetProperty("body").GetString()!;
            Assert.StartsWith(AgentReportComment.Marker, body);
            Assert.Contains("A language model wrote any proposal below", body);
            Assert.Contains("Drafted by: Mistral", body);
            Assert.Contains("Reviewed by: Nvidia", body);
            Assert.Contains("Result: Accepted", body);
            AgentReportCommentTests.NotIn(body, "@octocat");
            AgentReportCommentTests.NotIn(body, "https://");
            AgentReportCommentTests.In(body, "@\u200Boctocat");
            Assert.Contains("Posted the report as a comment", result.Err);
        }

        [Fact]
        public async Task AFailedModel_IsPostedToo_WithoutTheProvidersResponseBody()
        {
            var result = await Run(Triage("--comment"), Env(), Ok(IssueJson("Agent::Allowed", "Task::issue-triage", "Model::Nvidia")), Ok(Board()), Ok(RealNotFound, HttpStatusCode.NotFound), Ok("{}", HttpStatusCode.Created));

            Assert.Equal(1, result.Code);
            using var comment = JsonDocument.Parse(result.Handler.Requests[3].Body);
            var body = comment.RootElement.GetProperty("body").GetString()!;
            Assert.Contains("Result: Failed", body);
            Assert.Contains("HTTP 404 \\(not found", body.Replace("(", "\\(").Replace("\\\\(", "\\("));
            foreach (var leak in new[] { "KTtt0", "9b96341b", "Function" })
            {
                Assert.DoesNotContain(leak, body);
                Assert.DoesNotContain(leak, result.Out);
                Assert.DoesNotContain(leak, result.Err);
            }

            using var report = JsonDocument.Parse(result.Out);
            Assert.Contains("HTTP 404 (not found", report.RootElement.GetProperty("triage").GetProperty("error").GetString());
        }

        [Fact]
        public async Task ANotEligibleIssue_IsPostedWithItsReason()
        {
            var result = await Run(Triage("--comment"), Env(), Ok(IssueJson("Task::issue-triage")), Ok("{}", HttpStatusCode.Created));

            Assert.Equal(1, result.Code);
            using var comment = JsonDocument.Parse(result.Handler.Requests[1].Body);
            Assert.Contains("Gate: not-allowed", comment.RootElement.GetProperty("body").GetString());
            Assert.Equal(2, result.Handler.Requests.Count);
        }

        [Fact]
        public async Task AnUnreadableIssue_HasNothingToCommentOn()
        {
            var result = await Run(Triage("--comment"), Env(), Ok("{}", HttpStatusCode.NotFound));

            Assert.Equal(1, result.Code);
            Assert.Single(result.Handler.Requests);
            Assert.DoesNotContain("Posted", result.Err);
        }

        [Fact]
        public async Task AFailedPost_ExitsTwo_WithAHint_AndNeverTheToken_ButTheReportIsStillPrinted()
        {
            var result = await Run(Triage("--comment"), Env(), Ok(IssueJson(Labels)), Ok(Board()), Ok(Chat(Good)), Ok(Chat("{\"verdict\":\"ok\",\"issues\":[]}")), Ok("{\"message\":\"" + WriteToken + "\"}", HttpStatusCode.Forbidden));

            Assert.Equal(2, result.Code);
            Assert.Contains("Could not post the comment", result.Err);
            Assert.Contains("permission to write issue comments", result.Err);
            foreach (var secret in new[] { ProjectToken, WriteToken, MistralKey, NvidiaKey })
            {
                Assert.DoesNotContain(secret, result.Err);
                Assert.DoesNotContain(secret, result.Out);
            }

            using var report = JsonDocument.Parse(result.Out);
            Assert.Equal("Completed", report.RootElement.GetProperty("stage").GetString());
        }

        [Fact]
        public void TheOptionIsParsed()
        {
            Assert.True(AgentOptions.Parse(Triage("--comment")).Comment);
            Assert.False(AgentOptions.Parse(Triage()).Comment);
        }
    }
}
