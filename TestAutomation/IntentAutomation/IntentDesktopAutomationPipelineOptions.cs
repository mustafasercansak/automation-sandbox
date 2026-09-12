namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Mutable stage configuration for the desktop intent automation pipeline.</summary>
    public sealed class IntentDesktopAutomationPipelineOptions
    {
        /// <summary>Desktop candidate matching and review thresholds.</summary>
        public IntentDesktopExplorationOptions Exploration { get; set; } = new IntentDesktopExplorationOptions();
        /// <summary>Settings for locator repository recording.</summary>
        public IntentDesktopLocatorRecordingOptions Recording { get; set; } = new IntentDesktopLocatorRecordingOptions();
        /// <summary>Settings for C# test-source generation.</summary>
        public FlaUiCSharpTestGenerationOptions Generation { get; set; } = new FlaUiCSharpTestGenerationOptions();

        // Convenience property that forwards AssertGenerationMode to the desktop C# generation options.
        /// <summary>Convenience property that forwards AssertGenerationMode to the desktop C# generation options.</summary>
        public AssertGenerationMode AssertGenerationMode
        {
            get => Generation.AssertGenerationMode;
            set => Generation.AssertGenerationMode = value;
        }
    }
}
