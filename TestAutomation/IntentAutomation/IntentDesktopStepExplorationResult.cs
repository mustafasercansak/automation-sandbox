using System.Collections.Generic;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Editable desktop shortlist and review decision for one scenario step.</summary>
    public sealed class IntentDesktopStepExplorationResult
    {
        /// <summary>Scenario step associated with this matching or recording result.</summary>
        public IntentStep Step { get; set; } = new IntentStep();
        /// <summary>Ranked candidate evidence retained by this result or report.</summary>
        public List<IntentDesktopElementCandidate> Candidates { get; set; } = new List<IntentDesktopElementCandidate>();
        /// <summary>Whether automatic use is declined pending human review.</summary>
        public bool RequiresReview { get; set; }
        /// <summary>Human-readable explanation of the stage&apos;s matching or recording decision.</summary>
        public string Diagnostic { get; set; } = "";
    }
}
