using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using AutomationSandbox.WebDiscovery;

namespace AutomationSandbox.PlaywrightLiveExploration
{
    // Closes the gap the "MCP Exploration" docs previously described as Planned: instead of
    // requiring a hand-written Playwright test that calls
    // page.EvaluateAsync<WebElementInfo>(PlaywrightDomCaptureScript.JavaScript) itself, this
    // class owns that browser lifecycle directly via the Microsoft.Playwright .NET SDK - no
    // Model Context Protocol, no Node.js process, no new external tool dependency. See
    // docs/intent-driven-automation.md for why a real MCP bridge (Node.js-based Playwright MCP
    // server) was ruled out for this project.

    /// <summary>Owns a Playwright browser session for navigation and DOM capture; dispose asynchronously to release browser resources.</summary>
    public sealed class PlaywrightLiveExplorer : IAsyncDisposable
    {
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

        /// <summary>Starts Playwright and launches a browser using the supplied options.</summary>
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

        /// <summary>Navigates to the supplied URL and returns a captured DOM tree, bounded by
        /// <paramref name="discoveryOptions" /> (or <see cref="WebDiscoveryOptions.Default" /> when omitted) so a
        /// large SPA or data-grid page cannot produce an unbounded JSON payload over the Playwright protocol;
        /// check <see cref="WebElementInfo.HitMaxDepth" />, <see cref="WebElementInfo.HitMaxElements" />, and
        /// <see cref="WebElementInfo.TimedOut" /> on the result to detect a truncated capture.</summary>
        public async Task<WebElementInfo> CaptureAsync(string url, WebDiscoveryOptions? discoveryOptions = null)
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

                return await PlaywrightDomCapture.CaptureAsync(page, discoveryOptions, url).ConfigureAwait(false);
            }
            finally
            {
                await page.CloseAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Closes the owned browser and releases the Playwright session.</summary>
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
