using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ScenarioRunner
{
    // Guards the manual workflows that hold model keys and spend quota: doc-benchmark.yml (#560) and lesson-replay.yml (#561).
    // Like agent-triage.yml, each must be started only by a person and must pass nothing a person supplies into a script text.
    public class ManualModelWorkflowTests
    {
        public static IEnumerable<object[]> Workflows() => new[] { new object[] { "doc-benchmark.yml" }, new object[] { "lesson-replay.yml" } };

        private static string[] Lines(string file) => AgentTriageWorkflowTests.ReadRepoFile(".github", "workflows", file);

        private static string Text(string file) => string.Join("\n", Lines(file));

        [Theory]
        [MemberData(nameof(Workflows))]
        public void TheOnlyTriggerIsAManualDispatch(string file)
        {
            var lines = Lines(file);
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
            Assert.DoesNotMatch(@"(?m)^\s*(pull_request|pull_request_target|issue_comment|push|schedule|workflow_run|release|discussion):", Text(file));
        }

        [Theory]
        [MemberData(nameof(Workflows))]
        public void NoPermissionIsWrite(string file)
        {
            var text = Text(file);

            Assert.Matches(@"(?m)^permissions:\n  contents: read\n", text);
            Assert.DoesNotMatch(@": write", text);
            Assert.DoesNotContain("write-all", text);
        }

        [Theory]
        [MemberData(nameof(Workflows))]
        public void NoExpressionIsInterpolatedIntoAScript_AndSecretsOnlyReachTheShellThroughEnv(string file)
        {
            var lines = Lines(file);
            var inRun = false;
            var runIndent = 0;
            foreach (var line in lines)
            {
                var run = Regex.Match(line, @"^(\s*)run:\s*(\|)?");
                if (run.Success)
                {
                    inRun = true;
                    runIndent = run.Groups[1].Length;
                    Assert.DoesNotContain("${{", line);
                    continue;
                }

                if (inRun)
                {
                    var indent = line.Length - line.TrimStart().Length;
                    if (line.Trim().Length > 0 && indent <= runIndent)
                    {
                        inRun = false;
                    }
                    else
                    {
                        Assert.DoesNotContain("${{", line);
                    }
                }
            }

            foreach (var secret in Regex.Matches(Text(file), @"\$\{\{\s*secrets\.[A-Z_]+\s*\}\}").Cast<Match>())
            {
                var line = lines.First(l => l.Contains(secret.Value));
                Assert.Matches(@"^\s+[A-Z_]+: \$\{\{ secrets\.", line);
            }
        }

        [Theory]
        [MemberData(nameof(Workflows))]
        public void TheReportsAreUploadedEvenWhenTheRunFails_AndBackticksAreRemovedFromTheSummary(string file)
        {
            var text = Text(file);

            Assert.Contains("uses: actions/upload-artifact@v7", text);
            Assert.Matches(@"(?s)Upload the reports\n\s+if: always\(\)", text);
            Assert.Contains("sed \"s/\\`/'/g\"", text);
        }

        [Theory]
        [MemberData(nameof(Workflows))]
        public void ModelNamesFromInputsAreValidatedBeforeUse(string file)
        {
            Assert.Contains("[A-Za-z0-9._:/@-]{0,100}", Text(file));
        }

        [Fact]
        public void TheDocBenchmark_RunsOnlyTheLiveBenchmarkTest_WithTheOptInSwitchAndAnOutputDirectory_AndNeedsNoMoreThanContents()
        {
            var text = Text("doc-benchmark.yml");

            Assert.Contains("FullyQualifiedName~DocBenchmarkTests.RunDocBenchmark_LiveAgainstConfiguredModels", text);
            Assert.Contains("DOC_BENCHMARK: '1'", text);
            Assert.Contains("DOC_BENCHMARK_OUT:", text);
            Assert.Matches(@"(?m)^permissions:\n  contents: read\n\nconcurrency:", text);
        }

        [Fact]
        public void TheLessonReplay_BuildsAReadOnlyIndexBeforeTheLatestCaseDate_ThenRunsOnlyTheLiveReplayTest()
        {
            var text = Text("lesson-replay.yml");

            Assert.Contains("lessons build --repo \"$GITHUB_REPOSITORY\"", text);
            Assert.Contains("--before \"$asof\"", text);
            Assert.Contains("LESSON_REPLAY: '1'", text);
            Assert.Contains("LESSON_REPLAY_INDEX:", text);
            Assert.Contains("LESSON_REPLAY_OUT:", text);
            Assert.Contains("FullyQualifiedName~LessonReplayTests.RunLessonReplay_LiveAgainstConfiguredModels", text);
            Assert.Matches(@"(?m)^permissions:\n  contents: read\n  issues: read\n  pull-requests: read\n\nconcurrency:", text);
            Assert.DoesNotContain("--comment", text);
            Assert.DoesNotContain("--label-needs-human", text);
        }
    }
}
