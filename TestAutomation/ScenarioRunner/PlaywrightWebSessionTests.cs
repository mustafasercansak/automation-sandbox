using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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

        [Fact]
        public async Task FillAsync_And_ClickAsync_DriveARealFormSubmission()
        {
            var htmlPath = WriteTempHtml("PlaywrightWebSessionTests_Form", """
                <input data-testid="name-input" />
                <button data-testid="submit-button">Submit</button>
                <div data-testid="result"></div>
                <script>
                    document.querySelector('[data-testid=submit-button]').addEventListener('click', () => {
                        const name = document.querySelector('[data-testid=name-input]').value;
                        document.querySelector('[data-testid=result]').textContent = 'Hello, ' + name;
                    });
                </script>
                """);
            try
            {
                await using var session = await PlaywrightWebSession.StartAsync();
                await session.NavigateAsync(new Uri(htmlPath).AbsoluteUri);

                await session.FillAsync("[data-testid='name-input']", "Ada");
                await session.ClickAsync("[data-testid='submit-button']");

                var dom = await session.CaptureAsync();
                var result = Flatten(dom).Single(e => e.TestId == "result");
                Assert.Equal("Hello, Ada", result.Text);
            }
            finally
            {
                DeleteIfExists(htmlPath);
            }
        }

        [Fact]
        public async Task SaveStorageStateAsync_PersistsAcrossSessions_NewSessionStartsAlreadyAuthenticated()
        {
            const string loginFixtureHtml = """
                <!doctype html>
                <html>
                <body>
                    <input data-testid="username" />
                    <button data-testid="login-button">Log in</button>
                    <div data-testid="token-display"></div>
                    <script>
                        document.querySelector('[data-testid=login-button]').addEventListener('click', () => {
                            const username = document.querySelector('[data-testid=username]').value;
                            localStorage.setItem('authToken', 'token-for-' + username);
                        });
                        document.querySelector('[data-testid=token-display]').textContent = localStorage.getItem('authToken') || 'anonymous';
                    </script>
                </body>
                </html>
                """;

            // Playwright's storage state only tracks localStorage for real http(s) origins (confirmed
            // empirically: a file:// page always round-trips as "origins": []), so persistence needs an
            // actual HTTP server, not a file:// fixture like the rest of this file.
            using var server = new FixtureHttpServer(loginFixtureHtml);
            var statePath = Path.Combine(Path.GetTempPath(), "PlaywrightWebSessionTests_StorageState_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                await using (var loginSession = await PlaywrightWebSession.StartAsync())
                {
                    await loginSession.NavigateAsync(server.BaseUrl);
                    await loginSession.FillAsync("[data-testid='username']", "ada");
                    await loginSession.ClickAsync("[data-testid='login-button']");

                    await loginSession.SaveStorageStateAsync(statePath);
                }

                await using var restoredSession = await PlaywrightWebSession.StartAsync(storageStatePath: statePath);
                await restoredSession.NavigateAsync(server.BaseUrl);

                var dom = await restoredSession.CaptureAsync();
                var tokenDisplay = Flatten(dom).Single(e => e.TestId == "token-display");
                Assert.Equal("token-for-ada", tokenDisplay.Text);
            }
            finally
            {
                if (File.Exists(statePath))
                {
                    File.Delete(statePath);
                }
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

        // Minimal loopback-only HTTP server for the one test that needs a real http:// origin
        // (storage state round-tripping - see SaveStorageStateAsync_PersistsAcrossSessions...).
        // Serves the same fixture HTML for every request; no routing needed.
        private sealed class FixtureHttpServer : IDisposable
        {
            private readonly HttpListener _listener;
            private readonly CancellationTokenSource _cts = new();

            public string BaseUrl { get; }

            public FixtureHttpServer(string html)
            {
                var port = GetFreeTcpPort();
                BaseUrl = $"http://127.0.0.1:{port}/";
                _listener = new HttpListener();
                _listener.Prefixes.Add(BaseUrl);
                _listener.Start();
                _ = ServeLoopAsync(html);
            }

            private async Task ServeLoopAsync(string html)
            {
                var bytes = Encoding.UTF8.GetBytes(html);
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

                    context.Response.ContentType = "text/html";
                    context.Response.ContentLength64 = bytes.Length;
                    // net48 has no Stream.WriteAsync(byte[]) overload - only the (buffer, offset, count) one.
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    context.Response.OutputStream.Close();
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

            public void Dispose()
            {
                _cts.Cancel();
                _listener.Stop();
                _listener.Close();
            }
        }
    }
}
