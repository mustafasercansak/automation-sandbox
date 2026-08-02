namespace IntentAutomation
{
    public sealed class IntentExplorationOptions
    {
        public int MaxCandidatesPerStep { get; set; } = 5;
        public double ReviewThreshold { get; set; } = 0.35;
    }
}
