using UiModel;

namespace IntentAutomation
{
    public sealed class IntentLocatorRecordingResult
    {
        public IntentStep Step { get; set; } = new IntentStep();
        public IntentElementCandidate? Candidate { get; set; }
        public string LocatorKey { get; set; } = "";
        public bool Recorded { get; set; }
        public string Diagnostic { get; set; } = "";
        public LocatorRecord? Record { get; set; }
    }
}
