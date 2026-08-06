using System.Collections.Generic;

namespace IntentAutomation
{
    public sealed class IntentDesktopStepExplorationResult
    {
        public IntentStep Step { get; set; } = new IntentStep();
        public List<IntentDesktopElementCandidate> Candidates { get; set; } = new List<IntentDesktopElementCandidate>();
        public bool RequiresReview { get; set; }
        public string Diagnostic { get; set; } = "";
    }
}
