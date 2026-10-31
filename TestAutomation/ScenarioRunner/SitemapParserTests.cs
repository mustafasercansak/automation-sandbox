using AutomationSandbox.PlaywrightLiveExploration;

namespace ScenarioRunner
{
    public class SitemapParserTests
    {
        [Fact]
        public void UrlSet_ListsPageUrlsInOrderWithoutDuplicates()
        {
            var document = SitemapParser.Parse(
                "<?xml version=\"1.0\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
                "<url><loc>https://example.test/a</loc><lastmod>2026-01-01</lastmod></url>" +
                "<url><loc> https://example.test/b </loc></url>" +
                "<url><loc>https://example.test/a</loc></url></urlset>");

            Assert.Equal(new[] { "https://example.test/a", "https://example.test/b" }, document.PageUrls);
            Assert.Empty(document.SitemapUrls);
        }

        [Fact]
        public void SitemapIndex_ListsChildSitemaps()
        {
            var document = SitemapParser.Parse(
                "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
                "<sitemap><loc>https://example.test/s1.xml</loc></sitemap><sitemap><loc>https://example.test/s2.xml</loc></sitemap></sitemapindex>");

            Assert.Equal(new[] { "https://example.test/s1.xml", "https://example.test/s2.xml" }, document.SitemapUrls);
            Assert.Empty(document.PageUrls);
        }

        [Fact]
        public void OnlyAbsoluteHttpUrlsAreKept()
        {
            var document = SitemapParser.Parse(
                "<urlset><url><loc>/relative</loc></url><url><loc>ftp://example.test/f</loc></url><url><loc></loc></url><url/>" +
                "<url><loc>http://example.test/ok</loc></url></urlset>");

            Assert.Equal(new[] { "http://example.test/ok" }, document.PageUrls);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not xml at all")]
        [InlineData("<urlset><url><loc>https://example.test/a</loc></url>")]
        [InlineData("<html><body>hello</body></html>")]
        public void MalformedOrUnrelatedContent_YieldsAnEmptyDocument(string? xml)
        {
            var document = SitemapParser.Parse(xml);

            Assert.Empty(document.PageUrls);
            Assert.Empty(document.SitemapUrls);
        }

        [Fact]
        public void ADocumentTypeDeclaration_IsRejectedInsteadOfExpanded()
        {
            var document = SitemapParser.Parse(
                "<?xml version=\"1.0\"?><!DOCTYPE urlset [<!ENTITY big \"https://example.test/expanded\">]>" +
                "<urlset><url><loc>&big;</loc></url></urlset>");

            Assert.Empty(document.PageUrls);
        }
    }
}
