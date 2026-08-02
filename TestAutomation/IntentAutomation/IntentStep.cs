namespace IntentAutomation
{
    public sealed class IntentStep
    {
        public int Order { get; set; }
        public IntentActionType ActionType { get; set; } = IntentActionType.Unknown;
        public string TestIntent { get; set; } = "";
        public string TargetDescription { get; set; } = "";
        public string Value { get; set; } = "";
        public string ExpectedOutcome { get; set; } = "";
        public string LocatorKey { get; set; } = "";
    }
}
