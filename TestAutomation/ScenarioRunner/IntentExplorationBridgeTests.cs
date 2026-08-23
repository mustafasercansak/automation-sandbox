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

        [Fact]
        public void Match_ForcesReview_WhenElementIsSemanticallyUnrelated_EvenIfActionCompatible()
        {
            // Issue #5: "Delete customer" intent on a page with only an "Export Report" button.
            // actionCompatible = true (it's a button) and locator confidence is high (0.98),
            // giving total ~0.597 >= 0.35, but semanticScore is 0.00 < 0.01.
            // The bridge must retain the candidate for diagnostics but force RequiresReview = true.
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
                    }
                }
            };

            var dom = new WebElementInfo { TagName = "body" };
            dom.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Export Report",
                TestId = "export-report",
                BoundingRectangle = new BoundingRectangle(100, 200, 120, 36),
            });

            var bridge = new IntentExplorationBridge();
            var result = bridge.Match(scenario, dom);

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
            // the match is ambiguous and must require review rather than guessing.
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
                    }
                }
            };

            var dom = new WebElementInfo { TagName = "body" };
            dom.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Save Draft",
                TestId = "btn-save-draft",
                BoundingRectangle = new BoundingRectangle(100, 200, 100, 30),
            });
            dom.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Save Final",
                TestId = "btn-save-final",
                BoundingRectangle = new BoundingRectangle(220, 200, 100, 30),
            });

            var bridge = new IntentExplorationBridge();
            var result = bridge.Match(scenario, dom);

            var stepResult = result.StepResults[0];
            Assert.True(stepResult.Candidates.Count >= 2);
            Assert.True(stepResult.RequiresReview);
            Assert.Contains("too close to runner-up", stepResult.Diagnostic);
        }

        [Fact]
        public void Match_UsesTargetDescription_WhenNarrativeFieldsDisagree()
        {
            var scenario = new IntentScenario
            {
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Click,
                        TargetDescription = "save",
                        TestIntent = "Click cancel and discard the draft",
                        ExpectedOutcome = "The draft is cancelled",
                    }
                }
            };
            var dom = new WebElementInfo { TagName = "body" };
            dom.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Cancel",
                TestId = "cancel-button",
            });
            dom.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Save",
                TestId = "save-button",
            });

            var result = new IntentExplorationBridge().Match(scenario, dom);

            Assert.Equal("save-button", result.StepResults[0].Candidates[0].Element.TestId);
        }

        [Fact]
        public void Match_CustomerDemo_SemanticAndMarginScores_ArePinned()
        {
            // Regression guard: pins the calibrated semantic scores and margin behaviors
            // on the reference customer DOM fixture.
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

            var emailStep = result.StepResults.Single(s => s.Step.TargetDescription == "email");
            Assert.False(emailStep.RequiresReview);
            Assert.True(emailStep.Candidates[0].SemanticScore >= 0.20);

            var saveStep = result.StepResults.Single(s => s.Step.ActionType == IntentActionType.Click);
            Assert.False(saveStep.RequiresReview);
            Assert.True(saveStep.Candidates[0].SemanticScore >= 0.01);
            Assert.Equal("save-button", saveStep.Candidates[0].Element.TestId);
        }

        [Fact]
        public void Match_MatchesBelowTheFoldOffscreenElements_WhenNotHidden()
        {
            var scenario = new IntentScenario
            {
                Goal = "Complete purchase on long page",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Click,
                        TargetDescription = "checkout",
                        TestIntent = "Click the checkout button",
                        ExpectedOutcome = "Order is submitted",
                    }
                }
            };

            var dom = new WebElementInfo { TagName = "body" };
            // Below-the-fold element (IsOffscreen = true, IsHidden = false, Y = 3000)
            dom.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Checkout",
                TestId = "btn-checkout",
                IsOffscreen = true,
                IsHidden = false,
                BoundingRectangle = new BoundingRectangle(100, 3000, 120, 36),
            });
            // Truly hidden checkout button (IsHidden = true)
            dom.Children.Add(new WebElementInfo
            {
                TagName = "button",
                Role = "button",
                AccessibleName = "Hidden Checkout",
                TestId = "hidden-checkout",
                IsHidden = true,
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0),
            });

            var bridge = new IntentExplorationBridge();
            var result = bridge.Match(scenario, dom);

            var stepResult = result.StepResults[0];
            Assert.False(stepResult.RequiresReview);
            Assert.NotEmpty(stepResult.Candidates);
            Assert.Equal("btn-checkout", stepResult.Candidates[0].Element.TestId);
            Assert.True(stepResult.Candidates[0].Element.IsOffscreen);
            Assert.False(stepResult.Candidates[0].Element.IsHidden);
            Assert.DoesNotContain(stepResult.Candidates, c => c.Element.TestId == "hidden-checkout");
        }

        [Fact]
        public void Match_SupportsCommonInteractionActions()
        {
            var scenario = new IntentScenario
            {
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Hover, TargetDescription = "account menu" },
                    new IntentStep { Order = 2, ActionType = IntentActionType.UploadFile, TargetDescription = "resume file" },
                    new IntentStep { Order = 3, ActionType = IntentActionType.PressKey, TargetDescription = "search field" },
                    new IntentStep { Order = 4, ActionType = IntentActionType.Wait, TargetDescription = "confirmation message" },
                }
            };
            var dom = new WebElementInfo { TagName = "body" };
            dom.Children.Add(new WebElementInfo { TagName = "div", AccessibleName = "Account Menu", TestId = "account-menu" });
            dom.Children.Add(new WebElementInfo { TagName = "input", InputType = "file", AccessibleName = "Resume File", TestId = "resume-file" });
            dom.Children.Add(new WebElementInfo { TagName = "input", Role = "searchbox", AccessibleName = "Search Field", TestId = "search-field" });
            dom.Children.Add(new WebElementInfo { TagName = "div", Role = "status", AccessibleName = "Confirmation Message", TestId = "confirmation" });

            var result = new IntentExplorationBridge().Match(scenario, dom);

            Assert.Equal("account-menu", result.StepResults[0].Candidates[0].Element.TestId);
            Assert.Equal("resume-file", result.StepResults[1].Candidates[0].Element.TestId);
            Assert.Equal("search-field", result.StepResults[2].Candidates[0].Element.TestId);
            Assert.Equal("confirmation", result.StepResults[3].Candidates[0].Element.TestId);
            Assert.All(result.StepResults, step => Assert.False(step.RequiresReview));
        }

        [Fact]
        public void Match_ExcludesCheckboxesAndRadios_FromSelectCandidates()
        {
            // #198: Select maps to selectOption, which Playwright only accepts on real
            // dropdown elements - checkboxes and radios must match Check/Uncheck instead.
            var scenario = new IntentScenario
            {
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Select, TargetDescription = "record type" },
                }
            };

            var result = new IntentExplorationBridge().Match(scenario, BuildChoiceDom());

            var stepResult = result.StepResults[0];
            Assert.False(stepResult.RequiresReview);
            Assert.Equal("record-type", stepResult.Candidates[0].Element.TestId);
            Assert.DoesNotContain(stepResult.Candidates, candidate => candidate.Element.TestId == "newsletter-checkbox");
            Assert.DoesNotContain(stepResult.Candidates, candidate => candidate.Element.TestId == "shipping-radio");
        }

        [Fact]
        public void Match_MatchesCheckboxesAndRadios_ForCheck()
        {
            var scenario = new IntentScenario
            {
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Check, TargetDescription = "newsletter" },
                    new IntentStep { Order = 2, ActionType = IntentActionType.Check, TargetDescription = "shipping method" },
                }
            };

            var result = new IntentExplorationBridge().Match(scenario, BuildChoiceDom());

            Assert.Equal("newsletter-checkbox", result.StepResults[0].Candidates[0].Element.TestId);
            Assert.Equal("shipping-radio", result.StepResults[1].Candidates[0].Element.TestId);
            Assert.All(result.StepResults, step => Assert.False(step.RequiresReview));
        }

        [Fact]
        public void Match_ExcludesRadios_FromUncheckCandidates()
        {
            // A radio button can be checked but never unchecked (#198).
            var scenario = new IntentScenario
            {
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Uncheck, TargetDescription = "newsletter" },
                }
            };

            var result = new IntentExplorationBridge().Match(scenario, BuildChoiceDom());

            var stepResult = result.StepResults[0];
            Assert.False(stepResult.RequiresReview);
            Assert.Equal("newsletter-checkbox", stepResult.Candidates[0].Element.TestId);
            Assert.DoesNotContain(stepResult.Candidates, candidate => candidate.Element.TestId == "shipping-radio");
        }

        [Fact]
        public void Match_PhrasingVariations_ResolveToCorrectCandidatesWithoutFalseReviews()
        {
            // #237: Natural language variations should match the intended elements with high
            // confidence and not trigger false reviews due to filler words or role descriptors.
            var scenario = new IntentScenario
            {
                Goal = "Submit customer registration form",
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Fill, TargetDescription = "the user's email address in the input field" },
                    new IntentStep { Order = 2, ActionType = IntentActionType.Fill, TargetDescription = "company name textbox" },
                    new IntentStep { Order = 3, ActionType = IntentActionType.Select, TargetDescription = "choose the record type from dropdown" },
                    new IntentStep { Order = 4, ActionType = IntentActionType.Click, TargetDescription = "click the save button to submit" },
                }
            };

            var result = new IntentExplorationBridge().Match(scenario, BuildCustomerDom());

            Assert.All(result.StepResults, step => Assert.False(step.RequiresReview));
            Assert.Equal("email-input", result.StepResults[0].Candidates[0].Element.TestId);
            Assert.Equal("company-name", result.StepResults[1].Candidates[0].Element.TestId);
            Assert.Equal("record-type", result.StepResults[2].Candidates[0].Element.TestId);
            Assert.Equal("save-button", result.StepResults[3].Candidates[0].Element.TestId);
        }

        [Fact]
        public void Constructor_ValidatesOptionsRanges()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentExplorationBridge(new IntentExplorationOptions { MaxCandidatesPerStep = 0 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentExplorationBridge(new IntentExplorationOptions { ReviewThreshold = -0.1 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentExplorationBridge(new IntentExplorationOptions { ReviewThreshold = 1.1 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentExplorationBridge(new IntentExplorationOptions { MinimumSemanticScore = -0.1 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentExplorationBridge(new IntentExplorationOptions { MinimumSemanticScore = 1.1 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentExplorationBridge(new IntentExplorationOptions { MinimumCandidateMargin = -0.1 }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntentExplorationBridge(new IntentExplorationOptions { MinimumCandidateMargin = 1.1 }));
        }

        private static IntentElementCandidate BestFor(IntentExplorationResult result, string targetDescription)
        {
            return result.StepResults
                .Single(step => step.Step.TargetDescription == targetDescription)
                .Candidates[0];
        }

        private static WebElementInfo BuildChoiceDom()
        {
            var root = new WebElementInfo
            {
                TagName = "body",
                BoundingRectangle = new BoundingRectangle(0, 0, 1024, 768),
            };
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
                TagName = "input",
                Role = "checkbox",
                InputType = "checkbox",
                AccessibleName = "Newsletter",
                TestId = "newsletter-checkbox",
                BoundingRectangle = new BoundingRectangle(100, 200, 20, 20),
            });
            root.Children.Add(new WebElementInfo
            {
                TagName = "input",
                Role = "radio",
                InputType = "radio",
                AccessibleName = "Shipping Method",
                TestId = "shipping-radio",
                BoundingRectangle = new BoundingRectangle(100, 240, 20, 20),
            });

            return root;
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
