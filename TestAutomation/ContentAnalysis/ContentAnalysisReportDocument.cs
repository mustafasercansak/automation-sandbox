using System;
using System.Collections.Generic;

namespace AutomationSandbox.ContentAnalysis
{
    /// <summary>Versioned collection of content-analysis entries accumulated across one or more page scans.</summary>
    public sealed class ContentAnalysisReportDocument
    {
        // Schema v1: initial page-scan report, one entry per analyzed URL.
        /// <summary>Newest document schema version this build can read and write.</summary>
        public const int CurrentSchemaVersion = 1;
        /// <summary>Version of the persisted document shape.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        /// <summary>Timestamp when this document snapshot was assembled from the underlying log.</summary>
        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Every page scan recorded so far, in recording order.</summary>
        public List<ContentAnalysisReportEntry> Entries { get; set; } = new List<ContentAnalysisReportEntry>();
    }
}
