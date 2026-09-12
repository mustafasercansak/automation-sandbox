using System.Collections.Generic;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Editable web matching results for an ordered scenario, used by recording and reporting stages.</summary>
    public sealed class IntentExplorationResult
    {
        /// <summary>Editable scenario associated with this planning or exploration result.</summary>
        public IntentScenario Scenario { get; set; } = new IntentScenario();
        /// <summary>Matching outcomes in scenario-step order.</summary>
        public List<IntentStepExplorationResult> StepResults { get; set; } = new List<IntentStepExplorationResult>();
    }
}
