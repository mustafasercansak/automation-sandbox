namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Mutable stage configuration for the web intent automation pipeline.</summary>
    public sealed class IntentAutomationPipelineOptions
    {
        /// <summary>Web candidate matching and review thresholds.</summary>
        public IntentExplorationOptions Exploration { get; set; } = new IntentExplorationOptions();
        /// <summary>Settings for locator repository recording.</summary>
        public IntentLocatorRecordingOptions Recording { get; set; } = new IntentLocatorRecordingOptions();
        /// <summary>Settings for C# test-source generation.</summary>
        public PlaywrightCSharpTestGenerationOptions Generation { get; set; } = new PlaywrightCSharpTestGenerationOptions();
        /// <summary>Settings for Playwright TypeScript test-source generation.</summary>
        public PlaywrightTypeScriptTestGenerationOptions TypeScriptGeneration { get; set; } = new PlaywrightTypeScriptTestGenerationOptions();

        // Convenience property that syncs AssertGenerationMode across both C# and TypeScript generation options.
        /// <summary>Convenience property that syncs AssertGenerationMode across both C# and TypeScript generation options.</summary>
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
