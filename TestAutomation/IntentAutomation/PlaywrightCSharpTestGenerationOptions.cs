namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Mutable naming, comment, and assertion settings for generated Playwright C# tests.</summary>
    public sealed class PlaywrightCSharpTestGenerationOptions
    {
        /// <summary>Requested namespace for generated C# test classes.</summary>
        public string Namespace { get; set; } = "GeneratedTests";
        /// <summary>Requested generated C# test-class identifier; empty lets the generator derive a name from the scenario.</summary>
        public string ClassName { get; set; } = "";
        /// <summary>Requested generated C# test-method identifier.</summary>
        public string MethodName { get; set; } = "";
        /// <summary>Whether generated source includes comments describing recorded locators.</summary>
        public bool IncludeLocatorComments { get; set; } = true;
        /// <summary>Policy for handling assertions that cannot be emitted faithfully.</summary>
        public AssertGenerationMode AssertGenerationMode { get; set; } = AssertGenerationMode.Strict;
    }
}
