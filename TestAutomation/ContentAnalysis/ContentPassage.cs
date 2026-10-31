namespace AutomationSandbox.ContentAnalysis
{
    /// <summary>One piece of visible text extracted from a captured page, identified by the element it came from.</summary>
    public sealed class ContentPassage
    {
        /// <summary>Creates a content passage.</summary>
        public ContentPassage(string cssSelector, string text, string? flowText = null)
        {
            CssSelector = cssSelector;
            Text = text;
            FlowText = flowText ?? text;
        }

        /// <summary>CSS selector of the element the text was captured from.</summary>
        public string CssSelector { get; }
        /// <summary>The captured text.</summary>
        public string Text { get; }
        /// <summary>The text with the text of block-level children (table cells, list items, headings, paragraphs)
        /// cut out and replaced by a line break, so the words of neighbouring blocks never read as running prose.
        /// The captured <see cref="Text" /> has its whitespace collapsed, so it cannot tell two cells from two
        /// words. Equal to <see cref="Text" /> when the passage has no block-level children.</summary>
        public string FlowText { get; }
    }
}
