using UiModel;

namespace WebDiscovery
{
    public sealed class WebElementInfo
    {
        public string TagName { get; set; } = "";
        public string Role { get; set; } = "";
        public string AccessibleName { get; set; } = "";
        public string Text { get; set; } = "";
        public string Id { get; set; } = "";
        public string NameAttribute { get; set; } = "";
        public string TestId { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string CssSelector { get; set; } = "";
        public bool IsHidden { get; set; }
        public bool IsOffscreen { get; set; }
        public string TreeScope { get; set; } = "light-dom";
        public string FrameUrl { get; set; } = "";
        public List<string> FrameAncestry { get; set; } = new();
        public BoundingRectangle BoundingRectangle { get; set; }
        public List<WebElementInfo> Children { get; set; } = new();
    }
}
