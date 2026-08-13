namespace IntentAutomation
{
    public sealed class IntentDesktopAutomationPipelineOptions
    {
        public IntentDesktopExplorationOptions Exploration { get; set; } = new IntentDesktopExplorationOptions();
        public IntentDesktopLocatorRecordingOptions Recording { get; set; } = new IntentDesktopLocatorRecordingOptions();
        public FlaUiCSharpTestGenerationOptions Generation { get; set; } = new FlaUiCSharpTestGenerationOptions();

        // Convenience property that forwards AssertGenerationMode to the desktop C# generation options.
        public AssertGenerationMode AssertGenerationMode
        {
            get => Generation.AssertGenerationMode;
            set => Generation.AssertGenerationMode = value;
        }
    }
}
