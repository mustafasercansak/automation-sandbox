namespace UiModel
{
    public sealed class LocatorRepositoryDocument
    {
        // SchemaVersion is deliberately kept at 1: LocatorRepositorySerializer.ValidateSchemaVersion
        // enforces strict equality (schemaVersion != CurrentSchemaVersion throws), so additive,
        // backward-compatible metadata (e.g. DivergedFromHeuristic on history entries) is added
        // as nullable fields without invalidating existing on-disk locator repositories.
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string ApplicationName { get; set; } = "";
        public string Platform { get; set; } = "windows-uia";
        public List<LocatorRecord> Locators { get; set; } = new();
    }

    public sealed class LocatorRecord
    {
        // Stable caller-owned key, for example "CustomerForm.Email".
        // AutomationId is deliberately just snapshot data, not the repository identity.
        public string LocatorKey { get; set; } = "";
        public string Description { get; set; } = "";
        public string TestIntent { get; set; } = "";
        public UiElementInfo Snapshot { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<LocatorHealingHistoryEntry> HealingHistory { get; set; } = new();
    }

    public sealed class LocatorHealingHistoryEntry
    {
        public DateTimeOffset HealedAt { get; set; } = DateTimeOffset.UtcNow;
        public string Source { get; set; } = "";
        public double Score { get; set; }
        public double ConfidenceThreshold { get; set; }
        public double? LlmConfidence { get; set; }
        public string? LlmProviderName { get; set; }
        public UiElementInfo? PreviousSnapshot { get; set; }
        public UiElementInfo? AcceptedSnapshot { get; set; }
        public ScoreComponents? ScoreBreakdown { get; set; }

        // Null on entries saved before issue #6: "unknown / not recorded", not "no divergence".
        // Allows distinguishing legacy entries from explicit agreement (false) or divergence (true).
        public bool? DivergedFromHeuristic { get; set; }
    }
}
