namespace SelfHealing
{
    /// <summary>
    /// Governs how <see cref="SelfHealingEngine"/> responds to locator-resolution failures,
    /// balancing execution velocity against the risk of executing or persisting false-positive heals.
    /// </summary>
    public enum HealingMode
    {
        /// <summary>
        /// Analyzes the UI tree and records/logs proposed candidate matches to telemetry and reports,
        /// but never retries the failed action and never persists changes to the locator repository.
        /// Rethrows an exception containing candidate diagnostics so test failures remain visible.
        /// </summary>
        Observe,

        /// <summary>
        /// Default mode. Routes broken locators to an offline manual review queue by recording
        /// candidate resolution telemetry (marked for review) and failing closed without mutating
        /// the application state or persisting unverified locators.
        /// </summary>
        Review,

        /// <summary>
        /// Opt-in autonomous healing. When a confident candidate match is found, retries the failed
        /// action against the healed element, and upon successful retry, automatically commits the
        /// new locator snapshot and healing history to the locator repository.
        /// </summary>
        AutoHeal,

        /// <summary>
        /// Strictly disables self-healing and candidate discovery upon locator failure. Re-throws
        /// the original locator exception immediately without tree capture or LLM fallback.
        /// </summary>
        FailClosed,
    }
}
