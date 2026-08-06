using System.Collections.Generic;

namespace IntentAutomation
{
    public sealed class IntentDesktopExplorationResult
    {
        public IntentScenario Scenario { get; set; } = new IntentScenario();
        public List<IntentDesktopStepExplorationResult> StepResults { get; set; } = new List<IntentDesktopStepExplorationResult>();
    }
}
