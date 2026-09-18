using System.Collections.Generic;

namespace AutomationSandbox.UiModel
{
    /// <summary>Traversal helpers for framework-independent UI trees.</summary>
    public static class UiElementTreeExtensions
    {
        // A hand-built or JSON-deserialized tree (see UiElementSnapshot.FromJson) has no
        // guarantee of the depth bound a live UiTreeWalker capture enforces via
        // Discovery.DiscoveryOptions.MaxDepth (#426). This is a generous backstop - far
        // beyond any real UI's nesting - not an operational parameter callers are expected
        // to hit; it exists only to turn a pathological input into a truncated result
        // instead of a stack overflow.
        private const int MaxDepth = 5000;

        /// <summary>Enumerates the root and its descendants in pre-order, preserving child order.</summary>
        public static IEnumerable<UiElementInfo> Flatten(this UiElementInfo root)
        {
            // Iterative (an explicit stack, not recursion) so traversal depth is bounded by
            // heap space rather than call-stack space, and MaxDepth below can therefore stop
            // a pathologically deep branch instead of overflowing first (#426). A node at
            // the cap is still yielded; only its deeper descendants are skipped - the same
            // truncation shape UiTreeWalker uses for HitMaxDepth.
            var stack = new Stack<(UiElementInfo Node, int Depth)>();
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                var (node, depth) = stack.Pop();
                yield return node;
                if (depth >= MaxDepth)
                {
                    continue;
                }

                // Push children back-to-front so the stack pops them front-to-back, keeping
                // the yielded sequence in the same left-to-right pre-order the old recursive
                // version produced.
                for (var i = node.Children.Count - 1; i >= 0; i--)
                {
                    stack.Push((node.Children[i], depth + 1));
                }
            }
        }
    }
}
