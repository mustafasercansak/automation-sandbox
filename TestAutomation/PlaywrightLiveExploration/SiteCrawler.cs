using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.WebDiscovery;

namespace AutomationSandbox.PlaywrightLiveExploration
{
    /// <summary>Explores a site breadth-first from a single starting URL, using an already-started
    /// <see cref="PlaywrightWebSession" /> for navigation and DOM capture - so a caller with no per-page
    /// navigation script (an existing-suite scan, a content-quality sweep) can hand over just a URL instead of
    /// hand-authoring the path through every page. Whatever the session was started with - headless or headed,
    /// with or without a saved storage state - applies to every page the crawl visits, so an authenticated
    /// session (<see cref="PlaywrightWebSession.StartAsync" /> with <c>storageStatePath</c>) crawls behind login
    /// for free.</summary>
    public static class SiteCrawler
    {
        /// <summary>
        /// Visits <paramref name="startUrl" /> and, breadth-first, every same-origin link discovered from it,
        /// up to <paramref name="options" />'s bounds. <paramref name="onPageCaptured" /> is invoked with each
        /// visited URL and its captured DOM - the hook point for running content analysis, locator discovery,
        /// or any other per-page work, since <see cref="SiteCrawler" /> itself has no dependency on those
        /// packages. A page that fails to navigate or capture is recorded in
        /// <see cref="SiteCrawlResult.Failures" /> and the crawl continues with the rest of the queue.
        /// </summary>
        public static async Task<SiteCrawlResult> CrawlAsync(
            PlaywrightWebSession session,
            string startUrl,
            SiteCrawlOptions? options = null,
            Func<string, WebElementInfo, CancellationToken, Task>? onPageCaptured = null,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (string.IsNullOrWhiteSpace(startUrl))
            {
                throw new ArgumentException("startUrl must not be null or empty.", nameof(startUrl));
            }

            if (!Uri.TryCreate(startUrl, UriKind.Absolute, out var startUri))
            {
                throw new ArgumentException("startUrl must be an absolute URL.", nameof(startUrl));
            }

            var effectiveOptions = options ?? new SiteCrawlOptions();
            var result = new SiteCrawlResult();
            var queued = new HashSet<string>(StringComparer.Ordinal) { Normalize(startUri) };
            var queue = new Queue<(string Url, int Depth)>();
            queue.Enqueue((startUrl, 0));
            var previousAttemptFailed = false;

            var fetchText = effectiveOptions.TextFetcher ??
                ((url, ct) => SiteCrawlTextFetcher.FetchAsync(url, effectiveOptions.RobotsUserAgent, ct));
            var robots = RobotsTxt.AllowAll;
            var origin = startUri.GetLeftPart(UriPartial.Authority);
            if (effectiveOptions.RespectRobotsTxt || effectiveOptions.SeedFromSitemap)
            {
                robots = RobotsTxt.Parse(await fetchText(origin + "/robots.txt", cancellationToken).ConfigureAwait(false));
            }

            bool Blocked(Uri uri) => effectiveOptions.RespectRobotsTxt &&
                !robots.IsAllowed(effectiveOptions.RobotsUserAgent, uri.PathAndQuery);

            if (Blocked(startUri))
            {
                result.BlockedByRobotsUrls.Add(startUrl);
                return result;
            }

            var crawlDelay = effectiveOptions.RespectRobotsTxt
                ? robots.GetCrawlDelaySeconds(effectiveOptions.RobotsUserAgent)
                : null;
            if (effectiveOptions.SeedFromSitemap)
            {
                foreach (var seed in await ReadSitemapAsync(origin, robots, fetchText, cancellationToken).ConfigureAwait(false))
                {
                    if (!Uri.TryCreate(seed, UriKind.Absolute, out var seedUri) ||
                        (effectiveOptions.SameOriginOnly && !SameOrigin(startUri, seedUri)))
                    {
                        continue;
                    }

                    if (Blocked(seedUri))
                    {
                        result.BlockedByRobotsUrls.Add(seed);
                        continue;
                    }

                    if (queued.Add(Normalize(seedUri)))
                    {
                        queue.Enqueue((seed, 1));
                        result.SitemapSeededUrls.Add(seed);
                    }
                }
            }

            var attempts = 0;

            while (queue.Count > 0 && result.VisitedUrls.Count < effectiveOptions.MaxPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (url, depth) = queue.Dequeue();

                // A page immediately after a *different* queued page's failed navigation can itself throw
                // "interrupted by another navigation": Chromium is still settling onto its own internal
                // error page from that unrelated prior failure when this page's GotoAsync fires, since the
                // session reuses one IPage across every navigation by design. A brief pause before the next
                // attempt - only paid right after a failure, never on the common path - lets that settle
                // first instead of firing straight into it.
                if (previousAttemptFailed)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                if (attempts++ > 0 && crawlDelay > 0)
                {
                    var pause = Math.Min(crawlDelay.Value, effectiveOptions.MaxCrawlDelaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(pause), cancellationToken).ConfigureAwait(false);
                }

                WebElementInfo dom;
                try
                {
                    await session.NavigateAsync(url).ConfigureAwait(false);
                    dom = await session.CaptureAsync(effectiveOptions.DiscoveryOptions).ConfigureAwait(false);
                    previousAttemptFailed = false;
                }
                catch (Exception ex)
                {
                    result.Failures.Add(new SiteCrawlFailure { Url = url, Diagnostic = ex.Message });
                    previousAttemptFailed = true;
                    continue;
                }

                result.VisitedUrls.Add(url);

                if (onPageCaptured != null)
                {
                    await onPageCaptured(url, dom, cancellationToken).ConfigureAwait(false);
                }

                if (depth >= effectiveOptions.MaxDepth || result.VisitedUrls.Count >= effectiveOptions.MaxPages)
                {
                    continue;
                }

                var links = await session.GetLinksAsync().ConfigureAwait(false);
                foreach (var link in links)
                {
                    if (!Uri.TryCreate(link, UriKind.Absolute, out var linkUri))
                    {
                        continue;
                    }

                    if (effectiveOptions.SameOriginOnly && !SameOrigin(startUri, linkUri))
                    {
                        result.SkippedUrls.Add(link);
                        continue;
                    }

                    if (Blocked(linkUri))
                    {
                        if (!result.BlockedByRobotsUrls.Contains(link))
                        {
                            result.BlockedByRobotsUrls.Add(link);
                        }

                        continue;
                    }

                    var normalized = Normalize(linkUri);
                    if (!queued.Add(normalized))
                    {
                        continue;
                    }

                    queue.Enqueue((link, depth + 1));
                }
            }

            return result;
        }

        private const int MaxSitemapDocuments = 10;
        private const int MaxSitemapUrls = 1000;

        // Sitemap files are untrusted: a bounded number of documents and URLs, and a file that cannot be
        // fetched or parsed is skipped instead of failing the crawl.
        private static async Task<List<string>> ReadSitemapAsync(
            string origin,
            RobotsTxt robots,
            Func<string, CancellationToken, Task<string?>> fetchText,
            CancellationToken cancellationToken)
        {
            var pending = new Queue<string>(robots.Sitemaps.Count > 0 ? robots.Sitemaps : new[] { origin + "/sitemap.xml" });
            var fetched = new HashSet<string>(StringComparer.Ordinal);
            var urls = new List<string>();
            while (pending.Count > 0 && fetched.Count < MaxSitemapDocuments && urls.Count < MaxSitemapUrls)
            {
                var sitemapUrl = pending.Dequeue();
                if (!fetched.Add(sitemapUrl))
                {
                    continue;
                }

                var document = SitemapParser.Parse(await fetchText(sitemapUrl, cancellationToken).ConfigureAwait(false));
                foreach (var child in document.SitemapUrls)
                {
                    pending.Enqueue(child);
                }

                foreach (var page in document.PageUrls)
                {
                    if (urls.Count >= MaxSitemapUrls)
                    {
                        break;
                    }

                    urls.Add(page);
                }
            }

            return urls;
        }

        private static bool SameOrigin(Uri a, Uri b)
        {
            return string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase);
        }

        // Fragments (#section) identify a position within a page, not a different page - without this, a
        // same-page anchor link would queue as if it were new content to visit.
        private static string Normalize(Uri uri)
        {
            return uri.GetLeftPart(UriPartial.Query);
        }
    }
}
