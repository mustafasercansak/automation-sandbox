using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ScenarioRunner
{
    // Guards .github/workflows/doc-benchmark.yml (#560). It holds model keys and spends quota, so, like agent-triage.yml, it must
    // be started only by a person and must pass nothing a person supplies into a script text.
    public class DocBenchmarkWorkflowTests
    {
        private static string[] Lines() => AgentTriageWorkflowTests.ReadRepoFile(".github", "workflows", "doc-benchmark.yml");

        private static string Text() => string.Join("\n", Lines());

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
            Assert.DoesNotMatch(@"(?m)^\s*(pull_request|pull_request_target|issues|issue_comment|push|schedule|workflow_run|release|discussion):", Text());
        }

        [Fact]
        public void ItHasReadOnlyContentsAccessAndNothingElse()
        {
            var text = Text();

            Assert.Matches(@"(?m)^permissions:\n  contents: read\n\nconcurrency:", text);
            Assert.DoesNotMatch(@": write", text);
            Assert.DoesNotContain("write-all", text);
        }

        [Fact]
        public void NoExpressionIsInterpolatedIntoAScript_AndSecretsOnlyReachTheShellThroughEnv()
        {
            var lines = Lines();
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

            foreach (var secret in Regex.Matches(Text(), @"\$\{\{\s*secrets\.[A-Z_]+\s*\}\}").Cast<Match>())
            {
                var line = lines.First(l => l.Contains(secret.Value));
                Assert.Matches(@"^\s+[A-Z_]+: \$\{\{ secrets\.", line);
            }
        }

        [Fact]
        public void ItRunsOnlyTheLiveBenchmarkTest_WithTheOptInSwitchAndAnOutputDirectory()
        {
            var text = Text();

            Assert.Contains("FullyQualifiedName~DocBenchmarkTests.RunDocBenchmark_LiveAgainstConfiguredModels", text);
            Assert.Contains("DOC_BENCHMARK: '1'", text);
            Assert.Contains("DOC_BENCHMARK_OUT:", text);
        }

        [Fact]
        public void TheReportsAreUploadedEvenWhenTheRunFails_AndBackticksAreRemovedFromTheSummary()
        {
            var text = Text();

            Assert.Contains("uses: actions/upload-artifact@v7", text);
            Assert.Matches(@"(?s)Upload the reports\n\s+if: always\(\)", text);
            Assert.Contains("sed \"s/\\`/'/g\"", text);
        }

        [Fact]
        public void ModelNamesFromInputsAreValidatedBeforeUse()
        {
            Assert.Contains("[A-Za-z0-9._:/@-]{0,100}", Text());
        }
    }
}
