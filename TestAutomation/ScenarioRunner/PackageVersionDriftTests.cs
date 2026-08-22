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
    }
}
