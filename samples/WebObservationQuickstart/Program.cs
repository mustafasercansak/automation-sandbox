using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutomationSandbox.ContentAnalysis;
using AutomationSandbox.PlaywrightLiveExploration;
using AutomationSandbox.WebDiscovery;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("  Automation Sandbox - Web Observation Quickstart");
        Console.WriteLine("  (long-lived session, auth persistence, and content analysis - end to end)");
        Console.WriteLine("================================================================================");
        Console.WriteLine();

        var baseDir = AppContext.BaseDirectory;
        var wwwrootPath = Path.Combine(baseDir, "wwwroot");
        if (!Directory.Exists(wwwrootPath))
        {
            // wwwroot is copied next to the built assembly by the project's CopyToOutputDirectory
            // item, the same convention PlaywrightEndToEndQuickstart uses.
            throw new DirectoryNotFoundException(
                $"Could not find wwwroot next to the built assembly ({baseDir}). " +
                "Rebuild the sample first: dotnet build samples/WebObservationQuickstart");
        }

        var statePath = Path.Combine(Path.GetTempPath(), "WebObservationQuickstart_" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            using var server = new LoopbackFileServer(wwwrootPath);
            var loginUrl = server.BaseUrl + "login.html";
            var dashboardUrl = server.BaseUrl + "dashboard.html";

            Console.WriteLine($"1. Serving the fixture app locally at {server.BaseUrl}");

            // -------------------------------------------------------------------------
            // Step 1: Authenticate once (FillAsync + ClickAsync), then persist the session
            // -------------------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("2. Logging in with a fresh session...");

            string welcomeAfterLogin;
            await using (var loginSession = await PlaywrightWebSession.StartAsync())
            {
                await loginSession.NavigateAsync(loginUrl);
                await loginSession.FillAsync("[data-testid='username']", "ada");
                await loginSession.ClickAsync("[data-testid='login-button']");

                var dom = await loginSession.CaptureAsync();
                welcomeAfterLogin = Flatten(dom).Single(e => e.TestId == "welcome").Text;
                Console.WriteLine($"   - Dashboard says: \"{welcomeAfterLogin}\"");

                await loginSession.SaveStorageStateAsync(statePath);
                Console.WriteLine($"   - Saved authenticated storage state to {statePath}");
            }

            if (welcomeAfterLogin != "Welcome back, token-for-ada")
            {
                throw new InvalidOperationException(
                    $"Expected the dashboard to greet the logged-in user, but got: \"{welcomeAfterLogin}\"");
            }

            WriteSuccess("Authenticated and captured the personalized dashboard.");

            // -------------------------------------------------------------------------
            // Step 2: Start a brand-new session from the saved state - no login step
            // -------------------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("3. Starting a brand-new session from the saved storage state (no login)...");

            IReadOnlyList<ContentIssue> contentIssues;
            IReadOnlyList<WebNetworkResponse> networkResponses;
            IReadOnlyList<WebConsoleMessage> consoleMessages;

            await using (var returningSession = await PlaywrightWebSession.StartAsync(storageStatePath: statePath))
            {
                await returningSession.NavigateAsync(dashboardUrl);

                var dashboardDom = await returningSession.CaptureAsync();
                var welcomeAfterRestore = Flatten(dashboardDom).Single(e => e.TestId == "welcome").Text;
                Console.WriteLine($"   - Dashboard says (no login this time): \"{welcomeAfterRestore}\"");

                if (welcomeAfterRestore != welcomeAfterLogin)
                {
                    throw new InvalidOperationException(
                        $"Expected the restored session to already be authenticated, but got: \"{welcomeAfterRestore}\"");
                }

                WriteSuccess("Storage state round-tripped - no repeated login.");

                // ---------------------------------------------------------------------
                // Step 3: Content-quality check + observation layer, on the same page
                // ---------------------------------------------------------------------
                Console.WriteLine();
                Console.WriteLine("4. Running ContentAnalyzer over the captured dashboard...");
                contentIssues = await ContentAnalyzer.AnalyzeAsync(dashboardDom);
                networkResponses = returningSession.NetworkResponses;
                consoleMessages = returningSession.ConsoleMessages;
            }

            foreach (var issue in contentIssues)
            {
                Console.WriteLine($"   - [{issue.IssueType}] {issue.CssSelector}: {issue.Message}");
            }

            if (!contentIssues.Any(issue => issue.IssueType == "Placeholder"))
            {
                throw new InvalidOperationException(
                    "Expected ContentAnalyzer to flag the planted TODO placeholder, but it didn't.");
            }

            WriteSuccess("The planted placeholder text was flagged.");

            Console.WriteLine();
            Console.WriteLine("5. Checking the observation layer for the planted broken image...");
            var brokenImageResponse = networkResponses.FirstOrDefault(
                r => r.Url.EndsWith("missing-asset.png", StringComparison.Ordinal));
            if (brokenImageResponse == null || brokenImageResponse.Status != 404)
            {
                throw new InvalidOperationException(
                    "Expected a 404 network response for the planted broken image, but didn't observe one.");
            }

            Console.WriteLine($"   - Network response: {brokenImageResponse.Status} {brokenImageResponse.Url}");
            foreach (var message in consoleMessages)
            {
                Console.WriteLine($"   - Console [{message.MessageType}] {message.Text}");
            }

            WriteSuccess("The observation layer caught the planted broken image.");

            Console.WriteLine();
            Console.WriteLine("Demo completed successfully!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error running demo: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
            return 1;
        }
        finally
        {
            if (File.Exists(statePath))
            {
                File.Delete(statePath);
            }
        }
    }

    private static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"   >>> SUCCESS: {message}");
        Console.ResetColor();
    }

    private static IEnumerable<WebElementInfo> Flatten(WebElementInfo root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}
