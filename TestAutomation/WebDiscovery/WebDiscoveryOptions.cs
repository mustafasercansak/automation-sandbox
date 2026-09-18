namespace AutomationSandbox.WebDiscovery
{
    /// <summary>Traversal limits for live DOM capture, mirroring <c>AutomationSandbox.Discovery.DiscoveryOptions</c>
    /// for the web capture path. Unlike the FlaUI/UIA walker, the browser-side <c>walk()</c> script enforces
    /// these bounds itself (see <see cref="PlaywrightDomCaptureScript.BuildJavaScript" />), since a single
    /// <c>page.EvaluateAsync</c> call cannot be interrupted from .NET mid-flight.</summary>
    public sealed class WebDiscoveryOptions
    {
        /// <summary>Maximum traversal depth relative to the capture root (<c>document.body</c>); each shadow-root
        /// or same-origin iframe crossing counts as one additional level, same as a regular child element.</summary>
        public int MaxDepth { get; set; } = 25;

        /// <summary>Maximum number of elements captured before traversal stops.</summary>
        public int MaxElements { get; set; } = 5000;

        /// <summary>Maximum wall-clock time allowed for the in-page traversal, checked by the script itself via
        /// <c>Date.now()</c> against a deadline computed once up front.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Creates a fresh options instance with default traversal settings.</summary>
        public static WebDiscoveryOptions Default => new();
    }
}
