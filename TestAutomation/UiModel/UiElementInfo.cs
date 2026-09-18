namespace AutomationSandbox.UiModel
{
    /// <summary>Editable UI tree node used by capture adapters, snapshot serialization, and structural scoring. Build and annotate the tree before resolving locators.</summary>
    public sealed class UiElementInfo
    {
        /// <summary>UI Automation control type used for structural matching, such as Button or Edit; empty when unavailable.</summary>
        public string ControlType { get; set; } = "";
        /// <summary>Human-readable accessible name of the element; empty when unavailable.</summary>
        public string Name { get; set; } = "";
        /// <summary>Captured automation identifier. It may be empty or duplicated and must not be treated as a unique node identity.</summary>
        public string AutomationId { get; set; } = "";
        /// <summary>Captured native or DOM class name; empty when unavailable.</summary>
        public string ClassName { get; set; } = "";
        /// <summary>Captured position and dimensions; an empty rectangle represents missing geometry.</summary>
        public BoundingRectangle BoundingRectangle { get; set; }

        // Parent/sibling context denormalized into the snapshot so the SelfHealing
        // layer can score a candidate without walking back up the tree.

        /// <summary>Parent control type stored on the child to allow scoring without parent traversal.</summary>
        public string ParentControlType { get; set; } = "";
        /// <summary>Captured parent identifier for diagnostics; it need not be unique.</summary>
        public string ParentAutomationId { get; set; } = "";
        /// <summary>Zero-based position among the captured parent&apos;s children.</summary>
        public int SiblingIndex { get; set; }
        /// <summary>Number of siblings including this node; zero indicates missing sibling metadata.</summary>
        public int SiblingCount { get; set; }
        /// <summary>Narrative business intent retained for prompts, generated tests, and reports.</summary>
        public string TestIntent { get; set; } = "";
        /// <summary>Editable direct children in capture order; populate before resolving against the tree.</summary>
        public List<UiElementInfo> Children { get; set; } = new();

        // Direct-child ControlType multiset recorded at snapshot time, e.g. "DataGrid:1" or
        // "Button:2|Edit:1" (see UiElementSnapshot.ComputeChildControlTypeSignature). Lets the
        // SelfHealing layer tell a container apart from a structurally identical sibling
        // without persisting the whole descendant subtree. Null on snapshots taken before
        // this field existed and on live tree nodes (which carry real Children); empty string
        // on a captured leaf.
        /// <summary>Direct-child ControlType multiset recorded at snapshot time, e.g. &quot;DataGrid:1&quot; or &quot;Button:2|Edit:1&quot; (see UiElementSnapshot.ComputeChildControlTypeSignature). Lets the SelfHealing layer tell a container apart from a structurally identical sibling without persisting the whole descendant subtree. Null on snapshots taken before this field existed and on live tree nodes (which carry real Children); empty string on a captured leaf.</summary>
        public string? ChildControlTypeSignature { get; set; }
    }

    /// <summary>Value describing an element&apos;s origin and dimensions in the coordinate system of its capture adapter.</summary>
    public readonly struct BoundingRectangle : IEquatable<BoundingRectangle>
    {
        /// <summary>Rectangle with all four coordinates and dimensions set to zero.</summary>
        public static readonly BoundingRectangle Empty = new(0, 0, 0, 0);

        /// <summary>Horizontal origin in the capture adapter&apos;s coordinate system.</summary>
        public double X { get; }
        /// <summary>Vertical origin in the capture adapter&apos;s coordinate system.</summary>
        public double Y { get; }
        /// <summary>Captured horizontal extent.</summary>
        public double Width { get; }
        /// <summary>Captured vertical extent.</summary>
        public double Height { get; }

        // Returns true if the rectangle has non-zero width or height, indicating a rendered,
        // actionable bounding box. Controls with zero width and height (whether at (0,0) or (100,200))
        // have no surface area and cannot receive interactions.
        /// <summary>Returns true if the rectangle has non-zero width or height, indicating a rendered, actionable bounding box. Controls with zero width and height (whether at (0,0) or (100,200)) have no surface area and cannot receive interactions.</summary>
        public bool IsUsable => Width > 0.0 || Height > 0.0;

        // Convenient readability property equivalent to this == Empty.
        /// <summary>Convenient readability property equivalent to this == Empty.</summary>
        public bool IsEmpty => X == 0.0 && Y == 0.0 && Width == 0.0 && Height == 0.0;

        /// <summary>Captured position and dimensions; an empty rectangle represents missing geometry.</summary>
        [System.Text.Json.Serialization.JsonConstructor]
        public BoundingRectangle(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        // Performs exact double value comparisons across all coordinates and dimensions.
        /// <summary>Performs exact double value comparisons across all coordinates and dimensions.</summary>
        public bool Equals(BoundingRectangle other)
        {
            return X.Equals(other.X)
                && Y.Equals(other.Y)
                && Width.Equals(other.Width)
                && Height.Equals(other.Height);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is BoundingRectangle other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Width.GetHashCode();
                hash = (hash * 397) ^ Height.GetHashCode();
                return hash;
            }
        }

        /// <summary>Compares all coordinates and dimensions using exact value equality semantics.</summary>
        public static bool operator ==(BoundingRectangle left, BoundingRectangle right) => left.Equals(right);

        /// <summary>Compares all coordinates and dimensions using exact value equality semantics.</summary>
        public static bool operator !=(BoundingRectangle left, BoundingRectangle right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"({X}, {Y}, {Width}, {Height})";
    }
}
