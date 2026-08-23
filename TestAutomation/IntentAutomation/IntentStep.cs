namespace IntentAutomation
{
    public sealed class IntentStep
    {
        public int Order { get; set; }
        public IntentActionType ActionType { get; set; } = IntentActionType.Unknown;
        // Narrative business context recorded in generated tests, reports, and healing snapshots.
        // Candidate matching deliberately does not use this field.
        public string TestIntent { get; set; } = "";
        // The authoritative free-text description used to match this step to a UI element.
        public string TargetDescription { get; set; } = "";
        public string Value { get; set; } = "";
        // Describes the state expected after the action for reporting and assertion generation.
        // Candidate matching deliberately does not use this field.
        public string ExpectedOutcome { get; set; } = "";
        public AssertionKind AssertionKind { get; set; } = AssertionKind.None;
        public string ExpectedValue { get; set; } = "";
    }
}
