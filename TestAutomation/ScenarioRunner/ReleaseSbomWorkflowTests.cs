using System;
using System.IO;
using Xunit;

namespace ScenarioRunner
{
    public class ReleaseSbomWorkflowTests
    {
        [Fact]
        public void ReleaseWorkflow_GeneratesPinnedCycloneDxSbomForEveryPublishedPackage()
        {
            var workflow = ReadReleaseWorkflow();

            // The tool version is pinned for reproducible releases; CycloneDX 6.x runs on
            // the .NET 10 runtime the setup-dotnet step installs, and its CLI spells JSON
            // output as --output-format json (the old -j/--json flag was removed in 6.0).
            Assert.Contains("dotnet tool install --global CycloneDX --version ", workflow, StringComparison.Ordinal);
            Assert.Contains("--output-format json", workflow, StringComparison.Ordinal);

            string[] projects =
            {
                "UiModel",
                "SelfHealing",
                "LlmHealing",
                "Discovery",
                "WebDiscovery",
                "IntentAutomation",
                "PlaywrightLiveExploration",
            };
            foreach (var project in projects)
            {
                Assert.Contains("'" + project + "'", workflow, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void ReleaseWorkflow_UploadsSbomsAndAttachesThemToTheGitHubRelease()
        {
            var workflow = ReadReleaseWorkflow();

            Assert.Contains("name: release-sboms", workflow, StringComparison.Ordinal);
            Assert.Contains("path: ./sbom/*.json", workflow, StringComparison.Ordinal);
            // SBOMs are release assets next to the nupkg/snupkg files, so a consumer can
            // audit the dependency graph without downloading the packages themselves.
            Assert.Contains("\"./sbom/*.json\",", workflow, StringComparison.Ordinal);
        }

        private static string ReadReleaseWorkflow()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AutomationSandbox.sln")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return File.ReadAllText(Path.Combine(directory!.FullName, ".github", "workflows", "release.yml"));
        }
    }
}
