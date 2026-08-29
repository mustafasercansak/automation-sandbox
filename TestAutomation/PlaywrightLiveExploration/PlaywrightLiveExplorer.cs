using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using WebDiscovery;

namespace PlaywrightLiveExploration
{
    // Closes the gap the "MCP Exploration" docs previously described as Planned: instead of
    // requiring a hand-written Playwright test that calls
    // page.EvaluateAsync<WebElementInfo>(PlaywrightDomCaptureScript.JavaScript) itself, this
    // class owns that browser lifecycle directly via the Microsoft.Playwright .NET SDK - no
    // Model Context Protocol, no Node.js process, no new external tool dependency. See
    // docs/intent-driven-automation.md for why a real MCP bridge (Node.js-based Playwright MCP
    // server) was ruled out for this project.

    public sealed class PlaywrightLiveExplorer : IAsyncDisposable
    {
        // Playwright's own EvaluateAsync<T> deserializer reflects over settable properties and
        // cannot populate UiModel.BoundingRectangle (a readonly struct with a constructor, no
        // setters) - observed to throw "Property set method not found." live against a real
        // Chromium page. Round-tripping through a JSON string and System.Text.Json (which
        // supports constructor-matched deserialization) sidesteps that, and matches how
        // PlaywrightApplicationConnector.ParseJson already deserializes DOM capture JSON.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        private readonly PlaywrightLiveExplorerOptions _options;

        // internal (not private) so ScenarioRunner can drive disposal with fake IPlaywright/
        // IBrowser instances - a faulting CloseAsync must still dispose the driver (#306).
        internal PlaywrightLiveExplorer(IPlaywright playwright, IBrowser browser, PlaywrightLiveExplorerOptions options)
        {
            _playwright = playwright;
            _browser = browser;
            _options = options;
        }

        public static async Task<PlaywrightLiveExplorer> LaunchAsync(PlaywrightLiveExplorerOptions? options = null)
        {
            var effectiveOptions = options ?? new PlaywrightLiveExplorerOptions();
            var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            try
            {
                var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = effectiveOptions.Headless,
                }).ConfigureAwait(false);
                return new PlaywrightLiveExplorer(playwright, browser, effectiveOptions);
            }
            catch
            {
                playwright.Dispose();
                throw;
            }
        }

        public async Task<WebElementInfo> CaptureAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("url must not be null or empty.", nameof(url));
            }

            var page = await _browser.NewPageAsync().ConfigureAwait(false);
            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    Timeout = _options.NavigationTimeoutMilliseconds,
                }).ConfigureAwait(false);

                var stringifyScript = $"() => JSON.stringify(({PlaywrightDomCaptureScript.JavaScript})())";
                var json = await page.EvaluateAsync<string>(stringifyScript).ConfigureAwait(false);
                var dom = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<WebElementInfo>(json, JsonOptions);
                if (dom == null)
                {
                    throw new InvalidOperationException($"DOM capture script returned no result for '{url}'.");
                }

                return dom;
            }
            finally
            {
                await page.CloseAsync().ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _browser.CloseAsync().ConfigureAwait(false);
            }
            catch
            {
                // Suppress browser close errors so Playwright driver disposal always completes.
            }
            finally
            {
                _playwright.Dispose();
            }
        }
    }
}
