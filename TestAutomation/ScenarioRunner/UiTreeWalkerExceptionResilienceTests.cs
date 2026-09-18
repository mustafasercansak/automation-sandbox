using System;
using System.Drawing;
using AutomationSandbox.Discovery;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.AutomationElements.Infrastructure;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.Core.Identifiers;
using FlaUI.Core.Patterns;
using FlaUI.UIA3;
using Xunit;
namespace ScenarioRunner
{
    /// <summary>
    /// Covers #425: every per-element catch in UiTreeWalker must record a warning and keep
    /// walking when ContinueOnElementError is set, for ANY exception type thrown while
    /// reading a FlaUI element's properties or children - not just the three COM-adjacent
    /// types it used to whitelist (COMException, InvalidOperationException,
    /// UnauthorizedAccessException). There is no mocking library in this repository (see
    /// AGENTS.md's "Modern Package &amp; Security Standard"), so these tests build a minimal,
    /// fully in-memory FlaUI element graph by subclassing FlaUI.Core's own
    /// FrameworkAutomationElementBase/AutomationBase abstract classes instead of talking to a
    /// real UIA-backed window. Only the members UiTreeWalker actually touches
    /// (Properties.ControlType/Name/AutomationId/ClassName/BoundingRectangle/IsOffscreen and
    /// FindAllChildren) are given real behavior; everything else throws NotSupportedException
    /// because the walker's traversal never reaches it.
    /// </summary>
    public class UiTreeWalkerExceptionResilienceTests
    {
        [Fact]
        public void Discover_UncommonExceptionFromOneElement_RecordsWarningAndKeepsSiblings()
        {
            using var automation = new UIA3Automation();
            var testAutomation = new TestAutomationBase(automation);

            var goodChild = BuildElement(testAutomation, new TestElementSpec
            {
                Name = "GoodChild",
                AutomationId = "good-child",
                ControlTypeValue = ControlType.Button,
            });

            var badChild = BuildElement(testAutomation, new TestElementSpec
            {
                Name = "BadChild",
                AutomationId = "bad-child",
                ControlTypeValue = ControlType.Button,
                // Precomputed properties (ControlType/ClassName/BoundingRectangle/IsOffscreen)
                // are read fine during the parent's pre-filter pass; only Name - re-read once
                // WalkNode actually visits this child - throws. This exercises the primary
                // WalkNodeSafely catch (#425), not the filtering-loop catch a level up.
                ThrowOnPropertyId = TestElementPropertyIds.Instance.Name.Id,
                ExceptionToThrow = new PurposefullyUncommonException("simulated uncommon accessor failure"),
            });

            var root = BuildElement(testAutomation, new TestElementSpec
            {
                Name = "Root",
                AutomationId = "root",
                ControlTypeValue = ControlType.Window,
                Children = new[] { goodChild, badChild },
            });

            var options = new DiscoveryOptions
            {
                MaxDepth = 5,
                MaxElements = 50,
                Timeout = TimeSpan.FromSeconds(5),
                ContinueOnElementError = true,
            };

            var result = UiTreeWalker.Discover(root, options);

            Assert.NotNull(result.Root);
            Assert.Equal("root", result.Root!.AutomationId);
            var survivingChild = Assert.Single(result.Root.Children);
            Assert.Equal("good-child", survivingChild.AutomationId);
            Assert.True(result.ErrorCount >= 1, "Expected the uncommon exception to be recorded as an error, not silently dropped.");
            // string.Contains(string, StringComparison) has no net48 overload (AGENTS.md);
            // IndexOf is the cross-target-safe equivalent.
            Assert.Contains(result.Warnings, w => w.IndexOf(nameof(PurposefullyUncommonException), StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void Discover_UncommonException_StillPropagatesWhenContinueOnElementErrorIsDisabled()
        {
            using var automation = new UIA3Automation();
            var testAutomation = new TestAutomationBase(automation);

            var badChild = BuildElement(testAutomation, new TestElementSpec
            {
                Name = "BadChild",
                AutomationId = "bad-child",
                ControlTypeValue = ControlType.Button,
                ThrowOnPropertyId = TestElementPropertyIds.Instance.Name.Id,
                ExceptionToThrow = new PurposefullyUncommonException("simulated uncommon accessor failure"),
            });

            var root = BuildElement(testAutomation, new TestElementSpec
            {
                Name = "Root",
                AutomationId = "root",
                ControlTypeValue = ControlType.Window,
                Children = new[] { badChild },
            });

            var options = new DiscoveryOptions
            {
                MaxDepth = 5,
                MaxElements = 50,
                Timeout = TimeSpan.FromSeconds(5),
                ContinueOnElementError = false,
            };

            // The broadened catch must still honor the opt-out: with ContinueOnElementError
            // disabled, the original exception type is expected to propagate unchanged.
            Assert.Throws<PurposefullyUncommonException>(() => UiTreeWalker.Discover(root, options));
        }

        private static AutomationElement BuildElement(AutomationBase automation, TestElementSpec spec)
        {
            return new AutomationElement(new TestFrameworkAutomationElement(automation, spec));
        }

        // A deliberately unusual exception type: not COMException, not
        // InvalidOperationException, not UnauthorizedAccessException - proving the walker's
        // catch is now genuinely broad rather than a fourth hardcoded type.
        private sealed class PurposefullyUncommonException : Exception
        {
            public PurposefullyUncommonException(string message) : base(message)
            {
            }
        }

        private sealed class TestElementSpec
        {
            public string Name { get; set; } = "";
            public string AutomationId { get; set; } = "";
            public string ClassName { get; set; } = "";
            public ControlType ControlTypeValue { get; set; } = ControlType.Pane;
            public Rectangle BoundingRectangleValue { get; set; } = new Rectangle(0, 0, 10, 10);
            public bool IsOffscreenValue { get; set; }
            public AutomationElement[] Children { get; set; } = Array.Empty<AutomationElement>();
            public int? ThrowOnPropertyId { get; set; }
            public Exception? ExceptionToThrow { get; set; }
        }

        // Supplies distinct, self-owned PropertyIds for exactly the six properties
        // UiTreeWalker reads (ControlType, Name, AutomationId, ClassName,
        // BoundingRectangle, IsOffscreen). Every other member of this interface is never
        // touched by the walker, so it returns a shared placeholder id.
        private sealed class TestElementPropertyIds : IAutomationElementPropertyIds
        {
            public static readonly TestElementPropertyIds Instance = new TestElementPropertyIds();

            private static readonly PropertyId Placeholder = new PropertyId(0, "Unused");

            public PropertyId ControlType { get; } = new PropertyId(1, "ControlType");
            public PropertyId Name { get; } = new PropertyId(2, "Name");
            public PropertyId AutomationId { get; } = new PropertyId(3, "AutomationId");
            public PropertyId ClassName { get; } = new PropertyId(4, "ClassName");
            public PropertyId BoundingRectangle { get; } = new PropertyId(5, "BoundingRectangle");
            public PropertyId IsOffscreen { get; } = new PropertyId(6, "IsOffscreen");

            public PropertyId AcceleratorKey => Placeholder;
            public PropertyId AccessKey => Placeholder;
            public PropertyId AnnotationObjects => Placeholder;
            public PropertyId AnnotationTypes => Placeholder;
            public PropertyId AriaProperties => Placeholder;
            public PropertyId AriaRole => Placeholder;
            public PropertyId CenterPoint => Placeholder;
            public PropertyId ClickablePoint => Placeholder;
            public PropertyId ControllerFor => Placeholder;
            public PropertyId Culture => Placeholder;
            public PropertyId DescribedBy => Placeholder;
            public PropertyId FillColor => Placeholder;
            public PropertyId FillType => Placeholder;
            public PropertyId FlowsFrom => Placeholder;
            public PropertyId FlowsTo => Placeholder;
            public PropertyId FrameworkId => Placeholder;
            public PropertyId FullDescription => Placeholder;
            public PropertyId HasKeyboardFocus => Placeholder;
            public PropertyId HeadingLevel => Placeholder;
            public PropertyId HelpText => Placeholder;
            public PropertyId IsContentElement => Placeholder;
            public PropertyId IsControlElement => Placeholder;
            public PropertyId IsDataValidForForm => Placeholder;
            public PropertyId IsDialog => Placeholder;
            public PropertyId IsEnabled => Placeholder;
            public PropertyId IsKeyboardFocusable => Placeholder;
            public PropertyId IsPassword => Placeholder;
            public PropertyId IsPeripheral => Placeholder;
            public PropertyId IsRequiredForForm => Placeholder;
            public PropertyId ItemStatus => Placeholder;
            public PropertyId ItemType => Placeholder;
            public PropertyId LabeledBy => Placeholder;
            public PropertyId LandmarkType => Placeholder;
            public PropertyId Level => Placeholder;
            public PropertyId LiveSetting => Placeholder;
            public PropertyId LocalizedControlType => Placeholder;
            public PropertyId LocalizedLandmarkType => Placeholder;
            public PropertyId NativeWindowHandle => Placeholder;
            public PropertyId OptimizeForVisualContent => Placeholder;
            public PropertyId Orientation => Placeholder;
            public PropertyId OutlineColor => Placeholder;
            public PropertyId OutlineThickness => Placeholder;
            public PropertyId PositionInSet => Placeholder;
            public PropertyId ProcessId => Placeholder;
            public PropertyId ProviderDescription => Placeholder;
            public PropertyId Rotation => Placeholder;
            public PropertyId RuntimeId => Placeholder;
            public PropertyId Size => Placeholder;
            public PropertyId SizeOfSet => Placeholder;
            public PropertyId VisualEffects => Placeholder;
        }

        // The rest of IPropertyLibrary (pattern-availability and per-pattern property ids) is
        // never consulted by UiTreeWalker, which only ever reads Automation.PropertyLibrary.
        // Element - so every other sub-library returns null and is never dereferenced.
        private sealed class TestPropertyLibrary : IPropertyLibrary
        {
            public static readonly TestPropertyLibrary Instance = new TestPropertyLibrary();

            public IAutomationElementPatternAvailabilityPropertyIds PatternAvailability => null!;
            public IAutomationElementPropertyIds Element => TestElementPropertyIds.Instance;
            public IAnnotationPatternPropertyIds Annotation => null!;
            public IDockPatternPropertyIds Dock => null!;
            public IDragPatternPropertyIds Drag => null!;
            public IDropTargetPatternPropertyIds DropTarget => null!;
            public IExpandCollapsePatternPropertyIds ExpandCollapse => null!;
            public IGridItemPatternPropertyIds GridItem => null!;
            public IGridPatternPropertyIds Grid => null!;
            public ILegacyIAccessiblePatternPropertyIds LegacyIAccessible => null!;
            public IMultipleViewPatternPropertyIds MultipleView => null!;
            public IRangeValuePatternPropertyIds RangeValue => null!;
            public IScrollPatternPropertyIds Scroll => null!;
            public ISelection2PatternPropertyIds Selection2 => null!;
            public ISelectionItemPatternPropertyIds SelectionItem => null!;
            public ISelectionPatternPropertyIds Selection => null!;
            public ISpreadsheetItemPatternPropertyIds SpreadsheetItem => null!;
            public IStylesPatternPropertyIds Styles => null!;
            public ITableItemPatternPropertyIds TableItem => null!;
            public ITablePatternPropertyIds Table => null!;
            public ITogglePatternPropertyIds Toggle => null!;
            public ITransform2PatternPropertyIds Transform2 => null!;
            public ITransformPatternPropertyIds Transform => null!;
            public IValuePatternPropertyIds Value => null!;
            public IWindowPatternPropertyIds Window => null!;
        }

        // AutomationBase requires an IEventLibrary/IPatternLibrary/ITextAttributeLibrary too
        // (its constructor eagerly reads PatternLibrary.AllForCurrentFramework), and those
        // interfaces are just as large as IPropertyLibrary for no test benefit - so this
        // reuses the real ones from a throwaway UIA3Automation instance (already proven safe
        // to construct in this CI environment by every existing live test) and only supplies
        // a custom PropertyLibrary.
        private sealed class TestAutomationBase : AutomationBase
        {
            private readonly object _notSupportedValue;

            public TestAutomationBase(UIA3Automation real)
                : base(TestPropertyLibrary.Instance, real.EventLibrary, real.PatternLibrary, real.TextAttributeLibrary)
            {
                _notSupportedValue = real.NotSupportedValue;
            }

            public override ITreeWalkerFactory TreeWalkerFactory => throw new NotSupportedException();
            public override AutomationType AutomationType => throw new NotSupportedException();
            public override object NotSupportedValue => _notSupportedValue;
            public override object MixedAttributeValue => throw new NotSupportedException();
            public override TimeSpan TransactionTimeout { get; set; }
            public override TimeSpan ConnectionTimeout { get; set; }
            public override ConnectionRecoveryBehaviorOptions ConnectionRecoveryBehavior { get; set; }
            public override CoalesceEventsOptions CoalesceEvents { get; set; }

            public override AutomationElement GetDesktop() => throw new NotSupportedException();
            public override AutomationElement FromPoint(Point point) => throw new NotSupportedException();
            public override AutomationElement FromHandle(IntPtr hwnd) => throw new NotSupportedException();
            public override AutomationElement FocusedElement() => throw new NotSupportedException();
            public override FocusChangedEventHandlerBase RegisterFocusChangedEvent(Action<AutomationElement> action) => throw new NotSupportedException();
            public override void UnregisterFocusChangedEvent(FocusChangedEventHandlerBase eventHandler) => throw new NotSupportedException();
            public override void UnregisterAllEvents() => throw new NotSupportedException();
            public override bool Compare(AutomationElement element1, AutomationElement element2) => throw new NotSupportedException();
        }

        // The element under test. Only InternalGetPropertyValue and FindAll have real
        // behavior - the two hooks UiTreeWalker actually calls (via Properties.* and
        // FindAllChildren()). Everything else FrameworkAutomationElementBase declares
        // abstract (patterns, caching, events, focus, ...) is outside the walker's traversal
        // and throws if ever reached, so a future change relying on it fails loudly here
        // instead of silently returning a meaningless default.
        private sealed class TestFrameworkAutomationElement : FrameworkAutomationElementBase
        {
            private readonly TestElementSpec _spec;

            public TestFrameworkAutomationElement(AutomationBase automation, TestElementSpec spec)
                : base(automation)
            {
                _spec = spec;
            }

            protected override object InternalGetPropertyValue(int propertyId, bool cached, bool useDefaultIfNotSupported)
            {
                if (_spec.ThrowOnPropertyId.HasValue && propertyId == _spec.ThrowOnPropertyId.Value)
                {
                    throw _spec.ExceptionToThrow ?? new InvalidOperationException("Test fake configured to throw but no exception was supplied.");
                }

                var ids = TestElementPropertyIds.Instance;
                if (propertyId == ids.ControlType.Id) return _spec.ControlTypeValue;
                if (propertyId == ids.Name.Id) return _spec.Name;
                if (propertyId == ids.AutomationId.Id) return _spec.AutomationId;
                if (propertyId == ids.ClassName.Id) return _spec.ClassName;
                if (propertyId == ids.BoundingRectangle.Id) return _spec.BoundingRectangleValue;
                if (propertyId == ids.IsOffscreen.Id) return _spec.IsOffscreenValue;
                throw new NotSupportedException($"Test fake does not implement property id {propertyId}.");
            }

            public override AutomationElement[] FindAll(TreeScope treeScope, ConditionBase condition) => _spec.Children;

            public override void SetFocus() => throw new NotSupportedException();
            protected override object InternalGetPattern(int patternId, bool cached) => throw new NotSupportedException();
            public override AutomationElement? FindFirst(TreeScope treeScope, ConditionBase condition) => throw new NotSupportedException();
            public override AutomationElement[] FindAllWithOptions(TreeScope treeScope, ConditionBase condition, TreeTraversalOptions traversalOptions, AutomationElement root) => throw new NotSupportedException();
            public override AutomationElement? FindFirstWithOptions(TreeScope treeScope, ConditionBase condition, TreeTraversalOptions traversalOptions, AutomationElement root) => throw new NotSupportedException();
            public override AutomationElement? FindAt(TreeScope treeScope, int index, ConditionBase condition) => throw new NotSupportedException();
            public override bool TryGetClickablePoint(out Point point) => throw new NotSupportedException();
            public override ActiveTextPositionChangedEventHandlerBase RegisterActiveTextPositionChangedEvent(TreeScope treeScope, Action<AutomationElement, ITextRange> action) => throw new NotSupportedException();
            public override AutomationEventHandlerBase RegisterAutomationEvent(EventId @event, TreeScope treeScope, Action<AutomationElement, EventId> action) => throw new NotSupportedException();
            public override PropertyChangedEventHandlerBase RegisterPropertyChangedEvent(TreeScope treeScope, Action<AutomationElement, PropertyId, object> action, PropertyId[] properties) => throw new NotSupportedException();
            public override StructureChangedEventHandlerBase RegisterStructureChangedEvent(TreeScope treeScope, Action<AutomationElement, StructureChangeType, int[]> action) => throw new NotSupportedException();
            public override NotificationEventHandlerBase RegisterNotificationEvent(TreeScope treeScope, Action<AutomationElement, NotificationKind, NotificationProcessing, string, string> action) => throw new NotSupportedException();
            public override TextEditTextChangedEventHandlerBase RegisterTextEditTextChangedEventHandler(TreeScope treeScope, TextEditChangeType textEditChangeType, Action<AutomationElement, TextEditChangeType, string[]> action) => throw new NotSupportedException();
            public override void UnregisterActiveTextPositionChangedEventHandler(ActiveTextPositionChangedEventHandlerBase eventHandler) => throw new NotSupportedException();
            public override void UnregisterAutomationEventHandler(AutomationEventHandlerBase eventHandler) => throw new NotSupportedException();
            public override void UnregisterPropertyChangedEventHandler(PropertyChangedEventHandlerBase eventHandler) => throw new NotSupportedException();
            public override void UnregisterStructureChangedEventHandler(StructureChangedEventHandlerBase eventHandler) => throw new NotSupportedException();
            public override void UnregisterNotificationEventHandler(NotificationEventHandlerBase eventHandler) => throw new NotSupportedException();
            public override void UnregisterTextEditTextChangedEventHandler(TextEditTextChangedEventHandlerBase eventHandler) => throw new NotSupportedException();
            public override PatternId[] GetSupportedPatterns() => throw new NotSupportedException();
            public override PropertyId[] GetSupportedProperties() => throw new NotSupportedException();
            public override AutomationElement? GetUpdatedCache() => throw new NotSupportedException();
            public override AutomationElement[] GetCachedChildren() => throw new NotSupportedException();
            public override AutomationElement GetCachedParent() => throw new NotSupportedException();
            public override object GetCurrentMetadataValue(PropertyId targetId, int metadataId) => throw new NotSupportedException();

            // Pattern support is never touched by UiTreeWalker's traversal (it only reads
            // Properties.* and FindAllChildren()), so every pattern initializer is an
            // intentional stub.
            protected override IAutomationPattern<IAnnotationPattern> InitializeAnnotationPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IDockPattern> InitializeDockPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IDragPattern> InitializeDragPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IDropTargetPattern> InitializeDropTargetPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IExpandCollapsePattern> InitializeExpandCollapsePattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IGridItemPattern> InitializeGridItemPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IGridPattern> InitializeGridPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IInvokePattern> InitializeInvokePattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IItemContainerPattern> InitializeItemContainerPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ILegacyIAccessiblePattern> InitializeLegacyIAccessiblePattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IMultipleViewPattern> InitializeMultipleViewPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IObjectModelPattern> InitializeObjectModelPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IRangeValuePattern> InitializeRangeValuePattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IScrollItemPattern> InitializeScrollItemPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IScrollPattern> InitializeScrollPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ISelectionItemPattern> InitializeSelectionItemPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ISelection2Pattern> InitializeSelection2Pattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ISelectionPattern> InitializeSelectionPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ISpreadsheetItemPattern> InitializeSpreadsheetItemPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ISpreadsheetPattern> InitializeSpreadsheetPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IStylesPattern> InitializeStylesPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ISynchronizedInputPattern> InitializeSynchronizedInputPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ITableItemPattern> InitializeTableItemPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ITablePattern> InitializeTablePattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ITextChildPattern> InitializeTextChildPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ITextEditPattern> InitializeTextEditPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IText2Pattern> InitializeText2Pattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ITextPattern> InitializeTextPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ITogglePattern> InitializeTogglePattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ITransform2Pattern> InitializeTransform2Pattern() => throw new NotSupportedException();
            protected override IAutomationPattern<ITransformPattern> InitializeTransformPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IValuePattern> InitializeValuePattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IVirtualizedItemPattern> InitializeVirtualizedItemPattern() => throw new NotSupportedException();
            protected override IAutomationPattern<IWindowPattern> InitializeWindowPattern() => throw new NotSupportedException();
        }
    }
}
