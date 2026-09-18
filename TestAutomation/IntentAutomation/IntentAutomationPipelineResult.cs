using System.Collections.Generic;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Editable outputs of the web pipeline, including generated source and the derived report.</summary>
    public sealed class IntentAutomationPipelineResult
    {
        /// <summary>Scenario and diagnostics returned by the planner.</summary>
        public IntentPlanningResult Planning { get; set; } = new IntentPlanningResult();
        /// <summary>Captured-tree candidate matches and review decisions.</summary>
        public IntentExplorationResult Exploration { get; set; } = new IntentExplorationResult();
        /// <summary>Per-step locator recording outcomes.</summary>
        public IReadOnlyList<IntentLocatorRecordingResult> RecordingResults { get; set; } = new List<IntentLocatorRecordingResult>();
        /// <summary>Generated Playwright C# test source; generation does not execute the test.</summary>
        public string PlaywrightCSharpTestCode { get; set; } = "";
        /// <summary>Generated Playwright TypeScript test source; generation does not execute the test.</summary>
        public string PlaywrightTypeScriptTestCode { get; set; } = "";
        /// <summary>Flow report assembled from the pipeline stages and generated source.</summary>
        public IntentFlowReportDocument Report { get; set; } = new IntentFlowReportDocument();
    }
}
