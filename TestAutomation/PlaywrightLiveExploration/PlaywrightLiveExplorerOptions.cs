namespace AutomationSandbox.PlaywrightLiveExploration
{
    /// <summary>Mutable launch and navigation settings for live browser capture.</summary>
    public sealed class PlaywrightLiveExplorerOptions
    {
        /// <summary>Whether the launched browser runs without a visible window.</summary>
        public bool Headless { get; set; } = true;
        /// <summary>Maximum navigation wait in milliseconds.</summary>
        public int NavigationTimeoutMilliseconds { get; set; } = 30_000;
    }
}
