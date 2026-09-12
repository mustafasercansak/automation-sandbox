using System.Collections.Generic;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Editable outputs of the desktop pipeline, including generated source and the derived report.</summary>
    public sealed class IntentDesktopAutomationPipelineResult
    {
        /// <summary>Scenario and diagnostics returned by the planner.</summary>
        public IntentPlanningResult Planning { get; set; } = new IntentPlanningResult();
        /// <summary>Captured-tree candidate matches and review decisions.</summary>
        public IntentDesktopExplorationResult Exploration { get; set; } = new IntentDesktopExplorationResult();
        /// <summary>Per-step locator recording outcomes.</summary>
        public IReadOnlyList<IntentDesktopLocatorRecordingResult> RecordingResults { get; set; } = new List<IntentDesktopLocatorRecordingResult>();
        /// <summary>Generated FlaUI C# test source; execution requires Windows and the target application.</summary>
        public string FlaUiCSharpTestCode { get; set; } = "";
        /// <summary>Flow report assembled from the pipeline stages and generated source.</summary>
        public IntentFlowReportDocument Report { get; set; } = new IntentFlowReportDocument();
    }
}
