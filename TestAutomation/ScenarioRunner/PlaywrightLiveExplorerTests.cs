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
    // `playwright install` browser download is an external prerequisite. Cross-platform in
    // principle (Playwright browsers run on Linux/macOS too), but this only runs as part of
    // ScenarioRunner, which is net48/Windows-only, same as the FlaUI live tests.
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
