namespace IntentAutomation
{
    public sealed class IntentDesktopAutomationPipelineOptions
    {
        public IntentDesktopExplorationOptions Exploration { get; set; } = new IntentDesktopExplorationOptions();
        public IntentDesktopLocatorRecordingOptions Recording { get; set; } = new IntentDesktopLocatorRecordingOptions();
        public FlaUiCSharpTestGenerationOptions Generation { get; set; } = new FlaUiCSharpTestGenerationOptions();
    }
}
