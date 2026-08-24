using System.Collections.Generic;

namespace IntentAutomation
{
    /// <summary>
    /// Represents an input request to an <see cref="IIntentPlanner"/> specifying a goal, test data, and target.
    /// </summary>
    public sealed class IntentPlanningRequest
    {
        /// <summary>
        /// Optional human-readable name for the test scenario.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// The business or testing goal to achieve (e.g. "Register a new user account").
        /// </summary>
        public string Goal { get; set; } = "";

        /// <summary>
        /// Target URL for web automation scenarios (web-specific). Leave empty for desktop scenarios.
        /// </summary>
        public string TargetUrl { get; set; } = "";

        /// <summary>
        /// Key-value test data pairs used to populate form controls during planning.
        /// </summary>
        public IDictionary<string, string> TestData { get; set; } = new Dictionary<string, string>();
    }
}

