using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PlaywrightLiveExploration;
using SelfHealing;
using UiModel;
using WebDiscovery;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("  Automation Sandbox - Playwright End-to-End Self-Healing & Safety Sample");
        Console.WriteLine("================================================================================");
        Console.WriteLine();

        var baseDir = AppContext.BaseDirectory;
        var v1Path = Path.Combine(baseDir, "wwwroot", "v1.html");
        var v2Path = Path.Combine(baseDir, "wwwroot", "v2.html");

        if (!File.Exists(v1Path) || !File.Exists(v2Path))
        {
            v1Path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "v1.html"));
            v2Path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "v2.html"));
        }

        var reportJsonPath = Path.Combine(Directory.GetCurrentDirectory(), "healing-report.json");
        var reportHtmlPath = Path.Combine(Directory.GetCurrentDirectory(), "healing-report.html");
        var repositoryPath = Path.Combine(Directory.GetCurrentDirectory(), "locators.json");

        var reportSink = new HealingReportFileSink(reportJsonPath, reportHtmlPath);
        var locatorRepo = new LocatorRepository(repositoryPath);

        try
        {
            Console.WriteLine("1. Launching Playwright Headless Chromium...");
            await using var explorer = await PlaywrightLiveExplorer.LaunchAsync();

            // -------------------------------------------------------------------------
            // Step 1: Capture Baseline from v1 Application
            // -------------------------------------------------------------------------
            Console.WriteLine($"2. Capturing baseline DOM from App v1 ({v1Path})...");
            var v1Dom = await explorer.CaptureAsync(new Uri(v1Path).AbsoluteUri);
            var v1Tree = WebElementMapper.ToUiElementTree(v1Dom);

            // Locate v1 controls to store in baseline repository
            var v1Flattened = Flatten(v1Tree).ToList();
            var v1CheckoutBtn = v1Flattened.FirstOrDefault(e => e.AutomationId == "checkout-btn")
                ?? throw new InvalidOperationException("Could not find checkout-btn in v1 DOM.");
            var v1ApplyPromoBtn = v1Flattened.FirstOrDefault(e => e.AutomationId == "apply-promo-btn")
                ?? throw new InvalidOperationException("Could not find apply-promo-btn in v1 DOM.");

            locatorRepo.Upsert("Checkout.SubmitButton", v1CheckoutBtn, platform: "web-playwright");
            locatorRepo.Upsert("Checkout.ApplyPromoButton", v1ApplyPromoBtn, platform: "web-playwright");

            Console.WriteLine($"   - Captured locator 'Checkout.SubmitButton': ID='{v1CheckoutBtn.AutomationId}', Text='{v1CheckoutBtn.Name}', Box={v1CheckoutBtn.BoundingRectangle}");
            Console.WriteLine($"   - Captured locator 'Checkout.ApplyPromoButton': ID='{v1ApplyPromoBtn.AutomationId}', Text='{v1ApplyPromoBtn.Name}', Box={v1ApplyPromoBtn.BoundingRectangle}");

            // -------------------------------------------------------------------------
            // Step 2: Navigate to App v2 (Refactored UI) and Execute Batch Healing Pipeline
            // -------------------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine($"3. Navigating to Refactored App v2 ({v2Path})...");
            var v2Dom = await explorer.CaptureAsync(new Uri(v2Path).AbsoluteUri);
            var v2Tree = WebElementMapper.ToUiElementTree(v2Dom);

            Console.WriteLine("4. Executing batch locator resolution with one-to-one ownership reconciliation:");
            Console.WriteLine();

            var batchRequests = new[]
            {
                new BatchHealingRequest("Checkout.SubmitButton", v1CheckoutBtn),
                new BatchHealingRequest("Checkout.ApplyPromoButton", v1ApplyPromoBtn),
            };

            var batchResult = SelfHealingResolver.ResolveBatch(
                batchRequests,
                v2Tree,
                log: msg => Console.WriteLine("     " + msg));

            Console.WriteLine();
            Console.WriteLine($"   Contested candidates: {batchResult.ContestedCandidateCount}, Reconciled declines: {batchResult.ReconciliationDeclineCount}");

            var submitItem = batchResult.Items.First(i => i.Request.LocatorKey == "Checkout.SubmitButton");
            var promoItem = batchResult.Items.First(i => i.Request.LocatorKey == "Checkout.ApplyPromoButton");

            // Record both results into the healing report
            var outcomeSubmit = HealingReportEntry.OutcomeFromResolutionStatus(submitItem.Result.ResolutionStatus);
            var outcomePromo = submitItem.Result.RejectedByReconciliation
                ? HealingReportEntry.OwnershipConflictOutcome
                : (promoItem.Result.RejectedByReconciliation
                    ? HealingReportEntry.OwnershipConflictOutcome
                    : HealingReportEntry.OutcomeFromResolutionStatus(promoItem.Result.ResolutionStatus));

            reportSink.Record(HealingReportEntry.FromResolutionAttempt(
                "Checkout.SubmitButton",
                v1CheckoutBtn,
                submitItem.Result,
                outcomeSubmit,
                platform: "web-playwright"));

            reportSink.Record(HealingReportEntry.FromResolutionAttempt(
                "Checkout.ApplyPromoButton",
                v1ApplyPromoBtn,
                promoItem.Result,
                promoItem.Result.RejectedByReconciliation ? HealingReportEntry.OwnershipConflictOutcome : HealingReportEntry.OutcomeFromResolutionStatus(promoItem.Result.ResolutionStatus),
                platform: "web-playwright"));

            // Verify Step A: Moved/Renamed button safely healed
            if (submitItem.Result.IsConfident && submitItem.Result.Matched?.AutomationId == "complete-order-btn")
            {
                locatorRepo.Upsert("Checkout.SubmitButton", submitItem.Result.Matched, LocatorHealingHistoryEntryFactory.FromHealResult(submitItem.Result, v1CheckoutBtn), platform: "web-playwright");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"   >>> SUCCESS (Healed): 'Checkout.SubmitButton' -> '{submitItem.Result.Matched.AutomationId}' (Disposition: {submitItem.ReconciliationDisposition}, Score: {submitItem.Result.Score:F2})");
                Console.ResetColor();
            }
            else
            {
                throw new InvalidOperationException($"Expected 'Checkout.SubmitButton' to win contention and heal, but got: {submitItem.ReconciliationDisposition}");
            }

            // Verify Step B: Deleted button correctly declined by ownership guard
            if (!promoItem.Result.IsConfident && promoItem.Result.RejectedByReconciliation)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"   >>> SUCCESS (False-Heal Prevented): 'Checkout.ApplyPromoButton' was declined by ownership reconciliation (Disposition: {promoItem.ReconciliationDisposition}, Status: {promoItem.Result.ResolutionStatus}) -> Routed to manual review.");
                Console.ResetColor();
            }
            else
            {
                throw new InvalidOperationException($"Expected 'Checkout.ApplyPromoButton' to be declined by reconciliation, but got: {promoItem.ReconciliationDisposition}");
            }

            // -------------------------------------------------------------------------
            // Step 3: Report Verification & Artifacts
            // -------------------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("5. Generated Artifacts:");
            Console.WriteLine($"   - JSON Report: {reportJsonPath}");
            Console.WriteLine($"   - HTML Report: {reportHtmlPath}");
            Console.WriteLine($"   - Persisted Locators: {repositoryPath}");
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
    }

    private static System.Collections.Generic.IEnumerable<UiElementInfo> Flatten(UiElementInfo node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}
