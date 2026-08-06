using System.Collections.Generic;

namespace IntentAutomation
{
    public sealed class IntentDesktopAutomationPipelineResult
    {
        public IntentPlanningResult Planning { get; set; } = new IntentPlanningResult();
        public IntentDesktopExplorationResult Exploration { get; set; } = new IntentDesktopExplorationResult();
        public IReadOnlyList<IntentDesktopLocatorRecordingResult> RecordingResults { get; set; } = new List<IntentDesktopLocatorRecordingResult>();
        public string FlaUiCSharpTestCode { get; set; } = "";
    }
}
