namespace UiModel
{
    public sealed class UiElementInfo
    {
        public string ControlType { get; set; } = "";
        public string Name { get; set; } = "";
        public string AutomationId { get; set; } = "";
        public string ClassName { get; set; } = "";
        public BoundingRectangle BoundingRectangle { get; set; }

        // Parent/sibling context denormalized into the snapshot so the SelfHealing
        // layer can score a candidate without walking back up the tree.

        public string ParentControlType { get; set; } = "";
        public string ParentAutomationId { get; set; } = "";
        public int SiblingIndex { get; set; }
        public int SiblingCount { get; set; }
        public string TestIntent { get; set; } = "";
        public List<UiElementInfo> Children { get; set; } = new();

        // Direct-child ControlType multiset recorded at snapshot time, e.g. "DataGrid:1" or
        // "Button:2|Edit:1" (see UiElementSnapshot.ComputeChildControlTypeSignature). Lets the
        // SelfHealing layer tell a container apart from a structurally identical sibling
        // without persisting the whole descendant subtree. Null on snapshots taken before
        // this field existed and on live tree nodes (which carry real Children); empty string
        // on a captured leaf.
        public string? ChildControlTypeSignature { get; set; }
    }

    public readonly struct BoundingRectangle : IEquatable<BoundingRectangle>
    {
        public static readonly BoundingRectangle Empty = new(0, 0, 0, 0);

        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        // Returns true if the rectangle has non-zero width or height, indicating a rendered,
        // actionable bounding box. Controls with zero width and height (whether at (0,0) or (100,200))
        // have no surface area and cannot receive interactions.
        public bool IsUsable => Width > 0.0 || Height > 0.0;

        // Convenient readability property equivalent to this == Empty.
        public bool IsEmpty => X == 0.0 && Y == 0.0 && Width == 0.0 && Height == 0.0;

        [System.Text.Json.Serialization.JsonConstructor]
        public BoundingRectangle(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        // Performs exact double value comparisons across all coordinates and dimensions.
        public bool Equals(BoundingRectangle other)
        {
            return X.Equals(other.X)
                && Y.Equals(other.Y)
                && Width.Equals(other.Width)
                && Height.Equals(other.Height);
        }

        public override bool Equals(object? obj) => obj is BoundingRectangle other && Equals(other);

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

        public static bool operator ==(BoundingRectangle left, BoundingRectangle right) => left.Equals(right);

        public static bool operator !=(BoundingRectangle left, BoundingRectangle right) => !left.Equals(right);

        public override string ToString() => $"({X}, {Y}, {Width}, {Height})";
    }
}
