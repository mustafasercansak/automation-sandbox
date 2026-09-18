using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ScenarioRunner
{
    // eng/PublicApiAudit has no test coverage of its own (it is a top-level-statements Exe
    // project, so its logic isn't directly unit-testable from another project) even though
    // ci.yml's public-api-audit-sync job now fails the build on any drift it reports - a
    // parsing bug there would either miss real API surface silently or false-positive every
    // PR. These integration tests run the actual published tool as a subprocess against a
    // small synthetic fixture tree, the same way a consumer invokes it.
    public class PublicApiAuditTests
    {
        private static readonly string[] Packages =
        {
            "UiModel", "SelfHealing", "LlmHealing", "Discovery",
            "WebDiscovery", "IntentAutomation", "PlaywrightLiveExploration",
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

        // Writes one or more fake source files into a single package folder of a throwaway
        // fixture tree (all seven package folders are created, matching the tool's hardcoded
        // package list, so it never hits a DirectoryNotFoundException on the six it doesn't
        // populate) and returns the parsed JSON array the real tool produced.
        private static JsonElement[] RunAudit(string package, params (string FileName, string Source)[] files)
        {
            var repoRoot = FindRepoRoot();
            var fixtureRoot = Path.Combine(Path.GetTempPath(), "PublicApiAuditTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var p in Packages)
                {
                    Directory.CreateDirectory(Path.Combine(fixtureRoot, "TestAutomation", p));
                }

                foreach (var (fileName, source) in files)
                {
                    File.WriteAllText(Path.Combine(fixtureRoot, "TestAutomation", package, fileName), source);
                }

                var outputPath = Path.Combine(fixtureRoot, "output.json");
                var psi = new ProcessStartInfo("dotnet")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add("run");
                psi.ArgumentList.Add("--project");
                psi.ArgumentList.Add(Path.Combine(repoRoot, "eng", "PublicApiAudit"));
                psi.ArgumentList.Add("--");
                psi.ArgumentList.Add(fixtureRoot);
                psi.ArgumentList.Add(outputPath);

                using var process = Process.Start(psi)!;
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                Assert.True(process.WaitForExit(TimeSpan.FromSeconds(60)), "PublicApiAudit did not exit within 60s.");
                Assert.True(process.ExitCode == 0, $"PublicApiAudit exited {process.ExitCode}.\nstdout: {stdout}\nstderr: {stderr}");

                return JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(outputPath))!;
            }
            finally
            {
                if (Directory.Exists(fixtureRoot))
                {
                    Directory.Delete(fixtureRoot, recursive: true);
                }
            }
        }

        private static JsonElement? FindType(JsonElement[] entries, string fullName) =>
            entries.Cast<JsonElement?>().FirstOrDefault(e => e!.Value.GetProperty("Type").GetString() == fullName);

        private static bool HasMemberContaining(JsonElement entry, string substring) =>
            entry.GetProperty("Members").EnumerateArray().Any(m => m.GetString()!.Contains(substring, StringComparison.Ordinal));

        [Fact]
        public void PublicType_RecordsPublicAndProtectedMembers_ExcludesPrivate()
        {
            var entries = RunAudit("UiModel", ("PublicWidget.cs", @"
namespace AutomationSandbox.UiModel
{
    public class PublicWidget
    {
        public int PublicProperty { get; set; }
        protected int ProtectedProperty { get; set; }
        private int PrivateProperty { get; set; }
        public void PublicMethod() { }
        private void PrivateMethod() { }
    }
}
"));

            var type = FindType(entries, "AutomationSandbox.UiModel.PublicWidget");
            Assert.NotNull(type);
            Assert.True(HasMemberContaining(type!.Value, "PublicProperty"));
            Assert.True(HasMemberContaining(type!.Value, "ProtectedProperty"));
            Assert.True(HasMemberContaining(type!.Value, "PublicMethod"));
            Assert.False(HasMemberContaining(type!.Value, "PrivateProperty"));
            Assert.False(HasMemberContaining(type!.Value, "PrivateMethod"));
        }

        [Fact]
        public void InternalType_IsExcludedEntirely_EvenWithPublicLookingMembers()
        {
            var entries = RunAudit("UiModel", ("InternalWidget.cs", @"
namespace AutomationSandbox.UiModel
{
    internal class InternalWidget
    {
        public int LooksPublicButIsnt { get; set; }
    }
}
"));

            Assert.Null(FindType(entries, "AutomationSandbox.UiModel.InternalWidget"));
        }

        [Fact]
        public void PublicEnum_RecordsItsMembers()
        {
            var entries = RunAudit("UiModel", ("PublicColor.cs", @"
namespace AutomationSandbox.UiModel
{
    public enum PublicColor
    {
        Red,
        Green,
    }
}
"));

            var type = FindType(entries, "AutomationSandbox.UiModel.PublicColor");
            Assert.NotNull(type);
            Assert.True(HasMemberContaining(type!.Value, "Red"));
            Assert.True(HasMemberContaining(type!.Value, "Green"));
        }

        [Fact]
        public void PartialClassAcrossTwoFiles_MergesIntoOneEntry()
        {
            // docs/public-api-audit.md documents this explicitly: "Partial declarations
            // merge" - two files contributing to the same type must produce one JSON entry
            // with both files listed and both files' members present, not two entries.
            var entries = RunAudit(
                "UiModel",
                ("SplitWidget.Part1.cs", @"
namespace AutomationSandbox.UiModel
{
    public partial class SplitWidget
    {
        public int FromFileA { get; set; }
    }
}
"),
                ("SplitWidget.Part2.cs", @"
namespace AutomationSandbox.UiModel
{
    public partial class SplitWidget
    {
        public int FromFileB { get; set; }
    }
}
"));

            var matches = entries.Where(e => e.GetProperty("Type").GetString() == "AutomationSandbox.UiModel.SplitWidget").ToArray();
            var type = Assert.Single(matches);
            Assert.Equal(2, type.GetProperty("Files").GetArrayLength());
            Assert.True(HasMemberContaining(type, "FromFileA"));
            Assert.True(HasMemberContaining(type, "FromFileB"));
        }
    }
}
