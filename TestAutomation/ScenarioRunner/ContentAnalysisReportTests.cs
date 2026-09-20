using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutomationSandbox.ContentAnalysis;
using AutomationSandbox.WebDiscovery;

namespace ScenarioRunner
{
    public class ContentAnalysisReportTests : IDisposable
    {
        private readonly string _tempReportPath;

        public ContentAnalysisReportTests()
        {
            _tempReportPath = Path.Combine(Path.GetTempPath(), "ContentAnalysisReportTest_" + Guid.NewGuid().ToString("N") + ".content-report.json");
        }

        public void Dispose()
        {
            if (File.Exists(_tempReportPath))
            {
                File.Delete(_tempReportPath);
            }

            var lockPath = _tempReportPath + ".lock";
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }

            var htmlPath = Path.ChangeExtension(_tempReportPath, ".html");
            if (File.Exists(htmlPath))
            {
                File.Delete(htmlPath);
            }
        }

        [Fact]
        public void CountPassages_CountsDistinctVisibleTextNodes_ExcludingWrapperDuplicates()
        {
            // A wrapper whose Text exactly matches its one meaningful child's Text (the button/span
            // case ExtractPassages already special-cases) must not be double-counted.
            var dom = Root(
                Leaf("h1", "Welcome"),
                new WebElementInfo
                {
                    TagName = "button",
                    CssSelector = "button",
                    Text = "Save",
                    Children = new List<WebElementInfo> { Leaf("span", "Save") },
                });

            Assert.Equal(2, ContentAnalyzer.CountPassages(dom));
        }

        [Fact]
        public void CountPassages_MatchesTheNumberOfPassagesHeuristicsActuallyConsidered()
        {
            var dom = Root(Leaf("h1", "Order failed!!!!"), Leaf("p", "Please click the the button."));

            var passageCount = ContentAnalyzer.CountPassages(dom);
            var issues = ContentAnalyzer.RunHeuristics(dom);

            Assert.Equal(2, passageCount);
            Assert.Equal(2, issues.Count); // one issue per passage here, not a coincidence of the fixture
        }

        [Fact]
        public void FromAnalysis_BuildsAnEntry_WithUrlPassageCountAndCopiedIssues()
        {
            var dom = Root(Leaf("h1", "TODO: replace this banner copy."));
            var issues = ContentAnalyzer.RunHeuristics(dom);

            var entry = ContentAnalysisReportEntry.FromAnalysis("https://example.test/page", dom, issues);

            Assert.Equal("https://example.test/page", entry.Url);
            Assert.Equal(1, entry.PassageCount);
            var issue = Assert.Single(entry.Issues);
            Assert.Equal("Placeholder", issue.IssueType);
            Assert.Equal(ContentIssueSource.Heuristic, issue.Source);
        }

        [Fact]
        public void ReportFileSink_Record_AppendsWithoutRewritingPriorLines()
        {
            // Same append-only contract as HealingReportFileSink (#424): seed one existing line,
            // then prove a second Record() call leaves those original bytes untouched.
            var existingLine = JsonSerializer.Serialize(new ContentAnalysisReportEntry { Url = "https://example.test/first" });
            File.WriteAllText(_tempReportPath, existingLine + "\n");
            var sink = new ContentAnalysisReportFileSink(_tempReportPath, htmlFilePath: null);

            sink.Record(new ContentAnalysisReportEntry { Url = "https://example.test/second" });

            var lines = File.ReadAllLines(_tempReportPath);
            Assert.Equal(2, lines.Length);
            Assert.Equal(existingLine, lines[0]);

            var report = sink.LoadReport();
            Assert.Equal(new[] { "https://example.test/first", "https://example.test/second" }, report.Entries.Select(e => e.Url));
        }

        [Fact]
        public void ReportFileSink_LoadReport_RoundTripsEntryAndIssueFields()
        {
            var sink = new ContentAnalysisReportFileSink(_tempReportPath, htmlFilePath: null);
            var entry = new ContentAnalysisReportEntry
            {
                Url = "https://example.test/pricing",
                PassageCount = 3,
                Issues = new List<ContentIssue>
                {
                    new ContentIssue("h1", "Wellcome to our site", "Spelling", "\"Wellcome\" looks misspelled.", ContentIssueSource.Llm),
                    new ContentIssue("p", "Order failed!!!!", "RepeatedPunctuation", "Repeated punctuation (\"!!!!\").", ContentIssueSource.Heuristic),
                },
            };

            sink.Record(entry);
            var report = sink.LoadReport();

            var loaded = Assert.Single(report.Entries);
            Assert.Equal("https://example.test/pricing", loaded.Url);
            Assert.Equal(3, loaded.PassageCount);
            Assert.Equal(2, loaded.Issues.Count);
            Assert.Equal("Spelling", loaded.Issues[0].IssueType);
            Assert.Equal(ContentIssueSource.Llm, loaded.Issues[0].Source);
            Assert.Equal("h1", loaded.Issues[0].CssSelector);
            Assert.Equal(ContentIssueSource.Heuristic, loaded.Issues[1].Source);
        }

        [Fact]
        public void ReportFileSink_SavesHtmlAtomically_AndCleansUpTempFiles()
        {
            var htmlPath = Path.ChangeExtension(_tempReportPath, ".html");
            var sink = new ContentAnalysisReportFileSink(_tempReportPath, htmlPath);

            sink.Record(new ContentAnalysisReportEntry { Url = "https://example.test/home" });

            Assert.True(File.Exists(htmlPath));
            Assert.Contains("<!doctype html>", File.ReadAllText(htmlPath));

            var prefix = Path.GetFileName(_tempReportPath);
            var leftoverTempFiles = Directory.GetFiles(Path.GetDirectoryName(htmlPath)!, prefix + "*.tmp");
            Assert.Empty(leftoverTempFiles);
        }

        [Fact]
        public void HtmlRenderer_EncodesIssueTextAndMessage_PreventingScriptInjection()
        {
            var document = new ContentAnalysisReportDocument
            {
                Entries =
                {
                    new ContentAnalysisReportEntry
                    {
                        Url = "https://example.test/<script>alert(1)</script>",
                        Issues = { new ContentIssue("p", "<script>alert(1)</script>", "Grammar", "<b>bad</b>", ContentIssueSource.Llm) },
                    },
                },
            };

            var html = ContentAnalysisReportHtmlRenderer.Render(document);

            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("&lt;script&gt;", html);
        }

        [Fact]
        public void HtmlRenderer_RendersNoIssuesFound_ForACleanPage()
        {
            var document = new ContentAnalysisReportDocument
            {
                Entries = { new ContentAnalysisReportEntry { Url = "https://example.test/clean", Issues = new List<ContentIssue>() } },
            };

            var html = ContentAnalysisReportHtmlRenderer.Render(document);

            Assert.Contains("No issues found.", html);
        }

        private static WebElementInfo Root(params WebElementInfo[] children)
        {
            return new WebElementInfo
            {
                TagName = "body",
                CssSelector = "body",
                Text = "",
                Children = new List<WebElementInfo>(children),
            };
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
    }
}
