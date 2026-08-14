using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PlaywrightLiveExploration;
using UiModel;
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

        [Fact]
        public async Task CaptureAsync_FlagsCrossOriginIframe_WithoutSilentlyDroppingTheBoundary()
        {
            // Two separate file:// documents are distinct origins to Chromium, so this exercises the
            // real cross-origin path: the parent page cannot read the child's contentDocument.
            var tempDir = Path.Combine(Path.GetTempPath(), "PlaywrightLiveExplorerTests_CrossOrigin_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "inner.html"),
                    "<!doctype html><html><body><button data-testid=\"secret-pay\">Pay</button></body></html>");
                var mainHtmlPath = Path.Combine(tempDir, "main.html");
                File.WriteAllText(mainHtmlPath,
                    "<!doctype html><html><body><h1>Main</h1><iframe name=\"payment\" src=\"inner.html\"></iframe></body></html>");

                await using var explorer = await PlaywrightLiveExplorer.LaunchAsync();
                var dom = await explorer.CaptureAsync(new Uri(mainHtmlPath).AbsoluteUri);
                var flattened = Flatten(dom).ToList();

                // The frame's contents are genuinely unreachable from the parent page.
                Assert.DoesNotContain(flattened, e => e.TestId == "secret-pay");

                // The iframe element itself is still captured, and is flagged so a caller can tell
                // "blocked by same-origin policy" apart from "this frame is simply empty".
                var frame = Assert.Single(flattened, e => e.TagName == "iframe");
                Assert.True(frame.IsCrossOriginFrame, "A frame the script could not read must be flagged as cross-origin.");
                Assert.Empty(frame.Children);

                // The suggestions emitted for it locate the iframe element, which is valid - no
                // broken locator is produced for content that was never captured.
                var suggestions = PlaywrightLocatorEmitter.Suggest(frame).ToList();
                Assert.NotEmpty(suggestions);
                Assert.All(suggestions, s => Assert.DoesNotContain("FrameLocator", s.Expression));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public async Task CaptureAsync_CapturesIframeElements_GeneratesFrameLocatorsAndPreservesInGenerators()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "PlaywrightLiveExplorerTests_Iframes_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var mainHtmlPath = Path.Combine(tempDir, "main.html");
                File.WriteAllText(mainHtmlPath, """
                    <!doctype html>
                    <html>
                    <body>
                        <h1>Main Page</h1>
                        <iframe name="details" srcdoc="
                            <input data-testid='inner-email' name='innerEmail' placeholder='Inner Email' />
                            <iframe id='nestedFrame' srcdoc='&lt;button data-testid=&quot;nested-save&quot;&gt;Nested Save&lt;/button&gt;'></iframe>
                        "></iframe>
                    </body>
                    </html>
                    """);

                // The iframes here use srcdoc, which inherits the parent document's origin, so the
                // walker crosses the frame boundary without any relaxed browser flag - no
                // --allow-file-access-from-files needed even though the page is served from file://.
                await using var explorer = await PlaywrightLiveExplorer.LaunchAsync();
                var dom = await explorer.CaptureAsync(new Uri(mainHtmlPath).AbsoluteUri);

                var flattened = Flatten(dom).ToList();
                var innerEmail = flattened.Single(e => e.TestId == "inner-email");
                var nestedSave = flattened.Single(e => e.TestId == "nested-save");

                // Verify FrameAncestry capture
                Assert.Equal(new[] { "iframe[name='details']" }, innerEmail.FrameAncestry);
                Assert.Equal(new[] { "iframe[name='details']", "iframe#nestedFrame" }, nestedSave.FrameAncestry);

                // Verify locator emitter produces FrameLocator chains
                var emailSuggestions = PlaywrightLocatorEmitter.Suggest(innerEmail);
                Assert.Equal("page.FrameLocator(\"iframe[name='details']\").GetByTestId(\"inner-email\")", emailSuggestions[0].Expression);

                var saveSuggestions = PlaywrightLocatorEmitter.Suggest(nestedSave);
                Assert.Equal("page.FrameLocator(\"iframe[name='details']\").FrameLocator(\"iframe#nestedFrame\").GetByTestId(\"nested-save\")", saveSuggestions[0].Expression);

                // Verify C# and TypeScript generators emit correct FrameLocator code
                var scenario = new IntentAutomation.IntentScenario
                {
                    Name = "Iframe interaction flow",
                    Goal = "Interact with elements inside nested iframes",
                    Steps = new System.Collections.Generic.List<IntentAutomation.IntentStep>
                    {
                        new IntentAutomation.IntentStep
                        {
                            Order = 1,
                            ActionType = IntentAutomation.IntentActionType.Fill,
                            TargetDescription = "inner email",
                            TestIntent = "Fill inner email",
                            Value = "inner@example.com",
                            LocatorKey = "Field.InnerEmail",
                        },
                        new IntentAutomation.IntentStep
                        {
                            Order = 2,
                            ActionType = IntentAutomation.IntentActionType.Click,
                            TargetDescription = "nested save",
                            TestIntent = "Click nested save button",
                            LocatorKey = "Action.NestedSave",
                        },
                    }
                };

                var repoPath = Path.Combine(tempDir, "locators.json");
                var repository = new LocatorRepository(repoPath);
                var exploration = new IntentAutomation.IntentExplorationBridge().Match(scenario, dom);
                var recordingResults = new IntentAutomation.IntentLocatorRepositoryRecorder().Record(exploration, repository);

                var csharpCode = new IntentAutomation.PlaywrightCSharpTestGenerator().Generate(scenario, recordingResults);
                var typeScriptCode = new IntentAutomation.PlaywrightTypeScriptTestGenerator().Generate(scenario, recordingResults);

                Assert.Contains("Page.FrameLocator(\"iframe[name='details']\").GetByTestId(\"inner-email\")", csharpCode);
                Assert.Contains("Page.FrameLocator(\"iframe[name='details']\").FrameLocator(\"iframe#nestedFrame\").GetByTestId(\"nested-save\")", csharpCode);

                Assert.Contains("page.frameLocator('iframe[name=\\'details\\']').getByTestId('inner-email')", typeScriptCode);
                Assert.Contains("page.frameLocator('iframe[name=\\'details\\']').frameLocator('iframe#nestedFrame').getByTestId('nested-save')", typeScriptCode);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
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
