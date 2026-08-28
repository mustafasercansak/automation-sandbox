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

        [Fact]
        public void BilingualDocumentation_HasMatchingEnglishAndTurkishStructure()
        {
            var repoRoot = FindRepoRoot();
            var docsDir = Path.Combine(repoRoot, "docs");
            var docFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.TopDirectoryOnly);
            var bilingualFileCount = 0;

            foreach (var file in docFiles)
            {
                var content = File.ReadAllText(file);
                if (!TrySplitBilingualSections(content, out var englishSection, out var turkishSection))
                {
                    continue;
                }

                bilingualFileCount++;
                AssertMatchingStructure(Path.GetFileName(file), englishSection, turkishSection);
            }

            Assert.True(bilingualFileCount > 0, "Expected at least one bilingual documentation file.");
        }

        [Fact]
        public void BilingualDocumentationParity_RejectsStructuralMismatch()
        {
            const string content = "## English\n\n### Overview\n\n```csharp\nexample\n```\n\n## Türkçe\n\n### Genel Bakış\n";

            Assert.True(TrySplitBilingualSections(content, out var englishSection, out var turkishSection));
            Assert.ThrowsAny<Exception>(() => AssertMatchingStructure("mismatch.md", englishSection, turkishSection));
        }

        [Fact]
        public void BilingualDocumentationParity_IgnoresSingleLanguageDocumentation()
        {
            const string content = "## English\n\n### Overview\n";

            Assert.False(TrySplitBilingualSections(content, out _, out _));
        }

        private static bool TrySplitBilingualSections(string content, out string englishSection, out string turkishSection)
        {
            var englishMatch = Regex.Match(content, @"^## .*English.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            var turkishMatch = Regex.Match(content, @"^## .*Türkçe.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (!englishMatch.Success || !turkishMatch.Success || englishMatch.Index >= turkishMatch.Index)
            {
                englishSection = "";
                turkishSection = "";
                return false;
            }

            englishSection = content.Substring(englishMatch.Index + englishMatch.Length, turkishMatch.Index - englishMatch.Index - englishMatch.Length);
            var sharedContentRegex = new Regex(@"^---\s*$\r?\n^## ", RegexOptions.Multiline);
            var sharedContentMatch = sharedContentRegex.Match(content, turkishMatch.Index + turkishMatch.Length);
            var turkishSectionEnd = sharedContentMatch.Success ? sharedContentMatch.Index : content.Length;
            turkishSection = content.Substring(turkishMatch.Index + turkishMatch.Length, turkishSectionEnd - turkishMatch.Index - turkishMatch.Length);
            return true;
        }

        private static void AssertMatchingStructure(string fileName, string englishSection, string turkishSection)
        {
            var englishStructure = DescribeStructure(englishSection);
            var turkishStructure = DescribeStructure(turkishSection);
            Assert.Equal(
                englishStructure.HeadingLevels,
                turkishStructure.HeadingLevels);
            Assert.Equal(
                englishStructure.FencedCodeBlockCount,
                turkishStructure.FencedCodeBlockCount);
        }

        private static DocumentationStructure DescribeStructure(string section)
        {
            var headings = Regex.Matches(section, @"^(?<level>#{3,6})\s+.+$", RegexOptions.Multiline)
                .Cast<Match>()
                .Select(match => match.Groups["level"].Length)
                .ToArray();
            var fencedCodeBlockCount = Regex.Matches(section, @"^\s{0,3}```", RegexOptions.Multiline).Count / 2;
            return new DocumentationStructure(headings, fencedCodeBlockCount);
        }

        private sealed class DocumentationStructure
        {
            public DocumentationStructure(int[] headingLevels, int fencedCodeBlockCount)
            {
                HeadingLevels = headingLevels;
                FencedCodeBlockCount = fencedCodeBlockCount;
            }

            public int[] HeadingLevels { get; }
            public int FencedCodeBlockCount { get; }
        }
    }
}
