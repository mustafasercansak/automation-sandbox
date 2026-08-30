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
        public void SampleProject_PinsAPublishedPrereleaseOnTheSameMinorLine()
        {
            // Relaxed for #336: the sample pin no longer has to equal the (possibly
            // unreleased) Directory.Build.props <Version> exactly. It must still be a
            // valid semver prerelease on the same major.minor line, so a stale pin or a
            // wrong package is still caught. release.yml bumps it to the exact version
            // after that version is confirmed live on nuget.org.
            var repoRoot = FindRepoRoot();
            var propsPath = Path.Combine(repoRoot, "Directory.Build.props");
            var sampleProjPath = Path.Combine(repoRoot, "samples", "HeuristicHealingQuickstart", "HeuristicHealingQuickstart.csproj");

            var propsContent = File.ReadAllText(propsPath);
            var propsVersion = Regex.Match(propsContent, @"<Version>(?<v>[^<]+)</Version>").Groups["v"].Value.Trim();
            var minorLine = Regex.Match(propsVersion, @"^(?<mm>\d+\.\d+)\.").Groups["mm"].Value;
            Assert.False(string.IsNullOrEmpty(minorLine), "Directory.Build.props <Version> must be major.minor.patch.");

            var sampleContent = File.ReadAllText(sampleProjPath);
            var sampleMatch = Regex.Match(sampleContent, @"<PackageReference\s+Include=""AutomationSandbox\.SelfHealing""\s+Version=""(?<v>[^""]+)""");
            Assert.True(sampleMatch.Success, "HeuristicHealingQuickstart.csproj must reference AutomationSandbox.SelfHealing as a PackageReference.");

            var sampleVersion = sampleMatch.Groups["v"].Value.Trim();
            Assert.Matches(@"^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$", sampleVersion);
            Assert.StartsWith(minorLine + ".", sampleVersion);
        }

        [Fact]
        public void PerPrSampleCheck_BuildsAgainstRepoSource_NotNuGet()
        {
            // #336: the per-PR check must not depend on a version that only exists on
            // nuget.org after release.yml runs. It swaps the PackageReference for a
            // ProjectReference and builds against current source.
            var repoRoot = FindRepoRoot();
            var verifyScriptPath = Path.Combine(repoRoot, "samples", "HeuristicHealingQuickstart", "verify.ps1");
            Assert.True(File.Exists(verifyScriptPath));

            var script = File.ReadAllText(verifyScriptPath);
            Assert.Contains("ProjectReference", script);
            Assert.Contains("SelfHealing.csproj", script);
            Assert.DoesNotContain("api.nuget.org", script);
            Assert.DoesNotContain("0.2.0-beta.3", script);

            var ciPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");
            var ci = File.ReadAllText(ciPath);
            Assert.Contains("verify.ps1", ci);
            Assert.DoesNotContain("verify-published.ps1", ci);
        }

        [Fact]
        public void PublishedConsumerCheck_RestoresExactVersionFromNuGet_AndRunsInReleaseYml()
        {
            // #336: the "the published package works for a consumer" check lives in
            // release.yml as a post-publish step, taking the just-published version.
            var repoRoot = FindRepoRoot();
            var scriptPath = Path.Combine(repoRoot, "samples", "HeuristicHealingQuickstart", "verify-published.ps1");
            Assert.True(File.Exists(scriptPath), "verify-published.ps1 must exist for the release-time consumer check.");

            var script = File.ReadAllText(scriptPath);
            Assert.Contains("param(", script);
            Assert.Contains("$Version", script);
            Assert.Contains("api.nuget.org", script);
            Assert.Contains("--no-cache", script);

            var releasePath = Path.Combine(repoRoot, ".github", "workflows", "release.yml");
            var release = File.ReadAllText(releasePath);
            Assert.Contains("verify-published.ps1 -Version", release);
            Assert.Contains("bump SSoT", release, StringComparison.OrdinalIgnoreCase);

            var ciPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");
            var ci = File.ReadAllText(ciPath);
            Assert.DoesNotContain("nuget-consumer", ci);
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

        [Fact]
        public void ReleaseNotes_ExistForCurrentVersionAndAreNonEmpty()
        {
            var repoRoot = FindRepoRoot();
            var propsPath = Path.Combine(repoRoot, "Directory.Build.props");
            var propsContent = File.ReadAllText(propsPath);
            var version = Regex.Match(propsContent, @"<Version>(?<v>[^<]+)</Version>").Groups["v"].Value.Trim();

            var tagNotesPath = Path.Combine(repoRoot, "docs", "release-notes", $"v{version}.md");
            var versionNotesPath = Path.Combine(repoRoot, "docs", "release-notes", $"{version}.md");

            var exists = File.Exists(tagNotesPath) || File.Exists(versionNotesPath);
            Assert.True(exists, $"Release notes file must exist at {tagNotesPath} or {versionNotesPath}.");

            var activePath = File.Exists(tagNotesPath) ? tagNotesPath : versionNotesPath;
            var text = File.ReadAllText(activePath);
            Assert.False(string.IsNullOrWhiteSpace(text), $"Release notes file at {activePath} must not be empty.");
            Assert.True(text.Length > 50, "Release notes file must contain meaningful content.");
        }

        [Fact]
        public void ReleaseWorkflow_ReferencesExternalReleaseNotes()
        {
            var repoRoot = FindRepoRoot();
            var releaseYmlPath = Path.Combine(repoRoot, ".github", "workflows", "release.yml");
            Assert.True(File.Exists(releaseYmlPath));

            var content = File.ReadAllText(releaseYmlPath);
            Assert.Contains("Validate and write release notes", content);
            Assert.Contains("docs/release-notes/", content);
        }

        [Fact]
        public void NoDocumentationFiles_ContainHardcodedPackageVersion()
        {
            var repoRoot = FindRepoRoot();
            var propsPath = Path.Combine(repoRoot, "Directory.Build.props");
            var propsContent = File.ReadAllText(propsPath);
            var currentVersion = Regex.Match(propsContent, @"<Version>(?<v>[^<]+)</Version>").Groups["v"].Value.Trim();

            var docsDir = Path.Combine(repoRoot, "docs");
            var docFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories);

            foreach (var docFile in docFiles)
            {
                var relativePath = docFile.Substring(repoRoot.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                if (relativePath.StartsWith("docs" + Path.DirectorySeparatorChar + "release-notes", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.StartsWith("docs/release-notes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = File.ReadAllText(docFile);
                Assert.False(text.Contains(currentVersion), $"Documentation file '{relativePath}' contains hardcoded package version '{currentVersion}'. Use dynamic commands (--prerelease) or SSoT references instead.");
            }

            var readmePath = Path.Combine(repoRoot, "README.md");
            if (File.Exists(readmePath))
            {
                var readmeText = File.ReadAllText(readmePath);
                Assert.False(readmeText.Contains(currentVersion), $"README.md contains hardcoded package version '{currentVersion}'. Use dynamic live badges and SSoT references instead.");
            }

            var indexPath = Path.Combine(repoRoot, "index.md");
            if (File.Exists(indexPath))
            {
                var indexText = File.ReadAllText(indexPath);
                Assert.False(indexText.Contains(currentVersion), $"index.md contains hardcoded package version '{currentVersion}'. Use dynamic live badges and SSoT references instead.");
            }
        }
    }
}
