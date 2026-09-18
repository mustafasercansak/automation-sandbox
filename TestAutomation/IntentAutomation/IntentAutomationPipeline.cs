using System;
using AutomationSandbox.UiModel;
using AutomationSandbox.WebDiscovery;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Coordinates web scenario planning, captured-tree matching, locator recording, and test/report generation.</summary>
    public sealed class IntentAutomationPipeline
    {
        private readonly IIntentPlanner _planner;
        private readonly IntentExplorationBridge _explorationBridge;
        private readonly IntentLocatorRepositoryRecorder _recorder;
        private readonly PlaywrightCSharpTestGenerator _csharpGenerator;
        private readonly PlaywrightTypeScriptTestGenerator _typeScriptGenerator;

        /// <summary>Configures the web pipeline planner and stage options, using deterministic planning when no planner is supplied.</summary>
        public IntentAutomationPipeline(
            IIntentPlanner? planner = null,
            IntentAutomationPipelineOptions? options = null)
        {
            var effectiveOptions = options ?? new IntentAutomationPipelineOptions();
            _planner = planner ?? new DeterministicIntentPlanner();
            _explorationBridge = new IntentExplorationBridge(effectiveOptions.Exploration);
            _recorder = new IntentLocatorRepositoryRecorder(effectiveOptions.Recording);
            _csharpGenerator = new PlaywrightCSharpTestGenerator(effectiveOptions.Generation);
            _typeScriptGenerator = new PlaywrightTypeScriptTestGenerator(effectiveOptions.TypeScriptGeneration);
        }

        /// <summary>Plans the request, matches against the supplied captured tree, records eligible locators, and generates test sources and a flow report.</summary>
        public IntentAutomationPipelineResult Run(
            IntentPlanningRequest request,
            WebElementInfo domRoot,
            LocatorRepository repository)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (domRoot == null)
            {
                throw new ArgumentNullException(nameof(domRoot));
            }

            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            var planning = _planner.Plan(request);
            var exploration = _explorationBridge.Match(planning.Scenario, domRoot);
            var recordingResults = _recorder.Record(exploration, repository);
            var csharpCode = _csharpGenerator.Generate(planning.Scenario, recordingResults);
            var typeScriptCode = _typeScriptGenerator.Generate(planning.Scenario, recordingResults);

            var result = new IntentAutomationPipelineResult
            {
                Planning = planning,
                Exploration = exploration,
                RecordingResults = recordingResults,
                PlaywrightCSharpTestCode = csharpCode,
                PlaywrightTypeScriptTestCode = typeScriptCode,
            };
            result.Report = IntentFlowReportDocument.FromPipelineResult(result);
            return result;
        }
    }
}
