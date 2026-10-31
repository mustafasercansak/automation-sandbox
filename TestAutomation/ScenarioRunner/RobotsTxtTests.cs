using AutomationSandbox.PlaywrightLiveExploration;

namespace ScenarioRunner
{
    public class RobotsTxtTests
    {
        private const string Agent = "AutomationSandbox";

        [Fact]
        public void BlankOrMissingFile_AllowsEverything()
        {
            Assert.True(RobotsTxt.Parse(null).IsAllowed(Agent, "/admin"));
            Assert.True(RobotsTxt.Parse("  \n").IsAllowed(Agent, "/admin"));
            Assert.True(RobotsTxt.AllowAll.IsAllowed(Agent, "/anything?x=1"));
        }

        [Fact]
        public void DisallowIsAPrefixMatch_AndAnEmptyDisallowAllowsAll()
        {
            var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /admin\n");

            Assert.False(robots.IsAllowed(Agent, "/admin"));
            Assert.False(robots.IsAllowed(Agent, "/admin/users?id=3"));
            Assert.False(robots.IsAllowed(Agent, "/administrators-guide")); // a plain prefix: it still starts with /admin
            Assert.True(robots.IsAllowed(Agent, "/docs"));
            Assert.True(RobotsTxt.Parse("User-agent: *\nDisallow:\n").IsAllowed(Agent, "/admin"));
        }

        [Fact]
        public void TheLongestMatchWins_AndAllowWinsATie()
        {
            var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /private/\nAllow: /private/readme.html\nDisallow: /tie\nAllow: /tie\n");

            Assert.False(robots.IsAllowed(Agent, "/private/secret.html"));
            Assert.True(robots.IsAllowed(Agent, "/private/readme.html"));
            Assert.True(robots.IsAllowed(Agent, "/tie"));
        }

        [Fact]
        public void WildcardAndEndAnchor_AreSupported()
        {
            var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /*.pdf$\nDisallow: /search?*sort=\nDisallow: /*/drafts/\n");

            Assert.False(robots.IsAllowed(Agent, "/files/report.pdf"));
            Assert.True(robots.IsAllowed(Agent, "/files/report.pdf?download=1"));
            Assert.True(robots.IsAllowed(Agent, "/files/report.pdfx"));
            Assert.False(robots.IsAllowed(Agent, "/search?q=a&sort=asc"));
            Assert.True(robots.IsAllowed(Agent, "/search?q=a"));
            Assert.False(robots.IsAllowed(Agent, "/blog/drafts/one"));
        }

        [Fact]
        public void TheMostSpecificUserAgentGroupWins_AndStarOnlyAppliesWhenNoGroupNamesUs()
        {
            var robots = RobotsTxt.Parse(
                "User-agent: *\nDisallow: /\n\n" +
                "User-agent: automationsandbox\nDisallow: /only-us\n\n" +
                "User-agent: otherbot\nDisallow: /other\n");

            Assert.True(robots.IsAllowed("AutomationSandbox/1.0 (+https://example.test)", "/page"));
            Assert.False(robots.IsAllowed("AutomationSandbox/1.0", "/only-us"));
            Assert.False(robots.IsAllowed("SomeoneElse", "/page"));
        }

        [Fact]
        public void ConsecutiveUserAgentLines_ShareOneGroup()
        {
            var robots = RobotsTxt.Parse("User-agent: botone\nUser-agent: AutomationSandbox\nDisallow: /shared\n");

            Assert.False(robots.IsAllowed(Agent, "/shared"));
            Assert.False(robots.IsAllowed("botone", "/shared"));
        }

        [Fact]
        public void CommentsBlankLinesWindowsLineEndingsAndUnknownFieldsAreIgnored()
        {
            var robots = RobotsTxt.Parse("﻿# comment\r\nUser-agent: *   # everyone\r\nHost: example.test\r\nDisallow: /tmp # scratch\r\n\r\nnot a rule\r\n");

            Assert.False(robots.IsAllowed(Agent, "/tmp/file"));
            Assert.True(robots.IsAllowed(Agent, "/ok"));
        }

        [Fact]
        public void RobotsTxtItselfIsAlwaysAllowed()
        {
            Assert.True(RobotsTxt.Parse("User-agent: *\nDisallow: /\n").IsAllowed(Agent, "/robots.txt"));
        }

        [Fact]
        public void CrawlDelay_AppliesToTheMatchedGroup_AndBadValuesAreIgnored()
        {
            var robots = RobotsTxt.Parse("User-agent: *\nCrawl-delay: 2.5\n\nUser-agent: AutomationSandbox\nCrawl-delay: 7\n\nUser-agent: badbot\nCrawl-delay: -3\n");

            Assert.Equal(7, robots.GetCrawlDelaySeconds(Agent));
            Assert.Equal(2.5, robots.GetCrawlDelaySeconds("someone"));
            Assert.Null(robots.GetCrawlDelaySeconds("badbot"));
            Assert.Null(RobotsTxt.Parse("User-agent: *\nCrawl-delay: soon\n").GetCrawlDelaySeconds(Agent));
        }

        [Fact]
        public void SitemapLines_AreCollectedWherever_TheyAppear_AndNonHttpOnesAreDropped()
        {
            var robots = RobotsTxt.Parse("Sitemap: https://example.test/sitemap.xml\nUser-agent: *\nDisallow: /x\nSitemap: http://example.test/news.xml\nSitemap: ftp://example.test/no.xml\nSitemap: relative.xml\n");

            Assert.Equal(new[] { "https://example.test/sitemap.xml", "http://example.test/news.xml" }, robots.Sitemaps);
        }

        [Fact]
        public void AHostileWildcardPattern_DoesNotStallTheMatcher()
        {
            var robots = RobotsTxt.Parse("User-agent: *\nDisallow: " + string.Concat(System.Linq.Enumerable.Repeat("*a", 40)) + "b\n");
            var path = "/" + new string('a', 5000);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            robots.IsAllowed(Agent, path);

            Assert.True(watch.Elapsed < System.TimeSpan.FromSeconds(5), "matching must be bounded");
        }
    }
}
