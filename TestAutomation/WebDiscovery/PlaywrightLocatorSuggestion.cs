namespace AutomationSandbox.WebDiscovery
{
    /// <summary>Read-only locator expression and its heuristic ranking explanation; verify uniqueness before using it in a test.</summary>
    public sealed class PlaywrightLocatorSuggestion
    {
        /// <summary>Locator strategy label, such as TestId, Role, or Css.</summary>
        public string Strategy { get; }
        /// <summary>Generated locator expression suitable for the target test framework.</summary>
        public string Expression { get; }
        /// <summary>Heuristic preference for this locator strategy; it is not measured runtime uniqueness or correctness.</summary>
        public double Confidence { get; }
        /// <summary>Human-readable explanation of why this candidate or locator strategy was ranked.</summary>
        public string Reason { get; }

        /// <summary>Captures a locator expression with its strategy preference and explanation; no browser lookup is performed.</summary>
        public PlaywrightLocatorSuggestion(
            string strategy = "",
            string expression = "",
            double confidence = 0.0,
            string reason = "")
        {
            Strategy = strategy;
            Expression = expression;
            Confidence = confidence;
            Reason = reason;
        }
    }
}
