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

        [Fact]
        public void Match_ForcesReview_WhenElementIsSemanticallyUnrelated_EvenIfActionCompatible()
        {
            // Issue #5: "Delete customer" intent on a desktop window with only an "Export Report" button.
            // actionCompatible = true (Button) gives total 0.55 >= 0.35, but semanticScore is 0.00 < 0.01.
            // Notice: BoundingRectangle must be non-zero to avoid being filtered as unusable.
            var scenario = new IntentScenario
            {
                Goal = "Delete customer from database",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Click,
                        TargetDescription = "delete customer",
                        TestIntent = "Click the delete action for customer",
                        ExpectedOutcome = "The customer record is deleted",
                        LocatorKey = "Action.DeleteCustomer",
                    }
                }
            };

            var window = new UiElementInfo { ControlType = "Window", BoundingRectangle = new BoundingRectangle(0, 0, 800, 600) };
            window.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                Name = "Export Report",
                AutomationId = "btnExport",
                BoundingRectangle = new BoundingRectangle(100, 200, 80, 24),
            });

            var bridge = new IntentDesktopExplorationBridge();
            var result = bridge.Match(scenario, window);

            var stepResult = result.StepResults[0];
            Assert.NotEmpty(stepResult.Candidates);
            Assert.Equal(0.0, stepResult.Candidates[0].SemanticScore);
            Assert.True(stepResult.RequiresReview);
            Assert.Contains("below semantic gate", stepResult.Diagnostic);
        }

        [Fact]
        public void Match_ForcesReview_WhenCandidateMarginIsAmbiguous()
        {
            // Issue #5: when two competing candidates score within MinimumCandidateMargin (0.05),
            // the match is ambiguous and must require review.
            var scenario = new IntentScenario
            {
                Goal = "Save customer changes",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Click,
                        TargetDescription = "save action",
                        TestIntent = "Click save action",
                        ExpectedOutcome = "Changes are saved",
                        LocatorKey = "Action.Save",
                    }
                }
            };

            var window = new UiElementInfo { ControlType = "Window", BoundingRectangle = new BoundingRectangle(0, 0, 800, 600) };
            window.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save Draft",
                AutomationId = "btnSaveDraft",
                BoundingRectangle = new BoundingRectangle(100, 200, 80, 24),
            });
            window.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save Final",
                AutomationId = "btnSaveFinal",
                BoundingRectangle = new BoundingRectangle(200, 200, 80, 24),
            });

            var bridge = new IntentDesktopExplorationBridge();
            var result = bridge.Match(scenario, window);

            var stepResult = result.StepResults[0];
            Assert.True(stepResult.Candidates.Count >= 2);
            Assert.True(stepResult.RequiresReview);
            Assert.Contains("too close to runner-up", stepResult.Diagnostic);
        }

        [Fact]
        public void Match_CustomerDemo_SemanticAndMarginScores_ArePinned()
        {
            // Regression guard: pins the calibrated semantic scores and margin behaviors
            // on the reference customer desktop window fixture.
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

            var emailStep = result.StepResults.Single(s => s.Step.TargetDescription == "email");
            Assert.False(emailStep.RequiresReview);
            Assert.True(emailStep.Candidates[0].SemanticScore >= 0.20);

            var saveStep = result.StepResults.Single(s => s.Step.ActionType == IntentActionType.Click);
            Assert.False(saveStep.RequiresReview);
            Assert.True(saveStep.Candidates[0].SemanticScore >= 0.01);
            Assert.Equal("btnSave", saveStep.Candidates[0].Element.AutomationId);
        }

        [Fact]
        public void Constructor_ValidatesOptionsRanges()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentDesktopExplorationBridge(new IntentDesktopExplorationOptions { MaxCandidatesPerStep = 0 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentDesktopExplorationBridge(new IntentDesktopExplorationOptions { ReviewThreshold = -0.1 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentDesktopExplorationBridge(new IntentDesktopExplorationOptions { ReviewThreshold = 1.1 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentDesktopExplorationBridge(new IntentDesktopExplorationOptions { MinimumSemanticScore = -0.1 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentDesktopExplorationBridge(new IntentDesktopExplorationOptions { MinimumSemanticScore = 1.1 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentDesktopExplorationBridge(new IntentDesktopExplorationOptions { MinimumCandidateMargin = -0.1 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentDesktopExplorationBridge(new IntentDesktopExplorationOptions { MinimumCandidateMargin = 1.1 }));
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
