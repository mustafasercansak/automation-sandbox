using System;
using System.Collections.Generic;
namespace Discovery
{
    public sealed class DiscoveryOptions
    {
        public int MaxDepth { get; set; } = 25;
        public int MaxElements { get; set; } = 5000;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
        public bool IncludeOffscreen { get; set; } = false;
        public ISet<string> IgnoredControlTypes { get; set; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ISet<string> IgnoredClassNames { get; set; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool ContinueOnElementError { get; set; } = true;
        public static DiscoveryOptions Default => new();
    }
}
