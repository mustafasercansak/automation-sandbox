using System.Diagnostics;
using UiModel;
using SelfHealing;
namespace ScenarioRunner
{
    // Pure-logic benchmark: no FlaUI/Windows dependency, so unlike the live UIA scenario
    // tests this can run on any OS/CI runner. A correctness+timing smoke test, not a hard
    // performance gate - shared CI runners are noisy, so this only logs elapsed time rather
    // than asserting a wall-clock bound (see the milestone plan for why).

    public class SyntheticTreeBenchmarkTests
    {
        [Theory]
        [InlineData(30, 100)]
        [InlineData(100, 100)]
        public void Resolve_FindsTargetAmongThousandsOfDecoys_AndStaysConfident(int groupCount, int controlsPerGroup)
        {
            var controlTypes = new[] { "Edit", "Button", "Label", "CheckBox", "ComboBox" };
            var random = new Random(42); // deterministic across runs
            var root = new UiElementInfo { ControlType = "Window", AutomationId = "MainForm" };
            var targetGroupIndex = groupCount / 2;
            var targetIndexInGroup = controlsPerGroup / 2;
            UiElementInfo? expected = null;
            for (var g = 0; g < groupCount; g++)
            {
                var group = new UiElementInfo
                {
                    ControlType = "Panel",
                    AutomationId = $"panel{g}",
                    ParentControlType = root.ControlType,
                    ParentAutomationId = root.AutomationId,
                    SiblingIndex = g,
                    SiblingCount = groupCount,
                    BoundingRectangle = new BoundingRectangle(0, g * 40, 500, 40),
                };
                for (var i = 0; i < controlsPerGroup; i++)
                {
                    var isTarget = g == targetGroupIndex && i == targetIndexInGroup;
                    var rect = new BoundingRectangle(i * 5, g * 40, 5, 5);
                    var control = new UiElementInfo
                    {
                        ControlType = isTarget ? "Edit" : controlTypes[random.Next(controlTypes.Length)],
                        Name = isTarget ? "Email" : $"decoy{g}_{i}",
                        AutomationId = isTarget ? "txtEmail_renamed" : $"ctrl{g}_{i}",
                        ParentControlType = group.ControlType,
                        ParentAutomationId = group.AutomationId,
                        SiblingIndex = i,
                        SiblingCount = controlsPerGroup,
                        BoundingRectangle = rect,
                    };
                    group.Children.Add(control);
                    if (isTarget)
                    {
                        // "Last known" snapshot: same structural position, stale AutomationId.
                        expected = new UiElementInfo
                        {
                            ControlType = "Edit",
                            Name = "Email",
                            AutomationId = "txtEmail",
                            ParentControlType = group.ControlType,
                            ParentAutomationId = group.AutomationId,
                            SiblingIndex = i,
                            SiblingCount = controlsPerGroup,
                            BoundingRectangle = rect,
                        };
                    }
                }

                root.Children.Add(group);
            }

            Assert.NotNull(expected);
            var stopwatch = Stopwatch.StartNew();
            var result = SelfHealingResolver.Resolve(expected!, root, log: _ => { });
            stopwatch.Stop();
            Console.WriteLine($"[Benchmark] {groupCount * controlsPerGroup} candidates scored in {stopwatch.ElapsedMilliseconds}ms - " +
                $"best score={result.Score:F2}, candidateCount={result.CandidateCount}.");
            Assert.NotNull(result.Matched);
            Assert.Equal("txtEmail_renamed", result.Matched!.AutomationId);
            Assert.True(result.IsConfident, $"Expected a confident match at scale, but score was: {result.Score}");
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                $"Expected {groupCount * controlsPerGroup} candidates to resolve within the CI-safe 5s bound, but took {stopwatch.ElapsedMilliseconds}ms.");
        }

        [Theory]
        [InlineData(100, 100)]
        [InlineData(1000, 100)]
        [InlineData(10000, 100)]
        public void LocatorRepository_Find_Benchmark_AtScale(int recordCount, int lookupCount)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "RepoBenchmark_" + Guid.NewGuid().ToString("N"));
            var filePath = Path.Combine(tempDir, "benchmark.locators.json");
            try
            {
                var repo = new LocatorRepository(filePath);
                var doc = new LocatorRepositoryDocument { ApplicationName = "BenchmarkApp" };
                for (var i = 0; i < recordCount; i++)
                {
                    doc.Locators.Add(new LocatorRecord
                    {
                        LocatorKey = $"Form.Field_{i}",
                        Snapshot = new UiElementInfo
                        {
                            ControlType = "Edit",
                            AutomationId = $"txt_{i}",
                            Name = $"Field {i}",
                            BoundingRectangle = new BoundingRectangle(10, i * 20, 100, 20),
                        },
                    });
                }
                repo.Save(doc);

                // Benchmark lookups
                var targetKey = $"Form.Field_{recordCount / 2}";
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < lookupCount; i++)
                {
                    var found = repo.Find(targetKey);
                    Assert.NotNull(found);
                }
                sw.Stop();
                Console.WriteLine($"[Benchmark] LocatorRepository.Find ({recordCount} records, {lookupCount} lookups): {sw.ElapsedMilliseconds}ms (avg {(double)sw.ElapsedMilliseconds / lookupCount:F3}ms/lookup)");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
    }
}
