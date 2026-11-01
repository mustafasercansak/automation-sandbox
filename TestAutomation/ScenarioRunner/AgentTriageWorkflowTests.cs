using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ScenarioRunner
{
    // Guards .github/workflows/agent-triage.yml (#589). The workflow holds model keys and a project token and is meant to
    // be started only by a person, so these tests pin what must never change: the only trigger, the permissions, and that
    // nothing a person supplies is interpolated into a script.
    public class AgentTriageWorkflowTests
    {
        internal static string[] ReadRepoFile(params string[] parts)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AutomationSandbox.sln")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return File.ReadAllLines(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
        }

        private static string[] Lines() => ReadRepoFile(".github", "workflows", "agent-triage.yml");

        private static string Text() => string.Join("\n", Lines());

        // The run itself lives in a script both triage workflows call.
        private static string Script() => string.Join("\n", ReadRepoFile(".github", "scripts", "run-agent-triage.sh"));

        [Fact]
        public void TheOnlyTriggerIsAManualDispatch()
        {
            var lines = Lines();
            var onIndex = Array.FindIndex(lines, l => l.StartsWith("on:", StringComparison.Ordinal));
            Assert.True(onIndex >= 0);
            var triggers = new List<string>();
            for (var i = onIndex + 1; i < lines.Length; i++)
            {
                if (lines[i].Length > 0 && !lines[i].StartsWith(" ", StringComparison.Ordinal))
                {
                    break;
                }

                var match = Regex.Match(lines[i], @"^  ([a-z_]+):");
                if (match.Success)
                {
                    triggers.Add(match.Groups[1].Value);
                }
            }

            Assert.Equal(new[] { "workflow_dispatch" }, triggers);
            // "issues:" is left out on purpose: it is also the name of a permission. The parsed trigger list above already proves
            // the issues event is not a trigger.
            Assert.DoesNotMatch(@"(?m)^\s*(pull_request|pull_request_target|issue_comment|push|schedule|workflow_run|release|discussion):", Text());
        }

        [Fact]
        public void PermissionsAreMinimal_AndOnlyIssuesMayBeWritten()
        {
            var text = Text();

            Assert.Matches(@"(?m)^permissions:\s*\n  contents: read\n", text);
            var block = Regex.Match(text, @"(?ms)^permissions:\n(?<body>(?:  .*\n|\s*#.*\n)+)").Groups["body"].Value;
            var writes = Regex.Matches(block, @"^\s{2}([a-z-]+): write", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(new[] { "issues" }, writes);
            Assert.DoesNotContain("write-all", text);
        }

        [Fact]
        public void NothingThatAPersonSuppliesIsInterpolatedIntoAScript()
        {
            var lines = Lines();
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\s+run:\s*\|"))
                {
                    continue;
                }

                var indent = lines[i].Length - lines[i].TrimStart().Length;
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var current = lines[j];
                    if (current.Trim().Length > 0 && current.Length - current.TrimStart().Length <= indent)
                    {
                        break;
                    }

                    Assert.False(current.Contains("${{"), "A run script interpolates an expression: " + current.Trim());
                }
            }
        }

        [Fact]
        public void SecretsAreOnlyEverPassedThroughEnv()
        {
            foreach (var line in Lines().Where(l => l.Contains("secrets.", StringComparison.Ordinal) && !l.TrimStart().StartsWith("#", StringComparison.Ordinal)))
            {
                Assert.Matches(@"^\s+[A-Z_]+: \$\{\{ secrets\.[A-Z_]+ \}\}\s*$", line);
            }

            Assert.Contains("PROJECT_TOKEN: ${{ secrets.PROJECT_TOKEN }}", Text());
            Assert.Contains("GH_TOKEN: ${{ github.token }}", Text());
        }

        [Fact]
        public void TheModelInputsAreValidatedBeforeTheyReachTheAgent()
        {
            var text = Text();

            Assert.Contains("Validate the inputs", text);
            Assert.Contains("^[1-9][0-9]{0,8}$", text);
            Assert.Contains("^[A-Za-z0-9._:/@-]{0,100}$", text);
            Assert.True(text.IndexOf("Validate the inputs", StringComparison.Ordinal) < text.IndexOf("Run the triage agent", StringComparison.Ordinal));
        }

        [Fact]
        public void TheToolIsRunForOneFramework_AndARunWithoutAValidReportIsAFailureNotAResult()
        {
            var text = Script();

            // The runner image carries SDK 10 too, so the tool project multi-targets and "dotnet run" needs --framework. The
            // first real run of this workflow failed there and still looked like a finished run.
            Assert.Contains("--framework net8.0", text);
            Assert.Contains("json.load(open(\"report.json\"))", text);
            Assert.Contains("code=2", text);
            Assert.True(text.IndexOf("json.load", StringComparison.Ordinal) < text.IndexOf("GITHUB_STEP_SUMMARY:-", StringComparison.Ordinal));
        }

        [Fact]
        public void TheReportIsMadeSafeBeforeItGoesIntoTheJobSummary_AndTheJobFailsOnlyWhenTheAgentCouldNotRun()
        {
            var script = Script();
            var text = Text();

            Assert.Contains("sed \"s/\\`/'/g\"", script);
            Assert.Contains("if [ \"$code\" -eq 2 ]; then exit 2; fi", script);
            Assert.Contains("--comment", script);
            Assert.Contains("concurrency:", text);
            Assert.Contains("cancel-in-progress: false", text);
        }

        [Fact]
        public void TheManualWorkflowRunsTheSharedScript_AndThatScriptValidatesTheIssueNumberItself()
        {
            Assert.Contains("run: .github/scripts/run-agent-triage.sh", Text());
            Assert.Contains("^[1-9][0-9]{0,8}$", Script());
        }
    }

    // Guards .github/workflows/agent-triage-label.yml (#589). This workflow starts without a person pressing a button, so the
    // tests pin the three things that keep that safe: what can start it, what it may do, and that it is off by default.
    public class AgentTriageLabelWorkflowTests
    {
        private static string[] Lines() => AgentTriageWorkflowTests.ReadRepoFile(".github", "workflows", "agent-triage-label.yml");

        private static string Text() => string.Join("\n", Lines());

        [Fact]
        public void TheOnlyTriggerIsTheLabeledAction_OfTheIssuesEvent()
        {
            var text = Text();

            Assert.Matches(@"(?m)^on:\n  issues:\n    types: \[labeled\]\n\npermissions:", text);
            Assert.DoesNotMatch(@"(?m)^\s*(workflow_dispatch|pull_request|pull_request_target|issue_comment|push|schedule|workflow_run|release|discussion):", text);
        }

        [Fact]
        public void ItRunsOnlyWhenTheSwitchIsOn_AndTheAddedLabelIsAgentAllowed()
        {
            var text = Text();

            Assert.Contains("vars.AGENT_TRIAGE_ON_LABEL == 'true'", text);
            Assert.Contains("github.event.label.name == 'Agent::Allowed'", text);
            var condition = Lines().Single(l => l.TrimStart().StartsWith("if:", StringComparison.Ordinal));
            Assert.Contains("&&", condition);
        }

        [Fact]
        public void ItIsReadOnly_NoWritePermissionAndNoCommentRequested()
        {
            var text = Text();

            Assert.Matches(@"(?m)^permissions:\n  contents: read\n  issues: read\n", text);
            Assert.DoesNotMatch(@": write", text);
            Assert.DoesNotContain("write-all", text);
            Assert.DoesNotContain("POST_COMMENT", text);
            Assert.DoesNotContain("--comment", text);
        }

        [Fact]
        public void ConcurrencyIsPerIssue_SharedWithTheManualWorkflow_AndSetOnTheJobSoOtherLabelsCannotReplaceARun()
        {
            var lines = Lines();
            var jobsIndex = Array.FindIndex(lines, l => l.StartsWith("jobs:", StringComparison.Ordinal));
            var concurrencyIndex = Array.FindIndex(lines, l => l.TrimStart().StartsWith("concurrency:", StringComparison.Ordinal));

            Assert.True(concurrencyIndex > jobsIndex, "concurrency must be set on the job, after the condition that filters labels");
            Assert.Contains("group: agent-triage-${{ github.event.issue.number }}", Text());
            Assert.Contains("cancel-in-progress: false", Text());
        }

        [Fact]
        public void NothingTheEventSuppliesIsInterpolatedIntoAScript_AndSecretsAreOnlyPassedThroughEnv()
        {
            var lines = Lines();
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\s+run:"))
                {
                    continue;
                }

                Assert.DoesNotContain("${{", lines[i]);
            }

            foreach (var line in lines.Where(l => l.Contains("secrets.", StringComparison.Ordinal) && !l.TrimStart().StartsWith("#", StringComparison.Ordinal)))
            {
                Assert.Matches(@"^\s+[A-Z_]+: \$\{\{ secrets\.[A-Z_]+ \}\}\s*$", line);
            }

            Assert.Contains("run: .github/scripts/run-agent-triage.sh", Text());
            Assert.Contains("ISSUE_NUMBER: ${{ github.event.issue.number }}", Text());
        }
    }
}
