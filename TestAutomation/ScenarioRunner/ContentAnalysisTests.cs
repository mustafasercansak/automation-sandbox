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

        private static WebElementInfo Leaf(string cssSelector, string text)
        {
            return new WebElementInfo
            {
                TagName = "div",
                CssSelector = cssSelector,
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
