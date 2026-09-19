using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;
using AutomationSandbox.WebDiscovery;

namespace AutomationSandbox.PlaywrightLiveExploration
{
    /// <summary>Owns a single Playwright page across multiple navigations, for authenticate-once /
    /// drive-many-actions usage that <see cref="PlaywrightLiveExplorer" />'s per-call page lifecycle does not
    /// support. Console messages, network responses, request failures, and uncaught page errors are recorded
    /// for the life of the session; dispose asynchronously to release browser resources.</summary>
    public sealed class PlaywrightWebSession : IAsyncDisposable
    {
        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        private readonly IBrowserContext _context;
        private readonly IPage _page;
        private readonly PlaywrightLiveExplorerOptions _options;
        private readonly object _observationsLock = new();
        private readonly List<WebConsoleMessage> _consoleMessages = new();
        private readonly List<WebNetworkResponse> _networkResponses = new();
        private readonly List<WebRequestFailure> _requestFailures = new();
        private readonly List<string> _pageErrors = new();

        // internal (not private) so tests can drive session disposal with fake Playwright types,
        // matching the PlaywrightLiveExplorer(IPlaywright, IBrowser, ...) convention.
        internal PlaywrightWebSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page, PlaywrightLiveExplorerOptions options)
        {
            _playwright = playwright;
            _browser = browser;
            _context = context;
            _page = page;
            _options = options;

            _page.Console += OnConsole;
            _page.Response += OnResponse;
            _page.RequestFailed += OnRequestFailed;
            _page.PageError += OnPageError;
        }

        /// <summary>Starts Playwright, launches a browser, and opens the one page the session will reuse
        /// across every subsequent navigation. When <paramref name="storageStatePath" /> is supplied, the
        /// browser context is pre-loaded with that previously saved <see cref="SaveStorageStateAsync" /> state
        /// (cookies and local storage), so a caller can authenticate once and reuse the result across later
        /// sessions instead of logging in again.</summary>
        public static async Task<PlaywrightWebSession> StartAsync(PlaywrightLiveExplorerOptions? options = null, string? storageStatePath = null)
        {
            var effectiveOptions = options ?? new PlaywrightLiveExplorerOptions();
            var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            try
            {
                var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = effectiveOptions.Headless,
                }).ConfigureAwait(false);
                try
                {
                    var context = await browser.NewContextAsync(new BrowserNewContextOptions
                    {
                        StorageStatePath = storageStatePath,
                    }).ConfigureAwait(false);
                    try
                    {
                        var page = await context.NewPageAsync().ConfigureAwait(false);
                        return new PlaywrightWebSession(playwright, browser, context, page, effectiveOptions);
                    }
                    catch
                    {
                        await context.CloseAsync().ConfigureAwait(false);
                        throw;
                    }
                }
                catch
                {
                    await browser.CloseAsync().ConfigureAwait(false);
                    throw;
                }
            }
            catch
            {
                playwright.Dispose();
                throw;
            }
        }

        /// <summary>Navigates the session's page to the supplied URL.</summary>
        public Task NavigateAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("url must not be null or empty.", nameof(url));
            }

            return _page.GotoAsync(url, new PageGotoOptions
            {
                Timeout = _options.NavigationTimeoutMilliseconds,
            });
        }

        /// <summary>Captures a DOM tree of the session's page at its current navigation state, bounded by
        /// <paramref name="discoveryOptions" /> (or <see cref="WebDiscoveryOptions.Default" /> when omitted).</summary>
        public Task<WebElementInfo> CaptureAsync(WebDiscoveryOptions? discoveryOptions = null)
        {
            return PlaywrightDomCapture.CaptureAsync(_page, discoveryOptions, _page.Url);
        }

        /// <summary>Fills the element matched by <paramref name="cssSelector" /> with <paramref name="value" />.</summary>
        public Task FillAsync(string cssSelector, string value)
        {
            return _page.Locator(cssSelector).FillAsync(value);
        }

        /// <summary>Clicks the element matched by <paramref name="cssSelector" />.</summary>
        public Task ClickAsync(string cssSelector)
        {
            return _page.Locator(cssSelector).ClickAsync();
        }

        /// <summary>Saves the session's current cookies and local storage to <paramref name="path" />, so a later
        /// <see cref="StartAsync" /> call can load it back and skip re-authenticating.</summary>
        public Task SaveStorageStateAsync(string path)
        {
            return _context.StorageStateAsync(new BrowserContextStorageStateOptions
            {
                Path = path,
            });
        }

        /// <summary>Selects the option with the given value in the &lt;select&gt; matched by <paramref name="cssSelector" />.</summary>
        public Task SelectAsync(string cssSelector, string value)
        {
            return _page.Locator(cssSelector).SelectOptionAsync(value);
        }

        /// <summary>Checks the checkbox or radio button matched by <paramref name="cssSelector" />.</summary>
        public Task CheckAsync(string cssSelector)
        {
            return _page.Locator(cssSelector).CheckAsync();
        }

        /// <summary>Unchecks the checkbox matched by <paramref name="cssSelector" />.</summary>
        public Task UncheckAsync(string cssSelector)
        {
            return _page.Locator(cssSelector).UncheckAsync();
        }

        /// <summary>Hovers the pointer over the element matched by <paramref name="cssSelector" />.</summary>
        public Task HoverAsync(string cssSelector)
        {
            return _page.Locator(cssSelector).HoverAsync();
        }

        /// <summary>Assigns <paramref name="filePath" /> to the file input matched by <paramref name="cssSelector" />.</summary>
        public Task UploadFileAsync(string cssSelector, string filePath)
        {
            return _page.Locator(cssSelector).SetInputFilesAsync(filePath);
        }

        /// <summary>Sends <paramref name="key" /> (Playwright key syntax, e.g. <c>"Enter"</c>) to the element matched
        /// by <paramref name="cssSelector" />.</summary>
        public Task PressKeyAsync(string cssSelector, string key)
        {
            return _page.Locator(cssSelector).PressAsync(key);
        }

        /// <summary>Waits for the element matched by <paramref name="cssSelector" /> to become visible, up to
        /// <paramref name="timeout" /> (or the session's navigation timeout when omitted).</summary>
        public Task WaitForVisibleAsync(string cssSelector, TimeSpan? timeout = null)
        {
            return _page.Locator(cssSelector).WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = (float?)(timeout ?? TimeSpan.FromMilliseconds(_options.NavigationTimeoutMilliseconds)).TotalMilliseconds,
            });
        }

        /// <summary>Whether the element matched by <paramref name="cssSelector" /> is currently visible.</summary>
        public Task<bool> IsVisibleAsync(string cssSelector)
        {
            return _page.Locator(cssSelector).IsVisibleAsync();
        }

        /// <summary>Whether the checkbox or radio button matched by <paramref name="cssSelector" /> is currently
        /// checked - the observable half of <see cref="CheckAsync" />/<see cref="UncheckAsync" />.</summary>
        public Task<bool> IsCheckedAsync(string cssSelector)
        {
            return _page.Locator(cssSelector).IsCheckedAsync();
        }

        /// <summary>The rendered text of the element matched by <paramref name="cssSelector" />, the same way
        /// <see cref="WebElementInfo.Text" /> is captured (<c>innerText</c>).</summary>
        public Task<string> GetTextAsync(string cssSelector)
        {
            return _page.Locator(cssSelector).InnerTextAsync();
        }

        /// <summary>The current value of the input, textarea, or select matched by <paramref name="cssSelector" />.</summary>
        public Task<string> GetValueAsync(string cssSelector)
        {
            return _page.Locator(cssSelector).InputValueAsync();
        }

        /// <summary>The session page's current URL.</summary>
        public string CurrentUrl => _page.Url;

        /// <summary>Every console message observed on the page since the session started.</summary>
        public IReadOnlyList<WebConsoleMessage> ConsoleMessages
        {
            get { lock (_observationsLock) { return _consoleMessages.ToArray(); } }
        }

        /// <summary>Every HTTP response observed on the page since the session started.</summary>
        public IReadOnlyList<WebNetworkResponse> NetworkResponses
        {
            get { lock (_observationsLock) { return _networkResponses.ToArray(); } }
        }

        /// <summary>Every request that failed outright (not merely an error status) since the session started.</summary>
        public IReadOnlyList<WebRequestFailure> RequestFailures
        {
            get { lock (_observationsLock) { return _requestFailures.ToArray(); } }
        }

        /// <summary>Every uncaught exception thrown by page script since the session started.</summary>
        public IReadOnlyList<string> PageErrors
        {
            get { lock (_observationsLock) { return _pageErrors.ToArray(); } }
        }

        /// <summary>Closes the owned page, context, and browser, and releases the Playwright session.</summary>
        public async ValueTask DisposeAsync()
        {
            _page.Console -= OnConsole;
            _page.Response -= OnResponse;
            _page.RequestFailed -= OnRequestFailed;
            _page.PageError -= OnPageError;

            try
            {
                await _context.CloseAsync().ConfigureAwait(false);
            }
            catch
            {
                // Suppress context close errors so browser/driver disposal always completes.
            }

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

        private void OnConsole(object? sender, IConsoleMessage message)
        {
            lock (_observationsLock)
            {
                _consoleMessages.Add(new WebConsoleMessage(message.Type, message.Text));
            }
        }

        private void OnResponse(object? sender, IResponse response)
        {
            lock (_observationsLock)
            {
                _networkResponses.Add(new WebNetworkResponse(response.Url, response.Status));
            }
        }

        private void OnRequestFailed(object? sender, IRequest request)
        {
            lock (_observationsLock)
            {
                _requestFailures.Add(new WebRequestFailure(request.Url, request.Failure));
            }
        }

        private void OnPageError(object? sender, string error)
        {
            lock (_observationsLock)
            {
                _pageErrors.Add(error);
            }
        }
    }
}
