using System;
using System.Text.Json;
using AutomationSandbox.SelfHealing;
using AutomationSandbox.UiModel;

namespace ScenarioRunner
{
    // A rectangle with an infinite or NaN number (WPF's Rect.Empty is +/- infinity, and another capture tool may emit anything) was scored
    // normally, then could not be written: "infinity cannot be written as valid JSON". The CLI exited 2 ("could not run") after it had
    // already logged a not-confident result, and the caller got no JSON (#639).
    public class BoundingRectangleNonFiniteTests
    {
        public static readonly object[][] NonFinite =
        {
            new object[] { double.PositiveInfinity, 0.0, 10.0, 10.0 },
            new object[] { 0.0, double.NegativeInfinity, 10.0, 10.0 },
            new object[] { 0.0, 0.0, double.NaN, 10.0 },
            new object[] { double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity },
        };

        [Theory]
        [MemberData(nameof(NonFinite))]
        public void ANonFiniteValue_MeansNoGeometry_TheRectangleIsEmptyAndUnusable(double x, double y, double width, double height)
        {
            var rectangle = new BoundingRectangle(x, y, width, height);

            Assert.Equal(BoundingRectangle.Empty, rectangle);
            Assert.True(rectangle.IsEmpty);
            Assert.False(rectangle.IsUsable);
        }

        [Fact]
        public void FiniteValues_AreKeptExactly_IncludingNegativeAndHuge()
        {
            var rectangle = new BoundingRectangle(-5.5, 1e300, 20, 0.25);

            Assert.Equal(-5.5, rectangle.X);
            Assert.Equal(1e300, rectangle.Y);
            Assert.Equal(20, rectangle.Width);
            Assert.Equal(0.25, rectangle.Height);
        }

        [Fact]
        public void AFileWithAnOutOfRangeNumber_IsReadAsNoGeometry_AndTheTreeCanBeWrittenAgain()
        {
            // 1e400 is read by System.Text.Json as an infinity.
            var json = "{\"ControlType\":\"Button\",\"Name\":\"n\",\"AutomationId\":\"a\",\"ParentControlType\":\"Pane\",\"BoundingRectangle\":{\"X\":1e400,\"Y\":0,\"Width\":-5,\"Height\":1e400}}";

            var tree = UiTreeSerializer.FromJson(json);
            var written = UiTreeSerializer.ToJson(tree);

            Assert.Equal(BoundingRectangle.Empty, tree.BoundingRectangle);
            Assert.Contains("\"Width\": 0", written, StringComparison.Ordinal);
        }

        [Fact]
        public void AResultForATreeWithInfiniteRectangles_IsStillWritableAsJson_SoACallerGetsTheDecision()
        {
            var expected = new UiElementInfo { ControlType = "Button", Name = "Save", AutomationId = "old", ParentControlType = "Pane", BoundingRectangle = new BoundingRectangle(10, 10, 80, 20) };
            var candidate = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save",
                AutomationId = "new",
                ParentControlType = "Pane",
                BoundingRectangle = new BoundingRectangle(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity),
            };
            var root = new UiElementInfo { ControlType = "Pane", Name = "root", AutomationId = "root" };
            root.Children.Add(candidate);

            var result = SelfHealingResolver.Resolve(expected, root, log: _ => { });
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });

            Assert.NotNull(result.Matched);
            Assert.Contains("\"AutomationId\": \"new\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Infinity", json, StringComparison.Ordinal);
            Assert.DoesNotContain("NaN", json, StringComparison.Ordinal);
        }
    }
}
