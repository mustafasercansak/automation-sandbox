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

        public List<UiElementInfo> Children { get; set; } = new();
    }

    public readonly struct BoundingRectangle
    {
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public BoundingRectangle(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}
