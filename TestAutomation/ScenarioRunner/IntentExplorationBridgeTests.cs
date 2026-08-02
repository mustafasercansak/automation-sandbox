using System.Collections.Generic;
using System.Linq;
using IntentAutomation;
using UiModel;
using WebDiscovery;
using Xunit;

namespace ScenarioRunner
{
    public class IntentExplorationBridgeTests
    {
        [Fact]
        public void Match_MapsIntentStepsToVisibleWebCandidates_WithLocatorSuggestions()
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
            var dom = BuildCustomerDom();

            var bridge = new IntentExplorationBridge();
            var result = bridge.Match(planningResult.Scenario, dom);

            var email = BestFor(result, "email");
            Assert.Equal("email-input", email.Element.TestId);
            Assert.Equal("TestId", email.LocatorSuggestions[0].Strategy);

            var recordType = BestFor(result, "record type");
            Assert.Equal("record-type", recordType.Element.TestId);

            var save = result.StepResults
                .Single(step => step.Step.ActionType == IntentActionType.Click)
                .Candidates[0];
            Assert.Equal("save-button", save.Element.TestId);

            Assert.DoesNotContain(result.StepResults.SelectMany(step => step.Candidates), candidate => candidate.Element.TestId == "hidden-email");
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
            var dom = new WebElementInfo { TagName = "body" };
            dom.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Save",
                TestId = "save-button",
            });

            var result = new IntentExplorationBridge().Match(scenario, dom);

            Assert.True(result.StepResults[0].RequiresReview);
            Assert.Empty(result.StepResults[0].Candidates);
            Assert.Contains("No visible DOM candidate", result.StepResults[0].Diagnostic);
        }

        private static IntentElementCandidate BestFor(IntentExplorationResult result, string targetDescription)
        {
            return result.StepResults
                .Single(step => step.Step.TargetDescription == targetDescription)
                .Candidates[0];
        }

        private static WebElementInfo BuildCustomerDom()
        {
            var root = new WebElementInfo
            {
                TagName = "body",
                BoundingRectangle = new BoundingRectangle(0, 0, 1024, 768),
            };

            root.Children.Add(new WebElementInfo
            {
                TagName = "input",
                Role = "textbox",
                AccessibleName = "Email",
                TestId = "email-input",
                Id = "email",
                NameAttribute = "email",
                BoundingRectangle = new BoundingRectangle(100, 80, 220, 32),
            });
            root.Children.Add(new WebElementInfo
            {
                TagName = "input",
                Role = "textbox",
                AccessibleName = "Email",
                TestId = "hidden-email",
                IsHidden = true,
                BoundingRectangle = new BoundingRectangle(100, 80, 220, 32),
            });
            root.Children.Add(new WebElementInfo
            {
                TagName = "input",
                Role = "textbox",
                AccessibleName = "Company Name",
                TestId = "company-name",
                BoundingRectangle = new BoundingRectangle(100, 120, 220, 32),
            });
            root.Children.Add(new WebElementInfo
            {
                TagName = "select",
                Role = "combobox",
                AccessibleName = "Record Type",
                TestId = "record-type",
                BoundingRectangle = new BoundingRectangle(100, 160, 220, 32),
            });
            root.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Save",
                TestId = "save-button",
                BoundingRectangle = new BoundingRectangle(100, 200, 120, 36),
            });
            root.Children.Add(new WebElementInfo
            {
                TagName = "table",
                Role = "grid",
                AccessibleName = "Customer Records",
                TestId = "customer-records",
                BoundingRectangle = new BoundingRectangle(100, 260, 400, 200),
            });

            return root;
        }
    }
}
