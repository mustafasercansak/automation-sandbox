namespace AutomationSandbox.ContentAnalysis
{
    /// <summary>One content-quality problem found in a captured page's visible text.</summary>
    public sealed class ContentIssue
    {
        /// <summary>Creates a content issue.</summary>
        public ContentIssue(string cssSelector, string text, string issueType, string message, ContentIssueSource source)
        {
            CssSelector = cssSelector;
            Text = text;
            IssueType = issueType;
            Message = message;
            Source = source;
        }

        /// <summary>CSS selector of the element the issue was found on.</summary>
        public string CssSelector { get; }
        /// <summary>The captured text the issue was found in.</summary>
        public string Text { get; }
        /// <summary>Short category, e.g. <c>"DuplicateWord"</c>, <c>"Placeholder"</c>, <c>"Grammar"</c>.</summary>
        public string IssueType { get; }
        /// <summary>Human-readable description of the problem.</summary>
        public string Message { get; }
        /// <summary>Which layer found this issue.</summary>
        public ContentIssueSource Source { get; }
    }
}
