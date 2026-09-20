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
    }
}
