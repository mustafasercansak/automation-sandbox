using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ScenarioRunner
{
    public class WorkflowActionVersionTests
    {
        private static readonly HashSet<string> AllowedOfficialActionVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "actions/checkout@v7",
            "actions/setup-dotnet@v6",
            "actions/setup-python@v5",
            "actions/upload-artifact@v7",
            "actions/download-artifact@v8",
            "actions/configure-pages@v6",
            "actions/upload-pages-artifact@v5",
            "actions/deploy-pages@v5",
            "ruby/setup-ruby@v1",
            "NuGet/login@v1"
        };

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
        public void AllWorkflows_UseOnlyAllowedOfficialActionVersions()
        {
            var repoRoot = FindRepoRoot();
            var workflowsDir = Path.Combine(repoRoot, ".github", "workflows");
            Assert.True(Directory.Exists(workflowsDir), "Workflows directory must exist.");

            var workflowFiles = Directory.GetFiles(workflowsDir, "*.yml");
            Assert.NotEmpty(workflowFiles);

            var invalidUsages = new List<string>();

            foreach (var file in workflowFiles)
            {
                var relativePath = Path.GetFileName(file);
                var lines = File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    var match = Regex.Match(line, @"^uses:\s*(?<action>[^\s#]+)");
                    if (match.Success)
                    {
                        var action = match.Groups["action"].Value.Trim();
                        if (!AllowedOfficialActionVersions.Contains(action))
                        {
                            invalidUsages.Add($"{relativePath}:{i + 1} uses '{action}' (expected active modern release from whitelist)");
                        }
                    }
                }
            }

            Assert.True(
                invalidUsages.Count == 0,
                $"Found {invalidUsages.Count} invalid/outdated/deprecated action reference(s):\n" + string.Join("\n", invalidUsages));
        }

        [Fact]
        public void NoWorkflow_UsesDeprecatedJekyllOrLegacyOutdatedActions()
        {
            var repoRoot = FindRepoRoot();
            var workflowsDir = Path.Combine(repoRoot, ".github", "workflows");
            var workflowFiles = Directory.GetFiles(workflowsDir, "*.yml");

            foreach (var file in workflowFiles)
            {
                var content = File.ReadAllText(file);
                Assert.DoesNotContain("actions/jekyll-build-pages", content);
                Assert.DoesNotContain("actions/checkout@v1", content);
                Assert.DoesNotContain("actions/checkout@v2", content);
                Assert.DoesNotContain("actions/checkout@v3", content);
                Assert.DoesNotContain("actions/checkout@v4", content);
                Assert.DoesNotContain("actions/setup-dotnet@v1", content);
                Assert.DoesNotContain("actions/setup-dotnet@v2", content);
                Assert.DoesNotContain("actions/setup-dotnet@v3", content);
                Assert.DoesNotContain("actions/setup-dotnet@v4", content);
                Assert.DoesNotContain("actions/upload-artifact@v1", content);
                Assert.DoesNotContain("actions/upload-artifact@v2", content);
                Assert.DoesNotContain("actions/upload-artifact@v3", content);
                Assert.DoesNotContain("actions/upload-artifact@v4", content);
                Assert.DoesNotContain("actions/download-artifact@v1", content);
                Assert.DoesNotContain("actions/download-artifact@v2", content);
                Assert.DoesNotContain("actions/download-artifact@v3", content);
                Assert.DoesNotContain("actions/download-artifact@v4", content);
            }
        }
    }
}
