namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Policy controlling how unsupported or incomplete assertions are handled during test generation.</summary>
    public enum AssertGenerationMode
    {
        // Strict (default): Emits real assertions for known AssertionKinds; emits inconclusive/review failure for unmapped/None kinds.
        /// <summary>Strict (default): Emits real assertions for known AssertionKinds; emits inconclusive/review failure for unmapped/None kinds.</summary>
        Strict,

        // Lenient: Emits real assertions for known AssertionKinds; emits presence check with a // TODO review comment for unmapped/None kinds.
        /// <summary>Lenient: Emits real assertions for known AssertionKinds; emits presence check with a // TODO review comment for unmapped/None kinds.</summary>
        Lenient,

        // Fallback: Emits presence/visibility check for unmapped/None kinds without error.
        /// <summary>Fallback: Emits presence/visibility check for unmapped/None kinds without error.</summary>
        Fallback,
    }
}
