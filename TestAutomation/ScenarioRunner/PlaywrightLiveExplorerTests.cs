using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PlaywrightLiveExploration;
using WebDiscovery;

namespace ScenarioRunner
{
    // These tests actually launch a real headless Chromium via the Microsoft.Playwright .NET
    // SDK (the same "real thing, not mocked" convention MainFormScenarioTests/
    // WpfMainWindowScenarioTests use for FlaUI/UIA3) against a local file:// HTML fixture, so
    // no network access is needed for the page under test itself - only the one-time
    // `playwright install` browser download is an external prerequisite. Runs cross-platform
    // across both Windows (net48) and Linux (net8.0).
    public class PlaywrightLiveExplorerTests : IDisposable
    {
        private readonly string _htmlPath;

        public PlaywrightLiveExplorerTests()
        {
            _htmlPath = Path.Combine(Path.GetTempPath(), "PlaywrightLiveExplorerTests_" + Guid.NewGuid().ToString("N") + ".html");
            File.WriteAllText(_htmlPath, """
                <!doctype html>
                <html>
                <body>
                    <input data-testid="email-input" name="email" placeholder="Email" />
                    <button data-testid="save-button">Save</button>
                </body>
                </html>
                """);
        }

        [Fact]
        public async Task CaptureAsync_ReturnsDomTreeMatchingTheLivePage()
        {
            await using var explorer = await PlaywrightLiveExplorer.LaunchAsync();

            var dom = await explorer.CaptureAsync(new Uri(_htmlPath).AbsoluteUri);

            var flattened = Flatten(dom).ToList();
            Assert.Contains(flattened, element => element.TestId == "email-input" && element.NameAttribute == "email");
            Assert.Contains(flattened, element => element.TestId == "save-button" && element.Text == "Save");
        }

        [Fact]
        public async Task CaptureAsync_RejectsNullOrEmptyUrl()
        {
            await using var explorer = await PlaywrightLiveExplorer.LaunchAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => explorer.CaptureAsync(""));
        }

        [Fact]
        public async Task CaptureAsync_CapturesBelowTheFoldOffscreenElements_PreservesGeometryAndMatches()
        {
            var longHtmlPath = Path.Combine(Path.GetTempPath(), "PlaywrightLiveExplorerTests_LongPage_" + Guid.NewGuid().ToString("N") + ".html");
            try
            {
                File.WriteAllText(longHtmlPath, """
                    <!doctype html>
                    <html>
                    <body>
                        <input data-testid="email-input" name="email" placeholder="Email" />
                        <div style="height: 3000px;">Spacer</div>
                        <button data-testid="checkout-button">Checkout</button>
                    </body>
                    </html>
                    """);

                await using var explorer = await PlaywrightLiveExplorer.LaunchAsync();
                var dom = await explorer.CaptureAsync(new Uri(longHtmlPath).AbsoluteUri);

                var flattened = Flatten(dom).ToList();
                var checkout = flattened.Single(e => e.TestId == "checkout-button");

                // Explicitly assert viewport-derived IsOffscreen and non-hidden status
                Assert.True(checkout.IsOffscreen, "Below-the-fold button should be detected as IsOffscreen = true in standard viewport.");
                Assert.False(checkout.IsHidden, "Below-the-fold button must not be marked as IsHidden.");
                Assert.True(checkout.BoundingRectangle.Y >= 3000, $"BoundingRectangle.Y should be >= 3000, actual: {checkout.BoundingRectangle.Y}");

                // Verify IntentExplorationBridge matches the offscreen element
                var scenario = new IntentAutomation.IntentScenario
                {
                    Goal = "Complete order",
                    Steps = new System.Collections.Generic.List<IntentAutomation.IntentStep>
                    {
                        new IntentAutomation.IntentStep
                        {
                            Order = 1,
                            ActionType = IntentAutomation.IntentActionType.Click,
                            TargetDescription = "checkout",
                            TestIntent = "Click checkout button",
                            LocatorKey = "Action.Checkout",
                        }
                    }
                };

                var exploration = new IntentAutomation.IntentExplorationBridge().Match(scenario, dom);
                Assert.False(exploration.StepResults[0].RequiresReview);
                Assert.Equal("checkout-button", exploration.StepResults[0].Candidates[0].Element.TestId);

                // Verify mapped UiElementInfo retains bounding rectangle
                var tree = WebElementMapper.ToUiElementTree(dom);
                var mappedNodes = tree.Children;
                var mappedCheckout = mappedNodes.FirstOrDefault(n => n.AutomationId == "checkout-button")
                    ?? tree.Children.SelectMany(c => c.Children).FirstOrDefault(n => n.AutomationId == "checkout-button");
                Assert.NotNull(mappedCheckout);
                Assert.True(mappedCheckout!.BoundingRectangle.Y >= 3000);
            }
            finally
            {
                if (File.Exists(longHtmlPath))
                {
                    File.Delete(longHtmlPath);
                }
            }
        }

        public void Dispose()
        {
            if (File.Exists(_htmlPath))
            {
                File.Delete(_htmlPath);
            }
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
}
