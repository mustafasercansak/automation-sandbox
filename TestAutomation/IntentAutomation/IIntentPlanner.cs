namespace IntentAutomation
{
    public interface IIntentPlanner
    {
        IntentPlanningResult Plan(IntentPlanningRequest request);
    }
}
