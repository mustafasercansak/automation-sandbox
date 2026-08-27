using System;
using System.IO;
using System.Threading.Tasks;
using SelfHealing;
using SelfHealing.Testing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    // Test demonstrating xUnit IClassFixture<SelfHealingTestFixture> integration
    public class SelfHealingClassFixtureTests : IClassFixture<SelfHealingTestFixture>
    {
        private readonly SelfHealingTestFixture _fixture;

        public SelfHealingClassFixtureTests(SelfHealingTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ClassFixture_HealsStaleLocator_EndToEnd()
        {
            const string locatorKey = "Checkout.Submit";

            var staleLocator = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btn-submit-old",
                Name = "Submit Order",
                ParentControlType = "Form",
                SiblingIndex = 1,
                SiblingCount = 2,
                BoundingRectangle = new BoundingRectangle(100, 200, 120, 35)
            };

            var liveTree = new UiElementInfo
            {
                ControlType = "Window",
                AutomationId = "MainWindow",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Form",
                        AutomationId = "OrderForm",
                        Children =
                        {
                            new UiElementInfo
                            {
                                ControlType = "Edit",
                                AutomationId = "txtEmail",
                                Name = "Email",
                                ParentControlType = "Form",
                                SiblingIndex = 0,
                                SiblingCount = 2,
                                BoundingRectangle = new BoundingRectangle(100, 150, 200, 30)
                            },
                            new UiElementInfo
                            {
                                ControlType = "Button",
                                AutomationId = "btn-submit-new",
                                Name = "Submit Order",
                                ParentControlType = "Form",
                                SiblingIndex = 1,
                                SiblingCount = 2,
                                BoundingRectangle = new BoundingRectangle(100, 200, 120, 35)
                            }
                        }
                    }
                }
            };

            var attempts = 0;
            var executedAutomationId = await _fixture.ExecuteWithHealingAsync(
                locatorKey: locatorKey,
                expected: staleLocator,
                action: element =>
                {
                    attempts++;
                    if (element.AutomationId == staleLocator.AutomationId)
                    {
                        throw new ElementNotFoundException("Simulated locator resolution failure: old button missing");
                    }
                    return Task.FromResult(element.AutomationId);
                },
                captureTreeRoot: () => liveTree);

            Assert.Equal(2, attempts);
            Assert.Equal("btn-submit-new", executedAutomationId);

            var savedRecord = _fixture.Repository.Find(locatorKey);
            Assert.NotNull(savedRecord);
            Assert.Equal("btn-submit-new", savedRecord!.Snapshot.AutomationId);
            Assert.Single(savedRecord.HealingHistory);
        }
    }

    // Test demonstrating SelfHealingTestBase inheritance pattern
    public class SelfHealingTestBasePatternTests : SelfHealingTestBase
    {
        public SelfHealingTestBasePatternTests()
            : base(new SelfHealingTestOptions
            {
                Profile = ThresholdProfile.Balanced,
                Mode = HealingMode.AutoHeal
            })
        {
        }

        [Fact]
        public async Task TestBase_ResolvesAndExecutesVoidAction()
        {
            const string locatorKey = "Navigation.Home";

            var staleLocator = new UiElementInfo
            {
                ControlType = "Hyperlink",
                AutomationId = "lnk-home-old",
                Name = "Home",
                ParentControlType = "ToolBar",
                BoundingRectangle = new BoundingRectangle(10, 10, 50, 20)
            };

            var liveTree = new UiElementInfo
            {
                ControlType = "ToolBar",
                AutomationId = "MainToolbar",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Hyperlink",
                        AutomationId = "lnk-home-v2",
                        Name = "Home",
                        ParentControlType = "ToolBar",
                        BoundingRectangle = new BoundingRectangle(10, 10, 50, 20)
                    }
                }
            };

            var actionExecuted = false;
            var attempts = 0;

            await ExecuteWithHealingAsync(
                locatorKey: locatorKey,
                expected: staleLocator,
                action: element =>
                {
                    attempts++;
                    if (element.AutomationId == staleLocator.AutomationId)
                    {
                        throw new ElementNotFoundException("Old link not found");
                    }
                    actionExecuted = true;
                    return Task.CompletedTask;
                },
                captureTreeRoot: () => liveTree);

            Assert.True(actionExecuted);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public void CustomOptions_AppliesCustomWeightsAndProfile()
        {
            var customOptions = new SelfHealingTestOptions
            {
                Profile = ThresholdProfile.Conservative,
                Mode = HealingMode.Observe
            };

            using var customFixture = SelfHealingTestFixture.Create(customOptions);

            Assert.Equal(0.90, customFixture.Engine.Weights.MinimumConfidence);
            Assert.Equal(HealingMode.Observe, customFixture.Engine.Mode);
            Assert.False(string.IsNullOrEmpty(customFixture.RepositoryPath));
        }
    }

    internal sealed class ElementNotFoundException : Exception
    {
        public ElementNotFoundException(string message) : base(message) { }
    }
}
