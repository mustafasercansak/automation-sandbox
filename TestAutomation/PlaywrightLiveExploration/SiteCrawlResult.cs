using System.Collections.Generic;

namespace AutomationSandbox.PlaywrightLiveExploration
{
    /// <summary>Outcome of one <see cref="SiteCrawler.CrawlAsync" /> run.</summary>
    public sealed class SiteCrawlResult
    {
        /// <summary>URLs actually navigated to and captured, in visit order.</summary>
        public List<string> VisitedUrls { get; set; } = new List<string>();
        /// <summary>Discovered links that were not visited - off-origin when
        /// <see cref="SiteCrawlOptions.SameOriginOnly" /> is true, or beyond the configured bounds.</summary>
        public List<string> SkippedUrls { get; set; } = new List<string>();
        /// <summary>Pages where navigation or capture threw; the crawl continues past a single broken page
        /// rather than aborting the whole run, so a site with one dead link doesn't lose every other page's
        /// findings.</summary>
        public List<SiteCrawlFailure> Failures { get; set; } = new List<SiteCrawlFailure>();
    }

    /// <summary>One page the crawler could not navigate to or capture.</summary>
    public sealed class SiteCrawlFailure
    {
        /// <summary>URL the crawler was attempting to visit.</summary>
        public string Url { get; set; } = "";
        /// <summary>Exception message captured from the failed navigation or capture.</summary>
        public string Diagnostic { get; set; } = "";
    }
}
