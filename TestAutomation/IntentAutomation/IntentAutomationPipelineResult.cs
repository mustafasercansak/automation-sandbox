using System.Collections.Generic;

namespace IntentAutomation
{
    public sealed class IntentAutomationPipelineResult
    {
        public IntentPlanningResult Planning { get; set; } = new IntentPlanningResult();
        public IntentExplorationResult Exploration { get; set; } = new IntentExplorationResult();
        public IReadOnlyList<IntentLocatorRecordingResult> RecordingResults { get; set; } = new List<IntentLocatorRecordingResult>();
        public string PlaywrightCSharpTestCode { get; set; } = "";
        public string PlaywrightTypeScriptTestCode { get; set; } = "";
        public IntentFlowReportDocument Report { get; set; } = new IntentFlowReportDocument();
    }
}
