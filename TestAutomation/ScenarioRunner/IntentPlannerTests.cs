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
        public void DeterministicIntentPlanner_RecognizesCommonInteractionActions()
        {
            var upload = new DeterministicIntentPlanner().Plan(new IntentPlanningRequest
            {
                Goal = "Upload a resume file",
                TestData = new Dictionary<string, string> { ["resume file"] = "/tmp/resume.pdf" },
            });
            var hover = new DeterministicIntentPlanner().Plan(new IntentPlanningRequest
            {
                Goal = "Hover over account menu",
            });
            var pressKey = new DeterministicIntentPlanner().Plan(new IntentPlanningRequest
            {
                Goal = "Press Enter on search field",
            });
            var wait = new DeterministicIntentPlanner().Plan(new IntentPlanningRequest
            {
                Goal = "Wait for confirmation message for 3 seconds",
            });

            Assert.Contains(upload.Scenario.Steps, step =>
                step.ActionType == IntentActionType.UploadFile &&
                step.TargetDescription == "resume file" &&
                step.Value == "/tmp/resume.pdf");
            Assert.Contains(hover.Scenario.Steps, step =>
                step.ActionType == IntentActionType.Hover && step.TargetDescription == "account menu");
            Assert.Contains(pressKey.Scenario.Steps, step =>
                step.ActionType == IntentActionType.PressKey &&
                step.TargetDescription == "search field" &&
                step.Value == "Enter");
            Assert.Contains(wait.Scenario.Steps, step =>
                step.ActionType == IntentActionType.Wait &&
                step.TargetDescription == "confirmation message" &&
                step.Value == "3000");
            Assert.False(upload.RequiresReview);
            Assert.False(hover.RequiresReview);
            Assert.False(pressKey.RequiresReview);
            Assert.False(wait.RequiresReview);
        }

        [Fact]
        public void DeterministicIntentPlanner_DisambiguatesKeyFieldsByValueShape()
        {
            var credential = new DeterministicIntentPlanner().Plan(new IntentPlanningRequest
            {
                Goal = "Register a new account",
                TestData = new Dictionary<string, string> { ["public key"] = "ssh-rsa AAAAB3NzaC1yc2E" },
            });
            var keyPress = new DeterministicIntentPlanner().Plan(new IntentPlanningRequest
            {
                Goal = "Submit the form",
                TestData = new Dictionary<string, string> { ["key"] = "Enter" },
            });

            Assert.Contains(credential.Scenario.Steps, step =>
                step.ActionType == IntentActionType.Fill && step.TargetDescription == "public key");
            Assert.Contains(keyPress.Scenario.Steps, step =>
                step.ActionType == IntentActionType.PressKey && step.Value == "Enter");
        }

        [Fact]
        public void DeterministicIntentPlanner_ChecksCheckboxLikeDataKeys()
        {
            var result = new DeterministicIntentPlanner().Plan(new IntentPlanningRequest
            {
                Goal = "Register a new account",
                TestData = new Dictionary<string, string> { ["newsletter"] = "yes" },
            });

            var step = result.Scenario.Steps.Single(s => s.TargetDescription == "newsletter");
            Assert.Equal(IntentActionType.Check, step.ActionType);
            Assert.Equal("newsletter is checked", step.ExpectedOutcome);
            Assert.StartsWith("Field.", step.LocatorKey);
        }

        [Fact]
        public void DeterministicIntentPlanner_UnchecksCheckboxLikeDataKeys_WhenValueIsFalsy()
        {
            var result = new DeterministicIntentPlanner().Plan(new IntentPlanningRequest
            {
                Goal = "Register a new account",
                TestData = new Dictionary<string, string> { ["terms"] = "false" },
            });

            var step = result.Scenario.Steps.Single(s => s.TargetDescription == "terms");
            Assert.Equal(IntentActionType.Uncheck, step.ActionType);
            Assert.Equal("terms is unchecked", step.ExpectedOutcome);
            Assert.StartsWith("Field.", step.LocatorKey);
        }

        [Fact]
        public void DeterministicIntentPlanner_RejectsEmptyGoal()
        {
            var planner = new DeterministicIntentPlanner();
            Assert.Throws<ArgumentException>(() => planner.Plan(new IntentPlanningRequest { Goal = " " }));
        }

        [Fact]
        public void IntentActionType_PreservesExistingNumericContract()
        {
            Assert.Equal(0, (int)IntentActionType.Unknown);
            Assert.Equal(1, (int)IntentActionType.Navigate);
            Assert.Equal(2, (int)IntentActionType.Fill);
            Assert.Equal(3, (int)IntentActionType.Click);
            Assert.Equal(4, (int)IntentActionType.Select);
            Assert.Equal(5, (int)IntentActionType.Assert);
            Assert.Equal(10, (int)IntentActionType.Check);
            Assert.Equal(11, (int)IntentActionType.Uncheck);
        }

        [Fact]
        public void DeterministicIntentPlanner_DerivesStructuredTextAssertion_WhenGoalSpecifiesExplicitValue()
        {
            var planner = new DeterministicIntentPlanner();
            var result = planner.Plan(new IntentPlanningRequest
            {
                Goal = "Complete checkout and verify order total should be $125",
            });

            var assertStep = result.Scenario.Steps.FirstOrDefault(s => s.ActionType == IntentActionType.Assert);
            Assert.NotNull(assertStep);
            Assert.Equal(AssertionKind.TextEquals, assertStep!.AssertionKind);
            Assert.Equal("$125", assertStep.ExpectedValue);
        }

        [Fact]
        public void DeterministicIntentPlanner_DefaultOutcomePhrases_NeverDeriveTextEquals()
        {
            var defaultPhrases = new[]
            {
                "The target page is loaded",
                "The requested workflow is submitted",
                "The created or updated result is visible",
                "record type has the requested value",
            };

            foreach (var phrase in defaultPhrases)
            {
                var (kind, value) = DeterministicIntentPlanner.DeriveAssertion(phrase, phrase);
                Assert.NotEqual(AssertionKind.TextEquals, kind);
                Assert.True(kind == AssertionKind.Visible || kind == AssertionKind.None,
                    $"Default phrase '{phrase}' should derive Visible or None, but got {kind} with value '{value}'.");
            }
        }

        [Fact]
        public void DeterministicIntentPlanner_DerivesMultiWordTextAssertion_WithoutTruncation()
        {
            var (kind, value) = DeterministicIntentPlanner.DeriveAssertion("Status should be Order Confirmed", "Check order status");
            Assert.Equal(AssertionKind.TextEquals, kind);
            Assert.Equal("Order Confirmed", value);
        }

        [Fact]
        public void DeterministicIntentPlanner_UrlPrecedesVisibilityWords_WhenBothArePresent()
        {
            var (kind, value) = DeterministicIntentPlanner.DeriveAssertion("The page is loaded and the URL should be https://shop.test/orders", "Navigate and verify");
            Assert.Equal(AssertionKind.UrlEquals, kind);
            Assert.Equal("https://shop.test/orders", value);
        }
    }
}
