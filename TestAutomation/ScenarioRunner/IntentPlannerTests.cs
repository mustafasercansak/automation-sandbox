using System;
using System.Collections.Generic;
using System.Linq;
using IntentAutomation;
using Xunit;

namespace ScenarioRunner
{
    public class IntentPlannerTests
    {
        [Fact]
        public void DeterministicIntentPlanner_CreatesCorporateRecordFlow_FromGoalAndData()
        {
            var planner = new DeterministicIntentPlanner();
            var result = planner.Plan(new IntentPlanningRequest
            {
                Name = "Create corporate customer",
                Goal = "Create a corporate customer record with valid email",
                TargetUrl = "https://example.test/customers",
                TestData = new Dictionary<string, string>
                {
                    ["first name"] = "Jane",
                    ["last name"] = "Doe",
                    ["email"] = "jane.doe@example.com",
                    ["company name"] = "Acme",
                }
            });

            Assert.False(result.RequiresReview);
            Assert.Equal("Create corporate customer", result.Scenario.Name);
            Assert.Equal("https://example.test/customers", result.Scenario.TargetUrl);
            Assert.Equal(IntentActionType.Navigate, result.Scenario.Steps[0].ActionType);
            Assert.Contains(result.Scenario.Steps, step => step.ActionType == IntentActionType.Select && step.TargetDescription == "record type" && step.Value == "Corporate");
            Assert.Contains(result.Scenario.Steps, step => step.ActionType == IntentActionType.Click && step.LocatorKey == "Action.PrimarySubmit");
            Assert.Contains(result.Scenario.Steps, step => step.ActionType == IntentActionType.Assert && step.LocatorKey == "Assert.ResultVisible");
            Assert.All(result.Scenario.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step.TestIntent)));
            Assert.Equal(result.Scenario.Steps.Select(step => step.Order), result.Scenario.Steps.Select((_, index) => index + 1));
        }

        [Fact]
        public void DeterministicIntentPlanner_MarksReview_WhenNoCompletionVerbIsPresent()
        {
            var planner = new DeterministicIntentPlanner();
            var result = planner.Plan(new IntentPlanningRequest
            {
                Goal = "Inspect the customer email field",
                TestData = new Dictionary<string, string> { ["email"] = "jane.doe@example.com" }
            });

            Assert.True(result.RequiresReview);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.IndexOf("No submit/save/register verb", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(result.Scenario.Steps, step => step.ActionType == IntentActionType.Click);
        }

        [Fact]
        public void DeterministicIntentPlanner_RejectsEmptyGoal()
        {
            var planner = new DeterministicIntentPlanner();
            Assert.Throws<ArgumentException>(() => planner.Plan(new IntentPlanningRequest { Goal = " " }));
        }
    }
}
