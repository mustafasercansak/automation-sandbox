using System.Net.Http;
using AutomationSandbox.ContentAnalysis;
using AutomationSandbox.PlaywrightLiveExploration;
using AutomationSandbox.WebDiscovery;
using AutomationSandbox.SiteAuditCli;

// Exit codes follow automation-sandbox-heal's convention so one CI habit covers both tools:
// 0 = nothing found, 1 = findings (the useful signal for a gate), 2 = the audit could not run.
internal static class Program
{
    private const int MaxPrintedIssues = 100;
    private const int CapturedTextLength = 10000;

    public static async Task<int> Main(string[] args)
    {
        AuditOptions options;
        try
        {
            options = AuditOptions.Parse(args, File.ReadAllLines);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine();
            PrintUsage(Console.Error);
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintUsage(Console.Out);
            return 0;
        }

        try
        {
            return await RunAsync(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> RunAsync(AuditOptions options)
    {
        if (options.InstallBrowser)
        {
            Console.WriteLine("Installing Playwright Chromium...");
            var installExit = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            if (installExit != 0)
            {
                Console.Error.WriteLine($"Error: 'playwright install chromium' failed with exit code {installExit}.");
                return 2;
            }
        }

        var dictionaryDirectory = options.DictionaryDirectory ?? DefaultDictionaryDirectory();
        var basePaths = new List<string>();
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
        {
            foreach (var language in options.Languages)
            {
                basePaths.Add(await DictionaryDownloader.EnsureAsync(
                    dictionaryDirectory, language, !options.NoDownload, http, Console.Out, CancellationToken.None));
            }
        }

        var spelling = SpellingChecker.FromDictionaryFiles(basePaths, options.AllowedWords);

        Directory.CreateDirectory(options.OutputDirectory);
        var jsonPath = Path.Combine(options.OutputDirectory, "site-audit-report.json");
        var htmlPath = Path.Combine(options.OutputDirectory, "site-audit-report.html");
        // The sink appends, so a rerun into the same directory would mix two audits into one report.
        File.Delete(jsonPath);
        File.Delete(htmlPath);
        var sink = new ContentAnalysisReportFileSink(jsonPath, htmlPath);

        Console.WriteLine($"Auditing {options.Url} (languages: {string.Join(", ", options.Languages)}; up to {options.MaxPages} pages, depth {options.MaxDepth})");

        var allIssues = new List<(string Url, ContentIssue Issue)>();
        await using var session = await PlaywrightWebSession.StartAsync();
        var result = await SiteCrawler.CrawlAsync(
            session,
            options.Url,
            new SiteCrawlOptions
            {
                MaxPages = options.MaxPages,
                MaxDepth = options.MaxDepth,
                // The default 160 characters would hide typos in any long paragraph (#541).
                DiscoveryOptions = new WebDiscoveryOptions { MaxTextLength = CapturedTextLength },
            },
            onPageCaptured: (url, dom, cancellationToken) =>
            {
                var issues = new List<ContentIssue>();
                if (!options.NoHeuristics)
                {
                    issues.AddRange(ContentAnalyzer.RunHeuristics(dom));
                }

                issues.AddRange(ContentAnalyzer.RunSpellCheck(dom, spelling, CapturedTextLength));
                issues = ContentAnalyzer.CollapseInheritedIssues(issues).ToList();
                sink.Record(ContentAnalysisReportEntry.FromAnalysis(url, dom, issues));
                allIssues.AddRange(issues.Select(issue => (url, issue)));
                Console.WriteLine($"  {url}: {issues.Count} issue(s)");
                return Task.CompletedTask;
            });

        if (result.VisitedUrls.Count == 0)
        {
            Console.Error.WriteLine($"Error: could not load the start URL. {result.Failures.FirstOrDefault()?.Diagnostic}");
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine($"Visited {result.VisitedUrls.Count} page(s); {result.Failures.Count} page(s) failed to load; {allIssues.Count} issue(s) found.");
        foreach (var failure in result.Failures)
        {
            Console.WriteLine($"  could not load {failure.Url}: {failure.Diagnostic}");
        }

        foreach (var (url, issue) in allIssues.Take(MaxPrintedIssues))
        {
            Console.WriteLine($"  [{issue.IssueType}] {url} {issue.CssSelector}: {issue.Message}");
        }

        if (allIssues.Count > MaxPrintedIssues)
        {
            Console.WriteLine($"  ...and {allIssues.Count - MaxPrintedIssues} more; see the report.");
        }

        Console.WriteLine();
        Console.WriteLine($"Report: {jsonPath}");
        Console.WriteLine($"        {htmlPath}");
        return allIssues.Count > 0 ? 1 : 0;
    }

    private static string DefaultDictionaryDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            root = Path.GetTempPath();
        }

        return Path.Combine(root, "automation-sandbox", "dictionaries");
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("automation-sandbox-audit: crawl a site from one URL and report spelling mistakes (no API key needed)");
        writer.WriteLine();
        writer.WriteLine("Usage: automation-sandbox-audit --url <https://...> [options]");
        writer.WriteLine();
        writer.WriteLine("  --url <url>            Start page (required). Only same-origin links are followed.");
        writer.WriteLine("  --lang <codes>         Comma-separated dictionaries, primary language first (default: en).");
        writer.WriteLine("                         Examples: tr,en  or  en-GB. A word is accepted if any dictionary accepts it.");
        writer.WriteLine("  --allow-word <word>    Never report this word (repeatable): brand and product names.");
        writer.WriteLine("  --allow-file <path>    Text file of allowed words, one per line ('#' starts a comment).");
        writer.WriteLine("  --max-pages <n>        Pages to visit at most (default: 25).");
        writer.WriteLine("  --max-depth <n>        Link depth from the start page (default: 3).");
        writer.WriteLine("  --output-dir <dir>     Where the JSON and HTML reports go (default: ./site-audit-report, overwritten).");
        writer.WriteLine("  --dictionary-dir <dir> Where dictionaries are cached (default: a per-user folder).");
        writer.WriteLine("  --no-download          Never download dictionaries; fail if one is missing.");
        writer.WriteLine("  --install-browser      Install Playwright Chromium first (needed once on a fresh machine).");
        writer.WriteLine("  --no-heuristics        Skip the duplicate-word, placeholder and punctuation checks.");
        writer.WriteLine();
        writer.WriteLine("Exit codes: 0 no findings, 1 findings, 2 the audit could not run.");
    }
}
