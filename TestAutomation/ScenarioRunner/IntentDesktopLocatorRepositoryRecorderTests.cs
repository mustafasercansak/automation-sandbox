using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntentAutomation;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class IntentDesktopLocatorRepositoryRecorderTests : IDisposable
    {
        private readonly string _directory;
        private readonly string _filePath;

        public IntentDesktopLocatorRepositoryRecorderTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "IntentDesktopLocatorRepositoryRecorderTests_" + Guid.NewGuid().ToString("N"));
            _filePath = Path.Combine(_directory, "desktop.locators.json");
        }

        [Fact]
        public void Record_PersistsBestIntentCandidatesToLocatorRepository()
        {
            var explorationResult = BuildExplorationResult();
            var repository = new LocatorRepository(_filePath);
            var recorder = new IntentDesktopLocatorRepositoryRecorder(new IntentDesktopLocatorRecordingOptions
            {
                ApplicationName = "CustomerApp",
            });

            var results = recorder.Record(explorationResult, repository);

            Assert.Equal(2, results.Count(result => result.Recorded));
            Assert.Contains(results, result => result.LocatorKey == "Field.Email" && result.Recorded);
            Assert.Contains(results, result => result.LocatorKey == "Action.PrimarySubmit" && result.Recorded);

            var document = repository.Load();
            Assert.Equal("CustomerApp", document.ApplicationName);
            Assert.Equal("windows-uia", document.Platform);
            Assert.Equal(2, document.Locators.Count);

            var email = document.Locators.Single(locator => locator.LocatorKey == "Field.Email");
            Assert.Equal("txtEmail", email.Snapshot.AutomationId);
            Assert.Equal("Fill Email for: Create a customer record", email.TestIntent);
            Assert.Empty(email.Snapshot.Children);

            var save = document.Locators.Single(locator => locator.LocatorKey == "Action.PrimarySubmit");
            Assert.Equal("btnSave", save.Snapshot.AutomationId);
            Assert.Equal("Button", save.Snapshot.ControlType);
        }

        [Fact]
        public void Record_SkipsReviewCandidatesByDefault()
        {
            var step = new IntentStep
            {
                Order = 1,
                ActionType = IntentActionType.Fill,
                LocatorKey = "Field.TaxId",
                TestIntent = "Fill tax id",
            };
            var explorationResult = new IntentDesktopExplorationResult
            {
                Scenario = new IntentScenario { Goal = "Create customer" },
                StepResults = new List<IntentDesktopStepExplorationResult>
                {
                    new IntentDesktopStepExplorationResult
                    {
                        Step = step,
                        RequiresReview = true,
                        Candidates = new List<IntentDesktopElementCandidate>
                        {
                            new IntentDesktopElementCandidate
                            {
                                Step = step,
                                Element = new UiElementInfo { ControlType = "Edit", AutomationId = "txtTaxId" },
                                Score = 0.9,
                            }
                        }
                    }
                }
            };

            var results = new IntentDesktopLocatorRepositoryRecorder()
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

        private static IntentDesktopExplorationResult BuildExplorationResult()
        {
            var emailStep = new IntentStep
            {
                Order = 1,
                ActionType = IntentActionType.Fill,
                TargetDescription = "Email",
                LocatorKey = "Field.Email",
                TestIntent = "Fill Email for: Create a customer record",
            };
            var saveStep = new IntentStep
            {
                Order = 2,
                ActionType = IntentActionType.Click,
                TargetDescription = "primary submit or save action",
                LocatorKey = "Action.PrimarySubmit",
                TestIntent = "Click Save for: Create a customer record",
            };

            return new IntentDesktopExplorationResult
            {
                Scenario = new IntentScenario { Goal = "Create a customer record" },
                StepResults = new List<IntentDesktopStepExplorationResult>
                {
                    new IntentDesktopStepExplorationResult
                    {
                        Step = emailStep,
                        Candidates = new List<IntentDesktopElementCandidate>
                        {
                            new IntentDesktopElementCandidate
                            {
                                Step = emailStep,
                                Element = new UiElementInfo
                                {
                                    ControlType = "Edit",
                                    Name = "Email",
                                    AutomationId = "txtEmail",
                                    Children = new List<UiElementInfo>
                                    {
                                        new UiElementInfo { ControlType = "Text", Name = "autocomplete" }
                                    },
                                },
                                Score = 0.95,
                            },
                        },
                    },
                    new IntentDesktopStepExplorationResult
                    {
                        Step = saveStep,
                        Candidates = new List<IntentDesktopElementCandidate>
                        {
                            new IntentDesktopElementCandidate
                            {
                                Step = saveStep,
                                Element = new UiElementInfo
                                {
                                    ControlType = "Button",
                                    Name = "Save",
                                    AutomationId = "btnSave",
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
