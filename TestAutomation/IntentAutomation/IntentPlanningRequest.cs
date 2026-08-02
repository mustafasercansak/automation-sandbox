using System.Collections.Generic;

namespace IntentAutomation
{
    public sealed class IntentPlanningRequest
    {
        public string Name { get; set; } = "";
        public string Goal { get; set; } = "";
        public string TargetUrl { get; set; } = "";
        public IDictionary<string, string> TestData { get; set; } = new Dictionary<string, string>();
    }
}
