using System.Runtime.CompilerServices;

// Lets ScenarioRunner unit test ContentAnalysisPrompt's Build/ParseIssues directly, without an
// HTTP call, the same convention PlaywrightLiveExploration uses for its own internal seams.
[assembly: InternalsVisibleTo("ScenarioRunner")]
