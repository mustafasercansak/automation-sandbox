using System;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.WebDiscovery;

namespace AutomationSandbox.PlaywrightLiveExploration
{
    /// <summary>Bounds and scope for <see cref="SiteCrawler.CrawlAsync" />, the same bounded-traversal
    /// philosophy <c>WebDiscoveryOptions</c> applies to a single-page DOM capture - a large or malformed site
    /// truncates gracefully instead of crawling indefinitely.</summary>
    public sealed class SiteCrawlOptions
    {
        /// <summary>Maximum number of pages to visit before the crawl stops, including the start page. Default 20.</summary>
        public int MaxPages { get; set; } = 20;
        /// <summary>Maximum link-follow depth from the start page (0 = only the start page itself). Default 3.</summary>
        public int MaxDepth { get; set; } = 3;
        /// <summary>When true (the default), only links sharing the start URL's scheme and host are followed;
        /// off-origin links are recorded in <see cref="SiteCrawlResult.SkippedUrls" /> instead of visited.</summary>
        public bool SameOriginOnly { get; set; } = true;
        /// <summary>Capture options applied to every crawled page, for example a larger
        /// <see cref="WebDiscoveryOptions.MaxTextLength" /> when the crawl feeds a text check. Null uses the
        /// capture defaults.</summary>
        public WebDiscoveryOptions? DiscoveryOptions { get; set; }
        /// <summary>When true, the crawl fetches the start origin's <c>robots.txt</c> once and never visits a URL it
        /// disallows for <see cref="RobotsUserAgent" />; blocked URLs are listed in
        /// <see cref="SiteCrawlResult.BlockedByRobotsUrls" />, and a <c>Crawl-delay</c> is honoured up to
        /// <see cref="MaxCrawlDelaySeconds" />. A missing or unreadable <c>robots.txt</c> allows everything. Default
        /// false, so existing crawls behave exactly as before; turn it on before pointing the crawler at a site you
        /// do not own.</summary>
        public bool RespectRobotsTxt { get; set; }
        /// <summary>The name matched against <c>User-agent</c> groups and sent when fetching <c>robots.txt</c> and
        /// sitemaps. Default <c>AutomationSandbox</c>.</summary>
        public string RobotsUserAgent { get; set; } = "AutomationSandbox";
        /// <summary>Longest pause, in seconds, the crawl takes between two pages to honour a <c>Crawl-delay</c>; a
        /// larger value in <c>robots.txt</c> is capped to this. Default 10.</summary>
        public double MaxCrawlDelaySeconds { get; set; } = 10;
        /// <summary>When true, pages listed in the site's sitemap (the <c>Sitemap:</c> lines of <c>robots.txt</c>, or
        /// <c>/sitemap.xml</c> when there are none) are queued after the start page, so pages no link reaches are
        /// still visited. They count as one link away from the start page, are still bound by
        /// <see cref="MaxPages" /> and <see cref="SameOriginOnly" />, and obey <see cref="RespectRobotsTxt" />. At
        /// most 10 sitemap files and 1,000 URLs are read. Default false.</summary>
        public bool SeedFromSitemap { get; set; }
        /// <summary>Fetches the text of <c>robots.txt</c> and sitemap URLs and returns null when it cannot (any
        /// non-success status, timeout, or a response over 2 MB). Null uses a built-in HTTP client. Tests supply
        /// their own so they need no network.</summary>
        public Func<string, CancellationToken, Task<string?>>? TextFetcher { get; set; }
    }
}
