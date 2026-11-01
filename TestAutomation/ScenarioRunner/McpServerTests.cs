using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.Mcp;

namespace ScenarioRunner
{
    public class McpServerTests
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

        // Runs the server over an in-memory stream and returns the response lines it wrote.
        private static async Task<List<JsonDocument>> Talk(string input, ProjectBoardReaderTests.StubHandler? handler = null, Dictionary<string, string?>? env = null)
        {
            var http = handler == null ? null : new HttpClient(handler);
            var tools = McpTools.Create(Env(env), http);
            var output = new StringWriter();
            await McpProtocol.RunAsync(new StringReader(input), output, tools, "1.2.3", CancellationToken.None);
            return output.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l)).ToList();
        }

        private static string Request(int id, string method, string? parameters = null) =>
            "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\"" + (parameters == null ? "" : ",\"params\":" + parameters) + "}\n";

        private static string Call(int id, string tool, object arguments) =>
            Request(id, "tools/call", JsonSerializer.Serialize(new { name = tool, arguments }));

        private static string ToolText(JsonDocument response)
        {
            var result = response.RootElement.GetProperty("result");
            return result.GetProperty("content")[0].GetProperty("text").GetString()!;
        }

        private static bool IsToolError(JsonDocument response) => response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean();

        private const string Expected = "{\"ControlType\":\"Edit\",\"Name\":\"\",\"AutomationId\":\"txtEmail\",\"BoundingRectangle\":{\"X\":112,\"Y\":70,\"Width\":200,\"Height\":23},\"ParentControlType\":\"Window\",\"SiblingIndex\":2,\"SiblingCount\":7}";

        private const string Tree = "{\"ControlType\":\"Window\",\"Name\":\"Main\",\"AutomationId\":\"MainForm\",\"BoundingRectangle\":{\"X\":0,\"Y\":0,\"Width\":800,\"Height\":600},\"ParentControlType\":\"\",\"Children\":[" +
            "{\"ControlType\":\"Edit\",\"Name\":\"\",\"AutomationId\":\"textBox1\",\"BoundingRectangle\":{\"X\":112,\"Y\":70,\"Width\":200,\"Height\":23},\"ParentControlType\":\"Window\",\"SiblingIndex\":2,\"SiblingCount\":7}]}";

        [Fact]
        public async Task Initialize_AnswersWithTheRequestedVersion_AndOnlyToolCapabilities()
        {
            var responses = await Talk(Request(1, "initialize", "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}"));

            var result = Assert.Single(responses).RootElement.GetProperty("result");
            Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
            Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
            Assert.Single(result.GetProperty("capabilities").EnumerateObject());
            Assert.Equal("automation-sandbox-mcp", result.GetProperty("serverInfo").GetProperty("name").GetString());
            Assert.Equal("1.2.3", result.GetProperty("serverInfo").GetProperty("version").GetString());
        }

        [Fact]
        public async Task AnUnknownProtocolVersion_FallsBackToTheOneTheServerSpeaks()
        {
            var responses = await Talk(Request(1, "initialize", "{\"protocolVersion\":\"1999-01-01\"}"));

            Assert.Equal(McpProtocol.ProtocolVersion, responses[0].RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        }

        [Fact]
        public async Task ANotification_GetsNoResponse_AndPingIsAnswered()
        {
            var input = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n" + Request(2, "ping");

            var responses = await Talk(input);

            var only = Assert.Single(responses);
            Assert.Equal(2, only.RootElement.GetProperty("id").GetInt32());
        }

        [Fact]
        public async Task ToolsList_NamesTheFourReadOnlyTools_EachWithAnObjectSchema()
        {
            var responses = await Talk(Request(1, "tools/list"));

            var tools = responses[0].RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
            Assert.Equal(new[] { "check_issue_eligibility", "classify_ci_log", "heal_locator", "triage_issue_dry_run" }, tools.Select(t => t.GetProperty("name").GetString()!).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            foreach (var tool in tools)
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString()));
                Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
                Assert.False(tool.GetProperty("inputSchema").GetProperty("additionalProperties").GetBoolean());
            }
        }

        [Fact]
        public async Task NoToolAcceptsACredentialAsAnArgument()
        {
            var responses = await Talk(Request(1, "tools/list"));

            foreach (var tool in responses[0].RootElement.GetProperty("result").GetProperty("tools").EnumerateArray())
            {
                foreach (var property in tool.GetProperty("inputSchema").GetProperty("properties").EnumerateObject())
                {
                    var name = property.Name.ToLowerInvariant();
                    foreach (var word in new[] { "token", "key", "secret", "password", "credential", "auth" })
                    {
                        Assert.True(name.IndexOf(word, StringComparison.Ordinal) < 0, tool.GetProperty("name").GetString() + " has an argument named " + property.Name);
                    }
                }
            }
        }

        [Fact]
        public async Task HealLocator_ReturnsTheDecisionAsJson_AndTheCallIsNotAnError()
        {
            var responses = await Talk(Call(1, "heal_locator", new { expected = Expected, tree = Tree }));

            Assert.False(IsToolError(responses[0]));
            using var decision = JsonDocument.Parse(ToolText(responses[0]));
            Assert.True(decision.RootElement.GetProperty("IsConfident").GetBoolean());
            Assert.Contains("textBox1", ToolText(responses[0]));
        }

        [Theory]
        [InlineData("{\"expected\":\"not json\",\"tree\":\"{}\"}")]
        [InlineData("{\"expected\":5,\"tree\":\"{}\"}")]
        [InlineData("{\"tree\":\"{}\"}")]
        [InlineData("{\"expected\":\"{}\",\"tree\":\"{}\",\"profile\":\"reckless\"}")]
        public async Task HealLocator_RejectsBadArguments_AsAToolError(string arguments)
        {
            var responses = await Talk(Request(1, "tools/call", "{\"name\":\"heal_locator\",\"arguments\":" + arguments + "}"));

            Assert.True(IsToolError(responses[0]));
        }

        [Fact]
        public async Task HealLocator_RefusesAnOversizeDocument_WithoutParsingIt()
        {
            var responses = await Talk(Call(1, "heal_locator", new { expected = new string('x', McpTools.MaxDocumentLength + 1), tree = "{}" }));

            Assert.True(IsToolError(responses[0]));
            Assert.Contains("longer than", ToolText(responses[0]));
        }

        [Fact]
        public async Task ClassifyCiLog_ReturnsTheCategory_AndMasksACredentialInTheLog()
        {
            var log = "error CS1002: ; expected\nusing token=ghp_abcdefghijklmnopqrstuvwxyz0123456789 here";

            var responses = await Talk(Call(1, "classify_ci_log", new { log }));

            Assert.False(IsToolError(responses[0]));
            using var json = JsonDocument.Parse(ToolText(responses[0]));
            Assert.Equal("build", json.RootElement.GetProperty("category").GetString());
            Assert.DoesNotContain("ghp_abcdefghijklmnopqrstuvwxyz0123456789", ToolText(responses[0]));
        }

        [Fact]
        public async Task ClassifyCiLog_RequiresALogString()
        {
            var responses = await Talk(Call(1, "classify_ci_log", new { }));

            Assert.True(IsToolError(responses[0]));
        }

        private static string IssueJson(params string[] labels) =>
            "{\"number\":7,\"title\":\"Crash\",\"state\":\"open\",\"body\":\"It crashes.\",\"labels\":[" + string.Join(",", labels.Select(l => "{\"name\":\"" + l + "\"}")) + "]}";

        private static string Board(string status) => ProjectBoardReaderTests.Page(new[]
        {
            "{\"content\":{\"number\":7,\"repository\":{\"nameWithOwner\":\"owner/repo\"}},\"fieldValueByName\":{\"name\":\"" + status + "\"}}",
        });

        private static string Chat(string content) => "{\"choices\":[{\"message\":{\"content\":" + JsonSerializer.Serialize(content) + "}}]}";

        private static readonly string[] Labels = { "Agent::Allowed", "Task::issue-triage", "Model::Mistral", "Review::Nvidia" };

        [Fact]
        public async Task CheckIssueEligibility_ReportsTheGateDecision_AndOnlyReads()
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(Labels)), ProjectBoardReaderTests.Reply(Board("Ready")));

            var responses = await Talk(Call(1, "check_issue_eligibility", new { repository = "owner/repo", issue = 7 }), handler);

            using var json = JsonDocument.Parse(ToolText(responses[0]));
            Assert.True(json.RootElement.GetProperty("eligible").GetBoolean());
            Assert.Equal("issue-triage", json.RootElement.GetProperty("taskKind").GetString());
            Assert.Equal(2, handler.Requests.Count);
            Assert.DoesNotContain(handler.Requests, r => r.Body.IndexOf("mutation", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(handler.Requests, r => r.Url.EndsWith("/comments", StringComparison.Ordinal) || r.Url.EndsWith("/labels", StringComparison.Ordinal));
        }

        [Fact]
        public async Task CheckIssueEligibility_ANotAllowedIssue_IsReportedWithItsReason()
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson("Task::issue-triage")));

            var responses = await Talk(Call(1, "check_issue_eligibility", new { repository = "owner/repo", issue = 7 }), handler);

            using var json = JsonDocument.Parse(ToolText(responses[0]));
            Assert.False(json.RootElement.GetProperty("eligible").GetBoolean());
            Assert.Equal("not-allowed", json.RootElement.GetProperty("reason").GetString());
        }

        [Theory]
        [InlineData("{\"repository\":\"owner\",\"issue\":7}")]
        [InlineData("{\"repository\":\"../x/y\",\"issue\":7}")]
        [InlineData("{\"repository\":\"owner/repo\",\"issue\":0}")]
        [InlineData("{\"repository\":\"owner/repo\",\"issue\":\"7\"}")]
        [InlineData("{\"repository\":\"owner/repo\",\"issue\":7,\"project_id\":\"nope\"}")]
        public async Task TheIssueTools_RejectBadArguments_BeforeAnyRequest(string arguments)
        {
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(Labels)));

            var responses = await Talk(Request(1, "tools/call", "{\"name\":\"check_issue_eligibility\",\"arguments\":" + arguments + "}"), handler);

            Assert.True(IsToolError(responses[0]));
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task TheIssueTools_WithoutAServerToken_SayTheServerIsNotConfigured_AndNeverEchoAnyKey()
        {
            var env = new Dictionary<string, string?>(FullEnv) { ["PROJECT_TOKEN"] = null };
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(Labels)));

            var responses = await Talk(Call(1, "check_issue_eligibility", new { repository = "owner/repo", issue = 7 }), handler, env);

            Assert.True(IsToolError(responses[0]));
            Assert.Contains("PROJECT_TOKEN", ToolText(responses[0]));
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task TriageDryRun_ReturnsTheProposal_WritesNothing_AndNoSecretAppearsInAnyAnswer()
        {
            const string good = "{\"category\":\"bug\",\"summary\":\"It crashes on start.\",\"missing_information\":[],\"suggested_labels\":[\"bug\"]}";
            var handler = new ProjectBoardReaderTests.StubHandler(
                ProjectBoardReaderTests.Reply(IssueJson(Labels)), ProjectBoardReaderTests.Reply(Board("Ready")),
                ProjectBoardReaderTests.Reply(Chat(good)), ProjectBoardReaderTests.Reply(Chat("{\"verdict\":\"ok\",\"issues\":[]}")));

            var responses = await Talk(Call(1, "triage_issue_dry_run", new { repository = "owner/repo", issue = 7 }), handler);

            var text = ToolText(responses[0]);
            using var json = JsonDocument.Parse(text);
            Assert.Equal("Accepted", json.RootElement.GetProperty("triage").GetProperty("status").GetString());
            Assert.Equal(4, handler.Requests.Count);
            Assert.DoesNotContain(handler.Requests, r => r.Url.EndsWith("/comments", StringComparison.Ordinal) || r.Url.EndsWith("/labels", StringComparison.Ordinal));
            Assert.DoesNotContain(handler.Requests, r => r.Body.IndexOf("mutation", StringComparison.OrdinalIgnoreCase) >= 0);
            foreach (var secret in new[] { ProjectToken, IssueToken, MistralKey, NvidiaKey })
            {
                Assert.DoesNotContain(secret, text);
                Assert.DoesNotContain(secret, responses[0].RootElement.GetRawText());
            }
        }

        [Fact]
        public async Task TriageDryRun_WithoutAModel_IsAToolError()
        {
            var env = new Dictionary<string, string?> { ["PROJECT_TOKEN"] = ProjectToken, ["PROJECT_ID"] = Project };
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(Labels)));

            var responses = await Talk(Call(1, "triage_issue_dry_run", new { repository = "owner/repo", issue = 7 }), handler, env);

            Assert.True(IsToolError(responses[0]));
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task AnUnknownTool_IsAProtocolError()
        {
            var responses = await Talk(Call(1, "delete_everything", new { }));

            var error = responses[0].RootElement.GetProperty("error");
            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        }

        [Fact]
        public async Task AnUnknownMethod_IsMethodNotFound()
        {
            var responses = await Talk(Request(1, "resources/list"));

            Assert.Equal(-32601, responses[0].RootElement.GetProperty("error").GetProperty("code").GetInt32());
        }

        [Fact]
        public async Task MalformedJson_GetsAParseError_AndTheServerKeepsServing()
        {
            var responses = await Talk("{not json\n" + Request(2, "ping"));

            Assert.Equal(2, responses.Count);
            Assert.Equal(-32700, responses[0].RootElement.GetProperty("error").GetProperty("code").GetInt32());
            Assert.Equal(JsonValueKind.Null, responses[0].RootElement.GetProperty("id").ValueKind);
            Assert.Equal(2, responses[1].RootElement.GetProperty("id").GetInt32());
        }

        [Theory]
        [InlineData("[1,2]")]
        [InlineData("\"text\"")]
        [InlineData("{\"jsonrpc\":\"2.0\",\"id\":{},\"method\":\"ping\"}")]
        [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1}")]
        public async Task AnInvalidRequest_GetsAnInvalidRequestError(string line)
        {
            var responses = await Talk(line + "\n");

            Assert.Equal(-32600, responses[0].RootElement.GetProperty("error").GetProperty("code").GetInt32());
        }

        [Fact]
        public async Task ToolsCall_WithBadParams_IsInvalidParams()
        {
            foreach (var parameters in new[] { "{}", "{\"name\":5}", "{\"name\":\"classify_ci_log\",\"arguments\":[1]}" })
            {
                var responses = await Talk(Request(1, "tools/call", parameters));

                Assert.Equal(-32602, responses[0].RootElement.GetProperty("error").GetProperty("code").GetInt32());
            }
        }

        [Fact]
        public async Task AnOversizeMessage_IsDiscardedWithAnError_AndTheNextMessageIsStillServed()
        {
            var huge = new string('a', McpProtocol.MaxMessageLength + 10);

            var responses = await Talk(huge + "\n" + Request(2, "ping"));

            Assert.Equal(2, responses.Count);
            Assert.Equal(-32600, responses[0].RootElement.GetProperty("error").GetProperty("code").GetInt32());
            Assert.Equal(2, responses[1].RootElement.GetProperty("id").GetInt32());
        }

        [Fact]
        public async Task AToolAnswer_IsCutAtTheLimit_AndSaysSo()
        {
            var tools = new[] { new McpTool("big", "x", "{\"type\":\"object\"}", (a, c) => Task.FromResult(McpToolResult.Ok(new string('y', McpProtocol.MaxToolOutputLength * 2)))) };
            var output = new StringWriter();

            await McpProtocol.RunAsync(new StringReader(Call(1, "big", new { })), output, tools, "1", CancellationToken.None);

            using var json = JsonDocument.Parse(output.ToString());
            var text = json.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            Assert.True(text.Length < McpProtocol.MaxToolOutputLength + 100);
            Assert.Contains("output cut", text);
        }

        [Fact]
        public async Task AToolThatThrows_BecomesAToolError_WithOnlyTheExceptionType()
        {
            var tools = new[] { new McpTool("boom", "x", "{\"type\":\"object\"}", (a, c) => throw new InvalidOperationException("secret detail https://x/?k=abc")) };
            var output = new StringWriter();

            await McpProtocol.RunAsync(new StringReader(Call(1, "boom", new { })), output, tools, "1", CancellationToken.None);

            var text = output.ToString();
            Assert.Contains("InvalidOperationException", text);
            Assert.DoesNotContain("secret detail", text);
        }

        [Fact]
        public async Task EveryResponse_IsOneLine_OfJsonRpc2()
        {
            var output = new StringWriter();
            var tools = McpTools.Create(Env(), null);

            await McpProtocol.RunAsync(new StringReader(Request(1, "tools/list") + Call(2, "classify_ci_log", new { log = "line one\nline two\nerror CS1002" })), output, tools, "1", CancellationToken.None);

            var lines = output.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            foreach (var line in lines)
            {
                using var json = JsonDocument.Parse(line);
                Assert.Equal("2.0", json.RootElement.GetProperty("jsonrpc").GetString());
            }
        }
    }
}
