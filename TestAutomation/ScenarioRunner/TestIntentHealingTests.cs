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
            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSave_Old",
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

        // Stands in for the exception a UI framework throws when a locator no longer
        // resolves; the engine's default healing policy matches by exception type name.
        private sealed class ElementNotFoundException : Exception
        {
            public ElementNotFoundException(string message) : base(message)
            {
            }
        }
    }
}
