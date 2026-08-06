using UiModel;

namespace IntentAutomation
{
    public sealed class IntentDesktopLocatorRecordingResult
    {
        public IntentStep Step { get; set; } = new IntentStep();
        public IntentDesktopElementCandidate? Candidate { get; set; }
        public string LocatorKey { get; set; } = "";
        public bool Recorded { get; set; }
        public string Diagnostic { get; set; } = "";
        public LocatorRecord? Record { get; set; }
    }
}
