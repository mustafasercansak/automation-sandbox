namespace IntentAutomation
{
    public sealed class IntentDesktopLocatorRecordingOptions
    {
        public string ApplicationName { get; set; } = "";
        public string Platform { get; set; } = "windows-uia";
        public double MinimumScore { get; set; } = 0.35;
        public bool RecordReviewCandidates { get; set; }
    }
}
