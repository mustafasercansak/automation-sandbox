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

        // True on an <iframe> whose document the capture script could not read because the browser's
        // same-origin policy blocked it. Without this, such a frame is indistinguishable from an
        // empty same-origin one: both come back as an iframe node with no children, so a caller
        // cannot tell "this frame is empty" from "I was not allowed to look inside". Elements inside
        // it are absent from the snapshot; capture them by evaluating the script in the frame
        // context directly (see docs/web-automation.md).
        public bool IsCrossOriginFrame { get; set; }
        public string TreeScope { get; set; } = "light-dom";
        public string FrameUrl { get; set; } = "";
        public List<string> FrameAncestry { get; set; } = new();
        public BoundingRectangle BoundingRectangle { get; set; }
        public List<WebElementInfo> Children { get; set; } = new();
    }
}
