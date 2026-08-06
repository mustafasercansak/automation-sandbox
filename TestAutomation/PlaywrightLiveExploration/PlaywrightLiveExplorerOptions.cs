namespace PlaywrightLiveExploration
{
    public sealed class PlaywrightLiveExplorerOptions
    {
        public bool Headless { get; set; } = true;
        public int NavigationTimeoutMilliseconds { get; set; } = 30_000;
    }
}
