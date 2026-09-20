using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.PlaywrightLiveExploration;
using AutomationSandbox.WebDiscovery;

namespace ScenarioRunner
{
    // Real headless Chromium against a local loopback HTTP server, same convention as
    // PlaywrightWebSessionTests - no mocked browser, no external network access needed.
    // file:// fixtures (used by most other Playwright tests here) don't work for this file: GetLinksAsync
    // deliberately only returns http(s) links (excluding mailto:/javascript:/tel:), so a real http:// origin
    // is required to exercise multi-page link-following at all.
    public class SiteCrawlerTests
    {
        [Fact]
        public async Task CrawlAsync_VisitsStartPageAndLinkedPagesBreadthFirst()
        {
            var dir = CreateTempDir();
            using var server = new DirectoryHttpServer(dir);
            try
            {
                WriteHtml(dir, "a.html", "<h1 data-testid=\"marker\">A</h1><a href=\"b.html\">to b</a>");
                WriteHtml(dir, "b.html", "<h1 data-testid=\"marker\">B</h1><a href=\"c.html\">to c</a>");
                WriteHtml(dir, "c.html", "<h1 data-testid=\"marker\">C</h1>");
                await using var session = await PlaywrightWebSession.StartAsync();

                var result = await SiteCrawler.CrawlAsync(session, server.BaseUrl + "a.html");

                Assert.Equal(3, result.VisitedUrls.Count);
                Assert.Equal(server.BaseUrl + "a.html", result.VisitedUrls[0]);
                Assert.Contains(result.VisitedUrls, u => u == server.BaseUrl + "b.html");
                Assert.Contains(result.VisitedUrls, u => u == server.BaseUrl + "c.html");
                Assert.Empty(result.Failures);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Fact]
        public async Task CrawlAsync_DoesNotRevisitAPageReachableByACycle()
        {
            var dir = CreateTempDir();
            using var server = new DirectoryHttpServer(dir);
            try
            {
                WriteHtml(dir, "a.html", "<a href=\"b.html\">to b</a>");
                WriteHtml(dir, "b.html", "<a href=\"a.html\">back to a</a>");
                await using var session = await PlaywrightWebSession.StartAsync();

                var result = await SiteCrawler.CrawlAsync(session, server.BaseUrl + "a.html");

                Assert.Equal(2, result.VisitedUrls.Count);
                Assert.Equal(1, result.VisitedUrls.Count(u => u == server.BaseUrl + "a.html"));
                Assert.Equal(1, result.VisitedUrls.Count(u => u == server.BaseUrl + "b.html"));
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Fact]
        public async Task CrawlAsync_StopsAtMaxPages()
        {
            var dir = CreateTempDir();
            using var server = new DirectoryHttpServer(dir);
            try
            {
                WriteHtml(dir, "a.html", "<a href=\"b.html\">b</a><a href=\"c.html\">c</a><a href=\"d.html\">d</a>");
                WriteHtml(dir, "b.html", "");
                WriteHtml(dir, "c.html", "");
                WriteHtml(dir, "d.html", "");
                await using var session = await PlaywrightWebSession.StartAsync();

                var result = await SiteCrawler.CrawlAsync(session, server.BaseUrl + "a.html", new SiteCrawlOptions { MaxPages = 2 });

                Assert.Equal(2, result.VisitedUrls.Count);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Fact]
        public async Task CrawlAsync_StopsAtMaxDepth()
        {
            var dir = CreateTempDir();
            using var server = new DirectoryHttpServer(dir);
            try
            {
                WriteHtml(dir, "a.html", "<a href=\"b.html\">to b</a>");
                WriteHtml(dir, "b.html", "<a href=\"c.html\">to c</a>");
                WriteHtml(dir, "c.html", "");
                await using var session = await PlaywrightWebSession.StartAsync();

                // Depth 0 = a.html, depth 1 = b.html; c.html would be depth 2 and must not be visited.
                var result = await SiteCrawler.CrawlAsync(session, server.BaseUrl + "a.html", new SiteCrawlOptions { MaxDepth = 1, MaxPages = 100 });

                Assert.Equal(2, result.VisitedUrls.Count);
                Assert.DoesNotContain(result.VisitedUrls, u => u == server.BaseUrl + "c.html");
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Fact]
        public async Task CrawlAsync_SkipsOffOriginLinks_InsteadOfVisitingThem()
        {
            var dir = CreateTempDir();
            using var server = new DirectoryHttpServer(dir);
            try
            {
                // Never actually reachable - proves the crawler categorizes by URI comparison alone and
                // never attempts to navigate here (the test would hang/time out on a real network fetch
                // otherwise, since there is no such host).
                WriteHtml(dir, "a.html", "<a href=\"https://example.invalid/off-origin\">external</a>");
                await using var session = await PlaywrightWebSession.StartAsync();

                var result = await SiteCrawler.CrawlAsync(session, server.BaseUrl + "a.html");

                Assert.Single(result.VisitedUrls);
                Assert.Contains(result.SkippedUrls, u => u == "https://example.invalid/off-origin");
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Fact]
        public async Task CrawlAsync_InvokesCallbackWithEachVisitedUrlAndItsDom()
        {
            var dir = CreateTempDir();
            using var server = new DirectoryHttpServer(dir);
            try
            {
                WriteHtml(dir, "a.html", "<h1 data-testid=\"marker\">Page A</h1><a href=\"b.html\">to b</a>");
                WriteHtml(dir, "b.html", "<h1 data-testid=\"marker\">Page B</h1>");
                await using var session = await PlaywrightWebSession.StartAsync();
                var captured = new Dictionary<string, string>();

                await SiteCrawler.CrawlAsync(session, server.BaseUrl + "a.html", onPageCaptured: (url, dom, _) =>
                {
                    var marker = Flatten(dom).First(e => e.TestId == "marker");
                    captured[url] = marker.Text;
                    return Task.CompletedTask;
                });

                Assert.Equal("Page A", captured[server.BaseUrl + "a.html"]);
                Assert.Equal("Page B", captured[server.BaseUrl + "b.html"]);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Fact]
        public async Task CrawlAsync_RecordsAFailureAndContinues_WhenOnePageCannotBeReached()
        {
            var dir = CreateTempDir();
            using var server = new DirectoryHttpServer(dir);
            var deadPort = GetFreeTcpPort(); // free at snapshot time, nothing ever binds to it below
            try
            {
                WriteHtml(dir, "a.html",
                    $"<a href=\"http://127.0.0.1:{deadPort}/unreachable.html\">broken</a><a href=\"b.html\">to b</a>");
                WriteHtml(dir, "b.html", "");
                await using var session = await PlaywrightWebSession.StartAsync();

                var result = await SiteCrawler.CrawlAsync(session, server.BaseUrl + "a.html");

                Assert.Contains(result.Failures, f => f.Url == $"http://127.0.0.1:{deadPort}/unreachable.html");
                Assert.Contains(result.VisitedUrls, u => u == server.BaseUrl + "b.html");
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Fact]
        public async Task CrawlAsync_RejectsNullSession()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => SiteCrawler.CrawlAsync(null!, "https://example.test"));
        }

        [Fact]
        public async Task CrawlAsync_RejectsNonAbsoluteStartUrl()
        {
            await using var session = await PlaywrightWebSession.StartAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => SiteCrawler.CrawlAsync(session, "relative/path"));
        }

        [Fact]
        public async Task GetLinksAsync_ReturnsAbsoluteHttpLinks_DeduplicatedAndInDocumentOrder()
        {
            var dir = CreateTempDir();
            using var server = new DirectoryHttpServer(dir);
            try
            {
                WriteHtml(dir, "a.html",
                    "<a href=\"b.html\">b</a>" +
                    "<a href=\"b.html\">b again</a>" +
                    "<a href=\"mailto:test@example.com\">mail</a>" +
                    "<a href=\"javascript:void(0)\">js</a>" +
                    "<a href=\"c.html\">c</a>");
                WriteHtml(dir, "b.html", "");
                WriteHtml(dir, "c.html", "");
                await using var session = await PlaywrightWebSession.StartAsync();
                await session.NavigateAsync(server.BaseUrl + "a.html");

                var links = await session.GetLinksAsync();

                Assert.Equal(new[] { server.BaseUrl + "b.html", server.BaseUrl + "c.html" }, links);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Fact]
        public async Task WaitForTextChangeAsync_ResolvesOnceTheElementsTextActuallyChanges()
        {
            // A button click (synchronous onclick mutation) rather than a setTimeout: the change must not
            // fire before "before" is captured, and a timer race would make that nondeterministic.
            var path = WriteTempHtml(
                "SiteCrawlerTests_TextChange",
                "<h1 id=\"greeting\">Hello</h1>" +
                "<button id=\"switch-language\" onclick=\"document.getElementById('greeting').textContent = 'Merhaba'\">switch</button>");
            try
            {
                await using var session = await PlaywrightWebSession.StartAsync();
                await session.NavigateAsync(new Uri(path).AbsoluteUri);
                var before = await session.GetTextAsync("#greeting");

                await session.ClickAsync("#switch-language");
                await session.WaitForTextChangeAsync("#greeting", before, TimeSpan.FromSeconds(5));

                var after = await session.GetTextAsync("#greeting");
                Assert.Equal("Hello", before);
                Assert.Equal("Merhaba", after);
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Fact]
        public async Task WaitForTextChangeAsync_TimesOut_WhenTextNeverChanges()
        {
            var path = WriteTempHtml("SiteCrawlerTests_NoChange", "<h1 id=\"greeting\">Hello</h1>");
            try
            {
                await using var session = await PlaywrightWebSession.StartAsync();
                await session.NavigateAsync(new Uri(path).AbsoluteUri);

                await Assert.ThrowsAsync<TimeoutException>(
                    () => session.WaitForTextChangeAsync("#greeting", "Hello", TimeSpan.FromMilliseconds(300)));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Fact]
        public async Task WaitForTextAsync_Exact_ResolvesOnceTextEqualsTheExpectedValue()
        {
            var path = WriteTempHtml(
                "SiteCrawlerTests_TextEquals",
                "<h1 id=\"greeting\">Hello</h1>" +
                "<button id=\"switch-language\" onclick=\"document.getElementById('greeting').textContent = 'Merhaba'\">switch</button>");
            try
            {
                await using var session = await PlaywrightWebSession.StartAsync();
                await session.NavigateAsync(new Uri(path).AbsoluteUri);

                await session.ClickAsync("#switch-language");
                await session.WaitForTextAsync("#greeting", "Merhaba", exact: true, TimeSpan.FromSeconds(5));

                Assert.Equal("Merhaba", await session.GetTextAsync("#greeting"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Fact]
        public async Task WaitForTextAsync_Exact_TimesOut_WhenTextNeverEqualsTheExpectedValue()
        {
            var path = WriteTempHtml("SiteCrawlerTests_TextEqualsTimeout", "<h1 id=\"greeting\">Hello</h1>");
            try
            {
                await using var session = await PlaywrightWebSession.StartAsync();
                await session.NavigateAsync(new Uri(path).AbsoluteUri);

                await Assert.ThrowsAsync<TimeoutException>(
                    () => session.WaitForTextAsync("#greeting", "Merhaba", exact: true, TimeSpan.FromMilliseconds(300)));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Fact]
        public async Task WaitForTextAsync_Contains_ResolvesOnceTextContainsTheExpectedSubstring()
        {
            var path = WriteTempHtml(
                "SiteCrawlerTests_TextContains",
                "<h1 id=\"greeting\">Hello</h1>" +
                "<button id=\"switch-language\" onclick=\"document.getElementById('greeting').textContent = 'Merhaba, world'\">switch</button>");
            try
            {
                await using var session = await PlaywrightWebSession.StartAsync();
                await session.NavigateAsync(new Uri(path).AbsoluteUri);

                await session.ClickAsync("#switch-language");
                await session.WaitForTextAsync("#greeting", "world", exact: false, TimeSpan.FromSeconds(5));

                Assert.Equal("Merhaba, world", await session.GetTextAsync("#greeting"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        private static string CreateTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "SiteCrawlerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteHtml(string dir, string fileName, string bodyHtml)
        {
            File.WriteAllText(Path.Combine(dir, fileName), $"<!doctype html><html><body>{bodyHtml}</body></html>");
        }

        private static void DeleteDir(string dir)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
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

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        // Minimal loopback-only static file server: serves a temp directory's files by request path, so
        // SiteCrawler's link-following can be exercised against a real http:// origin without external
        // network access. GetLinksAsync only returns http(s) links by design, so file:// fixtures (used
        // elsewhere in this suite) can't exercise multi-page crawling at all.
        private sealed class DirectoryHttpServer : IDisposable
        {
            private readonly HttpListener _listener;
            private readonly CancellationTokenSource _cts = new();
            private readonly string _directory;

            public string BaseUrl { get; }

            public DirectoryHttpServer(string directory)
            {
                _directory = directory;
                var port = GetFreeTcpPort();
                BaseUrl = $"http://127.0.0.1:{port}/";
                _listener = new HttpListener();
                _listener.Prefixes.Add(BaseUrl);
                _listener.Start();
                _ = ServeLoopAsync();
            }

            private async Task ServeLoopAsync()
            {
                while (!_cts.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        return;
                    }

                    var relativePath = context.Request.Url!.AbsolutePath.TrimStart('/');
                    var filePath = Path.Combine(_directory, relativePath);
                    if (File.Exists(filePath))
                    {
                        var bytes = File.ReadAllBytes(filePath);
                        context.Response.ContentType = "text/html";
                        context.Response.ContentLength64 = bytes.Length;
                        // net48 has no Stream.WriteAsync(byte[]) overload - only the (buffer, offset, count) one.
                        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                    }

                    context.Response.OutputStream.Close();
                }
            }

            public void Dispose()
            {
                _cts.Cancel();
                _listener.Stop();
                _listener.Close();
            }
        }
    }
}
