namespace AutomationSandbox.PlaywrightLiveExploration
{
    /// <summary>A single browser console message observed during a <see cref="PlaywrightWebSession" />.</summary>
    public sealed class WebConsoleMessage
    {
        /// <summary>Creates a captured console message.</summary>
        public WebConsoleMessage(string messageType, string text)
        {
            MessageType = messageType;
            Text = text;
        }

        /// <summary>The console method used, e.g. <c>"log"</c>, <c>"warning"</c>, or <c>"error"</c>.</summary>
        public string MessageType { get; }
        /// <summary>The logged text.</summary>
        public string Text { get; }
    }
}
