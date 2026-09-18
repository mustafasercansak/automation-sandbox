using System;
using System.Collections.Generic;
using AutomationSandbox.UiModel;
namespace AutomationSandbox.Discovery
{
    /// <summary>Editable capture tree and telemetry accumulated during a live UIA traversal.</summary>
    public sealed class DiscoveryResult
    {
        /// <summary>Captured root and its included descendants.</summary>
        public UiElementInfo Root { get; set; } = new();
        /// <summary>Number of elements visited during traversal.</summary>
        public int VisitedCount { get; set; }
        /// <summary>Number of elements included in the returned tree.</summary>
        public int CapturedCount { get; set; }
        /// <summary>Number of elements excluded by capture policy.</summary>
        public int SkippedCount { get; set; }
        /// <summary>Number of element errors encountered during capture.</summary>
        public int ErrorCount { get; set; }
        /// <summary>Whether traversal encountered the configured depth limit.</summary>
        public bool HitMaxDepth { get; set; }
        /// <summary>Whether traversal stopped at the element-count limit.</summary>
        public bool HitMaxElements { get; set; }
        /// <summary>Whether capture stopped because its time budget expired.</summary>
        public bool TimedOut { get; set; }
        /// <summary>Whether caller cancellation interrupted capture.</summary>
        public bool WasCancelled { get; set; }
        /// <summary>Time spent performing this operation, including retries where applicable.</summary>
        public TimeSpan Elapsed { get; set; }
        /// <summary>Diagnostics accumulated while capturing or skipping elements.</summary>
        public List<string> Warnings { get; set; } = new();
    }
}
