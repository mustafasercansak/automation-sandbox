using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace AutomationSandbox.PlaywrightLiveExploration
{
    /// <summary>What one sitemap document lists: page URLs from a <c>urlset</c>, or further sitemap URLs from a
    /// <c>sitemapindex</c>.</summary>
    public sealed class SitemapDocument
    {
        /// <summary>Absolute http(s) page URLs, in document order, without duplicates.</summary>
        public List<string> PageUrls { get; set; } = new List<string>();

        /// <summary>Absolute http(s) URLs of other sitemaps, when the document is a sitemap index.</summary>
        public List<string> SitemapUrls { get; set; } = new List<string>();
    }

    /// <summary>Reads <c>sitemap.xml</c> and sitemap index files. Pure parsing, no network access. The
    /// document is untrusted: DTD processing is prohibited and the size is bounded, so an entity-expansion
    /// payload is rejected instead of expanded.</summary>
    public static class SitemapParser
    {
        private const long MaxCharacters = 10_000_000;

        /// <summary>Parses <paramref name="xml" />. Content that is not well-formed sitemap XML yields an empty
        /// document rather than an exception, so one broken sitemap does not stop a crawl.</summary>
        public static SitemapDocument Parse(string? xml)
        {
            var document = new SitemapDocument();
            if (string.IsNullOrWhiteSpace(xml))
            {
                return document;
            }

            XDocument parsed;
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaxCharacters,
                };
                using var reader = XmlReader.Create(new StringReader(xml!), settings);
                parsed = XDocument.Load(reader);
            }
            catch (XmlException)
            {
                return document;
            }

            var root = parsed.Root;
            if (root == null)
            {
                return document;
            }

            var isIndex = string.Equals(root.Name.LocalName, "sitemapindex", StringComparison.OrdinalIgnoreCase);
            var isUrlSet = string.Equals(root.Name.LocalName, "urlset", StringComparison.OrdinalIgnoreCase);
            if (!isIndex && !isUrlSet)
            {
                return document;
            }

            var itemName = isIndex ? "sitemap" : "url";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in root.Elements().Where(e => string.Equals(e.Name.LocalName, itemName, StringComparison.OrdinalIgnoreCase)))
            {
                var loc = item.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "loc", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
                if (string.IsNullOrEmpty(loc) || !Uri.TryCreate(loc, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !seen.Add(loc!))
                {
                    continue;
                }

                (isIndex ? document.SitemapUrls : document.PageUrls).Add(loc!);
            }

            return document;
        }
    }
}
