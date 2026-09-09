using System.Collections.Generic;

namespace AutomationSandbox.UiModel
{
    public static class UiElementTreeExtensions
    {
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
