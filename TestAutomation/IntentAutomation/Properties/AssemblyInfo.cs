using System.Runtime.CompilerServices;

// Exercise shared generator and matching behavior without publishing implementation helpers.
[assembly: InternalsVisibleTo("ScenarioRunner")]
// IntentExecution already depends on IntentAutomation (project reference) - this lets
// IntentWebExecutor call CodeGenerationUtilities.WaitTimeoutMilliseconds directly instead of
// re-implementing Wait-step timeout parsing a second time, which is how #481's seconds/
// milliseconds drift happened in the first place.
[assembly: InternalsVisibleTo("IntentExecution")]
