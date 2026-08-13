namespace IntentAutomation
{
    public sealed class PlaywrightTypeScriptTestGenerationOptions
    {
        public string TestTitle { get; set; } = "";
        public bool IncludeLocatorComments { get; set; } = true;
        public AssertGenerationMode AssertGenerationMode { get; set; } = AssertGenerationMode.Strict;
    }
}
