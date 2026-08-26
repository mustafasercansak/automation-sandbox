using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PlaywrightLiveExploration;
using SelfHealing;
using UiModel;
using WebDiscovery;
using Xunit;

namespace ScenarioRunner
{
    public class PlaywrightEndToEndSampleTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _v1HtmlPath;
        private readonly string _v2HtmlPath;

        public PlaywrightEndToEndSampleTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PlaywrightEndToEndSampleTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            // Locate sample htmls from samples/PlaywrightEndToEndQuickstart/wwwroot
            var repoRoot = FindRepoRoot();
            var sampleWwwroot = Path.Combine(repoRoot, "samples", "PlaywrightEndToEndQuickstart", "wwwroot");

            _v1HtmlPath = Path.Combine(sampleWwwroot, "v1.html");
            _v2HtmlPath = Path.Combine(sampleWwwroot, "v2.html");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        [Fact]
        public async Task SampleApp_TwoVersionRefactor_SafeHealAndFalseHealPrevention_PassesEndToEnd()
        {
            Assert.True(File.Exists(_v1HtmlPath), $"v1.html must exist at {_v1HtmlPath}");
            Assert.True(File.Exists(_v2HtmlPath), $"v2.html must exist at {_v2HtmlPath}");

            await using var explorer = await PlaywrightLiveExplorer.LaunchAsync();

            // 1. Capture v1 baseline
            var v1Dom = await explorer.CaptureAsync(new Uri(_v1HtmlPath).AbsoluteUri);
            var v1Tree = WebElementMapper.ToUiElementTree(v1Dom);

            var v1Elements = Flatten(v1Tree).ToList();
            var v1CheckoutBtn = Assert.Single(v1Elements, e => e.AutomationId == "checkout-btn");
            var v1ApplyPromoBtn = Assert.Single(v1Elements, e => e.AutomationId == "apply-promo-btn");

            // 2. Capture v2 refactored app
            var v2Dom = await explorer.CaptureAsync(new Uri(_v2HtmlPath).AbsoluteUri);
            var v2Tree = WebElementMapper.ToUiElementTree(v2Dom);

            // 3. Batch resolution with one-to-one ownership reconciliation
            var batchRequests = new[]
            {
                new BatchHealingRequest("Checkout.SubmitButton", v1CheckoutBtn),
                new BatchHealingRequest("Checkout.ApplyPromoButton", v1ApplyPromoBtn),
            };

            var batchResult = SelfHealingResolver.ResolveBatch(batchRequests, v2Tree);

            var submitItem = Assert.Single(batchResult.Items, i => i.Request.LocatorKey == "Checkout.SubmitButton");
            var promoItem = Assert.Single(batchResult.Items, i => i.Request.LocatorKey == "Checkout.ApplyPromoButton");

            // Assert Checkout button was safely healed to complete-order-btn
            Assert.True(submitItem.Result.IsConfident);
            Assert.Equal("complete-order-btn", submitItem.Result.Matched?.AutomationId);
            Assert.Equal(BatchReconciliationDisposition.WonContention, submitItem.ReconciliationDisposition);

            // Assert Apply Promo button (deleted in v2) was safely declined to prevent false heal
            Assert.False(promoItem.Result.IsConfident);
            Assert.True(promoItem.Result.RejectedByReconciliation);
            Assert.Equal(HealResolutionStatus.OwnershipConflict, promoItem.Result.ResolutionStatus);
            Assert.Equal(BatchReconciliationDisposition.DeclinedByStrongerClaim, promoItem.ReconciliationDisposition);

            // 4. Persistence and Healing Report Sink
            var reportJsonPath = Path.Combine(_tempDir, "healing-report.json");
            var reportHtmlPath = Path.Combine(_tempDir, "healing-report.html");
            var reportSink = new HealingReportFileSink(reportJsonPath, reportHtmlPath);

            reportSink.Record(HealingReportEntry.FromResolutionAttempt(
                "Checkout.SubmitButton",
                v1CheckoutBtn,
                submitItem.Result,
                HealingReportEntry.OutcomeFromResolutionStatus(submitItem.Result.ResolutionStatus),
                platform: "web-playwright"));

            reportSink.Record(HealingReportEntry.FromResolutionAttempt(
                "Checkout.ApplyPromoButton",
                v1ApplyPromoBtn,
                promoItem.Result,
                HealingReportEntry.OwnershipConflictOutcome,
                platform: "web-playwright"));

            Assert.True(File.Exists(reportJsonPath));
            Assert.True(File.Exists(reportHtmlPath));

            var htmlContent = File.ReadAllText(reportHtmlPath);
            Assert.Contains("Checkout.SubmitButton", htmlContent);
            Assert.Contains("Checkout.ApplyPromoButton", htmlContent);
        }

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
}
