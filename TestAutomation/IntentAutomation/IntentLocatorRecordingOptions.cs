namespace IntentAutomation
{
    public sealed class IntentLocatorRecordingOptions
    {
        public string ApplicationName { get; set; } = "";
        public string Platform { get; set; } = "web-playwright";
        public double MinimumScore { get; set; } = 0.35;
        public bool RecordReviewCandidates { get; set; }
    }
}
