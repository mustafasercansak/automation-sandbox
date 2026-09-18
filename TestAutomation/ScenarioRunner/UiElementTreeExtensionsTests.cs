using AutomationSandbox.UiModel;
namespace ScenarioRunner
{
    public class UiElementTreeExtensionsTests
    {
        [Fact]
        public void Flatten_PreOrder_VisitsRootThenChildrenLeftToRight()
        {
            var root = new UiElementInfo { AutomationId = "root" };
            var first = new UiElementInfo { AutomationId = "first" };
            var second = new UiElementInfo { AutomationId = "second" };
            root.Children.Add(first);
            root.Children.Add(second);
            first.Children.Add(new UiElementInfo { AutomationId = "first-child" });

            var order = root.Flatten().Select(n => n.AutomationId).ToList();

            Assert.Equal(new[] { "root", "first", "first-child", "second" }, order);
        }

        [Fact]
        public void Flatten_ArtificiallyDeepLinearChain_TruncatesInsteadOfStackOverflowing()
        {
            // #426: this public extension used to recurse with no depth guard. A hand-built
            // or JSON-deserialized UiElementInfo tree (see UiElementSnapshot.FromJson) has no
            // guarantee it stays within any real UI's nesting, so a 10,000+-level
            // single-branch chain (the worst case for stack depth - a balanced tree with the
            // same node count would be far shallower) has to come back truncated instead of
            // overflowing the stack.
            const int chainDepth = 12_000;
            var root = new UiElementInfo { AutomationId = "node-0" };
            var current = root;
            for (var i = 1; i < chainDepth; i++)
            {
                var child = new UiElementInfo { AutomationId = $"node-{i}" };
                current.Children.Add(child);
                current = child;
            }

            // Must not throw a StackOverflowException (which the CLR can't catch anyway - an
            // uncaught one kills the test process outright rather than failing the test).
            var flattened = root.Flatten().ToList();

            Assert.NotEmpty(flattened);
            Assert.True(flattened.Count < chainDepth, $"Expected Flatten to truncate well below the full {chainDepth}-node chain, but it returned {flattened.Count} nodes.");
            Assert.Equal("node-0", flattened[0].AutomationId);
        }
    }
}
