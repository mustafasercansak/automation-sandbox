namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Mutable shortlist and review thresholds for web intent matching.</summary>
    public sealed class IntentExplorationOptions
    {
        /// <summary>Maximum number of ranked candidates retained per scenario step.</summary>
        public int MaxCandidatesPerStep { get; set; } = 5;
        /// <summary>Minimum overall matching score required to avoid manual review.</summary>
        public double ReviewThreshold { get; set; } = 0.35;

        // Minimum semantic overlap score required before an element candidate is accepted
        // without review (issue #5). Calibrated to 0.01: demands at least one non-zero token
        // match while accommodating single-token matches (e.g. "Save" with semantic ~0.043
        // against multi-field intent targets). Re-evaluated with benchmark dataset under #15.
        /// <summary>Minimum semantic overlap score required before an element candidate is accepted without review (issue #5). Calibrated to 0.01: demands at least one non-zero token match while accommodating single-token matches (e.g. &quot;Save&quot; with semantic ~0.043 against multi-field intent targets). Re-evaluated with benchmark dataset under #15.</summary>
        public double MinimumSemanticScore { get; set; } = 0.01;

        // Minimum gap between the best and runner-up candidate scores before an intent match
        // can be accepted without review (issue #5). Reuses the shared #4 margin threshold (0.05).
        /// <summary>Minimum gap between the best and runner-up candidate scores before an intent match can be accepted without review (issue #5). Reuses the shared #4 margin threshold (0.05).</summary>
        public double MinimumCandidateMargin { get; set; } = 0.05;
    }
}
