using System;
using System.Collections.Generic;
namespace AutomationSandbox.Discovery
{
    /// <summary>Mutable traversal limits, filtering policy, and element-error handling for live UIA capture.</summary>
    public sealed class DiscoveryOptions
    {
        /// <summary>Maximum traversal depth relative to the capture root.</summary>
        public int MaxDepth { get; set; } = 25;
        /// <summary>Maximum number of elements visited before capture stops.</summary>
        public int MaxElements { get; set; } = 5000;
        /// <summary>Maximum elapsed time allowed for tree discovery.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
        /// <summary>Whether offscreen elements are included in the captured tree.</summary>
        public bool IncludeOffscreen { get; set; } = false;
        /// <summary>Control types excluded from capture using the configured set&apos;s comparison rules.</summary>
        public ISet<string> IgnoredControlTypes { get; set; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Native class names excluded from capture using the configured set&apos;s comparison rules.</summary>
        public ISet<string> IgnoredClassNames { get; set; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether capture continues after an individual UIA element read fails.</summary>
        public bool ContinueOnElementError { get; set; } = true;
        /// <summary>Creates a fresh options instance with default traversal and filtering settings.</summary>
        public static DiscoveryOptions Default => new();
    }
}
