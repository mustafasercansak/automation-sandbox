using System.Collections.Generic;
using WebDiscovery;

namespace IntentAutomation
{
    public sealed class IntentElementCandidate
    {
        public IntentStep Step { get; set; } = new IntentStep();
        public WebElementInfo Element { get; set; } = new WebElementInfo();
        public double Score { get; set; }
        public double SemanticScore { get; set; }
        public string Reason { get; set; } = "";
        public List<PlaywrightLocatorSuggestion> LocatorSuggestions { get; set; } = new List<PlaywrightLocatorSuggestion>();
    }
}
