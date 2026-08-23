using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ScenarioRunner
{
    public class DocumentationSiteIntegrityTests
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
        public void RootIndexMarkdown_ExistsWithValidFrontMatter()
        {
            var repoRoot = FindRepoRoot();
            var indexPath = Path.Combine(repoRoot, "index.md");
            Assert.True(File.Exists(indexPath), "Root index.md must exist for Jekyll GitHub Pages root rendering.");

            var content = File.ReadAllText(indexPath).TrimStart();
            Assert.StartsWith("---", content);
            Assert.Contains("layout: default", content);
        }

        [Fact]
        public void AllDocumentationFiles_HaveValidJekyllFrontMatter()
        {
            var repoRoot = FindRepoRoot();
            var docsDir = Path.Combine(repoRoot, "docs");
            var docFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.TopDirectoryOnly);

            Assert.NotEmpty(docFiles);

            foreach (var file in docFiles)
            {
                var relativePath = Path.GetFileName(file);
                var content = File.ReadAllText(file).TrimStart();

                Assert.True(content.StartsWith("---"), $"Doc file '{relativePath}' must start with YAML front matter ('---') so Jekyll compiles it to HTML.");
                Assert.True(content.Contains("layout: default"), $"Doc file '{relativePath}' must specify 'layout: default' in its YAML front matter.");
            }
        }

        [Fact]
        public void DocumentationFiles_InternalMarkdownLinks_TargetExistingFiles()
        {
            var repoRoot = FindRepoRoot();
            var docsDir = Path.Combine(repoRoot, "docs");
            var docFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.TopDirectoryOnly);

            var linkRegex = new Regex(@"\[(?<text>[^\]]+)\]\((?<target>[^)]+)\)");

            foreach (var file in docFiles)
            {
                var content = File.ReadAllText(file);
                var matches = linkRegex.Matches(content);

                foreach (Match match in matches)
                {
                    var target = match.Groups["target"].Value.Trim();

                    if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        target.StartsWith("#") ||
                        target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var hashIdx = target.IndexOf('#');
                    var cleanTarget = hashIdx >= 0 ? target.Substring(0, hashIdx) : target;

                    if (string.IsNullOrWhiteSpace(cleanTarget))
                    {
                        continue;
                    }

                    var targetPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, cleanTarget));
                    Assert.True(File.Exists(targetPath) || Directory.Exists(targetPath),
                        $"Broken link found in '{Path.GetFileName(file)}': target '{target}' does not exist on disk (resolved to '{targetPath}').");
                }
            }
        }
    }
}
