using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;

namespace ScenarioRunner
{
    // The whole path from a GitHub issue to a reviewed proposal, with a stub GitHub and fake models. What it proves is
    // the ordering: no model is asked unless the gate passed and routing succeeded, and nothing is ever written to GitHub.
    public class AgentTriageRunTests
    {
        private const string IssueToken = "issue-token-aaa111";
        private const string ProjectToken = "project-token-bbb222";
        private const string Project = "PVT_kwHOAr3aNM4BgUrT";

        private sealed class FakeModel : ITextModel
        {
            private readonly Func<TextCompletionRequest, TextCompletionResult> _answer;

            public FakeModel(string name, Func<TextCompletionRequest, TextCompletionResult> answer)
            {
                Name = name;
                _answer = answer;
            }

            public string Name { get; }

            public bool IsAvailable => true;

            public int Calls { get; private set; }

            public Task<TextCompletionResult> CompleteAsync(TextCompletionRequest request, CancellationToken cancellationToken = default)
            {
                Calls++;
                return Task.FromResult(_answer(request));
            }
        }

        private const string Good = "{\"category\":\"bug\",\"summary\":\"It crashes on start.\",\"missing_information\":[\"Which version?\"],\"suggested_labels\":[\"bug\"]}";

        private static TextCompletionResult Ok(string text) => new TextCompletionResult { Success = true, Text = text };

        private static string IssueJson(bool open = true, params string[] labels) =>
            "{\"number\":7,\"title\":\"Crash on start\",\"state\":\"" + (open ? "open" : "closed") + "\",\"body\":\"It crashes on start.\",\"labels\":[" +
            string.Join(",", labels.Select(l => "{\"name\":\"" + l + "\"}")) + "]}";

        private static string Board(string? status) => ProjectBoardReaderTests.Page(new[]
        {
            "{\"content\":{\"number\":7,\"repository\":{\"nameWithOwner\":\"owner/repo\"}},\"fieldValueByName\":" + (status == null ? "null" : "{\"name\":\"" + status + "\"}") + "}",
        });

        private static readonly string[] Eligible = { "Agent::Allowed", "Task::issue-triage", "Model::Drafter", "Review::Reviewer" };

        private static (TextModelRegistry Registry, FakeModel Drafter, FakeModel Reviewer) Models(string draft = Good, string review = "{\"verdict\":\"ok\",\"issues\":[]}")
        {
            var drafter = new FakeModel("Drafter", _ => Ok(draft));
            var reviewer = new FakeModel("Reviewer", _ => Ok(review));
            var registry = new TextModelRegistry();
            registry.Add(drafter);
            registry.Add(reviewer);
            return (registry, drafter, reviewer);
        }

        private static Task<AgentRunReport> Run(TextModelRegistry registry, ProjectBoardReaderTests.StubHandler handler, TextModelRoutingConfig? config = null, CancellationToken token = default) =>
            AgentTriageRun.RunAsync("owner/repo", 7, Project, ProjectToken, registry, config, issueToken: IssueToken, httpClient: new HttpClient(handler), cancellationToken: token);

        [Fact]
        public async Task AnEligibleIssue_IsReadGatedRoutedDraftedAndReviewed_AndNothingIsWritten()
        {
            var (registry, drafter, reviewer) = Models();
            var handler = new ProjectBoardReaderTests.StubHandler(
                ProjectBoardReaderTests.Reply(IssueJson(true, Eligible)),
                ProjectBoardReaderTests.Reply(Board("Ready")));

            var report = await Run(registry, handler);

            Assert.Equal(AgentRunStage.Completed, report.Stage);
            Assert.True(report.Gate!.Eligible);
            Assert.Equal("issue-triage", report.TaskKind);
            Assert.Empty(report.RoutingProblems);
            Assert.Equal(TriageStatus.Accepted, report.Triage!.Status);
            Assert.Equal("Drafter", report.Triage.DraftedBy);
            Assert.Equal("Reviewer", report.Triage.ReviewedBy);
            Assert.Equal("bug", report.Triage.Proposal!.Category);
            Assert.Equal(1, drafter.Calls);
            Assert.Equal(1, reviewer.Calls);
            Assert.StartsWith("Triage Accepted", report.Summary());
            // Exactly the issue read and the board read: no label, comment or card was touched.
            Assert.Equal(2, handler.Requests.Count);
            Assert.EndsWith("/repos/owner/repo/issues/7", handler.Requests[0].Url);
            Assert.Equal("https://api.github.com/graphql", handler.Requests[1].Url);
            Assert.DoesNotContain("mutation", handler.Requests[1].Body);
        }

        [Fact]
        public async Task AReviewerThatBlocks_ProducesACompletedRunWhoseOutcomeIsBlocked()
        {
            var (registry, _, _) = Models(review: "{\"verdict\":\"issues\",\"issues\":[\"The issue never mentions Windows.\"]}");
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(true, Eligible)), ProjectBoardReaderTests.Reply(Board("Ready")));

            var report = await Run(registry, handler);

            Assert.Equal(AgentRunStage.Completed, report.Stage);
            Assert.Equal(TriageStatus.Blocked, report.Triage!.Status);
            Assert.Equal("The issue never mentions Windows.", Assert.Single(report.Triage.Findings));
        }

        [Fact]
        public async Task AFailingDrafter_IsACompletedRunWhoseOutcomeIsFailed()
        {
            var drafter = new FakeModel("Drafter", _ => new TextCompletionResult { Success = false, ErrorMessage = "HTTP 500" });
            var registry = new TextModelRegistry();
            registry.Add(drafter);
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(true, "Agent::Allowed", "Task::issue-triage", "Model::Drafter")), ProjectBoardReaderTests.Reply(Board("Ready")));

            var report = await Run(registry, handler);

            Assert.Equal(AgentRunStage.Completed, report.Stage);
            Assert.Equal(TriageStatus.Failed, report.Triage!.Status);
            Assert.Contains("HTTP 500", report.Summary());
        }

        [Theory]
        [InlineData("closed", GateReasons.Closed)]
        [InlineData("rejected", GateReasons.Rejected)]
        [InlineData("working", GateReasons.AlreadyWorking)]
        [InlineData("not-allowed", GateReasons.NotAllowed)]
        [InlineData("no-task", GateReasons.TaskKindMissing)]
        [InlineData("backlog", GateReasons.ProjectNotReady)]
        [InlineData("not-on-board", GateReasons.BoardStatusUnknown)]
        public async Task AnIssueTheGateStops_NeverReachesAModel(string scenario, string reason)
        {
            var (registry, drafter, reviewer) = Models();
            var labels = Eligible.ToList();
            var open = true;
            string board = "Ready";
            string? boardBody = null;
            switch (scenario)
            {
                case "closed": open = false; break;
                case "rejected": labels.Add("Agent::Rejected"); break;
                case "working": labels.Add("Agent::Working"); break;
                case "not-allowed": labels.Remove("Agent::Allowed"); break;
                case "no-task": labels.Remove("Task::issue-triage"); break;
                case "backlog": board = "Backlog"; break;
                case "not-on-board": boardBody = ProjectBoardReaderTests.Page(new string[0]); break;
            }

            var handler = new ProjectBoardReaderTests.StubHandler(
                ProjectBoardReaderTests.Reply(IssueJson(open, labels.ToArray())),
                ProjectBoardReaderTests.Reply(boardBody ?? Board(board)));

            var report = await Run(registry, handler);

            Assert.Equal(AgentRunStage.NotEligible, report.Stage);
            Assert.Equal(reason, report.Gate!.Reason);
            Assert.Null(report.Triage);
            Assert.Equal(0, drafter.Calls);
            Assert.Equal(0, reviewer.Calls);
            Assert.StartsWith("Not eligible: " + reason, report.Summary());
            if (scenario == "not-on-board")
            {
                Assert.Contains("not on the project board", report.ReadError);
            }
        }

        [Fact]
        public async Task AnIssueThatCannotBeRead_IsNotEligible_AndNoModelIsAsked()
        {
            var (registry, drafter, _) = Models();
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply("{}", HttpStatusCode.NotFound));

            var report = await Run(registry, handler);

            Assert.Equal(AgentRunStage.NotEligible, report.Stage);
            Assert.Equal(GateReasons.IssueUnreadable, report.Gate!.Reason);
            Assert.Contains("404", report.ReadError);
            Assert.Equal(0, drafter.Calls);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task ATaskKindWithNoImplementationYet_IsReportedAndNoModelIsAsked()
        {
            var (registry, drafter, reviewer) = Models();
            var handler = new ProjectBoardReaderTests.StubHandler(
                ProjectBoardReaderTests.Reply(IssueJson(true, "Agent::Allowed", "Task::docs", "Model::Drafter")), ProjectBoardReaderTests.Reply(Board("Ready")));

            var report = await Run(registry, handler);

            Assert.Equal(AgentRunStage.UnsupportedTask, report.Stage);
            Assert.Equal("docs", report.TaskKind);
            Assert.Contains("not implemented yet", report.Summary());
            Assert.Equal(0, drafter.Calls + reviewer.Calls);
        }

        [Theory]
        [InlineData("Model::Nobody", "not a configured model")]
        [InlineData("Review::Drafter", "different model")]
        public async Task AMistakeInTheModelLabels_StopsBeforeAnyModelIsAsked(string badLabel, string expected)
        {
            var (registry, drafter, reviewer) = Models();
            var labels = new List<string> { "Agent::Allowed", "Task::issue-triage", "Model::Drafter" };
            labels.Add(badLabel);
            if (badLabel.StartsWith("Model::")) labels.Remove("Model::Drafter");
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(true, labels.ToArray())), ProjectBoardReaderTests.Reply(Board("Ready")));

            var report = await Run(registry, handler);

            Assert.Equal(AgentRunStage.RoutingFailed, report.Stage);
            Assert.Contains(report.RoutingProblems, p => p.Contains(expected));
            Assert.StartsWith("Routing stopped the task", report.Summary());
            Assert.Equal(0, drafter.Calls + reviewer.Calls);
        }

        [Fact]
        public async Task TheRoutingConfiguration_IsApplied_ADefaultCanReplaceTheLabels_AndARestrictionStops()
        {
            var (registry, drafter, _) = Models();
            var config = TextModelRoutingConfig.Parse("{\"models\":{\"Drafter\":{\"tasks\":[\"issue-triage\"]},\"Reviewer\":{\"tasks\":[\"issue-triage\"]}},\"defaults\":{\"issue-triage\":{\"model\":\"Drafter\",\"reviewer\":\"Reviewer\"}}}");
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(true, "Agent::Allowed", "Task::issue-triage")), ProjectBoardReaderTests.Reply(Board("Ready")));

            var fromDefault = await Run(registry, handler, config);

            Assert.Equal(TriageStatus.Accepted, fromDefault.Triage!.Status);
            Assert.Equal(1, drafter.Calls);

            var (registry2, drafter2, _) = Models();
            var restrictive = TextModelRoutingConfig.Parse("{\"models\":{\"Drafter\":{\"tasks\":[\"docs\"]},\"Reviewer\":{\"tasks\":[\"issue-triage\"]}}}");
            var handler2 = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(true, Eligible)), ProjectBoardReaderTests.Reply(Board("Ready")));

            var stopped = await Run(registry2, handler2, restrictive);

            Assert.Equal(AgentRunStage.RoutingFailed, stopped.Stage);
            Assert.Contains(stopped.RoutingProblems, p => p.Contains("not allowed to serve"));
            Assert.Equal(0, drafter2.Calls);
        }

        [Fact]
        public async Task NoTokenEverAppearsInAReport_EvenWhenTheTransportFails()
        {
            var (registry, _, _) = Models();
            var ok = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(true, Eligible)), ProjectBoardReaderTests.Reply(Board("Ready")));
            var failing = new ProjectBoardReaderTests.StubHandler(_ => throw new HttpRequestException("refused " + IssueToken + " " + ProjectToken));
            var boardFails = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(true, Eligible)), _ => throw new HttpRequestException("refused " + ProjectToken));

            foreach (var handler in new[] { ok, failing, boardFails })
            {
                var report = await Run(registry, handler);
                var text = JsonSerializer.Serialize(report) + report.Summary();

                Assert.DoesNotContain(IssueToken, text);
                Assert.DoesNotContain(ProjectToken, text);
                foreach (var request in handler.Requests)
                {
                    Assert.DoesNotContain(IssueToken, request.Url);
                    Assert.DoesNotContain(ProjectToken, request.Url);
                    Assert.DoesNotContain(IssueToken, request.Body);
                    Assert.DoesNotContain(ProjectToken, request.Body);
                }
            }
        }

        [Fact]
        public async Task CancellationIsTheOnlyThingThrown()
        {
            var (registry, _, _) = Models();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var handler = new ProjectBoardReaderTests.StubHandler(ProjectBoardReaderTests.Reply(IssueJson(true, Eligible)));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(registry, handler, token: cts.Token));
        }

        [Fact]
        public async Task CancellingBetweenTheStages_StopsTheRunBeforeAnyModelIsAsked()
        {
            // The token is cancelled while the board is being read. The run must notice it itself, on every platform,
            // instead of relying on the HTTP stack (.NET Framework's HttpClient does not throw for a cancelled token).
            var (registry, drafter, reviewer) = Models();
            using var cts = new CancellationTokenSource();
            var handler = new ProjectBoardReaderTests.StubHandler(
                ProjectBoardReaderTests.Reply(IssueJson(true, Eligible)),
                _ =>
                {
                    cts.Cancel();
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Board("Ready")) };
                });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(registry, handler, token: cts.Token));

            Assert.Equal(0, drafter.Calls + reviewer.Calls);
        }

        [Fact]
        public async Task ARegistryIsRequired()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => AgentTriageRun.RunAsync("owner/repo", 7, Project, ProjectToken, null!));
        }
    }
}
