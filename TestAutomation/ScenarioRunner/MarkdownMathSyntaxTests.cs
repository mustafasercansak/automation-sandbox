using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ScenarioRunner
{
    // Guards LaTeX math syntax and MathJax layout configuration across the repository.
    //
    // Verifies that:
    // 1. The active Jekyll layout (_layouts/default.html) loads MathJax 3 with proper math
    //    delimiters, code-tag exclusions, and escaped-dollar support, while dead layout copies
    //    under docs/ are removed.
    // 2. Repository markdown files outside code blocks and inline code spans do not contain
    //    unpaired or unescaped currency dollar signs (e.g. literal "$0") that would break
    //    inline math parsing.
    public class MarkdownMathSyntaxTests
    {
        private static readonly Regex InlineCodeSpan = new Regex(@"`[^`\n]*`", RegexOptions.Compiled);
        private static readonly Regex ValidInlineMath = new Regex(@"\$(?!\s)[^\$\n]+?(?<!\s)\$", RegexOptions.Compiled);
        private static readonly Regex ValidDisplayMath = new Regex(@"\$\$[^\$]+?\$\$", RegexOptions.Compiled);

        [Fact]
        public void JekyllLayout_HasValidMathJaxConfiguration()
        {
            var root = FindRepositoryRoot();
            var layoutPath = Path.Combine(root, "_layouts", "default.html");
            Assert.True(File.Exists(layoutPath), "_layouts/default.html must exist at repository root.");

            var deadLayoutPath = Path.Combine(root, "docs", "_layouts", "default.html");
            Assert.False(File.Exists(deadLayoutPath), "docs/_layouts/default.html is dead code and must not exist.");

            var deadConfigPath = Path.Combine(root, "docs", "_config.yml");
            Assert.False(File.Exists(deadConfigPath), "docs/_config.yml is dead code and must not exist.");

            var content = File.ReadAllText(layoutPath);

            Assert.Contains("tex-mml-chtml.js", content, StringComparison.Ordinal);
            Assert.Contains("inlineMath", content, StringComparison.Ordinal);
            Assert.Contains("displayMath", content, StringComparison.Ordinal);
            Assert.Contains("processEscapes: true", content, StringComparison.Ordinal);
            Assert.Contains("'pre'", content, StringComparison.Ordinal);
            Assert.Contains("'code'", content, StringComparison.Ordinal);
            Assert.Contains("mermaid", content, StringComparison.Ordinal);
        }

        [Fact]
        public void MarkdownFiles_HaveNoUnpairedOrUnescapedCurrencyDollars()
        {
            var offenders = new List<string>();

            foreach (var file in EnumerateMarkdownFiles())
            {
                var insideFencedCode = false;
                var lineNumber = 0;

                foreach (var rawLine in File.ReadLines(file))
                {
                    lineNumber++;
                    var trimmed = rawLine.TrimStart();

                    if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                    {
                        insideFencedCode = !insideFencedCode;
                        continue;
                    }

                    if (insideFencedCode)
                    {
                        continue;
                    }

                    // 1. Strip inline code spans (`...`) so code examples like `"$125"` or `$env:VAR` are not evaluated.
                    var stripped = InlineCodeSpan.Replace(rawLine, " ");

                    // 2. Strip valid display math ($$...$$) and valid inline math ($...$).
                    stripped = ValidDisplayMath.Replace(stripped, " ");
                    stripped = ValidInlineMath.Replace(stripped, " ");

                    // 3. Find unescaped dollar signs ($ not preceded by \).
                    for (var i = 0; i < stripped.Length; i++)
                    {
                        if (stripped[i] == '$')
                        {
                            var isEscaped = i > 0 && stripped[i - 1] == '\\';
                            if (!isEscaped)
                            {
                                offenders.Add($"{Path.GetFileName(file)}:{lineNumber} -> \"{rawLine.Trim()}\" (unescaped '$' at index {i})");
                            }
                        }
                    }
                }
            }

            Assert.True(
                offenders.Count == 0,
                "Markdown files must not contain unescaped or unpaired '$' in prose. " +
                "Use '\\$' for literal currency dollars ($0, $125), or enclose math in paired '$...$'. Offending lines:" +
                Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void MarkdownFilesWithMath_AreDiscovered()
        {
            var withMath = EnumerateMarkdownFiles()
                .Where(f =>
                {
                    var text = File.ReadAllText(f);
                    return text.Contains("$$") || text.Contains("$");
                })
                .ToList();

            Assert.NotEmpty(withMath);
            Assert.Contains(withMath, f => Path.GetFileName(f) == "README.md");
            Assert.Contains(withMath, f => Path.GetFileName(f) == "llm-providers.md");
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

            if (directory == null)
            {
                throw new InvalidOperationException("Could not locate repository root containing AutomationSandbox.sln.");
            }

            return directory.FullName;
        }
    }
}
