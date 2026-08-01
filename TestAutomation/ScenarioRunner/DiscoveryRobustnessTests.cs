using System;
using System.Collections.Generic;
using Discovery;
using UiModel;
using Xunit;
namespace ScenarioRunner
{
    public class DiscoveryRobustnessTests
    {
        [Fact]
        public void DiscoveryOptions_Default_ReturnsNewFreshInstanceEachTime()
        {
            var instance1 = DiscoveryOptions.Default;
            var instance2 = DiscoveryOptions.Default;
            Assert.NotSame(instance1, instance2);
            instance1.IgnoredControlTypes.Add("CustomType");
            Assert.DoesNotContain("CustomType", instance2.IgnoredControlTypes);
        }

        [Fact]
        public void DiscoveryOptions_Defaults_AreSetCorrectly()
        {
            var options = DiscoveryOptions.Default;
            Assert.Equal(25, options.MaxDepth);
            Assert.Equal(5000, options.MaxElements);
            Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
            Assert.False(options.IncludeOffscreen);
            Assert.True(options.ContinueOnElementError);
            Assert.NotNull(options.IgnoredControlTypes);
            Assert.NotNull(options.IgnoredClassNames);
        }

        [Fact]
        public void DiscoveryResult_DefaultState_IsClean()
        {
            var result = new DiscoveryResult();
            Assert.Equal(0, result.VisitedCount);
            Assert.Equal(0, result.CapturedCount);
            Assert.Equal(0, result.SkippedCount);
            Assert.Equal(0, result.ErrorCount);
            Assert.False(result.HitMaxDepth);
            Assert.False(result.HitMaxElements);
            Assert.False(result.TimedOut);
            Assert.False(result.WasCancelled);
            Assert.NotNull(result.Root);
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void DiscoveryOptions_FilterMatching_IgnoresCase()
        {
            var options = new DiscoveryOptions();
            options.IgnoredControlTypes.Add("button");
            options.IgnoredClassNames.Add("panelclass");
            Assert.True(options.IgnoredControlTypes.Contains("Button"));
            Assert.True(options.IgnoredClassNames.Contains("PanelClass"));
        }
    }
}
