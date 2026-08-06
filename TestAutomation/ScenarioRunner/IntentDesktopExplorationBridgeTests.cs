using System.Collections.Generic;
using System.Linq;
using IntentAutomation;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class IntentDesktopExplorationBridgeTests
    {
        [Fact]
        public void Match_MapsIntentStepsToUsableDesktopCandidates()
        {
            var planner = new DeterministicIntentPlanner();
            var planningResult = planner.Plan(new IntentPlanningRequest
            {
                Goal = "Create a corporate customer record with valid email",
                TestData = new Dictionary<string, string>
                {
                    ["email"] = "jane.doe@example.com",
                    ["company name"] = "Acme",
                }
            });
            var window = BuildCustomerWindow();

            var bridge = new IntentDesktopExplorationBridge();
            var result = bridge.Match(planningResult.Scenario, window);

            var email = BestFor(result, "email");
            Assert.Equal("txtEmail", email.Element.AutomationId);

            var recordType = BestFor(result, "record type");
            Assert.Equal("cmbRecordType", recordType.Element.AutomationId);

            var save = result.StepResults
                .Single(step => step.Step.ActionType == IntentActionType.Click)
                .Candidates[0];
            Assert.Equal("btnSave", save.Element.AutomationId);

            Assert.DoesNotContain(result.StepResults.SelectMany(step => step.Candidates), candidate => candidate.Element.AutomationId == "txtHiddenEmail");
            Assert.All(result.StepResults.Where(step => step.Step.ActionType != IntentActionType.Navigate), step => Assert.False(step.RequiresReview));
        }

        [Fact]
        public void Match_MarksReview_WhenNoCandidateMatchesIntentStep()
        {
            var scenario = new IntentScenario
            {
                Goal = "Create a customer record",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Fill,
                        TargetDescription = "tax identifier",
                        TestIntent = "Fill tax identifier for a customer",
                    }
                }
            };
            var window = new UiElementInfo { ControlType = "Window", BoundingRectangle = new BoundingRectangle(0, 0, 800, 600) };
            window.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save",
                AutomationId = "btnSave",
                BoundingRectangle = new BoundingRectangle(100, 200, 80, 24),
            });

            var result = new IntentDesktopExplorationBridge().Match(scenario, window);

            Assert.True(result.StepResults[0].RequiresReview);
            Assert.Empty(result.StepResults[0].Candidates);
            Assert.Contains("No usable desktop candidate", result.StepResults[0].Diagnostic);
        }

        private static IntentDesktopElementCandidate BestFor(IntentDesktopExplorationResult result, string targetDescription)
        {
            return result.StepResults
                .Single(step => step.Step.TargetDescription == targetDescription)
                .Candidates[0];
        }

        private static UiElementInfo BuildCustomerWindow()
        {
            var root = new UiElementInfo
            {
                ControlType = "Window",
                BoundingRectangle = new BoundingRectangle(0, 0, 800, 600),
            };

            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                AutomationId = "txtEmail",
                BoundingRectangle = new BoundingRectangle(100, 80, 220, 24),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                AutomationId = "txtHiddenEmail",
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Company Name",
                AutomationId = "txtCompanyName",
                BoundingRectangle = new BoundingRectangle(100, 120, 220, 24),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "ComboBox",
                Name = "Record Type",
                AutomationId = "cmbRecordType",
                BoundingRectangle = new BoundingRectangle(100, 160, 220, 24),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save",
                AutomationId = "btnSave",
                BoundingRectangle = new BoundingRectangle(100, 200, 80, 24),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "DataGrid",
                Name = "Customer Records",
                AutomationId = "dgvRecords",
                BoundingRectangle = new BoundingRectangle(100, 260, 400, 200),
            });

            return root;
        }
    }
}
