using System;
using System.Linq;
using AutomationSandbox.SiteAuditCli;

namespace ScenarioRunner
{
    public class SiteAuditOptionsTests
    {
        private static AuditOptions Parse(params string[] args)
        {
            return AuditOptions.Parse(args, path => throw new InvalidOperationException("No file expected: " + path));
        }

        [Fact]
        public void Parse_AppliesDefaults_WhenOnlyAUrlIsGiven()
        {
            var options = Parse("--url", "https://example.test/");

            Assert.Equal("https://example.test/", options.Url);
            Assert.Equal(new[] { "en" }, options.Languages.ToArray());
            Assert.Equal(25, options.MaxPages);
            Assert.Equal(3, options.MaxDepth);
            Assert.Equal("site-audit-report", options.OutputDirectory);
            Assert.Null(options.DictionaryDirectory);
            Assert.False(options.NoDownload);
            Assert.False(options.InstallBrowser);
            Assert.False(options.NoHeuristics);
        }

        [Fact]
        public void Parse_Help_DoesNotRequireAUrl()
        {
            Assert.True(Parse("--help").ShowHelp);
        }

        [Theory]
        [InlineData("ftp://example.test/")]
        [InlineData("example.test")]
        [InlineData("/relative/path")]
        public void Parse_RejectsAUrlThatIsNotAbsoluteHttpOrHttps(string url)
        {
            Assert.Throws<UsageException>(() => Parse("--url", url));
        }

        [Fact]
        public void Parse_RequiresAUrl()
        {
            Assert.Throws<UsageException>(() => Parse("--lang", "tr"));
        }

        [Fact]
        public void Parse_SplitsLanguagesKeepingOrder_AndDropsCaseInsensitiveDuplicates()
        {
            var options = Parse("--url", "https://example.test/", "--lang", "tr, en,TR,en-GB");

            Assert.Equal(new[] { "tr", "en", "en-GB" }, options.Languages.ToArray());
        }

        [Theory]
        [InlineData("../etc")]
        [InlineData("tr/../../x")]
        [InlineData("e")]
        [InlineData("english")]
        [InlineData("en_GB")]
        public void Parse_RejectsALanguageCodeThatCouldNotBeASafeDictionaryName(string language)
        {
            Assert.Throws<UsageException>(() => Parse("--url", "https://example.test/", "--lang", language));
        }

        [Fact]
        public void Parse_CollectsAllowedWordsFromFlagsAndFile_IgnoringBlankLinesAndComments()
        {
            var options = AuditOptions.Parse(
                new[] { "--url", "https://example.test/", "--allow-word", "Acme", "--allow-file", "words.txt", "--allow-word", "Zed" },
                path => new[] { "# brands", "Kubernetes", "", "  Playwright  " });

            Assert.Equal(new[] { "Acme", "Kubernetes", "Playwright", "Zed" }, options.AllowedWords.ToArray());
        }

        [Fact]
        public void Parse_ReadsLimitsAndFlags()
        {
            var options = Parse(
                "--url", "https://example.test/", "--max-pages", "7", "--max-depth", "0", "--output-dir", "out",
                "--dictionary-dir", "dicts", "--no-download", "--install-browser", "--no-heuristics");

            Assert.Equal(7, options.MaxPages);
            Assert.Equal(0, options.MaxDepth);
            Assert.Equal("out", options.OutputDirectory);
            Assert.Equal("dicts", options.DictionaryDirectory);
            Assert.True(options.NoDownload);
            Assert.True(options.InstallBrowser);
            Assert.True(options.NoHeuristics);
        }

        [Theory]
        [InlineData("--max-pages", "0")]
        [InlineData("--max-pages", "many")]
        [InlineData("--max-depth", "-1")]
        public void Parse_RejectsOutOfRangeLimits(string flag, string value)
        {
            Assert.Throws<UsageException>(() => Parse("--url", "https://example.test/", flag, value));
        }

        [Fact]
        public void Parse_RejectsUnknownArgumentsAndFlagsMissingTheirValue()
        {
            Assert.Throws<UsageException>(() => Parse("--url", "https://example.test/", "--bogus"));
            Assert.Throws<UsageException>(() => Parse("--url"));
        }

        [Theory]
        [InlineData("en", true)]
        [InlineData("tr", true)]
        [InlineData("en-GB", true)]
        [InlineData("pt-PT", true)]
        [InlineData("", false)]
        [InlineData("..", false)]
        [InlineData("en/x", false)]
        [InlineData("en\\x", false)]
        [InlineData("e", false)]
        public void DictionaryLocation_ValidatesLanguageCodes(string language, bool expected)
        {
            Assert.Equal(expected, DictionaryLocation.IsValidLanguageCode(language));
        }

        [Fact]
        public void DictionaryLocation_RejectsANullLanguage()
        {
            Assert.False(DictionaryLocation.IsValidLanguageCode(null));
        }

        [Fact]
        public void DictionaryLocation_BuildsPinnedRemoteUrls()
        {
            var url = DictionaryLocation.RemoteUrl("tr", "dic");

            Assert.Equal(
                "https://raw.githubusercontent.com/wooorm/dictionaries/" + DictionaryLocation.PinnedCommit + "/dictionaries/tr/index.dic",
                url);
            Assert.Equal(40, DictionaryLocation.PinnedCommit.Length);
        }

        [Fact]
        public void DictionaryLocation_RefusesToBuildAUrlOrPathFromAnInvalidLanguage()
        {
            Assert.Throws<ArgumentException>(() => DictionaryLocation.RemoteUrl("../x", "dic"));
            Assert.Throws<ArgumentException>(() => DictionaryLocation.LocalBasePath("dir", "../x"));
        }
    }
}
