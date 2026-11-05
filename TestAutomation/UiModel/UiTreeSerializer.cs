using System.Text.Json;
namespace AutomationSandbox.UiModel
{
    /// <summary>Serializes and deserializes editable UI trees as JSON.</summary>
    public static class UiTreeSerializer
    {
        /// <summary>The most nested elements a tree may hold when it is written to or read from JSON. Real trees are far shallower (the capture stops at a default depth of 25), but the default JSON depth limit of 64 allowed only 31 nested elements because each element costs two JSON levels, so a tree a caller had captured with a larger depth, or a deep page captured by another tool, could be neither saved nor read back.</summary>
        public const int MaxElementDepth = 256;

        /// <summary>The JSON nesting limit that goes with <see cref="MaxElementDepth" />: two levels per element (the object and its <c>Children</c> array) plus room for the wrapper objects a tool puts around a tree. It stays a bound, so a hostile file cannot make the reader recurse without limit.</summary>
        public const int MaxJsonDepth = (2 * MaxElementDepth) + 16;

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            MaxDepth = MaxJsonDepth,
        };

        /// <summary>Serializes the supplied document to the package&apos;s JSON representation. A tree nested deeper than <see cref="MaxElementDepth" /> is refused with a message that names the depth.</summary>
        public static string ToJson(UiElementInfo root)
        {
            var depth = DepthOf(root);
            if (depth > MaxElementDepth)
            {
                throw new JsonException($"The tree is {depth} elements deep; at most {MaxElementDepth} are supported.");
            }

            return JsonSerializer.Serialize(root, Options);
        }

        /// <summary>Deserializes JSON into the package&apos;s editable document model. JSON nested deeper than <see cref="MaxElementDepth" /> elements is refused with a message that says so.</summary>
        public static UiElementInfo FromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<UiElementInfo>(json, Options)
                    ?? throw new JsonException("Failed to deserialize UI tree from JSON.");
            }
            catch (JsonException ex) when (ex.Message.IndexOf("maximum configured depth", System.StringComparison.Ordinal) >= 0)
            {
                throw new JsonException($"The tree is nested more than {MaxElementDepth} elements deep (JSON depth limit {MaxJsonDepth}).", ex);
            }
        }

        // Iterative, so measuring a hostile or very deep object graph cannot itself overflow the stack.
        private static int DepthOf(UiElementInfo? root)
        {
            if (root == null)
            {
                return 0;
            }

            var deepest = 0;
            var pending = new System.Collections.Generic.Stack<(UiElementInfo Node, int Depth)>();
            pending.Push((root, 1));
            while (pending.Count > 0)
            {
                var (node, depth) = pending.Pop();
                if (depth > deepest)
                {
                    deepest = depth;
                }

                if (deepest > MaxElementDepth)
                {
                    return deepest;
                }

                if (node.Children == null)
                {
                    continue;
                }

                foreach (var child in node.Children)
                {
                    if (child != null)
                    {
                        pending.Push((child, depth + 1));
                    }
                }
            }

            return deepest;
        }
    }
}
