namespace IntentAutomation
{
    public enum AssertGenerationMode
    {
        // Strict (default): Emits real assertions for known AssertionKinds; emits inconclusive/review failure for unmapped/None kinds.
        Strict,

        // Lenient: Emits real assertions for known AssertionKinds; emits presence check with a // TODO review comment for unmapped/None kinds.
        Lenient,

        // Fallback: Emits presence/visibility check for unmapped/None kinds without error.
        Fallback,
    }
}
