namespace AutomationSandbox.IntentAutomation
{
    public interface IIntentPlanner
    {
        IntentPlanningResult Plan(IntentPlanningRequest request);
    }
}
