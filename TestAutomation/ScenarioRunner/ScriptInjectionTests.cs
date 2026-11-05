using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ScenarioRunner
{
    // A value written as ${{ ... }} inside a run: script is expanded by GitHub before the shell parses the script, so a value that holds a
    // quote or $(...) runs as shell. Everything a person, an event, a step output or a secret supplies has to reach a script through env:
    // and be used as a quoted variable. This guards every composite action (published for other repositories to call, so their callers'
    // values flow in) and every workflow (#636, #637).
    public class ScriptInjectionTests
    {
        // Expressions a caller, a dispatching user, an event, an earlier step or the secret store controls.
        private static readonly Regex Hostile = new Regex(@"\$\{\{\s*(inputs\.|github\.event\.|github\.head_ref|steps\.[A-Za-z0-9_-]+\.outputs\.|secrets\.)", RegexOptions.CultureInvariant);

        private static IEnumerable<string> ActionFiles()
        {
            var actions = Path.Combine(AgentTriageWorkflowTests.RepoRoot(), ".github", "actions");
            return Directory.GetDirectories(actions).Select(d => Path.Combine(d, "action.yml")).Where(File.Exists);
        }

        private static IEnumerable<string> WorkflowFiles() =>
            Directory.GetFiles(Path.Combine(AgentTriageWorkflowTests.RepoRoot(), ".github", "workflows"), "*.yml");

        // The lines inside every multi-line (or inline) run: value, including the run: line itself.
        private static IEnumerable<KeyValuePair<int, string>> ScriptLines(string path)
        {
            var lines = File.ReadAllLines(path);
            var inRun = false;
            var runIndent = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var run = Regex.Match(line, @"^(\s*)(?:- )?run:\s*(.*)$");
                if (run.Success)
                {
                    inRun = true;
                    runIndent = run.Groups[1].Length;
                    yield return new KeyValuePair<int, string>(i + 1, run.Groups[2].Value);
                    continue;
                }

                if (!inRun)
                {
                    continue;
                }

                var indent = line.Length - line.TrimStart().Length;
                if (line.Trim().Length > 0 && indent <= runIndent)
                {
                    inRun = false;
                    continue;
                }

                yield return new KeyValuePair<int, string>(i + 1, line);
            }
        }

        private static List<string> Offences(string path) =>
            ScriptLines(path).Where(l => Hostile.IsMatch(l.Value)).Select(l => Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path) + ":" + l.Key + ": " + l.Value.Trim()).ToList();

        [Fact]
        public void NoCompositeActionWritesAnInputOrEventValueIntoAScript()
        {
            var files = ActionFiles().ToList();
            Assert.True(files.Count >= 2, "Expected to find the composite actions under .github/actions (heal and site-audit).");

            var offences = files.SelectMany(Offences).ToList();

            Assert.True(offences.Count == 0, "Pass these through env: and use them as quoted variables:\n" + string.Join("\n", offences));
        }

        [Fact]
        public void NoWorkflowWritesAnExpressionThatSomeoneElseControlsIntoAScript()
        {
            var offences = WorkflowFiles().OrderBy(f => f, StringComparer.Ordinal).SelectMany(Offences).ToList();

            Assert.True(offences.Count == 0, "Pass these through env: and use them as quoted variables:\n" + string.Join("\n", offences));
        }

        [Fact]
        public void TheWorkflowsThatTurnAVersionIntoATagOrAnEnvironmentVariable_CheckItsShapeFirst()
        {
            // The version goes into GITHUB_ENV and a tag name: a line break in it would add environment variables.
            foreach (var file in new[] { "release.yml", "pack.yml", "nuget-consumer-verify.yml" })
            {
                var text = string.Join("\n", AgentTriageWorkflowTests.ReadRepoFile(".github", "workflows", file));

                Assert.Contains("VERSION_INPUT: ${{ inputs.version }}", text, StringComparison.Ordinal);
                Assert.Contains("-notmatch '^[0-9]+\\.[0-9]+\\.[0-9]+([-+][0-9A-Za-z.+-]+)?$'", text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void TheNuGetApiKey_ReachesThePushOnlyThroughTheEnvironment()
        {
            var lines = AgentTriageWorkflowTests.ReadRepoFile(".github", "workflows", "release.yml");

            var pushes = lines.Where(l => l.Contains("dotnet nuget push", StringComparison.Ordinal)).ToList();

            Assert.Equal(2, pushes.Count);
            Assert.All(pushes, p => Assert.Contains("--api-key $env:NUGET_API_KEY", p, StringComparison.Ordinal));
            Assert.Equal(2, lines.Count(l => l.Trim() == "NUGET_API_KEY: ${{ steps.nuget_login.outputs.NUGET_API_KEY }}"));
        }

        [Fact]
        public void TheScanFindsTheShapeItIsMeantToCatch()
        {
            var dir = Path.Combine(Path.GetTempPath(), "inject-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var file = Path.Combine(dir, "action.yml");
                File.WriteAllText(file, "runs:\n  steps:\n    - name: x\n      shell: bash\n      env:\n        SAFE: ${{ inputs.ok }}\n      run: |\n        echo \"$SAFE\"\n        echo \"${{ inputs.bad }}\"\n    - name: y\n      run: echo ${{ github.event.issue.title }}\n");

                var offences = Offences(file);

                Assert.Equal(2, offences.Count);
                Assert.Contains(offences, o => o.Contains("inputs.bad", StringComparison.Ordinal));
                Assert.Contains(offences, o => o.Contains("github.event.issue.title", StringComparison.Ordinal));
                Assert.DoesNotContain(offences, o => o.Contains("inputs.ok", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
