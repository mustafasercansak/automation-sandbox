using System.Collections.Generic;

namespace IntentAutomation
{
    public sealed class IntentExplorationResult
    {
        public IntentScenario Scenario { get; set; } = new IntentScenario();
        public List<IntentStepExplorationResult> StepResults { get; set; } = new List<IntentStepExplorationResult>();
    }
}
