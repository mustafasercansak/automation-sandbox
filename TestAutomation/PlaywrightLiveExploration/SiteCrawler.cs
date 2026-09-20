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

                WebElementInfo dom;
                try
                {
                    await session.NavigateAsync(url).ConfigureAwait(false);
                    dom = await session.CaptureAsync().ConfigureAwait(false);
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
