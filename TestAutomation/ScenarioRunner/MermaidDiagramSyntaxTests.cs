using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ScenarioRunner
{
    // Guards the mermaid diagrams embedded in the repository's markdown.
    //
    // #90 corrected the consensus stage in four diagrams and, in doing so, wrote edge labels like
    // -->|Yes (Agreed)|. Parentheses are legal inside a quoted node label but not inside an unquoted
    // edge label, so GitHub stopped rendering the README entirely — a worse outcome than the wrong
    // diagram it replaced. Reviewing the content did not catch it, because the content was correct.
    public class MermaidDiagramSyntaxTests
    {
        // -->|label| and the -- label --> form.
        private static readonly Regex PipeLabel = new Regex(@"--+>\s*\|([^|]*)\|", RegexOptions.Compiled);
        private static readonly Regex DashLabel = new Regex(@"--\s+([^-\n]*?)\s+--+>", RegexOptions.Compiled);

        // Characters mermaid's grammar treats as structure rather than text.
        private const string ReservedCharacters = "()<>";

        [Fact]
        public void MermaidEdgeLabels_WithReservedCharacters_AreQuoted()
        {
            var offenders = new List<string>();

            foreach (var file in EnumerateMarkdownFiles())
            {
                var insideDiagram = false;
                var lineNumber = 0;

                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;

                    if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                    {
                        insideDiagram = line.TrimStart().StartsWith("```mermaid", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    if (!insideDiagram)
                    {
                        continue;
                    }

                    foreach (Match match in PipeLabel.Matches(line).Cast<Match>().Concat(DashLabel.Matches(line).Cast<Match>()))
                    {
                        var label = match.Groups[1].Value.Trim();
                        if (label.StartsWith("\"", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (label.IndexOfAny(ReservedCharacters.ToCharArray()) >= 0)
                        {
                            offenders.Add($"{Path.GetFileName(file)}:{lineNumber} -> {label}");
                        }
                    }
                }
            }

            Assert.True(
                offenders.Count == 0,
                "Mermaid edge labels containing any of " + ReservedCharacters + " must be quoted, " +
                "otherwise the diagram fails to render. Offending labels:" + Environment.NewLine +
                string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void MarkdownFilesWithDiagrams_AreDiscovered()
        {
            // Without this, a path regression would turn the guard above into a vacuous pass.
            var withDiagrams = EnumerateMarkdownFiles()
                .Where(f => File.ReadAllText(f).IndexOf("```mermaid", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            Assert.NotEmpty(withDiagrams);
            Assert.Contains(withDiagrams, f => Path.GetFileName(f) == "README.md");
        }

        private static IEnumerable<string> EnumerateMarkdownFiles()
        {
            var root = FindRepositoryRoot();
            return Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly)
                .Concat(Directory.Exists(Path.Combine(root, "docs"))
                    ? Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories)
                    : Enumerable.Empty<string>())
                .OrderBy(f => f, StringComparer.Ordinal);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AutomationSandbox.sln")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return directory!.FullName;
        }
    }
}
