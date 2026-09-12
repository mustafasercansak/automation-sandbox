namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Synchronous planning boundary for converting a user goal into an editable intent scenario and review diagnostics.</summary>
    public interface IIntentPlanner
    {
        /// <summary>Produces a scenario and review diagnostics from the supplied application goal and context.</summary>
        IntentPlanningResult Plan(IntentPlanningRequest request);
    }
}
