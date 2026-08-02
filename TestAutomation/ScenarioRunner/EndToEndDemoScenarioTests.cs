using System;
using System.IO;
using System.Threading.Tasks;
using SelfHealing;
using UiModel;
using WebDiscovery;
using Xunit;

namespace ScenarioRunner
{
    /// <summary>
    /// Demonstrates an end-to-end self-healing test scenario for a Web or Desktop application
    /// when locators break due to a UI refactor.
    ///
    /// NOTE: This scenario runs with ZERO API token cost using the deterministic heuristic engine,
    /// proving that self-healing works 100% offline without requiring paid API keys!
    /// </summary>
    public class EndToEndDemoScenarioTests : IDisposable
    {
        private readonly string _repositoryPath;

        public EndToEndDemoScenarioTests()
        {
            _repositoryPath = Path.Combine(Path.GetTempPath(), "EndToEndDemo_" + Guid.NewGuid().ToString("N") + ".locator.json");
        }

        public void Dispose()
        {
            if (File.Exists(_repositoryPath))
            {
                File.Delete(_repositoryPath);
            }

            var lockPath = _repositoryPath + ".lock";
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }

        [Fact]
        public async Task EndToEnd_SelfHealingDemo_ResolvesRefactoredElementWithZeroTokenCost()
        {
            // =========================================================================
            // STEP 1: Initialize the persistent locator repository
            // =========================================================================
            var repository = new LocatorRepository(_repositoryPath);
            var engine = new SelfHealingEngine(repository);

            // =========================================================================
            // STEP 2: Define expected locator (from old version of the app)
            // =========================================================================
            var expectedLocator = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnRegister_V1_Old", // Broken ID after refactor
                Name = "Register",
                BoundingRectangle = new BoundingRectangle(100, 200, 150, 40),
                TestIntent = "Click the primary button to submit account registration"
            };

            // =========================================================================
            // STEP 3: Simulate live screen tree after developers refactored the UI
            // (Button renamed from "Register" -> "Create Account", ID -> "btnRegister_V2")
            // =========================================================================
            var currentLiveWebTree = new WebElementInfo
            {
                TagName = "body",
                BoundingRectangle = new BoundingRectangle(0, 0, 1920, 1080),
                Children =
                {
                    new WebElementInfo
                    {
                        TagName = "header",
                        Role = "banner",
                        Children =
                        {
                            new WebElementInfo { TagName = "h1", Text = "Welcome to Customer Portal" }
                        }
                    },
                    new WebElementInfo
                    {
                        TagName = "form",
                        Role = "form",
                        Children =
                        {
                            new WebElementInfo
                            {
                                TagName = "input",
                                Role = "textbox",
                                AccessibleName = "Email",
                                TestId = "email-field"
                            },
                            // The refactored button:
                            new WebElementInfo
                            {
                                TagName = "button",
                                Role = "button",
                                AccessibleName = "Create Account", // Renamed label
                                TestId = "btnRegister_V2",         // Renamed ID
                                BoundingRectangle = new BoundingRectangle(105, 205, 150, 40)
                            }
                        }
                    }
                }
            };

            // Map Web DOM tree to standard UiElementInfo tree
            UiElementInfo liveUiTree = WebElementMapper.ToUiElementTree(currentLiveWebTree);

            // =========================================================================
            // STEP 4: Execute test step using SelfHealingEngine (Zero Token Cost!)
            // =========================================================================
            bool actionExecutedSuccessfully = false;
            string clickedAutomationId = "";
            var attemptCount = 0;

            bool result = await engine.ExecuteWithHealingAsync(
                locatorKey: "RegistrationPage.SubmitButton",
                expected: expectedLocator,
                action: async (healedElement) =>
                {
                    // If initial element is missing/broken, engine automatically heals it
                    // and passes the healed element into this callback!
                    attemptCount++;
                    if (healedElement.AutomationId == "btnRegister_V1_Old")
                    {
                        throw new InvalidOperationException("Element not found with stale locator.");
                    }

                    actionExecutedSuccessfully = true;
                    clickedAutomationId = healedElement.AutomationId;
                    return await Task.FromResult(true);
                },
                captureTreeRoot: () => liveUiTree,
                testIntent: "Click the primary button to submit account registration",
                log: Console.WriteLine);

            // =========================================================================
            // STEP 5: Verify self-healing success & zero-cost persistence
            // =========================================================================
            Assert.True(result);
            Assert.True(actionExecutedSuccessfully);
            Assert.Equal(2, attemptCount);
            Assert.Equal("btnRegister_V2", clickedAutomationId);

            // Verify that the repository file was automatically created and updated with healed locator
            var savedRecord = repository.Find("RegistrationPage.SubmitButton");
            Assert.NotNull(savedRecord);
            Assert.Equal("btnRegister_V2", savedRecord!.Snapshot.AutomationId);
            Assert.Equal("Click the primary button to submit account registration", savedRecord.TestIntent);
            Assert.Single(savedRecord.HealingHistory);
            Assert.Equal("heuristic", savedRecord.HealingHistory[0].Source);
        }
    }
}
