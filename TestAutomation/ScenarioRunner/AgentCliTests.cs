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

namespace ScenarioRunner
{
    public class AgentOptionsTests
    {
        [Fact]
        public void ARepoAndAnIssue_AreEnough()
        {
            var options = AgentOptions.Parse(new[] { "triage", "--repo", "owner/repo.name", "--issue", "7" });

            Assert.Equal("triage", options.Command);
            Assert.Equal("owner/repo.name", options.Repository);
            Assert.Equal(7, options.Issue);
            Assert.Null(options.ProjectId);
            Assert.Null(options.RoutingFile);
            Assert.Empty(options.AllowedLabels);
        }

        [Fact]
        public void EveryOption_IsRead()
        {
            var options = AgentOptions.Parse(new[] { "triage", "--repo", "o/r", "--issue", "12", "--project-id", "PVT_kwHOAr3aNM4BgUrT", "--routing-file", "routing.json", "--allow-label", "bug", "--allow-label", "P1" });

            Assert.Equal("PVT_kwHOAr3aNM4BgUrT", options.ProjectId);
            Assert.Equal("routing.json", options.RoutingFile);
            Assert.Equal(new[] { "bug", "P1" }, options.AllowedLabels);
        }

        [Theory]
        [InlineData("-h")]
        [InlineData("--help")]
        public void Help_NeedsNoOtherArgument(string flag)
        {
            Assert.True(AgentOptions.Parse(new[] { flag }).ShowHelp);
            Assert.True(AgentOptions.Parse(new[] { "triage", flag }).ShowHelp);
        }

        [Theory]
        [InlineData(new string[0], "A command is required")]
        [InlineData(new[] { "run" }, "Unknown command")]
        [InlineData(new[] { "triage" }, "--repo")]
        [InlineData(new[] { "triage", "--repo", "o/r" }, "--issue is required")]
        [InlineData(new[] { "triage", "--issue", "7" }, "--repo")]
        [InlineData(new[] { "triage", "--repo", "owner", "--issue", "7" }, "owner/name")]
        [InlineData(new[] { "triage", "--repo", "o/r\n", "--issue", "7" }, "owner/name")]
        [InlineData(new[] { "triage", "--repo", "../o/r", "--issue", "7" }, "owner/name")]
        [InlineData(new[] { "triage", "--repo", "o/r", "--issue", "0" }, "positive")]
        [InlineData(new[] { "triage", "--repo", "o/r", "--issue", "-3" }, "positive")]
        [InlineData(new[] { "triage", "--repo", "o/r", "--issue", "seven" }, "positive")]
        [InlineData(new[] { "triage", "--repo", "o/r", "--issue", "7", "--project-id", "NOPE" }, "PVT_")]
        [InlineData(new[] { "triage", "--repo", "o/r", "--issue" }, "needs a value")]
        [InlineData(new[] { "triage", "--repo", "o/r", "--issue", "7", "--bogus" }, "Unknown argument")]
        public void InvalidArguments_AreRejectedWithAReason(string[] args, string expected)
        {
            var error = Assert.Throws<AgentUsageException>(() => AgentOptions.Parse(args));

            Assert.Contains(expected, error.Message);
        }

        [Theory]
        [InlineData("--token")]
        [InlineData("--project-token")]
        [InlineData("--api-key")]
        [InlineData("--GEMINI_API_KEY")]
        [InlineData("--secret")]
        [InlineData("--password")]
        public void ASecretNeverComesFromAnArgument_AndIsNeverEchoedBack(string flag)
        {
            var error = Assert.Throws<AgentUsageException>(() => AgentOptions.Parse(new[] { "triage", "--repo", "o/r", "--issue", "7", flag, "super-secret-value" }));

            Assert.Contains("environment variables only", error.Message);
            Assert.DoesNotContain("super-secret-value", error.Message);
        }

        [Fact]
        public void AnUnknownArgumentIsShortened_SoAPastedSecretIsNotEchoed()
        {
            var error = Assert.Throws<AgentUsageException>(() => AgentOptions.Parse(new[] { "triage", "--repo", "o/r", "--issue", "7", "ghp_abcdefghijklmnopqrstuvwxyz0123456789" }));

            Assert.DoesNotContain("0123456789", error.Message);
        }
    }

    public class AgentCommandTests
    {
        private const string ProjectToken = "project-token-ppp111";
        private const string IssueToken = "issue-token-iii222";
        private const string MistralKey = "mistral-key-mmm333";
        private const string NvidiaKey = "nvidia-key-nnn444";
        private const string Project = "PVT_kwHOAr3aNM4BgUrT";

        private static readonly Dictionary<string, string?> FullEnv = new Dictionary<string, string?>
        {
            ["PROJECT_TOKEN"] = ProjectToken,
            ["PROJECT_ID"] = Project,
            ["GH_TOKEN"] = IssueToken,
            ["MISTRAL_API_KEY"] = MistralKey,
            ["MISTRAL_MODEL"] = "mistral-test",
            ["NVIDIA_API_KEY"] = NvidiaKey,
            ["NVIDIA_MODEL"] = "nvidia-test",
        };

        private static Func<string, string?> Env(Dictionary<string, string?>? values = null)
        {
            var map = values ?? FullEnv;
            return key => map.TryGetValue(key, out var value) ? value : null;
        }

        private static string[] Triage(params string[] extra) =>
            new[] { "triage", "--repo", "owner/repo", "--issue", "7" }.Concat(extra).ToArray();

        private static string IssueJson(bool open = true, params string[] labels) =>
            "{\"number\":7,\"title\":\"Crash on start\",\"state\":\"" + (open ? "open" : "closed") + "\",\"body\":\"It crashes.\",\"labels\":[" +
            string.Join(",", labels.Select(l => "{\"name\":\"" + l + "\"}")) + "]}";

        private static string Board(string status) => ProjectBoardReaderTests.Page(new[]
        {
            "{\"content\":{\"number\":7,\"repository\":{\"nameWithOwner\":\"owner/repo\"}},\"fieldValueByName\":{\"name\":\"" + status + "\"}}",
        });

        private static string ChatAnswer(string content) => "{\"choices\":[{\"message\":{\"content\":" + JsonSerializer.Serialize(content) + "}}]}";

        private const string Good = "{\"category\":\"bug\",\"summary\":\"It crashes on start.\",\"missing_information\":[\"Which version?\"],\"suggested_labels\":[\"bug\"]}";

        private static readonly string[] Labels = { "Agent::Allowed", "Task::issue-triage", "Model::Mistral", "Review::Nvidia" };

        private static async Task<(int Code, string Out, string Err, ProjectBoardReaderTests.StubHandler Handler)> Run(
            string[] args, ProjectBoardReaderTests.StubHandler handler, Dictionary<string, string?>? env = null, Func<string, string>? readFile = null, CancellationToken token = default)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var code = await AgentCommand.RunAsync(args, Env(env), readFile ?? (_ => throw new IOException("no file")), stdout, stderr, new HttpClient(handler), token);
            return (code, stdout.ToString(), stderr.ToString(), handler);
        }

        private static ProjectBoardReaderTests.StubHandler Handler(params string[] bodies) =>
            new ProjectBoardReaderTests.StubHandler(bodies.Select(b => ProjectBoardReaderTests.Reply(b)).ToArray());

        [Fact]
        public async Task AnEligibleIssue_IsTriagedAndReviewed_ExitZero_AndTheReportIsJson()
        {
            var handler = Handler(IssueJson(true, Labels), Board("Ready"), ChatAnswer(Good), ChatAnswer("{\"verdict\":\"ok\",\"issues\":[]}"));

            var result = await Run(Triage(), handler);

            Assert.Equal(0, result.Code);
            using var json = JsonDocument.Parse(result.Out);
            var root = json.RootElement;
            Assert.Equal("Completed", root.GetProperty("stage").GetString());
            Assert.True(root.GetProperty("gate").GetProperty("eligible").GetBoolean());
            Assert.Equal("issue-triage", root.GetProperty("taskKind").GetString());
            var triage = root.GetProperty("triage");
            Assert.Equal("Accepted", triage.GetProperty("status").GetString());
            Assert.Equal("Mistral", triage.GetProperty("draftedBy").GetString());
            Assert.Equal("Nvidia", triage.GetProperty("reviewedBy").GetString());
            Assert.Equal("bug", triage.GetProperty("proposal").GetProperty("category").GetString());
            Assert.Equal("Which version?", triage.GetProperty("proposal").GetProperty("missingInformation")[0].GetString());
            Assert.Contains("Triage Accepted", result.Err);
            Assert.Equal(4, handler.Requests.Count);
        }

        [Fact]
        public async Task NoKeyOrTokenAppearsInTheOutputOrInAnyRequestUrlOrBodyItShouldNotBeIn()
        {
            var handler = Handler(IssueJson(true, Labels), Board("Ready"), ChatAnswer(Good), ChatAnswer("{\"verdict\":\"ok\",\"issues\":[]}"));

            var result = await Run(Triage(), handler);

            foreach (var secret in new[] { ProjectToken, IssueToken, MistralKey, NvidiaKey })
            {
                Assert.DoesNotContain(secret, result.Out);
                Assert.DoesNotContain(secret, result.Err);
                Assert.All(handler.Requests, r => Assert.DoesNotContain(secret, r.Url));
                Assert.All(handler.Requests, r => Assert.DoesNotContain(secret, r.Body));
            }

            Assert.Equal("Bearer " + IssueToken, handler.Requests[0].Auth);
            Assert.Equal("Bearer " + ProjectToken, handler.Requests[1].Auth);
            Assert.Equal("Bearer " + MistralKey, handler.Requests[2].Auth);
            Assert.Equal("Bearer " + NvidiaKey, handler.Requests[3].Auth);
        }

        [Fact]
        public async Task AClosedIssue_IsNotEligible_ExitOne_AndNoModelIsAsked()
        {
            var handler = Handler(IssueJson(false, Labels));

            var result = await Run(Triage(), handler);

            Assert.Equal(1, result.Code);
            using var json = JsonDocument.Parse(result.Out);
            Assert.Equal("NotEligible", json.RootElement.GetProperty("stage").GetString());
            Assert.Equal("closed", json.RootElement.GetProperty("gate").GetProperty("reason").GetString());
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task ABlockingReview_ExitsOne_AndTheFindingsAreInTheReport()
        {
            var handler = Handler(IssueJson(true, Labels), Board("Ready"), ChatAnswer(Good), ChatAnswer("{\"verdict\":\"issues\",\"issues\":[\"Windows is never mentioned.\"]}"));

            var result = await Run(Triage(), handler);

            Assert.Equal(1, result.Code);
            using var json = JsonDocument.Parse(result.Out);
            var triage = json.RootElement.GetProperty("triage");
            Assert.Equal("Blocked", triage.GetProperty("status").GetString());
            Assert.Equal("Windows is never mentioned.", triage.GetProperty("findings")[0].GetString());
        }

        [Fact]
        public async Task ARoutingMistake_ExitsOne_WithItsProblems_AndNoModelIsAsked()
        {
            var handler = Handler(IssueJson(true, "Agent::Allowed", "Task::issue-triage", "Model::Nobody"), Board("Ready"));

            var result = await Run(Triage(), handler);

            Assert.Equal(1, result.Code);
            using var json = JsonDocument.Parse(result.Out);
            Assert.Equal("RoutingFailed", json.RootElement.GetProperty("stage").GetString());
            Assert.Contains("Nobody", json.RootElement.GetProperty("routingProblems")[0].GetString());
            Assert.Equal(2, handler.Requests.Count);
        }

        [Fact]
        public async Task TheProjectIdMayComeFromTheCommandLine_AndTheEnvironmentIsNotRequiredToHaveIt()
        {
            var env = new Dictionary<string, string?>(FullEnv);
            env.Remove("PROJECT_ID");
            var handler = Handler(IssueJson(false, Labels));

            var withFlag = await Run(Triage("--project-id", Project), handler, env);
            var without = await Run(Triage(), Handler(IssueJson(false, Labels)), env);

            Assert.Equal(1, withFlag.Code);
            Assert.Equal(2, without.Code);
            Assert.Contains("project board id is required", without.Err);
        }

        [Fact]
        public async Task ARoutingFile_IsReadAndApplied()
        {
            var routing = "{\"models\":{\"Mistral\":{\"tasks\":[\"issue-triage\"]},\"Nvidia\":{\"tasks\":[\"docs\"]}}}";
            var handler = Handler(IssueJson(true, Labels), Board("Ready"));

            var result = await Run(Triage("--routing-file", "routing.json"), handler, readFile: path => path == "routing.json" ? routing : throw new IOException());

            Assert.Equal(1, result.Code);
            using var json = JsonDocument.Parse(result.Out);
            Assert.Equal("RoutingFailed", json.RootElement.GetProperty("stage").GetString());
            Assert.Contains("not allowed to serve", json.RootElement.GetProperty("routingProblems")[0].GetString());
        }

        [Theory]
        [InlineData("not json", "invalid")]
        [InlineData("{\"models\":{\"A\":{\"tasks\":[\"cook\"]}}}", "unknown task kind")]
        public async Task AnInvalidRoutingFile_ExitsTwo_BeforeAnyRequest(string content, string expected)
        {
            var handler = Handler(IssueJson(true, Labels));

            var result = await Run(Triage("--routing-file", "r.json"), handler, readFile: _ => content);

            Assert.Equal(2, result.Code);
            Assert.Contains(expected, result.Err);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task AMissingRoutingFile_ExitsTwo_WithoutEchoingTheSystemMessage()
        {
            var result = await Run(Triage("--routing-file", "r.json"), Handler(IssueJson(true, Labels)), readFile: _ => throw new IOException("C:\\secret\\path"));

            Assert.Equal(2, result.Code);
            Assert.Contains("could not be read", result.Err);
            Assert.DoesNotContain("secret", result.Err);
        }

        [Fact]
        public async Task WithoutAProjectToken_ExitsTwo_BeforeAnyRequest()
        {
            var env = new Dictionary<string, string?>(FullEnv) { ["PROJECT_TOKEN"] = "  " };
            var handler = Handler(IssueJson(true, Labels));

            var result = await Run(Triage(), handler, env);

            Assert.Equal(2, result.Code);
            Assert.Contains("PROJECT_TOKEN is required", result.Err);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task WithoutAnyModel_ExitsTwo_NamingTheVariablesToSet_BeforeAnyRequest()
        {
            var env = new Dictionary<string, string?> { ["PROJECT_TOKEN"] = ProjectToken, ["PROJECT_ID"] = Project, ["MISTRAL_API_KEY"] = MistralKey };
            var handler = Handler(IssueJson(true, Labels));

            var result = await Run(Triage(), handler, env);

            Assert.Equal(2, result.Code);
            Assert.Contains("No model is configured", result.Err);
            Assert.Contains("MISTRAL_MODEL", result.Err);
            Assert.Empty(handler.Requests);
            Assert.DoesNotContain(MistralKey, result.Err);
        }

        [Fact]
        public async Task BadArguments_ExitTwo_AndWriteNothingToStdout()
        {
            var result = await Run(new[] { "triage", "--repo", "owner/repo", "--issue", "7", "--token", "super-secret" }, Handler(IssueJson()));

            Assert.Equal(2, result.Code);
            Assert.Equal("", result.Out);
            Assert.Contains("environment variables only", result.Err);
            Assert.DoesNotContain("super-secret", result.Err);
            Assert.Empty(result.Handler.Requests);
        }

        [Fact]
        public async Task Help_ExitsZero_AndNamesTheEnvironmentVariablesAndExitCodes()
        {
            var result = await Run(new[] { "--help" }, Handler(IssueJson()));

            Assert.Equal(0, result.Code);
            Assert.Contains("PROJECT_TOKEN", result.Out);
            Assert.Contains("never read from arguments", result.Out);
            Assert.Contains("Exit codes", result.Out);
            Assert.Equal("", result.Err);
        }

        [Fact]
        public async Task Cancellation_ExitsTwo_WithoutAnException()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await Run(Triage(), Handler(IssueJson(true, Labels)), token: cts.Token);

            Assert.Equal(2, result.Code);
            Assert.Contains("Cancelled", result.Err);
        }
    }
}
