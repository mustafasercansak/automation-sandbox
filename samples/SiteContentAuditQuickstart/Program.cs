using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutomationSandbox.ContentAnalysis;
using AutomationSandbox.PlaywrightLiveExploration;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("  Automation Sandbox - Site Content Audit Quickstart");
        Console.WriteLine("  (SiteCrawler + ContentAnalyzer + ContentAnalysisReportFileSink - end to end)");
        Console.WriteLine("================================================================================");
        Console.WriteLine();

        // With a URL argument, this audits that real site (e.g. the project's own docs site:
        //   dotnet run --project samples/SiteContentAuditQuickstart -- https://mustafasercansak.github.io/automation-sandbox/
        // ) and only prints a summary. With no argument, it audits a small bundled fixture site
        // instead and additionally verifies specific planted findings - this is the mode CI runs,
        // so the check stays network-independent and deterministic.
        var auditingRealSite = args.Length > 0;
        LoopbackFileServer? fixtureServer = null;
        try
        {
            string startUrl;
            if (auditingRealSite)
            {
                startUrl = args[0];
                Console.WriteLine($"1. Auditing {startUrl}");
            }
            else
            {
                var baseDir = AppContext.BaseDirectory;
                var wwwrootPath = Path.Combine(baseDir, "wwwroot");
                if (!Directory.Exists(wwwrootPath))
                {
                    throw new DirectoryNotFoundException(
                        $"Could not find wwwroot next to the built assembly ({baseDir}). " +
                        "Rebuild the sample first: dotnet build samples/SiteContentAuditQuickstart");
                }

                fixtureServer = new LoopbackFileServer(wwwrootPath);
                startUrl = fixtureServer.BaseUrl + "index.html";
                Console.WriteLine($"1. No URL given - auditing the bundled fixture site at {startUrl}");
            }

            var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "site-content-audit-report.json");
            var htmlReportPath = Path.ChangeExtension(reportPath, ".html");
            var sink = new ContentAnalysisReportFileSink(reportPath, htmlReportPath);

            // Opt-in only: the audit still runs heuristic-only (no API key required) if this is unset.
            var llmProvider = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 0 }
                ? new ClaudeContentAnalysisProvider()
                : null;
            Console.WriteLine(llmProvider != null
                ? "2. ANTHROPIC_API_KEY is set - spelling/grammar/meaning review is enabled."
                : "2. ANTHROPIC_API_KEY is not set - running heuristic checks only.");

            Console.WriteLine();
            Console.WriteLine("3. Crawling and analyzing each page...");
            var totalIssues = 0;
            await using var session = await PlaywrightWebSession.StartAsync();
            var result = await SiteCrawler.CrawlAsync(
                session,
                startUrl,
                new SiteCrawlOptions { MaxPages = 20, MaxDepth = 3 },
                onPageCaptured: async (url, dom, ct) =>
                {
                    var issues = await ContentAnalyzer.AnalyzeAsync(dom, llmProvider, ct);
                    totalIssues += issues.Count;
                    Console.WriteLine($"   - {url}: {issues.Count} issue(s)");
                    foreach (var issue in issues)
                    {
                        Console.WriteLine($"       [{issue.IssueType}] {issue.CssSelector}: {issue.Message}");
                    }

                    sink.Record(ContentAnalysisReportEntry.FromAnalysis(url, dom, issues));
                });

            Console.WriteLine();
            Console.WriteLine("4. Crawl summary:");
            Console.WriteLine($"   - Visited:  {result.VisitedUrls.Count} page(s)");
            Console.WriteLine($"   - Skipped:  {result.SkippedUrls.Count} off-origin link(s)");
            Console.WriteLine($"   - Failed:   {result.Failures.Count} page(s)");
            foreach (var failure in result.Failures)
            {
                Console.WriteLine($"       {failure.Url}: {failure.Diagnostic}");
            }

            Console.WriteLine($"   - Total content issues found: {totalIssues}");
            Console.WriteLine();
            Console.WriteLine($"5. Report written to:");
            Console.WriteLine($"   - JSON: {reportPath}");
            Console.WriteLine($"   - HTML: {htmlReportPath}");

            if (!auditingRealSite)
            {
                VerifyFixtureFindings(result, totalIssues);
            }

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
            fixtureServer?.Dispose();
        }
    }

    // Checks the bundled fixture site's three planted findings were actually caught - this is what
    // makes the no-argument run a real regression check rather than just "did it crash."
    private static void VerifyFixtureFindings(SiteCrawlResult result, int totalIssues)
    {
        if (result.VisitedUrls.Count != 3)
        {
            throw new InvalidOperationException(
                $"Expected the crawler to visit all 3 fixture pages (index/about/contact), but visited {result.VisitedUrls.Count}.");
        }

        if (result.SkippedUrls.Count != 1 || !result.SkippedUrls.Any(u => u.Contains("example.com")))
        {
            throw new InvalidOperationException(
                "Expected the crawler to skip exactly the one off-origin link to example.com, but it didn't.");
        }

        if (!result.Failures.Any())
        {
            // Expected: the fixture has no broken links, so an empty Failures list confirms the
            // crawler didn't mistake a working page for a failure.
        }
        else
        {
            throw new InvalidOperationException(
                $"Expected no crawl failures on the fixture site, but got {result.Failures.Count}.");
        }

        if (totalIssues < 3)
        {
            throw new InvalidOperationException(
                $"Expected at least 3 planted content issues (placeholder, duplicate word, repeated punctuation) across the fixture site, but found {totalIssues}.");
        }

        WriteSuccess("All planted findings across the 3-page fixture site were caught.");
    }

    private static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"   >>> SUCCESS: {message}");
        Console.ResetColor();
    }
}
