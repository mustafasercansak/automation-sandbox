using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutomationSandbox.ContentAnalysis;
using AutomationSandbox.WebDiscovery;

namespace ScenarioRunner
{
    public class SpellingCheckerTests
    {
        private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "spelling");

        private static string Dictionary(string name)
        {
            return Path.Combine(FixtureDirectory, name);
        }

        private static SpellingChecker English(params string[] allowedWords)
        {
            return SpellingChecker.FromDictionaryFiles(new[] { Dictionary("mini-en") }, allowedWords);
        }

        private static SpellingChecker TurkishAndEnglish(params string[] allowedWords)
        {
            return SpellingChecker.FromDictionaryFiles(new[] { Dictionary("mini-tr"), Dictionary("mini-en") }, allowedWords);
        }

        private static ContentPassage Passage(string text)
        {
            return new ContentPassage("p", text);
        }

        [Fact]
        public void Check_FlagsAMisspelledWord_AsADictionaryIssue()
        {
            var issues = English().Check(Passage("Your order has been shiped."));

            var issue = Assert.Single(issues);
            Assert.Equal("Spelling", issue.IssueType);
            Assert.Equal(ContentIssueSource.Dictionary, issue.Source);
            Assert.Contains("\"shiped\"", issue.Message);
            Assert.Equal("p", issue.CssSelector);
        }

        [Fact]
        public void Check_OffersASuggestion_WhenTheDictionaryHasOne()
        {
            var issue = Assert.Single(English().Check(Passage("Your order has been shiped.")));

            Assert.Contains("shipped", issue.Message);
        }

        [Fact]
        public void Check_AcceptsAffixedForms_ThatAreNotListedLiterally()
        {
            var issues = English().Check(Passage("Click the buttons to continue your orders."));

            Assert.Empty(issues);
        }

        [Fact]
        public void Check_AcceptsAWord_WhenAnyLoadedDictionaryAcceptsIt()
        {
            var issues = TurkishAndEnglish().Check(Passage("Merhaba, your checkout is ready. Teşekkürler!"));

            Assert.Equal(new[] { "ready" }, issues.Select(i => ExtractWord(i.Message)).ToArray());
        }

        [Fact]
        public void Check_FlagsTurkishWordsWithMissingDiacritics_AndAcceptsInflections()
        {
            var issues = TurkishAndEnglish().Check(Passage("Siparişler gönderildi, siparis gonderildi."));

            Assert.Equal(new[] { "siparis", "gonderildi" }, issues.Select(i => ExtractWord(i.Message)).ToArray());
        }

        [Fact]
        public void Check_DoesNotFlagTokensThatAreNotProse()
        {
            var text = "Visit https://exmple.test/pagee or mail suport@exmple.test. HTML GitHub iPhone ab abc123def snake_casee";

            Assert.Empty(English().Check(Passage(text)));
        }

        [Fact]
        public void Check_DoesNotFlagAllowedWords_IncludingWithATurkishSuffix()
        {
            var checker = TurkishAndEnglish("Kubernetes", "acme");

            var issues = checker.Check(Passage("Kubernetes'in ACME and Acme and acme."));

            Assert.Empty(issues);
        }

        [Fact]
        public void Check_ReportsEachDistinctMisspellingOncePerPassage()
        {
            var issues = English().Check(Passage("shiped shiped wellcome shiped"));

            Assert.Equal(new[] { "shiped", "wellcome" }, issues.Select(i => ExtractWord(i.Message)).ToArray());
        }

        [Fact]
        public void Check_TreatsATypographicApostropheLikeAStraightOne()
        {
            var checker = TurkishAndEnglish("Kubernetes");

            Assert.Empty(checker.Check(Passage("Kubernetes’in")));
        }

        [Fact]
        public void Check_ReturnsNothing_ForEmptyText()
        {
            Assert.Empty(English().Check(Passage("   ")));
        }

        [Fact]
        public void FromDictionaryFiles_ThrowsFileNotFound_ForAMissingDictionary()
        {
            var ex = Assert.Throws<FileNotFoundException>(
                () => SpellingChecker.FromDictionaryFiles(new[] { Dictionary("does-not-exist") }));

            Assert.Contains("does-not-exist", ex.FileName);
        }

        [Fact]
        public void FromDictionaryFiles_Throws_WhenNoDictionaryIsSupplied()
        {
            Assert.Throws<ArgumentException>(() => SpellingChecker.FromDictionaryFiles(new string[0]));
        }

        [Fact]
        public void RunSpellCheck_SkipsCodeBlocks_AndChecksRepeatedTextOnce()
        {
            var dom = Root(
                Node("div", "p", "Wellcome"),
                Node("div", "span", "Wellcome"),
                Node("pre", "pre", "const wellcome = shiped;"));

            var issues = ContentAnalyzer.RunSpellCheck(dom, English());

            var issue = Assert.Single(issues);
            Assert.Equal("p", issue.CssSelector);
        }

        [Fact]
        public void HtmlRenderer_ShowsADictionaryBadge_ForDictionaryIssues()
        {
            var document = new ContentAnalysisReportDocument
            {
                Entries =
                {
                    new ContentAnalysisReportEntry
                    {
                        Url = "https://example.test/",
                        Issues = { new ContentIssue("p", "shiped", "Spelling", "\"shiped\" may be misspelled.", ContentIssueSource.Dictionary) },
                    },
                },
            };

            var html = ContentAnalysisReportHtmlRenderer.Render(document);

            Assert.Contains("dictionary-badge", html);
            Assert.Contains(">Dictionary<", html);
        }

        private static string ExtractWord(string message)
        {
            var start = message.IndexOf('"') + 1;
            return message.Substring(start, message.IndexOf('"', start) - start);
        }

        private static WebElementInfo Node(string tagName, string cssSelector, string text)
        {
            return new WebElementInfo { TagName = tagName, CssSelector = cssSelector, Text = text };
        }

        private static WebElementInfo Root(params WebElementInfo[] children)
        {
            return new WebElementInfo
            {
                TagName = "body",
                CssSelector = "body",
                Text = "",
                Children = children.ToList(),
            };
        }
    }
}
