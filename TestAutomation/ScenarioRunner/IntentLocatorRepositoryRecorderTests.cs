using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntentAutomation;
using UiModel;
using WebDiscovery;
using Xunit;

namespace ScenarioRunner
{
    public class IntentLocatorRepositoryRecorderTests : IDisposable
    {
        private readonly string _directory;
        private readonly string _filePath;

        public IntentLocatorRepositoryRecorderTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "IntentLocatorRepositoryRecorderTests_" + Guid.NewGuid().ToString("N"));
            _filePath = Path.Combine(_directory, "web.locators.json");
        }

        [Fact]
        public void Record_PersistsBestIntentCandidatesToLocatorRepository()
        {
            var explorationResult = BuildExplorationResult();
            var repository = new LocatorRepository(_filePath);
            var recorder = new IntentLocatorRepositoryRecorder(new IntentLocatorRecordingOptions
            {
                ApplicationName = "CustomerPortal",
            });

            var results = recorder.Record(explorationResult, repository);

            Assert.Equal(2, results.Count(result => result.Recorded));
            Assert.Contains(results, result => result.LocatorKey == "Field.Email" && result.Recorded);
            Assert.Contains(results, result => result.LocatorKey == "Action.PrimarySubmit" && result.Recorded);

            var document = repository.Load();
            Assert.Equal("CustomerPortal", document.ApplicationName);
            Assert.Equal("web-playwright", document.Platform);
            Assert.Equal(2, document.Locators.Count);

            var email = document.Locators.Single(locator => locator.LocatorKey == "Field.Email");
            Assert.Equal("email-input", email.Snapshot.AutomationId);
            Assert.Equal("Fill Email for: Create a customer record", email.TestIntent);
            Assert.Empty(email.Snapshot.Children);

            var save = document.Locators.Single(locator => locator.LocatorKey == "Action.PrimarySubmit");
            Assert.Equal("save-button", save.Snapshot.AutomationId);
            Assert.Equal("Button", save.Snapshot.ControlType);
        }

        [Fact]
        public void Record_SkipsReviewCandidatesByDefault()
        {
            var step = new IntentStep
            {
                Order = 1,
                ActionType = IntentActionType.Fill,
                TargetDescription = "TaxId",
                TestIntent = "Fill tax id",
            };
            var explorationResult = new IntentExplorationResult
            {
                Scenario = new IntentScenario { Goal = "Create customer" },
                StepResults = new List<IntentStepExplorationResult>
                {
                    new IntentStepExplorationResult
                    {
                        Step = step,
                        RequiresReview = true,
                        Candidates = new List<IntentElementCandidate>
                        {
                            new IntentElementCandidate
                            {
                                Step = step,
                                Element = new WebElementInfo { TagName = "input", Role = "textbox", TestId = "tax-id" },
                                Score = 0.9,
                            }
                        }
                    }
                }
            };

            var results = new IntentLocatorRepositoryRecorder()
                .Record(explorationResult, new LocatorRepository(_filePath));

            Assert.False(results[0].Recorded);
            Assert.Contains("requires review", results[0].Diagnostic);
            Assert.Empty(new LocatorRepository(_filePath).Load().Locators);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private static IntentExplorationResult BuildExplorationResult()
        {
            var emailStep = new IntentStep
            {
                Order = 1,
                ActionType = IntentActionType.Fill,
                TargetDescription = "Email",
                TestIntent = "Fill Email for: Create a customer record",
            };
            var saveStep = new IntentStep
            {
                Order = 2,
                ActionType = IntentActionType.Click,
                TargetDescription = "primary submit or save action",
                TestIntent = "Click Save for: Create a customer record",
            };

            return new IntentExplorationResult
            {
                Scenario = new IntentScenario { Goal = "Create a customer record" },
                StepResults = new List<IntentStepExplorationResult>
                {
                    new IntentStepExplorationResult
                    {
                        Step = emailStep,
                        Candidates = new List<IntentElementCandidate>
                        {
                            new IntentElementCandidate
                            {
                                Step = emailStep,
                                Element = new WebElementInfo
                                {
                                    TagName = "input",
                                    Role = "textbox",
                                    AccessibleName = "Email",
                                    TestId = "email-input",
                                    Children = new List<WebElementInfo>
                                    {
                                        new WebElementInfo { TagName = "div", Text = "autocomplete" }
                                    },
                                },
                                Score = 0.95,
                            },
                        },
                    },
                    new IntentStepExplorationResult
                    {
                        Step = saveStep,
                        Candidates = new List<IntentElementCandidate>
                        {
                            new IntentElementCandidate
                            {
                                Step = saveStep,
                                Element = new WebElementInfo
                                {
                                    TagName = "button",
                                    Role = "button",
                                    AccessibleName = "Save",
                                    TestId = "save-button",
                                },
                                Score = 0.88,
                            },
                        },
                    },
                },
            };
        }
    }
}
