using AutomationSandbox.UiModel;

namespace AutomationSandbox.WebDiscovery
{
    /// <summary>Editable DOM snapshot node, including accessibility, geometry, and frame or shadow-tree context.</summary>
    public sealed class WebElementInfo
    {
        /// <summary>HTML tag name captured for this node.</summary>
        public string TagName { get; set; } = "";
        /// <summary>Captured or inferred accessibility role used for semantic matching and role locators.</summary>
        public string Role { get; set; } = "";
        /// <summary>Accessible name exposed by the captured web element.</summary>
        public string AccessibleName { get; set; } = "";
        /// <summary>Captured element text used for matching and diagnostics.</summary>
        public string Text { get; set; } = "";
        /// <summary>DOM id attribute; may be empty or duplicated.</summary>
        public string Id { get; set; } = "";
        /// <summary>HTML name attribute captured for form controls.</summary>
        public string NameAttribute { get; set; } = "";
        /// <summary>HTML input type used to distinguish text, checkbox, radio, file, and other control semantics.</summary>
        public string InputType { get; set; } = "";
        /// <summary>Captured test identifier used for test-id locator suggestions.</summary>
        public string TestId { get; set; } = "";
        /// <summary>Captured native or DOM class name; empty when unavailable.</summary>
        public string ClassName { get; set; } = "";
        /// <summary>CSS locator scoped to the node&apos;s captured frame or shadow-tree context.</summary>
        public string CssSelector { get; set; } = "";
        /// <summary>Whether the CSS selector depends on DOM structure rather than a stable authored attribute.</summary>
        public bool IsStructuralCssSelector { get; set; }
        /// <summary>Whether capture classified the node as hidden from rendering or accessibility.</summary>
        public bool IsHidden { get; set; }
        /// <summary>Whether capture classified the node as outside the visible viewport.</summary>
        public bool IsOffscreen { get; set; }

        // True on an <iframe> whose document the capture script could not read because the browser's
        // same-origin policy blocked it. Without this, such a frame is indistinguishable from an
        // empty same-origin one: both come back as an iframe node with no children, so a caller
        // cannot tell "this frame is empty" from "I was not allowed to look inside". Elements inside
        // it are absent from the snapshot; capture them by evaluating the script in the frame
        // context directly (see docs/web-automation.md).
        /// <summary>True on an &lt;iframe&gt; whose document the capture script could not read because the browser&apos;s same-origin policy blocked it. Without this, such a frame is indistinguishable from an empty same-origin one: both come back as an iframe node with no children, so a caller cannot tell &quot;this frame is empty&quot; from &quot;I was not allowed to look inside&quot;. Elements inside it are absent from the snapshot; capture them by evaluating the script in the frame context directly (see docs/web-automation.md).</summary>
        public bool IsCrossOriginFrame { get; set; }
        /// <summary>Captured document or shadow-tree scope associated with this node.</summary>
        public string TreeScope { get; set; } = "light-dom";
        /// <summary>URL of the browsing context in which this node was captured.</summary>
        public string FrameUrl { get; set; } = "";
        /// <summary>Ordered frame context needed to locate this node within nested browsing contexts.</summary>
        public List<string> FrameAncestry { get; set; } = new();
        /// <summary>Captured position and dimensions; an empty rectangle represents missing geometry.</summary>
        public BoundingRectangle BoundingRectangle { get; set; }
        /// <summary>Editable direct children in capture order; populate before resolving against the tree.</summary>
        public List<WebElementInfo> Children { get; set; } = new();
    }
}
