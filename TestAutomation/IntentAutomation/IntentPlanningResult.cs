using System.Collections.Generic;

namespace IntentAutomation
{
    public sealed class IntentPlanningResult
    {
        public IntentScenario Scenario { get; set; } = new IntentScenario();
        public List<string> Diagnostics { get; set; } = new List<string>();
        public bool RequiresReview { get; set; }
    }
}
