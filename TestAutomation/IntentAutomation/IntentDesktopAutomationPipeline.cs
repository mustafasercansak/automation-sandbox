using System;
using UiModel;

namespace IntentAutomation
{
    // Desktop counterpart to IntentAutomationPipeline: plans intent steps (the same IIntentPlanner
    // works for both platforms), matches them against a live UiElementInfo tree, records accepted
    // locators, and generates an xUnit + FlaUI test skeleton.

    public sealed class IntentDesktopAutomationPipeline
    {
        private readonly IIntentPlanner _planner;
        private readonly IntentDesktopExplorationBridge _explorationBridge;
        private readonly IntentDesktopLocatorRepositoryRecorder _recorder;
        private readonly FlaUiCSharpTestGenerator _generator;

        public IntentDesktopAutomationPipeline(
            IIntentPlanner? planner = null,
            IntentDesktopAutomationPipelineOptions? options = null)
        {
            var effectiveOptions = options ?? new IntentDesktopAutomationPipelineOptions();
            _planner = planner ?? new DeterministicIntentPlanner();
            _explorationBridge = new IntentDesktopExplorationBridge(effectiveOptions.Exploration);
            _recorder = new IntentDesktopLocatorRepositoryRecorder(effectiveOptions.Recording);
            _generator = new FlaUiCSharpTestGenerator(effectiveOptions.Generation);
        }

        public IntentDesktopAutomationPipelineResult Run(
            IntentPlanningRequest request,
            UiElementInfo desktopRoot,
            LocatorRepository repository)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (desktopRoot == null)
            {
                throw new ArgumentNullException(nameof(desktopRoot));
            }

            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            var planning = _planner.Plan(request);
            var exploration = _explorationBridge.Match(planning.Scenario, desktopRoot);
            var recordingResults = _recorder.Record(exploration, repository);
            var code = _generator.Generate(planning.Scenario, recordingResults);

            return new IntentDesktopAutomationPipelineResult
            {
                Planning = planning,
                Exploration = exploration,
                RecordingResults = recordingResults,
                FlaUiCSharpTestCode = code,
            };
        }
    }
}
