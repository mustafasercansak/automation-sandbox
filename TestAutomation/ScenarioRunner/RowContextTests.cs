using AutomationSandbox.UiModel;
using AutomationSandbox.SelfHealing;
namespace ScenarioRunner
{
    // #617: a list of rows that each hold an identical button. Nothing but the row tells them apart.
    public class RowContextTests
    {
        private static UiElementInfo Row(string label, string buttonAutomationId) =>
            new()
            {
                ControlType = "ListItem",
                ParentControlType = "List",
                Children =
                {
                    new UiElementInfo { ControlType = "Text", Name = label, ParentControlType = "ListItem" },
                    new UiElementInfo { ControlType = "Button", Name = "Connect", AutomationId = buttonAutomationId, ParentControlType = "ListItem" },
                },
            };

        private static UiElementInfo ListOfRows(params UiElementInfo[] rows)
        {
            var list = new UiElementInfo { ControlType = "List", ParentControlType = "Window" };
            list.Children.AddRange(rows);
            var root = new UiElementInfo { ControlType = "Window" };
            root.Children.Add(list);
            return root;
        }

        [Fact]
        public void Annotate_GivesAnElementTheTextOfItsRowWithoutItsOwnName()
        {
            var root = ListOfRows(Row("demo-a", "a"), Row("demo-b", "b"));

            UiRowContext.Annotate(root);

            var rows = root.Children[0].Children;
            Assert.Equal("demo-a", rows[0].Children[1].RowContext);
            Assert.Equal("demo-b", rows[1].Children[1].RowContext);
            Assert.Equal("Connect", rows[1].Children[0].RowContext);
        }

        [Fact]
        public void Annotate_LeavesElementsOutsideAnyRowEmpty()
        {
            var root = ListOfRows(Row("demo-a", "a"));

            UiRowContext.Annotate(root);

            Assert.Equal("", root.RowContext);
            Assert.Equal("", root.Children[0].RowContext);
            Assert.Equal("", root.Children[0].Children[0].RowContext);
        }

        [Fact]
        public void Annotate_CapsTheContextLength()
        {
            var row = new UiElementInfo { ControlType = "ListItem" };
            for (var i = 0; i < 50; i++)
            {
                row.Children.Add(new UiElementInfo { ControlType = "Text", Name = "label-number-" + i });
            }
            row.Children.Add(new UiElementInfo { ControlType = "Button", Name = "Connect" });

            UiRowContext.Annotate(row);

            Assert.True(row.Children[row.Children.Count - 1].RowContext.Length <= 160);
        }

        [Fact]
        public void WebElementMapper_RecordsTheRowContextOfAnElementInsideAListItem()
        {
            var root = new AutomationSandbox.WebDiscovery.WebElementInfo { TagName = "ul", Role = "list" };
            var item = new AutomationSandbox.WebDiscovery.WebElementInfo { TagName = "li", Role = "listitem" };
            item.Children.Add(new AutomationSandbox.WebDiscovery.WebElementInfo { TagName = "span", AccessibleName = "demo-signalr" });
            item.Children.Add(new AutomationSandbox.WebDiscovery.WebElementInfo { TagName = "button", Text = "Connect" });
            root.Children.Add(item);

            var tree = AutomationSandbox.WebDiscovery.WebElementMapper.ToUiElementTree(root);

            var button = tree.Children[0].Children[1];
            Assert.Equal("demo-signalr", button.RowContext);
            Assert.Equal("", tree.RowContext);
        }

        [Fact]
        public void Resolve_IdenticalSiblings_AreAmbiguousWithoutRowContext()
        {
            var expected = UiElementSnapshot.Capture(Row("demo-b", "stale").Children[1]);
            var root = ListOfRows(Row("demo-a", "a"), Row("demo-b", "b"));

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });

            Assert.False(result.IsConfident);
            Assert.Equal(HealResolutionStatus.Ambiguous, result.ResolutionStatus);
            Assert.Null(result.ScoreBreakdown!.RowContextScore);
        }

        [Fact]
        public void Resolve_IdenticalSiblings_AreSeparatedByRowContext()
        {
            var staleRow = ListOfRows(Row("demo-b", "stale"));
            UiRowContext.Annotate(staleRow);
            var expected = UiElementSnapshot.Capture(staleRow.Children[0].Children[0].Children[1]);
            var root = ListOfRows(Row("demo-a", "a"), Row("demo-b", "b"));
            UiRowContext.Annotate(root);

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });

            Assert.True(result.IsConfident, $"status {result.ResolutionStatus}, score {result.Score}, runner-up {result.RunnerUpScore}");
            Assert.Equal("b", result.Matched!.AutomationId);
            Assert.Equal(1.0, result.ScoreBreakdown!.RowContextScore);
        }

        [Fact]
        public void Resolve_WithoutRecordedRowContext_ScoresExactlyAsBefore()
        {
            var expected = new UiElementInfo { ControlType = "Edit", Name = "Email", ParentControlType = "Window", SiblingCount = 1 };
            var root = new UiElementInfo { ControlType = "Window" };
            root.Children.Add(new UiElementInfo { ControlType = "Edit", Name = "Email", AutomationId = "renamed", ParentControlType = "Window", SiblingCount = 1, RowContext = "some row text" });

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });

            Assert.Null(result.ScoreBreakdown!.RowContextScore);
            // control type, parent type, sibling position and name fired: 0.20 + 0.20 + 0.15 + 0.20 of 0.75 possible
            // without position, i.e. the unchanged denominator of 1.0 the engine used before row context existed.
            Assert.Equal(0.75, result.EvidenceCoverage, 3);
        }

        [Fact]
        public void Resolve_CandidateInTheWrongRow_ScoresLowerOnRowContext()
        {
            var expected = new UiElementInfo { ControlType = "Button", Name = "Connect", ParentControlType = "ListItem", RowContext = "demo-b" };
            var rightRow = Row("demo-b", "b").Children[1];
            rightRow.RowContext = "demo-b";
            var wrongRow = Row("demo-a", "a").Children[1];
            wrongRow.RowContext = "demo-a";
            var root = new UiElementInfo { ControlType = "Window" };
            root.Children.Add(wrongRow);
            root.Children.Add(rightRow);

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });

            Assert.Equal("b", result.Matched!.AutomationId);
            Assert.True(result.RunnerUpScore < result.Score);
        }
    }
}
