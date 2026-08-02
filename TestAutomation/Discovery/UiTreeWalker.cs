using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using FlaUI.Core.AutomationElements;
using UiModel;
namespace Discovery
{
    public static class UiTreeWalker
    {
        private const int DefaultMaxDepth = 25;
        private const int DefaultMaxElements = 5000;

        // DiscoveryOptions.Default.Timeout (10s) is sized for a typical scoped walk, but
        // BuildTree's IncludeOffscreen=true can pull in thousands of offscreen elements
        // (e.g. a DataGridView's internal cell tree) - confirmed live in CI, where a
        // contended runner timed out mid-walk (Elapsed == 10.00s, Errors=0) after visiting
        // ~1500 elements and never reached later siblings. A caller asking for the full
        // tree needs a budget that matches MaxElements' generosity, not the scoped default.
        private static readonly TimeSpan DefaultBuildTreeTimeout = TimeSpan.FromSeconds(60);

        public static UiElementInfo BuildTree(AutomationElement element, int maxDepth = DefaultMaxDepth, int maxElements = DefaultMaxElements)
        {
            var options = new DiscoveryOptions
            {
                MaxDepth = maxDepth,
                MaxElements = maxElements,
                IncludeOffscreen = true, // BuildTree preserves full tree for legacy calls
                Timeout = DefaultBuildTreeTimeout
            };
            return Discover(element, options).Root ?? new UiElementInfo();
        }

        public static DiscoveryResult Discover(
            AutomationElement element,
            DiscoveryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= DiscoveryOptions.Default;
            ValidateArguments(element, options);
            var result = new DiscoveryResult();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var discoveredRoot = WalkNodeSafely(
                    element,
                    parentControlType: "",
                    parentAutomationId: "",
                    siblingIndex: 0,
                    siblingCount: 1,
                    depth: 0,
                    options,
                    result,
                    stopwatch,
                    cancellationToken,
                    precomputed: null);
                if (discoveredRoot != null)
                {
                    result.Root = discoveredRoot;
                }
                else if (!result.WasCancelled && !result.TimedOut)
                {
                    result.Root = CreateFallbackRootSafely(element);
                }
            }
            finally
            {
                stopwatch.Stop();
                result.Elapsed = stopwatch.Elapsed;
            }

            return result;
        }

        // Caches a child's ControlType/ClassName/BoundingRectangle/IsOffscreen from the
        // pre-filter pass so WalkNode doesn't read the same COM properties a second time -
        // on a tree with thousands of nodes, reading each property twice was enough to push
        // real walks past the default Timeout (observed live in CI).
        private readonly struct PrecomputedProperties
        {
            public PrecomputedProperties(string controlType, string className, BoundingRectangle rect, bool isOffscreen)
            {
                ControlType = controlType;
                ClassName = className;
                Rect = rect;
                IsOffscreen = isOffscreen;
            }

            public string ControlType { get; }
            public string ClassName { get; }
            public BoundingRectangle Rect { get; }
            public bool IsOffscreen { get; }
        }

        private static UiElementInfo? WalkNodeSafely(
            AutomationElement element,
            string parentControlType,
            string parentAutomationId,
            int siblingIndex,
            int siblingCount,
            int depth,
            DiscoveryOptions options,
            DiscoveryResult result,
            Stopwatch stopwatch,
            CancellationToken cancellationToken,
            PrecomputedProperties? precomputed)
        {
            try
            {
                return WalkNode(
                    element,
                    parentControlType,
                    parentAutomationId,
                    siblingIndex,
                    siblingCount,
                    depth,
                    options,
                    result,
                    stopwatch,
                    cancellationToken,
                    precomputed);
            }
            catch (COMException ex)
            {
                HandleElementError(result, options, $"Stale element encountered at depth {depth}", ex);
            }
            catch (InvalidOperationException ex)
            {
                HandleElementError(result, options, $"Invalid operation at depth {depth}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                HandleElementError(result, options, $"Access denied at depth {depth}", ex);
            }

            return null;
        }

        private static UiElementInfo? WalkNode(
            AutomationElement element,
            string parentControlType,
            string parentAutomationId,
            int siblingIndex,
            int siblingCount,
            int depth,
            DiscoveryOptions options,
            DiscoveryResult result,
            Stopwatch stopwatch,
            CancellationToken cancellationToken,
            PrecomputedProperties? precomputed)
        {
            if (ShouldStop(options, result, stopwatch, cancellationToken))
            {
                return null;
            }

            if (result.CapturedCount >= options.MaxElements)
            {
                result.HitMaxElements = true;
                return null;
            }

            result.VisitedCount++;

            // Only the root call (precomputed == null) actually reads these from the element -
            // every child was already read once during the parent's pre-filter pass.
            var controlType = precomputed?.ControlType ?? element.Properties.ControlType.ValueOrDefault.ToString();
            var className = precomputed?.ClassName ?? element.Properties.ClassName.ValueOrDefault ?? "";
            var rect = precomputed?.Rect ?? ToBoundingRectangle(element);
            var isOffscreen = precomputed?.IsOffscreen ?? element.Properties.IsOffscreen.ValueOrDefault;

            // Check ControlType & ClassName filters
            if (depth > 0 &&
                (options.IgnoredControlTypes.Contains(controlType) || options.IgnoredClassNames.Contains(className)))
            {
                result.SkippedCount++;
                return null; // Do not pollute tree with ignored nodes or traverse their subtree
            }

            // Check IncludeOffscreen filter
            if (depth > 0 && !options.IncludeOffscreen && (isOffscreen || rect.Width <= 0 || rect.Height <= 0))
            {
                result.SkippedCount++;
                return null;
            }

            var node = new UiElementInfo
            {
                ControlType = controlType,
                Name = element.Properties.Name.ValueOrDefault ?? "",
                AutomationId = element.Properties.AutomationId.ValueOrDefault ?? "",
                ClassName = className,
                BoundingRectangle = rect,
                ParentControlType = parentControlType,
                ParentAutomationId = parentAutomationId,
                SiblingIndex = siblingIndex,
                SiblingCount = siblingCount,
            };
            result.CapturedCount++;
            if (result.CapturedCount >= options.MaxElements)
            {
                result.HitMaxElements = true;
                return node;
            }

            if (depth >= options.MaxDepth)
            {
                result.HitMaxDepth = true;
                return node;
            }

            if (ShouldStop(options, result, stopwatch, cancellationToken))
            {
                return node;
            }

            var children = FindChildrenSafely(element, options, result);

            // Pre-filter valid children so SiblingCount reflects captured tree consistency -
            // each child's properties are read exactly once here and carried forward into
            // WalkNode via PrecomputedProperties, instead of being read again there.
            var validChildren = new List<(AutomationElement Element, PrecomputedProperties Properties)>();
            foreach (var child in children)
            {
                if (ShouldStop(options, result, stopwatch, cancellationToken))
                {
                    break;
                }

                try
                {
                    var childType = child.Properties.ControlType.ValueOrDefault.ToString();
                    var childClass = child.Properties.ClassName.ValueOrDefault ?? "";
                    var childRect = ToBoundingRectangle(child);
                    var childOffscreen = child.Properties.IsOffscreen.ValueOrDefault;
                    if (options.IgnoredControlTypes.Contains(childType) || options.IgnoredClassNames.Contains(childClass))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    if (!options.IncludeOffscreen && (childOffscreen || childRect.Width <= 0 || childRect.Height <= 0))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    validChildren.Add((child, new PrecomputedProperties(childType, childClass, childRect, childOffscreen)));
                }
                catch (COMException ex)
                {
                    HandleElementError(result, options, $"Stale element encountered while filtering at depth {depth + 1}", ex);
                }
                catch (InvalidOperationException ex)
                {
                    HandleElementError(result, options, $"Invalid operation while filtering at depth {depth + 1}", ex);
                }
                catch (UnauthorizedAccessException ex)
                {
                    HandleElementError(result, options, $"Access denied while filtering at depth {depth + 1}", ex);
                }
            }

            for (var i = 0; i < validChildren.Count; i++)
            {
                if (result.CapturedCount >= options.MaxElements)
                {
                    result.HitMaxElements = true;
                    break;
                }

                if (ShouldStop(options, result, stopwatch, cancellationToken))
                {
                    break;
                }

                var childNode = WalkNodeSafely(
                    validChildren[i].Element,
                    node.ControlType,
                    node.AutomationId,
                    siblingIndex: i,
                    siblingCount: validChildren.Count,
                    depth + 1,
                    options,
                    result,
                    stopwatch,
                    cancellationToken,
                    validChildren[i].Properties);
                if (childNode != null)
                {
                    node.Children.Add(childNode);
                }
            }

            return node;
        }

        private static AutomationElement[] FindChildrenSafely(
            AutomationElement element,
            DiscoveryOptions options,
            DiscoveryResult result)
        {
            try
            {
                return element.FindAllChildren();
            }
            catch (COMException ex)
            {
                HandleElementError(result, options, "COMException while finding children", ex);
                return Array.Empty<AutomationElement>();
            }
            catch (InvalidOperationException ex)
            {
                HandleElementError(result, options, "InvalidOperationException while finding children", ex);
                return Array.Empty<AutomationElement>();
            }
            catch (UnauthorizedAccessException ex)
            {
                HandleElementError(result, options, "UnauthorizedAccessException while finding children", ex);
                return Array.Empty<AutomationElement>();
            }
        }

        private static bool ShouldStop(
            DiscoveryOptions options,
            DiscoveryResult result,
            Stopwatch stopwatch,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.WasCancelled = true;
                return true;
            }

            if (stopwatch.Elapsed >= options.Timeout)
            {
                result.TimedOut = true;
                return true;
            }

            return false;
        }

        private static void HandleElementError(
            DiscoveryResult result,
            DiscoveryOptions options,
            string context,
            Exception exception)
        {
            result.ErrorCount++;
            result.Warnings.Add($"{context}: {exception.Message}");
            if (!options.ContinueOnElementError)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }

        private static void ValidateArguments(AutomationElement element, DiscoveryOptions options)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            if (options.MaxDepth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options.MaxDepth), "MaxDepth must be zero or greater.");
            }

            if (options.MaxElements < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options.MaxElements), "MaxElements must be at least one.");
            }

            if (options.Timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options.Timeout), "Timeout must be greater than zero.");
            }

            if (options.IgnoredControlTypes == null)
            {
                throw new ArgumentNullException(nameof(options.IgnoredControlTypes));
            }

            if (options.IgnoredClassNames == null)
            {
                throw new ArgumentNullException(nameof(options.IgnoredClassNames));
            }
        }

        private static UiElementInfo CreateFallbackRootSafely(AutomationElement element)
        {
            var fallback = new UiElementInfo();
            TryPopulateFallback(() => fallback.ControlType = element.Properties.ControlType.ValueOrDefault.ToString());
            TryPopulateFallback(() => fallback.Name = element.Properties.Name.ValueOrDefault ?? "");
            TryPopulateFallback(() => fallback.AutomationId = element.Properties.AutomationId.ValueOrDefault ?? "");
            TryPopulateFallback(() => fallback.ClassName = element.Properties.ClassName.ValueOrDefault ?? "");
            TryPopulateFallback(() => fallback.BoundingRectangle = ToBoundingRectangle(element));
            return fallback;
        }

        private static void TryPopulateFallback(Action populate)
        {
            try
            {
                populate();
            }
            catch (Exception ex) when (
                ex is COMException ||
                ex is InvalidOperationException ||
                ex is UnauthorizedAccessException)
            {
                // A stale root must not turn the non-null fallback contract into another failure.
            }
        }

        private static BoundingRectangle ToBoundingRectangle(AutomationElement element)
        {
            var rect = element.Properties.BoundingRectangle.ValueOrDefault;
            return new BoundingRectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }
    }
}
