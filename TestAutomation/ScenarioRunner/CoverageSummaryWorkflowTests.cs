using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace ScenarioRunner
{
    public class CoverageSummaryWorkflowTests
    {
        [Fact]
        public void CiWorkflow_LabelsCoveragePerMatrixLegAndKeepsArtifacts()
        {
            var workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));

            Assert.Contains("coverage-label: Windows net48", workflow, StringComparison.Ordinal);
            Assert.Contains("coverage-label: Linux net8.0", workflow, StringComparison.Ordinal);
            Assert.Contains("Discovery and FlaUI live UIA paths are absent by design", workflow, StringComparison.Ordinal);
            Assert.Contains("write-coverage-summary.ps1", workflow, StringComparison.Ordinal);
            Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
            Assert.Contains("Upload code coverage", workflow, StringComparison.Ordinal);
            Assert.Contains("coverage.cobertura.xml", workflow, StringComparison.Ordinal);
        }

        [Fact]
        public void ScenarioRunner_ConfiguresCoverletCollectorForNet48AndModernFrameworks()
        {
            var projPath = Path.Combine(RepositoryRoot(), "TestAutomation", "ScenarioRunner", "ScenarioRunner.csproj");
            var content = File.ReadAllText(projPath);

            Assert.Contains("coverlet.collector", content, StringComparison.Ordinal);
            // Verify that net48 uses 6.0.4 (the latest release supporting .NET Framework / netstandard2.0 MSBuild targets)
            Assert.Contains("Condition=\"'$(TargetFramework)' == 'net48'\"", content, StringComparison.Ordinal);
            Assert.Contains("Version=\"6.0.4\"", content, StringComparison.Ordinal);
            // Verify modern runtimes use latest version
            Assert.Contains("Condition=\"'$(TargetFramework)' != 'net48'\"", content, StringComparison.Ordinal);
            Assert.Contains("Version=\"10.0.1\"", content, StringComparison.Ordinal);
        }

        [Fact]
        public void CoverageSummaryScript_RendersOverallAndPerAssemblyRowsWithoutCombiningLegs()
        {
            var testDirectory = Path.Combine(Path.GetTempPath(), "automation-sandbox-coverage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                var reportDirectory = Path.Combine(testDirectory, "report-one");
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(
                    Path.Combine(reportDirectory, "coverage.cobertura.xml"),
                    "<coverage line-rate=\"0.75\" branch-rate=\"0.5\" lines-covered=\"75\" lines-valid=\"100\" branches-covered=\"10\" branches-valid=\"20\">" +
                    "<packages>" +
                    "<package name=\"UiModel\" line-rate=\"0.8\" branch-rate=\"0.4\" />" +
                    "<package name=\"Discovery\" line-rate=\"0.6\" branch-rate=\"0.25\" />" +
                    "</packages></coverage>");
                var summaryPath = Path.Combine(testDirectory, "summary.md");
                var scriptPath = Path.Combine(RepositoryRoot(), ".github", "scripts", "write-coverage-summary.ps1");

                var result = RunPowerShell(
                    scriptPath,
                    reportDirectory,
                    "Windows net48",
                    summaryPath,
                    "Includes the net48-only Discovery assembly.");

                Assert.True(
                    result.ExitCode == 0,
                    "Coverage summary script failed. stdout: " + result.StandardOutput + " stderr: " + result.StandardError);
                var summary = File.ReadAllText(summaryPath);
                Assert.Contains("## Code coverage — Windows net48", summary, StringComparison.Ordinal);
                Assert.Contains("**Overall** | 75.0% (75/100) | 50.0% (10/20)", summary, StringComparison.Ordinal);
                Assert.Contains("`Discovery` | 60.0% | 25.0%", summary, StringComparison.Ordinal);
                Assert.Contains("`UiModel` | 80.0% | 40.0%", summary, StringComparison.Ordinal);
                Assert.Contains("figures are not combined", summary, StringComparison.Ordinal);
                Assert.Contains("no coverage threshold is enforced", summary, StringComparison.Ordinal);
                Assert.Contains("Platform note: Includes the net48-only Discovery assembly.", summary, StringComparison.Ordinal);
                Assert.DoesNotContain("report-one", summary, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public void CoverageSummaryScript_MissingReportWritesUnavailableNoteWithoutFailing()
        {
            var testDirectory = Path.Combine(Path.GetTempPath(), "automation-sandbox-coverage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                var summaryPath = Path.Combine(testDirectory, "summary.md");
                var scriptPath = Path.Combine(RepositoryRoot(), ".github", "scripts", "write-coverage-summary.ps1");

                var result = RunPowerShell(
                    scriptPath,
                    Path.Combine(testDirectory, "missing-results"),
                    "Linux net8.0",
                    summaryPath,
                    "Discovery is absent by design.");

                Assert.True(
                    result.ExitCode == 0,
                    "Coverage summary script failed. stdout: " + result.StandardOutput + " stderr: " + result.StandardError);
                var summary = File.ReadAllText(summaryPath);
                Assert.Contains("## Code coverage — Linux net8.0", summary, StringComparison.Ordinal);
                Assert.Contains("Coverage report unavailable", summary, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public void CoverageSummaryScript_DeduplicatesIdenticalAttachmentCopiesAndRendersSingleTable()
        {
            var testDirectory = Path.Combine(Path.GetTempPath(), "automation-sandbox-coverage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                var guidDirectory = Path.Combine(testDirectory, "bcbb30a1-a327-48ca-9615-48fa9ae66828");
                Directory.CreateDirectory(guidDirectory);
                var xmlContent = "<coverage line-rate=\"0.876\" branch-rate=\"0.798\" lines-covered=\"4708\" lines-valid=\"5376\" branches-covered=\"1783\" branches-valid=\"2233\">" +
                    "<packages>" +
                    "<package name=\"Discovery\" line-rate=\"0.682\" branch-rate=\"0.689\" />" +
                    "<package name=\"UiModel\" line-rate=\"0.915\" branch-rate=\"0.786\" />" +
                    "</packages></coverage>";

                File.WriteAllText(Path.Combine(guidDirectory, "coverage.cobertura.xml"), xmlContent);

                // VSTest attachment directory copy simulation
                var runnerDirectory = Path.Combine(testDirectory, "runnervm6lq3x", "In", "deployment-dir");
                Directory.CreateDirectory(runnerDirectory);
                File.WriteAllText(Path.Combine(runnerDirectory, "coverage.cobertura.xml"), xmlContent);

                var summaryPath = Path.Combine(testDirectory, "summary.md");
                var scriptPath = Path.Combine(RepositoryRoot(), ".github", "scripts", "write-coverage-summary.ps1");

                var result = RunPowerShell(
                    scriptPath,
                    testDirectory,
                    "Windows net48",
                    summaryPath,
                    "Includes the net48-only Discovery assembly.");

                Assert.True(
                    result.ExitCode == 0,
                    "Coverage summary script failed. stdout: " + result.StandardOutput + " stderr: " + result.StandardError);
                var summary = File.ReadAllText(summaryPath);
                Assert.Contains("## Code coverage — Windows net48", summary, StringComparison.Ordinal);
                Assert.Contains("**Overall** | 87.6% (4708/5376) | 79.8% (1783/2233)", summary, StringComparison.Ordinal);
                Assert.Contains("`Discovery` | 68.2% | 68.9%", summary, StringComparison.Ordinal);
                Assert.Contains("`UiModel` | 91.5% | 78.6%", summary, StringComparison.Ordinal);
                Assert.DoesNotContain("| Report |", summary, StringComparison.Ordinal);
                Assert.DoesNotContain("bcbb30a1", summary, StringComparison.Ordinal);
                Assert.DoesNotContain("runnervm6lq3x", summary, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        private static ProcessResult RunPowerShell(
            string scriptPath,
            string coveragePath,
            string legLabel,
            string summaryPath,
            string platformNote)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = "-NoProfile -File \"" + scriptPath +
                    "\" -CoverageSearchPath \"" + coveragePath +
                    "\" -LegLabel \"" + legLabel +
                    "\" -SummaryPath \"" + summaryPath +
                    "\" -PlatformNote \"" + platformNote + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start pwsh.");
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, standardOutput, standardError);
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AutomationSandbox.sln")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return directory!.FullName;
        }

        private sealed class ProcessResult
        {
            public ProcessResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }

            public int ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }
        }
    }
}
