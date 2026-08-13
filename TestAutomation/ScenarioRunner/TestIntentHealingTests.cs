using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LlmHealing;
using SelfHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class TestIntentHealingTests
    {
        [Fact]
        public void LlmHealingPrompt_IncludesTestIntentHeaderWhenProvided()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Submit",
                TestIntent = "Click the registration form submission button",
            };

            var candidate = new UiElementInfo
            {
                ControlType = "Button",
                Name = "Complete Registration",
                AutomationId = "btnComplete",
            };

            var candidates = new List<CandidateScore>
            {
                new()
                {
                    CandidateId = "c0",
                    Candidate = candidate,
                    TotalScore = 0.6,
                    Components = new ScoreComponents(),
                }
            };

            var prompt = LlmHealingPrompt.Build(expected, candidates);

            Assert.Contains("TEST INTENT (Goal of this test step):", prompt);
            Assert.Contains("Click the registration form submission button", prompt);
        }

        [Fact]
        public void UiElementSnapshot_PreservesTestIntent()
        {
            var element = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Search",
                AutomationId = "txtSearch",
                TestIntent = "Enter product query in header search field",
            };

            var snapshot = UiElementSnapshot.Capture(element);

            Assert.Equal("Enter product query in header search field", snapshot.TestIntent);
        }

        [Fact]
        public async Task SelfHealingEngine_PropagatesTestIntentDuringExecution()
        {
            var engine = new SelfHealingEngine();
            // Full structural metadata on both sides: after the #3 evidence gate, a
            // ControlType-only 1.0 match is thin evidence (coverage 0.20) and is no longer
            // confident. This test is about intent propagation, so the match must be solid.
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSave_Old",
                Name = "Save",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 1,
                BoundingRectangle = new BoundingRectangle(112, 178, 100, 30),
            };

            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Button",
                        AutomationId = "btnSave_New",
                        Name = "Save",
                        ParentControlType = "Window",
                        SiblingIndex = 0,
                        SiblingCount = 1,
                        BoundingRectangle = new BoundingRectangle(112, 178, 100, 30),
                    }
                }
            };

            UiElementInfo? capturedReceivedTarget = null;

            var result = await engine.ExecuteWithHealingAsync(
                "save_action",
                expected,
                action: element =>
                {
                    capturedReceivedTarget = element;
                    if (element.AutomationId == "btnSave_Old")
                    {
                        // A locator-resolution failure: the #2 default policy heals only
                        // this exact class of exception, anything else bubbles up.
                        throw new ElementNotFoundException("Missing!");
                    }
                    return Task.FromResult(true);
                },
                captureTreeRoot: () => currentTree,
                testIntent: "Save user settings modal form");

            Assert.True(result);
            Assert.NotNull(capturedReceivedTarget);
            Assert.Equal("btnSave_New", capturedReceivedTarget!.AutomationId);
        }

        // Named exactly after the locator-failure class the engine's default shouldHeal
        // policy accepts (it matches by type name, staying FlaUI-free - see issue #2).
        private sealed class ElementNotFoundException : Exception
        {
            public ElementNotFoundException(string message) : base(message) { }
        }
    }
}
