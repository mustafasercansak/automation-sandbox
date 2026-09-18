namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Supported intent actions shared by planners, matchers, and test generators.</summary>
    public enum IntentActionType
    {
        /// <summary>No supported action could be identified; the step requires review.</summary>
        Unknown = 0,
        /// <summary>Navigate to the URL or destination supplied by the step.</summary>
        Navigate = 1,
        /// <summary>Fill an editable target with the step value.</summary>
        Fill = 2,
        /// <summary>Activate the matched target with a click.</summary>
        Click = 3,
        /// <summary>Select an option in a selectable target.</summary>
        Select = 4,
        /// <summary>Verify the condition described by the step&apos;s assertion kind and expected value.</summary>
        Assert = 5,
        /// <summary>Move the pointer over the matched target.</summary>
        Hover = 6,
        /// <summary>Assign the supplied file path to a supported upload control.</summary>
        UploadFile = 7,
        /// <summary>Send the specified key to the target or supported page context.</summary>
        PressKey = 8,
        /// <summary>Wait for the target or state described by the step, subject to its timeout.</summary>
        Wait = 9,
        // Check/Uncheck are appended without renumbering: the numeric values are a
        // persisted contract (IntentPlannerTests pins (int)Select == 4).
        /// <summary>Check/Uncheck are appended without renumbering: the numeric values are a persisted contract (IntentPlannerTests pins (int)Select == 4).</summary>
        Check = 10,
        /// <summary>Clear a checkbox; radio buttons cannot be unchecked directly.</summary>
        Uncheck = 11,
    }
}
