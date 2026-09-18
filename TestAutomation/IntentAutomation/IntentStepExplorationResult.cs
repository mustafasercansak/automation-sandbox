using System.Collections.Generic;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Editable web shortlist and review decision for one scenario step.</summary>
    public sealed class IntentStepExplorationResult
    {
        /// <summary>Scenario step associated with this matching or recording result.</summary>
        public IntentStep Step { get; set; } = new IntentStep();
        /// <summary>Ranked candidate evidence retained by this result or report.</summary>
        public List<IntentElementCandidate> Candidates { get; set; } = new List<IntentElementCandidate>();
        /// <summary>Whether automatic use is declined pending human review.</summary>
        public bool RequiresReview { get; set; }
        /// <summary>Human-readable explanation of the stage&apos;s matching or recording decision.</summary>
        public string Diagnostic { get; set; } = "";
    }
}
