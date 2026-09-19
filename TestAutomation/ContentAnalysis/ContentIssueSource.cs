namespace AutomationSandbox.ContentAnalysis
{
    /// <summary>Which layer of <see cref="ContentAnalyzer" /> found a <see cref="ContentIssue" />.</summary>
    public enum ContentIssueSource
    {
        /// <summary>Found by a zero-dependency deterministic check.</summary>
        Heuristic = 0,
        /// <summary>Found by an <see cref="IContentAnalysisProvider" /> language-model review.</summary>
        Llm = 1,
    }
}
