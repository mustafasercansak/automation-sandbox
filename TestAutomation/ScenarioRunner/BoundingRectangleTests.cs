using System.Text.Json;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class BoundingRectangleTests
    {
        [Fact]
        public void Empty_HasZeroCoordinatesAndDimensions()
        {
            var empty = BoundingRectangle.Empty;

            Assert.Equal(0.0, empty.X);
            Assert.Equal(0.0, empty.Y);
            Assert.Equal(0.0, empty.Width);
            Assert.Equal(0.0, empty.Height);
            Assert.True(empty.IsEmpty);
            Assert.False(empty.IsUsable);
        }

        [Fact]
        public void IsEmpty_ReturnsTrue_OnlyWhenAllComponentsAreZero()
        {
            Assert.True(new BoundingRectangle(0, 0, 0, 0).IsEmpty);
            Assert.True(default(BoundingRectangle).IsEmpty);

            // Positioned but zero-dimension is not empty sentinel
            Assert.False(new BoundingRectangle(100, 200, 0, 0).IsEmpty);
            // Non-zero size at (0,0) is not empty
            Assert.False(new BoundingRectangle(0, 0, 100, 50).IsEmpty);
        }

        [Fact]
        public void IsUsable_ReturnsTrue_WhenWidthOrHeightIsPositive()
        {
            // Standard 2D box
            Assert.True(new BoundingRectangle(10, 20, 100, 50).IsUsable);
            // 1D horizontal separator
            Assert.True(new BoundingRectangle(0, 0, 100, 0).IsUsable);
            // 1D vertical separator
            Assert.True(new BoundingRectangle(0, 0, 0, 50).IsUsable);

            // Zero or negative dimensions are unusable
            Assert.False(new BoundingRectangle(0, 0, 0, 0).IsUsable);
            Assert.False(new BoundingRectangle(100, 200, 0, 0).IsUsable);
            Assert.False(new BoundingRectangle(0, 0, -10, 0).IsUsable);
            Assert.False(new BoundingRectangle(0, 0, 0, -10).IsUsable);
        }

        [Fact]
        public void Equality_AndOperators_CompareValuesAccurately()
        {
            var rect1 = new BoundingRectangle(10.5, 20.5, 30.5, 40.5);
            var rect2 = new BoundingRectangle(10.5, 20.5, 30.5, 40.5);
            var rect3 = new BoundingRectangle(10.5, 20.5, 30.5, 40.6);

            Assert.True(rect1.Equals(rect2));
            Assert.True(rect1.Equals((object)rect2));
            Assert.False(rect1.Equals(rect3));
            Assert.False(rect1.Equals(null));
            Assert.False(rect1.Equals("not a rect"));

            Assert.True(rect1 == rect2);
            Assert.False(rect1 == rect3);
            Assert.False(rect1 != rect2);
            Assert.True(rect1 != rect3);

            Assert.Equal(rect1.GetHashCode(), rect2.GetHashCode());
        }

        [Fact]
        public void ToString_FormatsCoordinatesAndDimensions()
        {
            var rect = new BoundingRectangle(10, 20, 30, 40);
            Assert.Equal("(10, 20, 30, 40)", rect.ToString());
        }

        [Fact]
        public void JsonSerialization_RoundTripsThroughSystemTextJson()
        {
            var original = new BoundingRectangle(112.5, 70.25, 200.0, 23.75);
            var json = JsonSerializer.Serialize(original);

            var deserialized = JsonSerializer.Deserialize<BoundingRectangle>(json);

            Assert.Equal(original, deserialized);
            Assert.Equal(112.5, deserialized.X);
            Assert.Equal(70.25, deserialized.Y);
            Assert.Equal(200.0, deserialized.Width);
            Assert.Equal(23.75, deserialized.Height);
            Assert.True(deserialized.IsUsable);
        }
    }
}
