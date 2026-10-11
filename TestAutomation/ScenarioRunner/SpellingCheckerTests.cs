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

        [Fact]
        public void CollapseInheritedIssues_KeepsOnlyTheMostSpecificElement()
        {
            var inner = new ContentIssue("body > p", "Please click the the button.", "DuplicateWord", "Repeated word \"the\".", ContentIssueSource.Heuristic);
            var outer = new ContentIssue("body", "About us. Please click the the button. Bye.", "DuplicateWord", "Repeated word \"the\".", ContentIssueSource.Heuristic);

            var kept = ContentAnalyzer.CollapseInheritedIssues(new[] { outer, inner });

            Assert.Same(inner, Assert.Single(kept));
        }

        [Fact]
        public void CollapseInheritedIssues_RecognisesAnInheritedIssue_WhenTheAncestorTextWasCutOffMidDescendant()
        {
            var inner = new ContentIssue("body > p", "Please click the the button to continue.", "DuplicateWord", "Repeated word \"the\".", ContentIssueSource.Heuristic);
            var outer = new ContentIssue("body", "About us. Please click the the button to cont", "DuplicateWord", "Repeated word \"the\".", ContentIssueSource.Heuristic);

            var kept = ContentAnalyzer.CollapseInheritedIssues(new[] { outer, inner });

            Assert.Same(inner, Assert.Single(kept));
        }

        [Fact]
        public void CollapseInheritedIssues_KeepsSeparateOccurrencesAndDifferentMessages()
        {
            var first = new ContentIssue("p:nth-of-type(1)", "Click the the button.", "DuplicateWord", "Repeated word \"the\".", ContentIssueSource.Heuristic);
            var second = new ContentIssue("p:nth-of-type(2)", "Open the the menu.", "DuplicateWord", "Repeated word \"the\".", ContentIssueSource.Heuristic);
            var otherMessage = new ContentIssue("body", "Click the the button. Open the the menu. Say a a thing.", "DuplicateWord", "Repeated word \"a\".", ContentIssueSource.Heuristic);

            var kept = ContentAnalyzer.CollapseInheritedIssues(new[] { first, second, otherMessage });

            Assert.Equal(3, kept.Count);
        }

        [Fact]
        public void Check_OffersASuggestionFromTheDictionaryThatKnowsTheWord_OnAMixedLanguagePage()
        {
            var issue = Assert.Single(TurkishAndEnglish().Check(Passage("Your order has been shiped.")));

            Assert.Contains("shipped", issue.Message);
        }

        private static SpellingChecker Numeric()
        {
            return SpellingChecker.FromDictionaryFiles(new[] { Dictionary("mini-num") });
        }

        [Fact]
        public void Check_AcceptsAFormMadeByAnAffixRuleWhoseNumericFlagIsZero()
        {
            // The Turkish dictionary defines its "-de" locative with flag 0, which the Hunspell library reads as
            // "no flag". Without the remap "evde" (in the house) is reported as a misspelling.
            Assert.Empty(Numeric().Check(Passage("evde okulda")));
        }

        [Fact]
        public void Check_StillFlagsAWrongFormOnANumericFlagDictionary()
        {
            var issue = Assert.Single(Numeric().Check(Passage("evdd okulda")));

            Assert.Contains("\"evdd\"", issue.Message);
        }

        [Fact]
        public void Check_AcceptsAKnownWordWithATurkishSuffixAfterAnApostrophe()
        {
            var issues = TurkishAndEnglish().Check(Passage("button'ları ve checkout'ta"));

            Assert.Empty(issues);
        }

        [Fact]
        public void Check_StillFlagsAnUnknownStemBeforeAnApostropheSuffix()
        {
            var issue = Assert.Single(TurkishAndEnglish().Check(Passage("buttonn'ları")));

            Assert.Contains("\"buttonn'ları\"", issue.Message);
        }

        [Fact]
        public void Check_SkipsASuffixThatFollowsAnApostropheAfterANumberOrSymbol()
        {
            Assert.Empty(TurkishAndEnglish().Check(Passage("merhaba %40+’ını 5'ini 3‘nin")));
        }

        [Theory]
        [InlineData("Backend‘nin")]
        [InlineData("Backend’nin")]
        [InlineData("Backend'nin")]
        [InlineData("Backendʼnin")]
        public void Check_TreatsEveryApostropheVariantAsOneWord(string text)
        {
            var issue = Assert.Single(TurkishAndEnglish().Check(Passage(text)));

            Assert.Contains("\"Backend'nin\"", issue.Message);
        }

        [Fact]
        public void Check_IgnoresTheLastWord_WhenTheTextMayHaveBeenCutOff()
        {
            var issues = English().Check(Passage("Your order has been shiped. Please wel"), textMayBeCutOff: true);

            Assert.Equal(new[] { "shiped" }, issues.Select(i => ExtractWord(i.Message)).ToArray());
        }

        [Fact]
        public void Check_ReportsTheLastWord_WhenTheTextWasNotCutOff()
        {
            var issues = English().Check(Passage("Your order has been shiped. Please wel"));

            Assert.Equal(new[] { "shiped", "wel" }, issues.Select(i => ExtractWord(i.Message)).ToArray());
        }

        [Fact]
        public void Check_ReportsTheLastWord_WhenACutOffTextEndsAfterACompleteWord()
        {
            var issues = English().Check(Passage("Your order has been shiped. Please wel."), textMayBeCutOff: true);

            Assert.Equal(new[] { "shiped", "wel" }, issues.Select(i => ExtractWord(i.Message)).ToArray());
        }

        [Fact]
        public void RunSpellCheck_IgnoresThePartialLastWordOfAPassageThatReachedTheCaptureLimit()
        {
            const string text = "Welcome to the shiped order wel";
            var dom = Root(Node("div", "p", text));

            var withLimit = ContentAnalyzer.RunSpellCheck(dom, English(), capturedTextLimit: text.Length);
            var withoutLimit = ContentAnalyzer.RunSpellCheck(dom, English());

            Assert.Equal(new[] { "shiped" }, withLimit.Select(i => ExtractWord(i.Message)).ToArray());
            Assert.Equal(new[] { "shiped", "wel" }, withoutLimit.Select(i => ExtractWord(i.Message)).ToArray());
        }

        [Theory]
        [InlineData("style")]
        [InlineData("script")]
        [InlineData("noscript")]
        [InlineData("template")]
        [InlineData("kbd")]
        [InlineData("samp")]
        public void Extraction_SkipsElementsWhoseTextIsNotProse(string tagName)
        {
            var dom = Root(Node("div", "p", "Welcome"), Node(tagName, tagName, "rgba trebuchet dasharray wellcome"));

            Assert.Equal(1, ContentAnalyzer.CountPassages(dom));
            Assert.Empty(ContentAnalyzer.RunSpellCheck(dom, English()));
            Assert.Empty(ContentAnalyzer.RunHeuristics(Root(Node(tagName, tagName, "TODO the the"))));
        }

        [Fact]
        public void Extraction_IgnoresTheTextOfAnSvgRoot_ButStillChecksItsLabels()
        {
            var label = Node("text", "svg > text", "Wellcome");
            var svg = new WebElementInfo
            {
                TagName = "svg",
                CssSelector = "#mermaid-1",
                Text = "#mermaid-1{font-family:trebuchet,verdana;} Wellcome",
                Children = { label },
            };

            var issues = ContentAnalyzer.RunSpellCheck(Root(svg), English());

            var issue = Assert.Single(issues);
            Assert.Equal("svg > text", issue.CssSelector);
            Assert.Equal(1, ContentAnalyzer.CountPassages(Root(svg)));
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
