using AutomationSandbox.UiModel;

namespace AutomationSandbox.IntentAutomation
{
    // Desktop counterpart to IntentElementCandidate: Element is a UiElementInfo captured via
    // Discovery/UiTreeWalker instead of a WebElementInfo DOM node, and there is no
    // PlaywrightLocatorSuggestion list - the strongest desktop locator (AutomationId, falling
    // back to Name+ControlType) is derived directly from the snapshot at generation time.

    /// <summary>Desktop matching evidence with fixed scores. The referenced editable step and element are not deep-frozen.</summary>
    public sealed class IntentDesktopElementCandidate
    {
        /// <summary>Scenario step associated with this matching or recording result.</summary>
        public IntentStep Step { get; }
        /// <summary>Captured element proposed for this step.</summary>
        public UiElementInfo Element { get; }
        /// <summary>Overall action and target matching score; it is not a calibrated correctness probability.</summary>
        public double Score { get; }
        /// <summary>Text-based target similarity used by the independent semantic acceptance gate.</summary>
        public double SemanticScore { get; }
        /// <summary>Human-readable explanation of why this candidate or locator strategy was ranked.</summary>
        public string Reason { get; }

        /// <summary>Captures desktop matching scores while retaining the supplied editable step and element references.</summary>
        public IntentDesktopElementCandidate(
            IntentStep? step = null,
            UiElementInfo? element = null,
            double score = 0.0,
            double semanticScore = 0.0,
            string reason = "")
        {
            Step = step ?? new IntentStep();
            Element = element ?? new UiElementInfo();
            Score = score;
            SemanticScore = semanticScore;
            Reason = reason;
        }
    }
}
