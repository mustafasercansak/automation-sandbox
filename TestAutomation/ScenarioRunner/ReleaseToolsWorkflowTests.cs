using System;
using System.Linq;

namespace ScenarioRunner
{
    // The agent, MCP and site-audit tools are published by release.yml behind their own switch (#658), apart from the nine
    // libraries and from the heal tool. These tests pin that wiring; the install-and-run check itself is
    // eng/Verify-PublishedTools.ps1, which was run against locally packed tools.
    public class ReleaseToolsWorkflowTests
    {
        private static string Release() => string.Join("\n", AgentTriageWorkflowTests.ReadRepoFile(".github", "workflows", "release.yml"));

        private static string Pack() => string.Join("\n", AgentTriageWorkflowTests.ReadRepoFile(".github", "workflows", "pack.yml"));

        [Fact]
        public void Release_HasASeparatePublishToolsSwitch_ThatIsOffByDefault()
        {
            var text = Release();

            Assert.Matches(@"(?s)publish_tools:\s+description:[^\n]+\n\s+required: false\n\s+default: false\n\s+type: boolean", text);
        }

        [Theory]
        [InlineData("AgentCli")]
        [InlineData("McpServer")]
        [InlineData("SiteAuditCli")]
        public void Release_PacksEachToolIntoItsOwnDirectory_OnlyWhenPublishToolsIsSet(string project)
        {
            var text = Release();

            Assert.Contains($"dotnet pack TestAutomation/{project}/{project}.csproj --configuration Release --no-build --output ./tool-nupkgs", text);
            Assert.Matches($@"- name: Pack {project}\n\s+if: \$\{{\{{ inputs\.publish_tools \}}\}}", text);
            // Never into the library directory, whose layout check would fail on a tool package.
            Assert.DoesNotContain($"{project}.csproj --configuration Release --no-build --output ./nupkgs", text);
        }

        [Fact]
        public void Release_PushesAndVerifiesTheTools_OnlyForARealRun()
        {
            var text = Release();

            Assert.Matches(@"- name: Push tool packages to nuget\.org\n\s+if: \$\{\{ inputs\.publish_tools && !inputs\.dry_run \}\}", text);
            Assert.Matches(@"- name: Verify the published tools for a consumer\n\s+if: \$\{\{ inputs\.publish_tools && !inputs\.dry_run \}\}", text);
            Assert.Contains("./eng/Verify-PublishedTools.ps1 -Version $env:PACKAGE_VERSION", text);
            Assert.Matches(@"inputs\.publish_to_nuget \|\| inputs\.publish_cli \|\| inputs\.publish_tools\) && !inputs\.dry_run", text);
            Assert.Contains("name: release-tool-nupkgs", text);
        }

        [Fact]
        public void Release_TheToolPush_ReceivesTheKeyThroughTheEnvironment_NotTheCommandLineText()
        {
            var lines = AgentTriageWorkflowTests.ReadRepoFile(".github", "workflows", "release.yml");
            var start = Array.FindIndex(lines, l => l.Contains("Push tool packages to nuget.org"));
            Assert.True(start >= 0);
            var block = string.Join("\n", lines.Skip(start).Take(10));

            Assert.Contains("NUGET_API_KEY: ${{ steps.nuget_login.outputs.NUGET_API_KEY }}", block);
            Assert.Contains("--api-key $env:NUGET_API_KEY", block);
        }

        [Fact]
        public void Pack_PacksAllFourToolsAsAnArtifact_WithoutPublishing()
        {
            var text = Pack();

            foreach (var project in new[] { "SelfHealingCli", "AgentCli", "McpServer", "SiteAuditCli" })
            {
                Assert.Contains($"dotnet pack TestAutomation/{project}/{project}.csproj --configuration Release --no-build --output ./tool-nupkgs", text);
            }

            Assert.Contains("name: tool-nupkgs", text);
            Assert.DoesNotContain("nuget push", text);
        }

        [Fact]
        public void TheVerifyScript_ChecksTheVersionShapeAndTheToolsOutputWithoutNetworkOrKeys()
        {
            var text = string.Join("\n", AgentTriageWorkflowTests.ReadRepoFile("eng", "Verify-PublishedTools.ps1"));

            Assert.Contains("^[0-9]+\\.[0-9]+\\.[0-9]+([-+][0-9A-Za-z.+-]+)?$", text);
            foreach (var name in new[] { "heal_locator", "classify_ci_log", "check_issue_eligibility", "triage_issue_dry_run" })
            {
                Assert.Contains(name, text);
            }
        }
    }
}
