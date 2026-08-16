using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ScenarioRunner
{
    // Guards the Cloudflare model-catalog probe in .github/workflows/provider-diagnostics.yml.
    //
    // The whole point of that workflow is to answer "which model ids will this provider accept",
    // so that a repository variable is set from a listing rather than from a guess. A probe that
    // returns an empty list defeats it silently: HTTP 200 with {"data":[]} looks like a working
    // credential reporting an empty catalog, and the natural next move is to guess a model name -
    // which is how grok-2-latest, a model that never existed, ended up configured.
    //
    // #107: the probe filtered on task=text-generation. Cloudflare matches task against the
    // display name ("Text Generation"), so the filter excluded every model in the catalog.
    public class ProviderDiagnosticsWorkflowTests
    {
        [Fact]
        public void CloudflareModelSearch_DoesNotFilterByTask()
        {
            var url = CloudflareModelSearchUrl();

            Assert.DoesNotContain("task=", url, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CloudflareModelSearch_RequestsTheOpenRouterFormat()
        {
            // Not cosmetic. The summary extracts ids with `.data[]?.id` and `.models[]?.name`;
            // Cloudflare's native response is {"result":[{"name":"@cf/..."}]}, which has neither,
            // so dropping this parameter would produce an empty cell for a successful call.
            var url = CloudflareModelSearchUrl();

            Assert.Contains("format=openrouter", url, StringComparison.Ordinal);
        }

        [Fact]
        public void CloudflareModelSearch_IsScopedToTheConfiguredAccount()
        {
            // A catalog read is per-account; there is no global Workers AI listing. Losing the
            // account segment turns the probe into a 7000 "No route for that URI".
            var url = CloudflareModelSearchUrl();

            Assert.Contains("/accounts/${CLOUDFLARE_ACCOUNT_ID}/ai/models/search", url, StringComparison.Ordinal);
        }

        private static string CloudflareModelSearchUrl()
        {
            var workflow = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "provider-diagnostics.yml");
            Assert.True(File.Exists(workflow), $"Expected the diagnostics workflow at {workflow}.");

            var line = File.ReadLines(workflow)
                .FirstOrDefault(l => l.Contains("ai/models/search", StringComparison.Ordinal));

            Assert.False(
                string.IsNullOrEmpty(line),
                "Could not find the Cloudflare model search URL in provider-diagnostics.yml. " +
                "If the probe moved, move this guard with it rather than deleting it.");

            return line!;
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
