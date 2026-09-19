namespace AutomationSandbox.PlaywrightLiveExploration
{
    /// <summary>A single network request that failed outright (e.g. DNS failure, connection reset) during a
    /// <see cref="PlaywrightWebSession" />, as distinct from a completed response carrying an error status.</summary>
    public sealed class WebRequestFailure
    {
        /// <summary>Creates a captured request failure.</summary>
        public WebRequestFailure(string url, string? failureText)
        {
            Url = url;
            FailureText = failureText;
        }

        /// <summary>The requested URL.</summary>
        public string Url { get; }
        /// <summary>The Playwright-reported failure reason, when available.</summary>
        public string? FailureText { get; }
    }
}
