namespace IntentAutomation
{
    public sealed class IntentDesktopExplorationOptions
    {
        public int MaxCandidatesPerStep { get; set; } = 5;
        public double ReviewThreshold { get; set; } = 0.35;

        // Minimum semantic overlap score required before a desktop candidate is accepted
        // without review (issue #5). Calibrated to 0.01: demands at least one non-zero token
        // match while accommodating single-token matches (e.g. "btnSave" with semantic ~0.043
        // against multi-field intent targets). Re-evaluated with benchmark dataset under #15.
        public double MinimumSemanticScore { get; set; } = 0.01;

        // Minimum gap between the best and runner-up candidate scores before an intent match
        // can be accepted without review (issue #5). Reuses the shared #4 margin threshold (0.05).
        public double MinimumCandidateMargin { get; set; } = 0.05;
    }
}
