namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Mutable title, comment, and assertion settings for generated Playwright TypeScript tests.</summary>
    public sealed class PlaywrightTypeScriptTestGenerationOptions
    {
        /// <summary>Human-readable title of the generated TypeScript test.</summary>
        public string TestTitle { get; set; } = "";
        /// <summary>Whether generated source includes comments describing recorded locators.</summary>
        public bool IncludeLocatorComments { get; set; } = true;
        /// <summary>Policy for handling assertions that cannot be emitted faithfully.</summary>
        public AssertGenerationMode AssertGenerationMode { get; set; } = AssertGenerationMode.Strict;
    }
}
