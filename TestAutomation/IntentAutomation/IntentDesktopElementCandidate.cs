using UiModel;

namespace IntentAutomation
{
    // Desktop counterpart to IntentElementCandidate: Element is a UiElementInfo captured via
    // Discovery/UiTreeWalker instead of a WebElementInfo DOM node, and there is no
    // PlaywrightLocatorSuggestion list - the strongest desktop locator (AutomationId, falling
    // back to Name+ControlType) is derived directly from the snapshot at generation time.

    public sealed class IntentDesktopElementCandidate
    {
        public IntentStep Step { get; set; } = new IntentStep();
        public UiElementInfo Element { get; set; } = new UiElementInfo();
        public double Score { get; set; }
        public string Reason { get; set; } = "";
    }
}
