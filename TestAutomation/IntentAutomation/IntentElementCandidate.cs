using System.Collections.Generic;
using AutomationSandbox.WebDiscovery;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Web matching evidence with fixed scores and a copied, read-only locator-suggestion list. The referenced editable step and element are not deep-frozen.</summary>
    public sealed class IntentElementCandidate
    {
        /// <summary>Scenario step associated with this matching or recording result.</summary>
        public IntentStep Step { get; }
        /// <summary>Captured element proposed for this step.</summary>
        public WebElementInfo Element { get; }
        /// <summary>Overall action and target matching score; it is not a calibrated correctness probability.</summary>
        public double Score { get; }
        /// <summary>Text-based target similarity used by the independent semantic acceptance gate.</summary>
        public double SemanticScore { get; }
        /// <summary>Human-readable explanation of why this candidate or locator strategy was ranked.</summary>
        public string Reason { get; }
        /// <summary>Ranked locator suggestions for the captured web element.</summary>
        public IReadOnlyList<PlaywrightLocatorSuggestion> LocatorSuggestions { get; }

        /// <summary>Captures web matching scores and copies the locator suggestions while retaining the supplied step and element references.</summary>
        public IntentElementCandidate(
            IntentStep? step = null,
            WebElementInfo? element = null,
            double score = 0.0,
            double semanticScore = 0.0,
            string reason = "",
            IReadOnlyList<PlaywrightLocatorSuggestion>? locatorSuggestions = null)
        {
            Step = step ?? new IntentStep();
            Element = element ?? new WebElementInfo();
            Score = score;
            SemanticScore = semanticScore;
            Reason = reason;
            LocatorSuggestions = new List<PlaywrightLocatorSuggestion>(locatorSuggestions ?? System.Array.Empty<PlaywrightLocatorSuggestion>()).AsReadOnly();
        }
    }
}
