namespace AutomationSandbox.ContentAnalysis
{
    /// <summary>One piece of visible text extracted from a captured page, identified by the element it came from.</summary>
    public sealed class ContentPassage
    {
        /// <summary>Creates a content passage.</summary>
        public ContentPassage(string cssSelector, string text)
        {
            CssSelector = cssSelector;
            Text = text;
        }

        /// <summary>CSS selector of the element the text was captured from.</summary>
        public string CssSelector { get; }
        /// <summary>The captured text.</summary>
        public string Text { get; }
    }
}
