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
            var window = new UiElementInfo
            {
                ControlType = "Window",
                BoundingRectangle = new BoundingRectangle(0, 0, 800, 600),
            };
            window.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                Name = "Cancel",
                AutomationId = "btnCancel",
                BoundingRectangle = new BoundingRectangle(100, 200, 80, 24),
            });
            window.Children.Add(new UiElementInfo
            {
                ControlType = "Button",
                Name = "Save",
                AutomationId = "btnSave",
                BoundingRectangle = new BoundingRectangle(200, 200, 80, 24),
            });

            var result = new IntentDesktopExplorationBridge().Match(scenario, window);

            Assert.Equal("btnSave", result.StepResults[0].Candidates[0].Element.AutomationId);
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
        public void Match_ExcludesCheckBoxesRadiosAndLists_FromSelectCandidates()
        {
            // #198: Select maps to AsComboBox().Select(...), which exists only for ComboBox -
            // CheckBox/RadioButton match Check/Uncheck instead, and List/ListItem/Tab/TabItem
            // no longer match Select at all.
            var scenario = new IntentScenario
            {
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Select, TargetDescription = "record type", LocatorKey = "Field.RecordType" },
                }
            };

            var result = new IntentDesktopExplorationBridge().Match(scenario, BuildChoiceWindow());

            var stepResult = result.StepResults[0];
            Assert.False(stepResult.RequiresReview);
            Assert.Equal("cmbRecordType", stepResult.Candidates[0].Element.AutomationId);
            Assert.DoesNotContain(stepResult.Candidates, candidate => candidate.Element.AutomationId == "chkNewsletter");
            Assert.DoesNotContain(stepResult.Candidates, candidate => candidate.Element.AutomationId == "radShipping");
            Assert.DoesNotContain(stepResult.Candidates, candidate => candidate.Element.AutomationId == "lstItems");
        }

        [Fact]
        public void Match_MatchesCheckBoxesAndRadioButtons_ForCheck()
        {
            var scenario = new IntentScenario
            {
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Check, TargetDescription = "newsletter", LocatorKey = "Field.Newsletter" },
                    new IntentStep { Order = 2, ActionType = IntentActionType.Check, TargetDescription = "shipping method", LocatorKey = "Field.ShippingMethod" },
                }
            };

            var result = new IntentDesktopExplorationBridge().Match(scenario, BuildChoiceWindow());

            Assert.Equal("chkNewsletter", result.StepResults[0].Candidates[0].Element.AutomationId);
            Assert.Equal("radShipping", result.StepResults[1].Candidates[0].Element.AutomationId);
            Assert.All(result.StepResults, step => Assert.False(step.RequiresReview));
        }

        [Fact]
        public void Match_ExcludesRadioButtons_FromUncheckCandidates()
        {
            // A radio button can be checked but never unchecked (#198).
            var scenario = new IntentScenario
            {
                Steps = new List<IntentStep>
                {
                    new IntentStep { Order = 1, ActionType = IntentActionType.Uncheck, TargetDescription = "newsletter", LocatorKey = "Field.Newsletter" },
                }
            };

            var result = new IntentDesktopExplorationBridge().Match(scenario, BuildChoiceWindow());

            var stepResult = result.StepResults[0];
            Assert.False(stepResult.RequiresReview);
            Assert.Equal("chkNewsletter", stepResult.Candidates[0].Element.AutomationId);
            Assert.DoesNotContain(stepResult.Candidates, candidate => candidate.Element.AutomationId == "radShipping");
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

        [Fact]
        public void Match_ExcludesElementsWithPositionedZeroDimensionBoundingRectangle()
        {
            // Issue #22: Pinning test for unified BoundingRectangle.IsUsable semantics.
            // A positioned control with 0 width/height (e.g. (100, 200, 0, 0)) has no rendered interactive
            // surface on screen and cannot receive clicks or input.
            // Unifying on IsUsable ensures desktop intent matching excludes collapsed 0-size elements,
            // matching SimilarityScorer's position usability predicate.
            var scenario = new IntentScenario
            {
                Goal = "Enter user email",
                Steps = new List<IntentStep>
                {
                    new IntentStep
                    {
                        Order = 1,
                        ActionType = IntentActionType.Fill,
                        TargetDescription = "email",
                        Value = "test@example.com",
                        LocatorKey = "LoginForm.Email",
                    }
                }
            };

            var window = new UiElementInfo { ControlType = "Window", BoundingRectangle = new BoundingRectangle(0, 0, 800, 600) };
            window.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                AutomationId = "txtCollapsedEmail",
                BoundingRectangle = new BoundingRectangle(100, 200, 0, 0), // Positioned but 0x0
            });
            window.Children.Add(new UiElementInfo
            {
                ControlType = "Edit",
                Name = "Email",
                AutomationId = "txtRenderedEmail",
                BoundingRectangle = new BoundingRectangle(100, 200, 200, 24), // Usable
            });

            var result = new IntentDesktopExplorationBridge().Match(scenario, window);
            var candidates = result.StepResults[0].Candidates;

            Assert.Single(candidates);
            Assert.Equal("txtRenderedEmail", candidates[0].Element.AutomationId);
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
            var window = new UiElementInfo { ControlType = "Window", BoundingRectangle = new BoundingRectangle(0, 0, 800, 600) };
            window.Children.Add(new UiElementInfo { ControlType = "MenuItem", Name = "Account Menu", AutomationId = "accountMenu", BoundingRectangle = new BoundingRectangle(10, 10, 100, 30) });
            window.Children.Add(new UiElementInfo { ControlType = "Button", Name = "Resume File", AutomationId = "resumeFile", BoundingRectangle = new BoundingRectangle(10, 50, 200, 30) });
            window.Children.Add(new UiElementInfo { ControlType = "Edit", Name = "Search Field", AutomationId = "searchField", BoundingRectangle = new BoundingRectangle(10, 90, 200, 30) });
            window.Children.Add(new UiElementInfo { ControlType = "Text", Name = "Confirmation Message", AutomationId = "confirmation", BoundingRectangle = new BoundingRectangle(10, 130, 200, 30) });

            var result = new IntentDesktopExplorationBridge().Match(scenario, window);

            Assert.Equal("accountMenu", result.StepResults[0].Candidates[0].Element.AutomationId);
            Assert.Equal("resumeFile", result.StepResults[1].Candidates[0].Element.AutomationId);
            Assert.Equal("searchField", result.StepResults[2].Candidates[0].Element.AutomationId);
            Assert.Equal("confirmation", result.StepResults[3].Candidates[0].Element.AutomationId);
            Assert.All(result.StepResults, step => Assert.False(step.RequiresReview));
        }

        private static UiElementInfo BuildChoiceWindow()
        {
            var root = new UiElementInfo
            {
                ControlType = "Window",
                BoundingRectangle = new BoundingRectangle(0, 0, 800, 600),
            };
            root.Children.Add(new UiElementInfo
            {
                ControlType = "ComboBox",
                Name = "Record Type",
                AutomationId = "cmbRecordType",
                BoundingRectangle = new BoundingRectangle(100, 160, 220, 24),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "CheckBox",
                Name = "Newsletter",
                AutomationId = "chkNewsletter",
                BoundingRectangle = new BoundingRectangle(100, 200, 20, 20),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "RadioButton",
                Name = "Shipping Method",
                AutomationId = "radShipping",
                BoundingRectangle = new BoundingRectangle(100, 240, 20, 20),
            });
            root.Children.Add(new UiElementInfo
            {
                ControlType = "List",
                Name = "Items",
                AutomationId = "lstItems",
                BoundingRectangle = new BoundingRectangle(100, 280, 220, 120),
            });

            return root;
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
