using System.Collections.Generic;

namespace IntentAutomation
{
    public sealed class IntentScenario
    {
        public string Name { get; set; } = "";
        public string Goal { get; set; } = "";
        public string TargetUrl { get; set; } = "";
        public List<IntentStep> Steps { get; set; } = new List<IntentStep>();
    }
}
