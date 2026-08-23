using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace ScenarioRunner
{
    public class SecurityAuditWorkflowTests
    {
        [Fact]
        public void DirectoryBuildProps_EnablesNuGetAuditAndGatesHighCriticalOnlyInCi()
        {
            var props = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Build.props"));

            Assert.Contains("<NuGetAudit>true</NuGetAudit>", props, StringComparison.Ordinal);
            Assert.Contains("<NuGetAuditMode>all</NuGetAuditMode>", props, StringComparison.Ordinal);
            Assert.Contains("<NuGetAuditLevel>moderate</NuGetAuditLevel>", props, StringComparison.Ordinal);
            Assert.Contains("'$(ContinuousIntegrationBuild)' == 'true'", props, StringComparison.Ordinal);
            Assert.Contains("NU1903;NU1904", props, StringComparison.Ordinal);
        }

        [Fact]
        public void CiWorkflow_RunsSecurityAuditOnBothMatrixLegs()
        {
            var workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));

            Assert.Contains("audit-target: AutomationSandbox.sln", workflow, StringComparison.Ordinal);
            Assert.Contains("audit-target: TestAutomation/ScenarioRunner/ScenarioRunner.csproj", workflow, StringComparison.Ordinal);
            Assert.Contains("write-security-audit-summary.ps1", workflow, StringComparison.Ordinal);
            Assert.Contains("--vulnerable --include-transitive --format json", workflow, StringComparison.Ordinal);
            Assert.Contains("--outdated --format json", workflow, StringComparison.Ordinal);
        }

        [Fact]
        public void SecurityAuditScript_RendersVulnerableAndOutdatedTables()
        {
            var testDirectory = CreateTestDirectory();
            try
            {
                var vulnerablePath = Path.Combine(testDirectory, "vulnerable.json");
                File.WriteAllText(vulnerablePath, """
                {
                  "version": 1,
                  "parameters": "--vulnerable --include-transitive",
                  "sources": [ "https://api.nuget.org/v3/index.json" ],
                  "projects": [
                    {
                      "path": "/repo/TestAutomation/ScenarioRunner/ScenarioRunner.csproj",
                      "frameworks": [
                        {
                          "framework": "net8.0",
                          "transitivePackages": [
                            {
                              "id": "System.Net.Http",
                              "resolvedVersion": "4.3.0",
                              "vulnerabilities": [
                                { "severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-7jgj-8wvc-jh57" }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
                """);

                var outdatedPath = Path.Combine(testDirectory, "outdated.json");
                File.WriteAllText(outdatedPath, """
                {
                  "version": 1,
                  "parameters": "--outdated",
                  "sources": [ "https://api.nuget.org/v3/index.json" ],
                  "projects": [
                    {
                      "path": "/repo/TestAutomation/ScenarioRunner/ScenarioRunner.csproj",
                      "frameworks": [
                        {
                          "framework": "net8.0",
                          "topLevelPackages": [
                            { "id": "xunit", "requestedVersion": "2.9.3", "resolvedVersion": "2.9.3", "latestVersion": "2.9.4" }
                          ]
                        }
                      ]
                    }
                  ]
                }
                """);

                var summaryPath = Path.Combine(testDirectory, "summary.md");
                var result = RunSecurityAuditScript(vulnerablePath, outdatedPath, "Linux net8.0", summaryPath);

                Assert.True(
                    result.ExitCode == 0,
                    "Security audit script failed. stdout: " + result.StandardOutput + " stderr: " + result.StandardError);
                var summary = File.ReadAllText(summaryPath);
                Assert.Contains("## Package security audit — Linux net8.0", summary, StringComparison.Ordinal);
                Assert.Contains("`System.Net.Http`", summary, StringComparison.Ordinal);
                Assert.Contains("Transitive", summary, StringComparison.Ordinal);
                Assert.Contains("High", summary, StringComparison.Ordinal);
                Assert.Contains("GHSA-7jgj-8wvc-jh57", summary, StringComparison.Ordinal);
                Assert.Contains("`xunit`", summary, StringComparison.Ordinal);
                Assert.Contains("2.9.3", summary, StringComparison.Ordinal);
                Assert.Contains("2.9.4", summary, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public void SecurityAuditScript_NoFindingsWritesCleanSummary()
        {
            var testDirectory = CreateTestDirectory();
            try
            {
                var vulnerablePath = Path.Combine(testDirectory, "vulnerable.json");
                File.WriteAllText(vulnerablePath, """
                {
                  "version": 1,
                  "parameters": "--vulnerable --include-transitive",
                  "sources": [ "https://api.nuget.org/v3/index.json" ],
                  "projects": [
                    { "path": "/repo/TestAutomation/LlmHealing/LlmHealing.csproj" }
                  ]
                }
                """);

                var outdatedPath = Path.Combine(testDirectory, "outdated.json");
                File.WriteAllText(outdatedPath, """
                {
                  "version": 1,
                  "parameters": "--outdated",
                  "sources": [ "https://api.nuget.org/v3/index.json" ],
                  "projects": [
                    { "path": "/repo/TestAutomation/LlmHealing/LlmHealing.csproj" }
                  ]
                }
                """);

                var summaryPath = Path.Combine(testDirectory, "summary.md");
                var result = RunSecurityAuditScript(vulnerablePath, outdatedPath, "Empty Leg", summaryPath);

                Assert.True(
                    result.ExitCode == 0,
                    "Security audit script failed. stdout: " + result.StandardOutput + " stderr: " + result.StandardError);
                var summary = File.ReadAllText(summaryPath);
                Assert.Contains("No known vulnerable packages found.", summary, StringComparison.Ordinal);
                Assert.Contains("All packages are up to date.", summary, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public void SecurityAuditScript_MalformedInputWritesUnavailableNoteWithoutFailing()
        {
            var testDirectory = CreateTestDirectory();
            try
            {
                var vulnerablePath = Path.Combine(testDirectory, "vulnerable.json");
                File.WriteAllText(vulnerablePath, "{ this is not valid json");

                var outdatedPath = Path.Combine(testDirectory, "outdated-missing.json");

                var summaryPath = Path.Combine(testDirectory, "summary.md");
                var result = RunSecurityAuditScript(vulnerablePath, outdatedPath, "Malformed Leg", summaryPath);

                Assert.True(
                    result.ExitCode == 0,
                    "Security audit script should tolerate malformed/missing input. stdout: " + result.StandardOutput + " stderr: " + result.StandardError);
                var summary = File.ReadAllText(summaryPath);
                Assert.Contains("Vulnerability report unavailable", summary, StringComparison.Ordinal);
                Assert.Contains("Outdated-package report unavailable", summary, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        private static string CreateTestDirectory()
        {
            var testDirectory = Path.Combine(Path.GetTempPath(), "automation-sandbox-audit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            return testDirectory;
        }

        private static ProcessResult RunSecurityAuditScript(
            string vulnerableReportPath,
            string outdatedReportPath,
            string legLabel,
            string summaryPath)
        {
            var scriptPath = Path.Combine(RepositoryRoot(), ".github", "scripts", "write-security-audit-summary.ps1");
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = "-NoProfile -File \"" + scriptPath +
                    "\" -VulnerableReportPath \"" + vulnerableReportPath +
                    "\" -OutdatedReportPath \"" + outdatedReportPath +
                    "\" -LegLabel \"" + legLabel +
                    "\" -SummaryPath \"" + summaryPath + "\"",
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
