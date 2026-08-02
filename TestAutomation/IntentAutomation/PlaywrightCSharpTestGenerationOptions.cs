namespace IntentAutomation
{
    public sealed class PlaywrightCSharpTestGenerationOptions
    {
        public string Namespace { get; set; } = "GeneratedTests";
        public string ClassName { get; set; } = "";
        public string MethodName { get; set; } = "";
        public bool IncludeLocatorComments { get; set; } = true;
    }
}
