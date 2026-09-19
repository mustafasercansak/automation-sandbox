using System.Collections.Generic;
using AutomationSandbox.IntentAutomation;

namespace AutomationSandbox.IntentExecution
{
    /// <summary>The outcome of running an entire planned <see cref="IntentScenario" /> against a live session.</summary>
    public sealed class IntentWebExecutionResult
    {
        /// <summary>The scenario that was executed.</summary>
        public IntentScenario Scenario { get; set; } = new IntentScenario();
        /// <summary>Per-step outcomes, in execution order. Stops at the first failed step - later steps are not
        /// attempted and do not appear here.</summary>
        public List<IntentStepExecutionResult> StepResults { get; set; } = new List<IntentStepExecutionResult>();
        /// <summary>Whether every step executed successfully.</summary>
        public bool Success { get; set; }
    }
}
