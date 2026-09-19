using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Playwright;
using AutomationSandbox.PlaywrightLiveExploration;
using AutomationSandbox.WebDiscovery;

namespace ScenarioRunner
{
    // Real headless Chromium against local file:// fixtures, same convention as
    // PlaywrightLiveExplorerTests - no mocked browser, no network access needed.
    public class PlaywrightWebSessionTests
    {
        [Fact]
        public async Task NavigateAsync_ReusesTheSamePage_CaptureAsyncReflectsWhicheverPageIsCurrent()
        {
            var pageAPath = WriteTempHtml("PlaywrightWebSessionTests_PageA", "<button data-testid=\"page-a-marker\">A</button>");
            var pageBPath = WriteTempHtml("PlaywrightWebSessionTests_PageB", "<button data-testid=\"page-b-marker\">B</button>");
            try
            {
                await using var session = await PlaywrightWebSession.StartAsync();

                await session.NavigateAsync(new Uri(pageAPath).AbsoluteUri);
                var domA = await session.CaptureAsync();
                Assert.Contains(Flatten(domA), e => e.TestId == "page-a-marker");
                Assert.DoesNotContain(Flatten(domA), e => e.TestId == "page-b-marker");

                await session.NavigateAsync(new Uri(pageBPath).AbsoluteUri);
                var domB = await session.CaptureAsync();
                Assert.Contains(Flatten(domB), e => e.TestId == "page-b-marker");
                Assert.DoesNotContain(Flatten(domB), e => e.TestId == "page-a-marker");
            }
            finally
            {
                DeleteIfExists(pageAPath);
                DeleteIfExists(pageBPath);
            }
        }

        [Fact]
        public async Task NavigateAsync_RejectsNullOrEmptyUrl()
        {
            await using var session = await PlaywrightWebSession.StartAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => session.NavigateAsync(""));
        }

        [Fact]
        public async Task Session_RecordsConsoleMessagesAndPageErrors_FromScriptRunOnNavigation()
        {
            var htmlPath = WriteTempHtml(
                "PlaywrightWebSessionTests_ConsoleAndError",
                "<script>console.warn('hello-warn'); throw new Error('boom');</script>");
            try
            {
                await using var session = await PlaywrightWebSession.StartAsync();

                await session.NavigateAsync(new Uri(htmlPath).AbsoluteUri);

                Assert.Contains(session.ConsoleMessages, m => m.MessageType == "warning" && m.Text == "hello-warn");
                Assert.Contains(session.PageErrors, e => e.Contains("boom"));
            }
            finally
            {
                DeleteIfExists(htmlPath);
            }
        }

        [Fact]
        public async Task Session_RecordsNetworkResponse_ForTheNavigatedPage()
        {
            var htmlPath = WriteTempHtml("PlaywrightWebSessionTests_Response", "<p>hi</p>");
            try
            {
                await using var session = await PlaywrightWebSession.StartAsync();
                var url = new Uri(htmlPath).AbsoluteUri;

                await session.NavigateAsync(url);

                Assert.Contains(session.NetworkResponses, r => r.Url == url && r.Status == 200);
            }
            finally
            {
                DeleteIfExists(htmlPath);
            }
        }

        [Fact]
        public async Task Session_RecordsRequestFailure_WhenNavigationTargetRefusesConnection()
        {
            // 127.0.0.1:65500 is loopback-only and not in Chromium's blocked-port list, so this
            // deterministically fails fast with ERR_CONNECTION_REFUSED - no external network needed.
            const string url = "http://127.0.0.1:65500/nope";
            await using var session = await PlaywrightWebSession.StartAsync();

            await Assert.ThrowsAsync<PlaywrightException>(() => session.NavigateAsync(url));

            Assert.Contains(session.RequestFailures, r => r.Url == url);
        }

        [Fact]
        public async Task DisposeAsync_ClosesTheSessionWithoutThrowing()
        {
            var session = await PlaywrightWebSession.StartAsync();

            await session.DisposeAsync();
        }

        private static string WriteTempHtml(string prefix, string bodyHtml)
        {
            var path = Path.Combine(Path.GetTempPath(), prefix + "_" + Guid.NewGuid().ToString("N") + ".html");
            File.WriteAllText(path, $"<!doctype html><html><body>{bodyHtml}</body></html>");
            return path;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static System.Collections.Generic.IEnumerable<WebElementInfo> Flatten(WebElementInfo root)
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
