using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SelfHealing;
using SelfHealing.Testing;
using UiModel;

namespace NUnitFixtureTests
{
    // Verifies the SelfHealingTestBase pattern documented in docs/consumer-quickstart.md actually
    // works end-to-end when compiled and run under NUnit, not just under the xUnit test project.
    [TestFixture]
    public class SelfHealingTestBaseNUnitTests : SelfHealingTestBase
    {
        public SelfHealingTestBaseNUnitTests()
            : base(new SelfHealingTestOptions
            {
                Profile = ThresholdProfile.Balanced,
                Mode = HealingMode.AutoHeal
            })
        {
        }

        [Test]
        public async Task ExecuteWithHealingAsync_HealsStaleLocator_EndToEnd()
        {
            const string locatorKey = "Checkout.Submit";

            var staleLocator = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btn-submit-old",
                Name = "Submit Order",
                ParentControlType = "Form",
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
                        ControlType = "Button",
                        AutomationId = "btn-submit-new",
                        Name = "Submit Order",
                        ParentControlType = "Form",
                        BoundingRectangle = new BoundingRectangle(100, 200, 120, 35)
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
                        throw new ElementNotFoundException("Old button not found");
                    }
                    actionExecuted = true;
                    return Task.CompletedTask;
                },
                captureTreeRoot: () => liveTree);

            Assert.That(actionExecuted, Is.True);
            Assert.That(attempts, Is.EqualTo(2));
        }

        [Test]
        public void Repository_And_Engine_AreExposedByBaseClass()
        {
            Assert.That(Repository, Is.Not.Null);
            Assert.That(Engine, Is.Not.Null);
        }
    }

    internal sealed class ElementNotFoundException : Exception
    {
        public ElementNotFoundException(string message) : base(message) { }
    }
}
