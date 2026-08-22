using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ScenarioRunner
{
    public class PackageVersionDriftTests
    {
        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")) &&
                    File.Exists(Path.Combine(current.FullName, "AutomationSandbox.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Could not find repository root directory.");
        }

        [Fact]
        public void DirectoryBuildProps_ContainsValidSemVerVersion()
        {
            var repoRoot = FindRepoRoot();
            var propsPath = Path.Combine(repoRoot, "Directory.Build.props");
            Assert.True(File.Exists(propsPath), "Directory.Build.props must exist at repository root.");

            var content = File.ReadAllText(propsPath);
            var match = Regex.Match(content, @"<Version>(?<v>[^<]+)</Version>");
            Assert.True(match.Success, "Directory.Build.props must define <Version>.");

            var version = match.Groups["v"].Value.Trim();
            Assert.False(string.IsNullOrWhiteSpace(version));
            Assert.Matches(@"^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$", version);
        }

        [Fact]
        public void SampleProject_PinsVersionMatchingDirectoryBuildProps()
        {
            var repoRoot = FindRepoRoot();
            var propsPath = Path.Combine(repoRoot, "Directory.Build.props");
            var sampleProjPath = Path.Combine(repoRoot, "samples", "HeuristicHealingQuickstart", "HeuristicHealingQuickstart.csproj");

            var propsContent = File.ReadAllText(propsPath);
            var expectedVersion = Regex.Match(propsContent, @"<Version>(?<v>[^<]+)</Version>").Groups["v"].Value.Trim();

            var sampleContent = File.ReadAllText(sampleProjPath);
            var sampleMatch = Regex.Match(sampleContent, @"<PackageReference\s+Include=""AutomationSandbox\.SelfHealing""\s+Version=""(?<v>[^""]+)""");
            Assert.True(sampleMatch.Success, "HeuristicHealingQuickstart.csproj must reference AutomationSandbox.SelfHealing.");

            var sampleVersion = sampleMatch.Groups["v"].Value.Trim();
            Assert.Equal(expectedVersion, sampleVersion);
        }

        [Fact]
        public void SampleVerifyScript_DynamicallyReadsDirectoryBuildProps()
        {
            var repoRoot = FindRepoRoot();
            var verifyScriptPath = Path.Combine(repoRoot, "samples", "HeuristicHealingQuickstart", "verify.ps1");
            Assert.True(File.Exists(verifyScriptPath));

            var scriptContent = File.ReadAllText(verifyScriptPath);
            Assert.Contains("Directory.Build.props", scriptContent);
            Assert.Contains("<Version>", scriptContent);
            Assert.DoesNotContain("0.2.0-beta.3", scriptContent);
        }

        [Fact]
        public void PackagingWorkflows_ResolveVersionFromDirectoryBuildPropsWhenUnset()
        {
            var repoRoot = FindRepoRoot();
            var packYmlPath = Path.Combine(repoRoot, ".github", "workflows", "pack.yml");
            var releaseYmlPath = Path.Combine(repoRoot, ".github", "workflows", "release.yml");

            Assert.True(File.Exists(packYmlPath));
            Assert.True(File.Exists(releaseYmlPath));

            var packContent = File.ReadAllText(packYmlPath);
            var releaseContent = File.ReadAllText(releaseYmlPath);

            Assert.Contains("Resolve Package Version", packContent);
            Assert.Contains("Directory.Build.props", packContent);

            Assert.Contains("Resolve Package Version", releaseContent);
            Assert.Contains("Directory.Build.props", releaseContent);
        }
    }
}
