using System;
using System.IO;
using System.Threading.Tasks;
using SelfHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class SelfHealingEngineTests : IDisposable
    {
        private readonly string _tempRepoPath;

        public SelfHealingEngineTests()
        {
            _tempRepoPath = Path.Combine(Path.GetTempPath(), "SelfHealingEngineTest_" + Guid.NewGuid().ToString("N") + ".locator.json");
        }

        public void Dispose()
        {
            if (File.Exists(_tempRepoPath))
            {
                File.Delete(_tempRepoPath);
            }

            var lockPath = _tempRepoPath + ".lock";
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }

        [Fact]
        public async Task SelfHealingEngine_ResolveAndRecordAsync_UpsertsHealedLocatorToRepository()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository);

            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "old_id",
                Name = "Email",
                BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
            };

            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Edit",
                        AutomationId = "new_healed_id",
                        Name = "Email",
                        BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
                    }
                }
            };

            var healResult = await engine.ResolveAndRecordAsync("email_field", expected, currentTree);

            Assert.True(healResult.IsConfident);
            Assert.Equal("new_healed_id", healResult.Matched!.AutomationId);

            var record = repository.Find("email_field");
            Assert.NotNull(record);
            Assert.Equal("new_healed_id", record!.Snapshot.AutomationId);
            Assert.Single(record.HealingHistory);
            Assert.Equal("heuristic", record.HealingHistory[0].Source);
        }

        [Fact]
        public async Task SelfHealingEngine_ExecuteWithHealingAsync_RetriesActionWithHealedElementWhenInitialFails()
        {
            var repository = new LocatorRepository(_tempRepoPath);
            var engine = new SelfHealingEngine(repository);

            var expected = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSubmit_Old",
                Name = "Submit",
                BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
            };

            var currentTree = new UiElementInfo
            {
                ControlType = "Window",
                Children =
                {
                    new UiElementInfo
                    {
                        ControlType = "Button",
                        AutomationId = "btnSubmit_Renamed",
                        Name = "Submit",
                        BoundingRectangle = new BoundingRectangle(50, 50, 80, 30),
                    }
                }
            };

            var attemptCount = 0;
            var resultText = await engine.ExecuteWithHealingAsync(
                "submit_btn",
                expected,
                action: element =>
                {
                    attemptCount++;
                    if (element.AutomationId == "btnSubmit_Old")
                    {
                        throw new InvalidOperationException("Element not found with old automation ID!");
                    }

                    return Task.FromResult("Clicked: " + element.AutomationId);
                },
                captureTreeRoot: () => currentTree);

            Assert.Equal(2, attemptCount);
            Assert.Equal("Clicked: btnSubmit_Renamed", resultText);

            var record = repository.Find("submit_btn");
            Assert.NotNull(record);
            Assert.Equal("btnSubmit_Renamed", record!.Snapshot.AutomationId);
        }
    }
}
