using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;
using PlaywrightLiveExploration;

namespace ScenarioRunner
{
    // Pure-logic disposal tests for #306: hand-written fakes (the same convention the fake
    // ILlmHealingProvider implementations in MainFormScenarioTests/WpfMainWindowScenarioTests
    // use) drive PlaywrightLiveExplorer through its internal constructor, so no real browser
    // is launched. The behavior under test: a faulting IBrowser.CloseAsync must not prevent
    // IPlaywright.Dispose from running, and must not escape DisposeAsync.
    public class PlaywrightLiveExplorerDisposalTests
    {
        [Fact]
        public async Task DisposeAsync_DisposesPlaywrightDriver_WhenBrowserCloseThrows()
        {
            var playwright = new FakePlaywright();
            var browser = new FakeBrowser { CloseException = new InvalidOperationException("browser connection dropped") };
            var explorer = new PlaywrightLiveExplorer(playwright, browser, new PlaywrightLiveExplorerOptions());

            await explorer.DisposeAsync();

            Assert.True(playwright.Disposed);
        }

        [Fact]
        public async Task DisposeAsync_DisposesPlaywrightDriver_WhenBrowserCloseSucceeds()
        {
            var playwright = new FakePlaywright();
            var browser = new FakeBrowser();
            var explorer = new PlaywrightLiveExplorer(playwright, browser, new PlaywrightLiveExplorerOptions());

            await explorer.DisposeAsync();

            Assert.True(browser.CloseCalled);
            Assert.True(playwright.Disposed);
        }

        private sealed class FakePlaywright : IPlaywright
        {
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;

            public IBrowserType Chromium => throw new NotImplementedException();
            public IBrowserType Firefox => throw new NotImplementedException();
            public IBrowserType Webkit => throw new NotImplementedException();
            public ISelectors Selectors => throw new NotImplementedException();
            public IAPIRequest APIRequest => throw new NotImplementedException();
            public IBrowserType this[string browserType] => throw new NotImplementedException();
            public IReadOnlyDictionary<string, BrowserNewContextOptions> Devices => throw new NotImplementedException();
        }

        private sealed class FakeBrowser : IBrowser
        {
            public Exception? CloseException { get; set; }
            public bool CloseCalled { get; private set; }

            public Task CloseAsync(BrowserCloseOptions? options = null)
            {
                CloseCalled = true;
                if (CloseException != null)
                {
                    throw CloseException;
                }
                return Task.CompletedTask;
            }

            public event EventHandler<IBrowser>? Disconnected { add { } remove { } }
            public event EventHandler<IBrowser>? Close { add { } remove { } }
            public event EventHandler<IBrowserContext>? Context { add { } remove { } }

            public IBrowserType BrowserType => throw new NotImplementedException();
            public IReadOnlyList<IBrowserContext> Contexts => throw new NotImplementedException();
            public bool IsConnected => throw new NotImplementedException();
            public string Version => throw new NotImplementedException();
            public Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions? options = null) => throw new NotImplementedException();
            public Task<IPage> NewPageAsync(BrowserNewPageOptions? options = null) => throw new NotImplementedException();
            public Task<ICDPSession> NewBrowserCDPSessionAsync() => throw new NotImplementedException();
            public Task<BrowserBindResult> BindAsync(string name, BrowserBindOptions? options = null) => throw new NotImplementedException();
            public Task UnbindAsync() => throw new NotImplementedException();
            // default(ValueTask) is already completed; ValueTask.CompletedTask does not
            // exist on net48, which ScenarioRunner targets on Windows.
            public ValueTask DisposeAsync() => default(ValueTask);
        }
    }
}
