using System.Collections.Generic;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Editable planned scenario with diagnostics and a review decision, allowing custom planners to refine their output.</summary>
    public sealed class IntentPlanningResult
    {
        /// <summary>Editable scenario associated with this planning or exploration result.</summary>
        public IntentScenario Scenario { get; set; } = new IntentScenario();
        /// <summary>Human-readable planning diagnostics retained for review and reporting.</summary>
        public List<string> Diagnostics { get; set; } = new List<string>();
        /// <summary>Whether automatic use is declined pending human review.</summary>
        public bool RequiresReview { get; set; }
    }
}
