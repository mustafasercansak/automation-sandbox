using System;
using System.Collections.Generic;
using UiModel;
namespace Discovery
{
    public sealed class DiscoveryResult
    {
        public UiElementInfo Root { get; set; } = new();
        public int VisitedCount { get; set; }
        public int CapturedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ErrorCount { get; set; }
        public bool HitMaxDepth { get; set; }
        public bool HitMaxElements { get; set; }
        public bool TimedOut { get; set; }
        public bool WasCancelled { get; set; }
        public TimeSpan Elapsed { get; set; }
        public List<string> Warnings { get; set; } = new();
    }
}
