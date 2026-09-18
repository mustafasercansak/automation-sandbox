namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Mutable repository metadata and acceptance policy for recording desktop intent matches.</summary>
    public sealed class IntentDesktopLocatorRecordingOptions
    {
        /// <summary>Application label used to identify the captured or tested system in reports and storage.</summary>
        public string ApplicationName { get; set; } = "";
        /// <summary>Platform label recorded with the document, normally web or desktop.</summary>
        public string Platform { get; set; } = "windows-uia";
        /// <summary>Minimum candidate score required by the recording policy.</summary>
        public double MinimumScore { get; set; } = 0.35;
        /// <summary>Whether the recorder may persist candidates whose exploration result requires review.</summary>
        public bool RecordReviewCandidates { get; set; }
    }
}
