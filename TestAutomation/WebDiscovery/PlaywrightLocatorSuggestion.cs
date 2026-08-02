namespace WebDiscovery
{
    public sealed class PlaywrightLocatorSuggestion
    {
        public string Strategy { get; set; } = "";
        public string Expression { get; set; } = "";
        public double Confidence { get; set; }
        public string Reason { get; set; } = "";
    }
}
