namespace IntentAutomation
{
    public sealed class IntentAutomationPipelineOptions
    {
        public IntentExplorationOptions Exploration { get; set; } = new IntentExplorationOptions();
        public IntentLocatorRecordingOptions Recording { get; set; } = new IntentLocatorRecordingOptions();
        public PlaywrightCSharpTestGenerationOptions Generation { get; set; } = new PlaywrightCSharpTestGenerationOptions();
        public PlaywrightTypeScriptTestGenerationOptions TypeScriptGeneration { get; set; } = new PlaywrightTypeScriptTestGenerationOptions();

        // Convenience property that syncs AssertGenerationMode across both C# and TypeScript generation options.
        public AssertGenerationMode AssertGenerationMode
        {
            get => Generation.AssertGenerationMode;
            set
            {
                Generation.AssertGenerationMode = value;
                TypeScriptGeneration.AssertGenerationMode = value;
            }
        }
    }
}
