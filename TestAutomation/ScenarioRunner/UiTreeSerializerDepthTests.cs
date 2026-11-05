using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using AutomationSandbox.UiModel;
using AutomationSandbox.WebDiscovery;

namespace ScenarioRunner
{
    // The default JSON depth limit of 64 allowed only 31 nested elements (two JSON levels each: the object and its Children array), and its
    // error for a deeper tree said "possible object cycle". A tree captured with a larger depth, or a deep page captured by another tool,
    // could be neither saved nor read back (#638).
    public class UiTreeSerializerDepthTests
    {
        private static UiElementInfo Chain(int levels)
        {
            var root = new UiElementInfo { ControlType = "Pane", Name = "r", AutomationId = "r" };
            var current = root;
            for (var i = 1; i < levels; i++)
            {
                var child = new UiElementInfo { ControlType = "Pane", Name = "n" + i, AutomationId = "a" + i };
                current.Children.Add(child);
                current = child;
            }

            return root;
        }

        private static int DepthOf(UiElementInfo root)
        {
            var deepest = 0;
            var pending = new System.Collections.Generic.Stack<Tuple<UiElementInfo, int>>();
            pending.Push(Tuple.Create(root, 1));
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                deepest = Math.Max(deepest, item.Item2);
                foreach (var child in item.Item1.Children)
                {
                    pending.Push(Tuple.Create(child, item.Item2 + 1));
                }
            }

            return deepest;
        }

        // The JSON of a chain of Pane elements ending in a Button, built as text so the test needs no deep object graph.
        private static string ChainJson(int wrappers)
        {
            var text = new StringBuilder();
            for (var i = 0; i < wrappers; i++)
            {
                text.Append("{\"ControlType\":\"Pane\",\"Name\":\"x\",\"AutomationId\":\"x").Append(i).Append("\",\"BoundingRectangle\":{\"X\":0,\"Y\":0,\"Width\":1,\"Height\":1},\"ParentControlType\":\"\",\"Children\":[");
            }

            text.Append("{\"ControlType\":\"Button\",\"Name\":\"leaf\",\"AutomationId\":\"leaf\",\"BoundingRectangle\":{\"X\":0,\"Y\":0,\"Width\":1,\"Height\":1},\"ParentControlType\":\"\"}");
            for (var i = 0; i < wrappers; i++)
            {
                text.Append("]}");
            }

            return text.ToString();
        }

        [Theory]
        [InlineData(25)]
        [InlineData(31)]
        [InlineData(32)]
        [InlineData(100)]
        [InlineData(UiTreeSerializer.MaxElementDepth)]
        public void ATreeUpToTheLimit_RoundTrips_IncludingThoseThatTheDefaultLimitRefused(int levels)
        {
            var json = UiTreeSerializer.ToJson(Chain(levels));

            var back = UiTreeSerializer.FromJson(json);

            Assert.Equal(levels, DepthOf(back));
        }

        [Fact]
        public void ATreeOneLevelPastTheLimit_IsRefusedOnWrite_WithAMessageThatNamesTheDepthNotACycle()
        {
            var ex = Assert.Throws<JsonException>(() => UiTreeSerializer.ToJson(Chain(UiTreeSerializer.MaxElementDepth + 1)));

            Assert.Contains((UiTreeSerializer.MaxElementDepth + 1) + " elements deep", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void JsonNestedPastTheLimit_IsRefusedOnRead_WithAClearMessage()
        {
            var ex = Assert.Throws<JsonException>(() => UiTreeSerializer.FromJson(ChainJson(UiTreeSerializer.MaxElementDepth + 50)));

            Assert.Contains("more than " + UiTreeSerializer.MaxElementDepth + " elements deep", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AHostileFileOfTwoHundredThousandLevels_IsRefusedQuickly_WithoutOverflowingTheStack()
        {
            var json = ChainJson(200000);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.Throws<JsonException>(() => UiTreeSerializer.FromJson(json));

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "The depth bound must stop the reader early; it took " + clock.Elapsed);
        }

        [Fact]
        public void AToleratedButOddTree_NullChildrenAndNullEntries_DoesNotBreakTheDepthCheck()
        {
            var root = Chain(3);
            root.Children.Add(null!);

            var json = UiTreeSerializer.ToJson(root);

            Assert.Contains("\"r\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void TheWebCapturePayload_IsReadWithTheSameBound_SoADeepPageSurvives()
        {
            // A browser capture is a tree of WebElementInfo (Children), also two JSON levels per element.
            var text = new StringBuilder();
            const int levels = 100;
            for (var i = 0; i < levels; i++)
            {
                text.Append("{\"TagName\":\"div\",\"Id\":\"d").Append(i).Append("\",\"IsVisible\":true,\"Children\":[");
            }

            text.Append("{\"TagName\":\"button\",\"Id\":\"deep-btn\",\"AccessibleName\":\"Deep\",\"IsVisible\":true,\"Children\":[]}");
            for (var i = 0; i < levels; i++)
            {
                text.Append("]}");
            }

            var root = PlaywrightApplicationConnector.ParseJson(text.ToString());

            Assert.Equal(levels + 1, DepthOf(root));
        }
    }
}
