namespace AutomationSandbox.PlaywrightLiveExploration
{
    /// <summary>A single HTTP response observed during a <see cref="PlaywrightWebSession" />.</summary>
    public sealed class WebNetworkResponse
    {
        /// <summary>Creates a captured network response.</summary>
        public WebNetworkResponse(string url, int status)
        {
            Url = url;
            Status = status;
        }

        /// <summary>The requested URL.</summary>
        public string Url { get; }
        /// <summary>The HTTP status code returned for that URL.</summary>
        public int Status { get; }
    }
}
