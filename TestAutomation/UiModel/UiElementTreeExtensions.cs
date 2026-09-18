using System.Collections.Generic;

namespace AutomationSandbox.UiModel
{
    /// <summary>Traversal helpers for framework-independent UI trees.</summary>
    public static class UiElementTreeExtensions
    {
        /// <summary>Enumerates the root and its descendants in pre-order, preserving child order.</summary>
        public static IEnumerable<UiElementInfo> Flatten(this UiElementInfo root)
        {
            yield return root;
            foreach (var child in root.Children)
            {
                foreach (var descendant in child.Flatten())
                {
                    yield return descendant;
                }
            }
        }
    }
}
