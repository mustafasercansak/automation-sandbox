namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Mutable application, naming, comment, and assertion settings for generated FlaUI C# tests.</summary>
    public sealed class FlaUiCSharpTestGenerationOptions
    {
        /// <summary>Requested namespace for generated C# test classes.</summary>
        public string Namespace { get; set; } = "GeneratedTests";
        /// <summary>Requested generated C# test-class identifier; empty lets the generator derive a name from the scenario.</summary>
        public string ClassName { get; set; } = "";
        /// <summary>Requested generated C# test-method identifier.</summary>
        public string MethodName { get; set; } = "";
        /// <summary>Whether generated source includes comments describing recorded locators.</summary>
        public bool IncludeLocatorComments { get; set; } = true;

        // Codegen has no way to know the compiled path of the app under test - this placeholder
        // is emitted verbatim into the generated constructor for the caller to fill in, unless
        // ApplicationExecutablePath is set (e.g. the caller already knows it, as our own live
        // tests do via WinFormsAppRelativePath).
        /// <summary>Codegen has no way to know the compiled path of the app under test - this placeholder is emitted verbatim into the generated constructor for the caller to fill in, unless ApplicationExecutablePath is set (e.g. the caller already knows it, as our own live tests do via WinFormsAppRelativePath).</summary>
        public string ApplicationExecutablePath { get; set; } = "TODO: path to the compiled application executable";
        /// <summary>Policy for handling assertions that cannot be emitted faithfully.</summary>
        public AssertGenerationMode AssertGenerationMode { get; set; } = AssertGenerationMode.Strict;
    }
}
