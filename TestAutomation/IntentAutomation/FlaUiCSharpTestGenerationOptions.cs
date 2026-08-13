namespace IntentAutomation
{
    public sealed class FlaUiCSharpTestGenerationOptions
    {
        public string Namespace { get; set; } = "GeneratedTests";
        public string ClassName { get; set; } = "";
        public string MethodName { get; set; } = "";
        public bool IncludeLocatorComments { get; set; } = true;

        // Codegen has no way to know the compiled path of the app under test - this placeholder
        // is emitted verbatim into the generated constructor for the caller to fill in, unless
        // ApplicationExecutablePath is set (e.g. the caller already knows it, as our own live
        // tests do via WinFormsAppRelativePath).
        public string ApplicationExecutablePath { get; set; } = "TODO: path to the compiled application executable";
        public AssertGenerationMode AssertGenerationMode { get; set; } = AssertGenerationMode.Strict;
    }
}
