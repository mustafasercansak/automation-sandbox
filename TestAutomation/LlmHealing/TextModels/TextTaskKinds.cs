using System;
using System.Collections.Generic;
using System.Linq;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>The kinds of text task a model can be routed to. The names are what routing configuration and
    /// reports use.</summary>
    public static class TextTaskKinds
    {
        /// <summary>Classify an issue, ask for missing information, suggest labels.</summary>
        public const string IssueTriage = "issue-triage";

        /// <summary>Classify a failed CI run from its log and propose an issue; no repository change.</summary>
        public const string CiFailure = "ci-failure";

        /// <summary>Draft or update a document from supplied source files.</summary>
        public const string Docs = "docs";

        /// <summary>Every known task kind.</summary>
        public static IReadOnlyList<string> All { get; } = new[] { IssueTriage, CiFailure, Docs };

        /// <summary>True when <paramref name="name" /> is a known task kind, ignoring case.</summary>
        public static bool IsKnown(string? name)
        {
            return name != null && All.Any(kind => string.Equals(kind, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The canonical spelling of a known task kind, or null.</summary>
        public static string? Normalize(string? name)
        {
            return name == null ? null : All.FirstOrDefault(kind => string.Equals(kind, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
