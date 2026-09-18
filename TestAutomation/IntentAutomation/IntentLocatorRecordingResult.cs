using AutomationSandbox.UiModel;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Editable web recording outcome linking a scenario step, proposed match, and stored locator.</summary>
    public sealed class IntentLocatorRecordingResult
    {
        /// <summary>Scenario step associated with this matching or recording result.</summary>
        public IntentStep Step { get; set; } = new IntentStep();
        /// <summary>Candidate selected for the recording attempt, if a match was available.</summary>
        public IntentElementCandidate? Candidate { get; set; }
        /// <summary>Logical repository key identifying a locator independently of its current automation identifier.</summary>
        public string LocatorKey { get; set; } = "";
        /// <summary>Whether this attempt wrote a locator record.</summary>
        public bool Recorded { get; set; }
        /// <summary>Human-readable explanation of the stage&apos;s matching or recording decision.</summary>
        public string Diagnostic { get; set; } = "";
        /// <summary>Stored locator record produced by this attempt, or null when recording was declined.</summary>
        public LocatorRecord? Record { get; set; }
    }
}
