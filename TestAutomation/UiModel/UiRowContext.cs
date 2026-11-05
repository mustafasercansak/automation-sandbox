using System.Text;

namespace AutomationSandbox.UiModel
{
    /// <summary>Fills <see cref="UiElementInfo.RowContext"/> across a captured tree (#617), so a list of rows that each hold an identical button can still be told apart by the text of the row.</summary>
    public static class UiRowContext
    {
        private const int MaxContextLength = 160;

        // Control types that mark a repeated row or list item, in the names the capture adapters
        // produce (UIA control types for desktop and mapped ARIA roles, raw tag names for plain DOM).
        private static readonly HashSet<string> RowControlTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "DataItem", "ListItem", "TreeItem", "row", "listitem", "tr", "li",
        };

        /// <summary>Sets <see cref="UiElementInfo.RowContext"/> on every element that sits inside a row or list item to the text of that row's other elements, and leaves it empty everywhere else. The tree is edited in place; an existing value is overwritten.</summary>
        public static void Annotate(UiElementInfo root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            // Iterative: framework-generated trees can nest far deeper than the call stack allows.
            var pending = new Stack<(UiElementInfo Node, UiElementInfo? Row)>();
            pending.Push((root, null));
            while (pending.Count > 0)
            {
                var (node, enclosingRow) = pending.Pop();
                node.RowContext = enclosingRow == null ? "" : TextOfRowExcluding(enclosingRow, node);
                var rowForChildren = IsRow(node) ? node : enclosingRow;
                foreach (var child in node.Children)
                {
                    pending.Push((child, rowForChildren));
                }
            }
        }

        private static bool IsRow(UiElementInfo node) => RowControlTypes.Contains(node.ControlType);

        // Names of every descendant of the row except the element itself and its own subtree,
        // in document order. The element's own name is excluded so a button's label cannot
        // stand in for the row it is in.
        private static string TextOfRowExcluding(UiElementInfo row, UiElementInfo excluded)
        {
            var text = new StringBuilder();
            var pending = new Stack<UiElementInfo>();
            for (var i = row.Children.Count - 1; i >= 0; i--)
            {
                pending.Push(row.Children[i]);
            }

            while (pending.Count > 0 && text.Length < MaxContextLength)
            {
                var node = pending.Pop();
                if (ReferenceEquals(node, excluded))
                {
                    continue;
                }

                var name = node.Name?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    if (text.Length > 0)
                    {
                        text.Append(' ');
                    }

                    text.Append(name);
                }

                for (var i = node.Children.Count - 1; i >= 0; i--)
                {
                    pending.Push(node.Children[i]);
                }
            }

            return text.Length > MaxContextLength ? text.ToString(0, MaxContextLength) : text.ToString();
        }
    }
}
