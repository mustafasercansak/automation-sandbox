using System.Collections.Generic;

namespace IntentAutomation
{
    public sealed class IntentStepExplorationResult
    {
        public IntentStep Step { get; set; } = new IntentStep();
        public List<IntentElementCandidate> Candidates { get; set; } = new List<IntentElementCandidate>();
        public bool RequiresReview { get; set; }
        public string Diagnostic { get; set; } = "";
    }
}
