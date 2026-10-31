using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.ContentAnalysis;
using AutomationSandbox.WebDiscovery;

namespace ScenarioRunner
{
    public class ContentAnalysisTests
    {
        [Fact]
        public void RunHeuristics_FlagsDuplicateConsecutiveWord()
        {
            var dom = Leaf("[data-testid='msg']", "Please click the the button to continue.");

            var issues = ContentAnalyzer.RunHeuristics(dom);

            var issue = Assert.Single(issues);
            Assert.Equal("DuplicateWord", issue.IssueType);
            Assert.Equal(ContentIssueSource.Heuristic, issue.Source);
        }

        [Fact]
        public void RunHeuristics_DoesNotFlagNonAdjacentRepeatedWord()
        {
            var dom = Leaf("[data-testid='msg']", "The cat chased the dog around the yard.");

            var issues = ContentAnalyzer.RunHeuristics(dom);

            Assert.Empty(issues);
        }

        // #550: the first audit of the project's own docs produced 32 DuplicateWord findings and none was
        // a defect. Each group below is one of those findings, taken from the real text.
        [Theory]
        [InlineData("No\tNo")]
        [InlineData("Capability\tCapability\tStatus")]
        [InlineData("Gemini\nGemini API")]
        [InlineData("OpenAI\nOpenAI API data controls")]
        public void RunHeuristics_DoesNotFlagTheSameWordInNeighbouringCellsOrListItems(string text)
        {
            // innerText separates table cells with a tab and rows, list items and blocks with a newline.
            Assert.Empty(ContentAnalyzer.RunHeuristics(Leaf("table", text)));
        }

        [Theory]
        [InlineData("29 29")]
        [InlineData("9 / 9 9 / 9")]
        [InlineData("MaxRetries 2 2")]
        public void RunHeuristics_DoesNotFlagARepeatedNumber(string text)
        {
            Assert.Empty(ContentAnalyzer.RunHeuristics(Leaf("td", text)));
        }

        [Theory]
        [InlineData("HealResult healResult")]
        [InlineData("Use maxRetries MaxRetries here")]
        [InlineData("Pass expected expected.json to the tool")]
        public void RunHeuristics_DoesNotFlagCodeNamesThatRepeatIgnoringCase(string text)
        {
            Assert.Empty(ContentAnalyzer.RunHeuristics(Leaf("p", text)));
        }

        [Theory]
        [InlineData("Please click the the button", "the")]
        [InlineData("The the cat sat down.", "The")]
        [InlineData("It was very very", "very")]
        [InlineData("Click the the.", "the")]
        [InlineData("It\u00A0was the\u00A0the end", "the")]
        public void RunHeuristics_StillFlagsARealRepeatedWord(string text, string repeated)
        {
            var issue = Assert.Single(ContentAnalyzer.RunHeuristics(Leaf("p", text)));
            Assert.Equal("DuplicateWord", issue.IssueType);
            Assert.Contains($"\"{repeated}\"", issue.Message);
        }

        [Fact]
        public void RunHeuristics_FlagsARealRepeatEvenWhenAnIdentifierRepeatComesFirst()
        {
            var issue = Assert.Single(ContentAnalyzer.RunHeuristics(Leaf("p", "HealResult healResult is is wrong")));
            Assert.Contains("\"is\"", issue.Message);
        }

        [Fact]
        public void RunHeuristics_FlagsARepeatedWordInsideOneCell_ButNotAcrossCells()
        {
            var issue = Assert.Single(ContentAnalyzer.RunHeuristics(Leaf("tr", "No\tclick the the button")));
            Assert.Contains("\"the\"", issue.Message);
        }

        // The real capture collapses all whitespace to single spaces (PlaywrightDomCaptureScript.textOf), so
        // these tests build trees whose Text looks exactly like that. The boundary between two cells has to
        // come from the tree, not from a tab or a newline that the capture never produces.
        private static WebElementInfo El(string tag, string css, string text, params WebElementInfo[] children)
        {
            var element = new WebElementInfo { TagName = tag, CssSelector = css, Text = text };
            element.Children.AddRange(children);
            return element;
        }

        [Fact]
        public void RunHeuristics_DoesNotFlagTheSameWordInNeighbouringTableCells()
        {
            // From the docs audit (#550): a table with two "No" columns, and "Gemini | Gemini API".
            var row = El("tr", "tr", "Observe No No Week 1",
                El("td", "td:nth-of-type(1)", "Observe"),
                El("td", "td:nth-of-type(2)", "No"),
                El("td", "td:nth-of-type(3)", "No"),
                El("td", "td:nth-of-type(4)", "Week 1"));
            var models = El("tr", "tr:nth-of-type(2)", "Gemini Gemini API",
                El("td", "td", "Gemini"),
                El("td", "td", "Gemini API"));

            Assert.Empty(ContentAnalyzer.RunHeuristics(El("table", "table", "Observe No No Week 1 Gemini Gemini API", row, models)));
        }

        [Fact]
        public void RunHeuristics_DoesNotFlagTheSameWordAtTheEndOfOneBlockAndStartOfTheNext()
        {
            var section = El("div", "div", "Save Save the draft",
                El("li", "li:nth-of-type(1)", "Save"),
                El("li", "li:nth-of-type(2)", "Save the draft"));
            var heading = El("section", "section", "risk risk is high",
                El("h2", "h2", "risk"),
                El("p", "p", "risk is high"));

            Assert.Empty(ContentAnalyzer.RunHeuristics(section));
            Assert.Empty(ContentAnalyzer.RunHeuristics(heading));
        }

        [Fact]
        public void RunHeuristics_StillFlagsARepeatAcrossInlineElements()
        {
            // <p>Click <b>the</b> the button</p> is one sentence; the repeat is a real typo.
            var paragraph = El("p", "p", "Click the the button", El("b", "p > b", "the"));

            var issue = Assert.Single(ContentAnalyzer.RunHeuristics(paragraph), i => i.IssueType == "DuplicateWord");
            Assert.Equal("p", issue.CssSelector);
        }

        [Fact]
        public void RunHeuristics_StillFlagsARepeatInsideOneBlockChild()
        {
            var table = El("table", "table", "Name the the value",
                El("tr", "tr", "Name the the value",
                    El("td", "td:nth-of-type(1)", "Name"),
                    El("td", "td:nth-of-type(2)", "the the value")));

            var issue = Assert.Single(ContentAnalyzer.RunHeuristics(table));
            Assert.Equal("td:nth-of-type(2)", issue.CssSelector);
        }

        [Fact]
        public void RunHeuristics_StillFlagsARepeatInTheParentsOwnTextBesideABlockChild()
        {
            var host = El("div", "div", "Please please read this note",
                El("p", "p", "this note"));

            Assert.Single(ContentAnalyzer.RunHeuristics(host), i => i.IssueType == "DuplicateWord");
        }

        [Fact]
        public void FlowTextOf_CutsABlockChildThatTheCaptureTruncatedInTheParent()
        {
            // The capture cuts a long page at a character limit: the parent ends partway into the last cell,
            // while that cell's own text (it starts later, so it fits longer) runs on. "No No" must not be
            // read as a repeat just because its second half is where the parent was cut off.
            var parent = El("tr", "tr", "Observe No No",
                El("td", "td:nth-of-type(1)", "Observe"),
                El("td", "td:nth-of-type(2)", "No"),
                El("td", "td:nth-of-type(3)", "No Week 1 and a long description that the parent no longer holds"));

            Assert.Empty(ContentAnalyzer.RunHeuristics(parent));
        }

        [Fact]
        public void RunHeuristics_DoesNotFlagAnAllowedRepeat_IgnoringCase_ButStillFlagsOthers()
        {
            // Turkish repeats a word on purpose: "adım adım" is "step by step".
            var allowed = new[] { "adım", "tek" };

            Assert.Empty(ContentAnalyzer.RunHeuristics(Leaf("p", "Adım adım kurulum."), allowed));
            Assert.Empty(ContentAnalyzer.RunHeuristics(Leaf("p", "Yalnızca tek tek kelimeleri değerlendirir."), allowed));
            Assert.Single(ContentAnalyzer.RunHeuristics(Leaf("p", "Please click the the button"), allowed));
            Assert.Single(ContentAnalyzer.RunHeuristics(Leaf("p", "alan alan açıklama"), allowed));
            Assert.Single(ContentAnalyzer.RunHeuristics(Leaf("p", "Adım adım kurulum.")));
        }

        [Fact]
        public void FlowTextOf_LeavesTextAloneWhenAChildCannotBeFoundOrIsHidden()
        {
            var hidden = new WebElementInfo { TagName = "div", CssSelector = "div > div", Text = "Save", IsHidden = true };
            var parent = El("div", "div", "Save Save", hidden, El("p", "p", "text the capture cut off"));

            Assert.Equal("Save Save", ContentAnalyzer.FlowTextOf(parent));
            Assert.Equal("plain", ContentAnalyzer.FlowTextOf(El("div", "div", "plain")));
            Assert.Equal("", ContentAnalyzer.FlowTextOf(El("div", "div", "")));
        }

        [Fact]
        public void RunHeuristics_FlagsLeftoverPlaceholderMarker()
        {
            var dom = Leaf("[data-testid='banner']", "TODO: replace this banner copy before launch.");

            var issues = ContentAnalyzer.RunHeuristics(dom);

            var issue = Assert.Single(issues);
            Assert.Equal("Placeholder", issue.IssueType);
        }

        [Fact]
        public void RunHeuristics_DoesNotFlagWordThatMerelyContainsAPlaceholderMarker()
        {
            var dom = Leaf("[data-testid='banner']", "This todorobot assistant is great.");

            var issues = ContentAnalyzer.RunHeuristics(dom);

            Assert.Empty(issues);
        }

        [Fact]
        public void RunHeuristics_FlagsFourOrMoreRepeatedPunctuationCharacters()
        {
            var dom = Leaf("[data-testid='alert']", "Order failed!!!!");

            var issues = ContentAnalyzer.RunHeuristics(dom);

            var issue = Assert.Single(issues);
            Assert.Equal("RepeatedPunctuation", issue.IssueType);
        }

        [Fact]
        public void RunHeuristics_DoesNotFlagAnEllipsisOrADoubleExclamation()
        {
            var dom = Leaf("[data-testid='alert']", "Loading... almost done!!");

            var issues = ContentAnalyzer.RunHeuristics(dom);

            Assert.Empty(issues);
        }

        [Fact]
        public void RunHeuristics_SkipsHiddenElements()
        {
            var dom = Leaf("[data-testid='msg']", "The the button is hidden.");
            dom.IsHidden = true;

            var issues = ContentAnalyzer.RunHeuristics(dom);

            Assert.Empty(issues);
        }

        [Fact]
        public void RunHeuristics_WrapperElementDuplicatingChildText_IsOnlyFlaggedOnce()
        {
            // <button><span>Save Save</span></button>: captured .Text is innerText/textContent, so the
            // button's Text equals its one child's Text - without dedup this would report the same
            // DuplicateWord finding twice, once per ancestor level (see ContentAnalyzer.ExtractPassages).
            var span = Leaf("button > span", "Save Save");
            var button = new WebElementInfo
            {
                TagName = "button",
                CssSelector = "button",
                Text = "Save Save",
                Children = { span },
            };

            var issues = ContentAnalyzer.RunHeuristics(button);

            var issue = Assert.Single(issues);
            Assert.Equal("button > span", issue.CssSelector);
        }

        [Fact]
        public async Task AnalyzeAsync_WithNoLlmProvider_ReturnsOnlyHeuristicIssues()
        {
            var dom = Leaf("[data-testid='msg']", "TODO fix the the copy.");

            var issues = await ContentAnalyzer.AnalyzeAsync(dom);

            Assert.Equal(2, issues.Count);
            Assert.All(issues, issue => Assert.Equal(ContentIssueSource.Heuristic, issue.Source));
        }

        [Fact]
        public async Task AnalyzeAsync_WithAnAvailableProvider_MergesHeuristicAndLlmIssues()
        {
            var dom = Leaf("[data-testid='msg']", "This are a grammatically broken sentence.");
            var fakeProvider = new FakeContentAnalysisProvider(passages =>
                new[] { new ContentIssue(passages[0].CssSelector, passages[0].Text, "Grammar", "\"This are\" should be \"This is\".", ContentIssueSource.Llm) });

            var issues = await ContentAnalyzer.AnalyzeAsync(dom, fakeProvider);

            var issue = Assert.Single(issues);
            Assert.Equal(ContentIssueSource.Llm, issue.Source);
        }

        [Fact]
        public async Task AnalyzeAsync_WithAnUnavailableProvider_DoesNotCallIt()
        {
            var dom = Leaf("[data-testid='msg']", "Perfectly ordinary text.");
            var fakeProvider = new FakeContentAnalysisProvider(_ => throw new InvalidOperationException("must not be called"))
            {
                IsAvailable = false,
            };

            var issues = await ContentAnalyzer.AnalyzeAsync(dom, fakeProvider);

            Assert.Empty(issues);
        }

        [Fact]
        public async Task ClaudeContentAnalysisProvider_WithNoApiKey_IsUnavailableAndReturnsNoIssues()
        {
            var provider = new ClaudeContentAnalysisProvider(apiKey: "");

            Assert.False(provider.IsAvailable);
            var issues = await provider.AnalyzeAsync(new[] { new ContentPassage("p", "some text") });
            Assert.Empty(issues);
        }

        [Fact]
        public void ContentAnalysisPrompt_ParseIssues_IgnoresAnIssueWithASelectorThatWasNeverSent()
        {
            var passages = new[] { new ContentPassage("#real", "Real text") };
            var rawResponse = """
                [{"selector": "#hallucinated", "issueType": "Grammar", "message": "not real"}]
                """;

            var issues = ContentAnalysisPrompt.ParseIssues(rawResponse, passages);

            Assert.Empty(issues);
        }

        [Fact]
        public void ContentAnalysisPrompt_ParseIssues_AcceptsAWellFormedIssueOnAKnownSelector()
        {
            var passages = new[] { new ContentPassage("#real", "This are wrong.") };
            var rawResponse = """
                [{"selector": "#real", "issueType": "Grammar", "message": "Subject-verb agreement."}]
                """;

            var issues = ContentAnalysisPrompt.ParseIssues(rawResponse, passages);

            var issue = Assert.Single(issues);
            Assert.Equal("#real", issue.CssSelector);
            Assert.Equal("This are wrong.", issue.Text);
            Assert.Equal(ContentIssueSource.Llm, issue.Source);
        }

        [Fact]
        public void ContentAnalysisPrompt_ParseIssues_ReturnsEmptyForMalformedJson()
        {
            var passages = new[] { new ContentPassage("#real", "Some text") };

            var issues = ContentAnalysisPrompt.ParseIssues("not json at all", passages);

            Assert.Empty(issues);
        }

        [Fact]
        public void RunHeuristics_DoesNotFlagPlaceholderMarkerInsideACodeBlock()
        {
            // "// TODO" is a real, deliberate marker inside source code - not a leftover doc placeholder.
            var dom = Root(CodeBlock("pre", "// TODO: replace this with your real config"));

            var issues = ContentAnalyzer.RunHeuristics(dom);

            Assert.Empty(issues);
        }

        [Fact]
        public void RunHeuristics_DoesNotFlagDuplicateWordInsideACodeElement()
        {
            var dom = Root(CodeBlock("code", "the the value is unused"));

            var issues = ContentAnalyzer.RunHeuristics(dom);

            Assert.Empty(issues);
        }

        [Fact]
        public void RunHeuristics_StillFlagsPlaceholderTextOutsideCodeBlocks_WhenACodeBlockIsAlsoPresent()
        {
            var dom = Root(
                Leaf("p", "TODO: replace this hero copy before launch."),
                CodeBlock("pre", "const answer = 42;"));

            var issues = ContentAnalyzer.RunHeuristics(dom);

            var issue = Assert.Single(issues);
            Assert.Equal("Placeholder", issue.IssueType);
            Assert.Equal("p", issue.CssSelector);
        }

        [Fact]
        public void CountPassages_ExcludesTextInsideCodeBlocks()
        {
            var dom = Root(Leaf("p", "Welcome"), CodeBlock("pre", "// TODO: fill in your API key"));

            Assert.Equal(1, ContentAnalyzer.CountPassages(dom));
        }

        private static WebElementInfo Leaf(string cssSelector, string text)
        {
            return new WebElementInfo
            {
                TagName = "div",
                CssSelector = cssSelector,
                Text = text,
            };
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

        private static WebElementInfo CodeBlock(string tagName, string text)
        {
            return new WebElementInfo
            {
                TagName = tagName,
                CssSelector = tagName,
                Text = text,
            };
        }

        private sealed class FakeContentAnalysisProvider : IContentAnalysisProvider
        {
            private readonly Func<IReadOnlyList<ContentPassage>, IReadOnlyList<ContentIssue>> _respond;

            public FakeContentAnalysisProvider(Func<IReadOnlyList<ContentPassage>, IReadOnlyList<ContentIssue>> respond)
            {
                _respond = respond;
            }

            public bool IsAvailable { get; set; } = true;

            public Task<IReadOnlyList<ContentIssue>> AnalyzeAsync(IReadOnlyList<ContentPassage> passages, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_respond(passages.ToList()));
            }
        }
    }
}
