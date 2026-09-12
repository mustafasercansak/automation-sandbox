namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Editable ordered action with matching text, business context, and assertion metadata; planners and reviewers may refine it before generation.</summary>
    public sealed class IntentStep
    {
        /// <summary>Execution order assigned to this scenario step.</summary>
        public int Order { get; set; }
        /// <summary>Supported operation this step requests.</summary>
        public IntentActionType ActionType { get; set; } = IntentActionType.Unknown;
        // Narrative business context recorded in generated tests, reports, and healing snapshots.
        // Candidate matching deliberately does not use this field.
        /// <summary>Narrative business intent retained for prompts, generated tests, and reports.</summary>
        public string TestIntent { get; set; } = "";
        // The authoritative free-text description used to match this step to a UI element.
        /// <summary>The authoritative free-text description used to match this step to a UI element.</summary>
        public string TargetDescription { get; set; } = "";
        /// <summary>Action-specific payload, such as text, an option, a path, a key, or a wait timeout.</summary>
        public string Value { get; set; } = "";
        // Describes the state expected after the action for reporting and assertion generation.
        // Candidate matching deliberately does not use this field.
        /// <summary>Describes the state expected after the action for reporting and assertion generation. Candidate matching deliberately does not use this field.</summary>
        public string ExpectedOutcome { get; set; } = "";
        /// <summary>Condition to verify when this step represents an assertion.</summary>
        public AssertionKind AssertionKind { get; set; } = AssertionKind.None;
        /// <summary>Expected assertion operand, interpreted according to AssertionKind.</summary>
        public string ExpectedValue { get; set; } = "";
    }
}
