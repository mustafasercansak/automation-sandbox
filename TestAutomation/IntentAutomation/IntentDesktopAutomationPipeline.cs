using System;
using AutomationSandbox.UiModel;

namespace AutomationSandbox.IntentAutomation
{
    // Desktop counterpart to IntentAutomationPipeline: plans intent steps (the same IIntentPlanner
    // works for both platforms), matches them against a live UiElementInfo tree, records accepted
    // locators, and generates an xUnit + FlaUI test skeleton.

    /// <summary>Desktop counterpart to IntentAutomationPipeline: plans intent steps (the same IIntentPlanner works for both platforms), matches them against a live UiElementInfo tree, records accepted locators, and generates an xUnit + FlaUI test skeleton.</summary>
    public sealed class IntentDesktopAutomationPipeline
    {
        private readonly IIntentPlanner _planner;
        private readonly IntentDesktopExplorationBridge _explorationBridge;
        private readonly IntentDesktopLocatorRepositoryRecorder _recorder;
        private readonly FlaUiCSharpTestGenerator _generator;

        /// <summary>Configures the desktop pipeline planner and stage options, using deterministic planning when no planner is supplied.</summary>
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

        /// <summary>Plans the desktop request, matches against the supplied captured tree, records eligible locators, and generates FlaUI test source and a flow report.</summary>
        public IntentDesktopAutomationPipelineResult Run(
            IntentDesktopPlanningRequest request,
            UiElementInfo desktopRoot,
            LocatorRepository repository)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return Run(request.ToPlanningRequest(), desktopRoot, repository);
        }

        /// <summary>Plans the desktop request, matches against the supplied captured tree, records eligible locators, and generates FlaUI test source and a flow report.</summary>
        public IntentDesktopAutomationPipelineResult Run(
            string goal,
            UiElementInfo desktopRoot,
            LocatorRepository repository)
        {
            if (string.IsNullOrWhiteSpace(goal))
            {
                throw new ArgumentException("Goal must not be empty.", nameof(goal));
            }

            var request = new IntentDesktopPlanningRequest { Goal = goal.Trim() };
            return Run(request, desktopRoot, repository);
        }

        /// <summary>Plans the desktop request, matches against the supplied captured tree, records eligible locators, and generates FlaUI test source and a flow report.</summary>
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

            var result = new IntentDesktopAutomationPipelineResult
            {
                Planning = planning,
                Exploration = exploration,
                RecordingResults = recordingResults,
                FlaUiCSharpTestCode = code,
            };
            result.Report = IntentFlowReportDocument.FromDesktopPipelineResult(result);
            return result;
        }
    }
}
