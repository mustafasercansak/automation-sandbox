namespace AutomationSandbox.UiModel
{
    /// <summary>Editable versioned locator document used to load, migrate, and save repository records.</summary>
    public sealed class LocatorRepositoryDocument
    {
        // SchemaVersion is deliberately kept at 1: LocatorRepositorySerializer.ValidateSchemaVersion
        // enforces strict equality (schemaVersion != CurrentSchemaVersion throws), so additive,
        // backward-compatible metadata (e.g. DivergedFromHeuristic on history entries) is added
        // as nullable fields without invalidating existing on-disk locator repositories.
        /// <summary>Newest document schema version this build can read and write.</summary>
        public const int CurrentSchemaVersion = 1;
        /// <summary>Version of the persisted document shape; newer unsupported versions are rejected by the reader.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        /// <summary>Application label used to identify the captured or tested system in reports and storage.</summary>
        public string ApplicationName { get; set; } = "";
        /// <summary>Platform label recorded with the document, normally web or desktop.</summary>
        public string Platform { get; set; } = "windows-uia";
        /// <summary>Editable locator records keyed by their logical names.</summary>
        public List<LocatorRecord> Locators { get; set; } = new();
    }

    /// <summary>Editable stored locator evidence, business context, and accepted healing history.</summary>
    public sealed class LocatorRecord
    {
        // Stable caller-owned key, for example "CustomerForm.Email".
        // AutomationId is deliberately just snapshot data, not the repository identity.
        /// <summary>Logical repository key identifying a locator independently of its current automation identifier.</summary>
        public string LocatorKey { get; set; } = "";
        /// <summary>Human-readable description retained with the locator record.</summary>
        public string Description { get; set; } = "";
        /// <summary>Narrative business intent retained for prompts, generated tests, and reports.</summary>
        public string TestIntent { get; set; } = "";
        /// <summary>Stored locator evidence used as the expected element on the next resolution attempt.</summary>
        public UiElementInfo Snapshot { get; set; } = new();
        /// <summary>Timestamp when this record was first created.</summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Timestamp of the most recent update to this record.</summary>
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Accepted healing events retained for this locator.</summary>
        public List<LocatorHealingHistoryEntry> HealingHistory { get; set; } = new();
    }

    /// <summary>Persisted record of an accepted heal and the evidence available when it was recorded.</summary>
    public sealed class LocatorHealingHistoryEntry
    {
        /// <summary>Timestamp at which the resolution event was recorded.</summary>
        public DateTimeOffset HealedAt { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Resolution mechanism that proposed the candidate.</summary>
        public string Source { get; set; } = "";
        /// <summary>Structural similarity score; it is not a calibrated correctness probability.</summary>
        public double Score { get; set; }
        /// <summary>Minimum structural score required for heuristic acceptance.</summary>
        public double ConfidenceThreshold { get; set; }
        /// <summary>Provider-reported confidence retained for diagnostics only; it never controls acceptance.</summary>
        public double? LlmConfidence { get; set; }
        /// <summary>Provider name or names recorded for an LLM proposal; absent for heuristic-only resolution.</summary>
        public string? LlmProviderName { get; set; }
        /// <summary>Locator evidence before this resolution attempt.</summary>
        public UiElementInfo? PreviousSnapshot { get; set; }
        /// <summary>Snapshot retained for an accepted heal; may be absent when acceptance was not recorded.</summary>
        public UiElementInfo? AcceptedSnapshot { get; set; }
        /// <summary>Per-signal explanation of the proposed candidate&apos;s structural score.</summary>
        public ScoreComponents? ScoreBreakdown { get; set; }

        // Null on entries saved before issue #6: "unknown / not recorded", not "no divergence".
        // Allows distinguishing legacy entries from explicit agreement (false) or divergence (true).
        /// <summary>Whether the LLM proposed a different candidate from the heuristic winner.</summary>
        public bool? DivergedFromHeuristic { get; set; }
    }
}
