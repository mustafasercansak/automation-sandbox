using System.Collections.Generic;

namespace AutomationSandbox.IntentAutomation
{
    public sealed class IntentExplorationResult
    {
        public IntentScenario Scenario { get; set; } = new IntentScenario();
        public List<IntentStepExplorationResult> StepResults { get; set; } = new List<IntentStepExplorationResult>();
    }
}
