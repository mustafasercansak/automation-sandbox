using System.Runtime.CompilerServices;

// Lets ScenarioRunner drive PlaywrightLiveExplorer with fake IPlaywright/IBrowser instances
// (disposal fault-injection, #306) without launching a real browser.
[assembly: InternalsVisibleTo("ScenarioRunner")]
