namespace IntentAutomation
{
    public enum IntentActionType
    {
        Unknown = 0,
        Navigate = 1,
        Fill = 2,
        Click = 3,
        Select = 4,
        Assert = 5,
        Hover = 6,
        UploadFile = 7,
        PressKey = 8,
        Wait = 9,
        // Check/Uncheck are appended without renumbering: the numeric values are a
        // persisted contract (IntentPlannerTests pins (int)Select == 4).
        Check = 10,
        Uncheck = 11,
    }
}
