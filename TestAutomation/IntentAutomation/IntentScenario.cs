using System.Collections.Generic;

namespace IntentAutomation
{
    /// <summary>
    /// Represents a structured, planned test scenario derived from a business goal.
    /// </summary>
    public sealed class IntentScenario
    {
        /// <summary>
        /// Human-readable name for the test scenario.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// High-level business goal describing the intent of the scenario.
        /// </summary>
        public string Goal { get; set; } = "";

        /// <summary>
        /// Target URL for web automation scenarios (web-specific). Optional / unused in desktop scenarios.
        /// </summary>
        public string TargetUrl { get; set; } = "";

        /// <summary>
        /// Ordered sequence of intent automation steps.
        /// </summary>
        public List<IntentStep> Steps { get; set; } = new List<IntentStep>();
    }
}

