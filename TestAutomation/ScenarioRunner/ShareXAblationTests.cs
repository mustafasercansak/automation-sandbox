using System;
using System.IO;
using System.Linq;
using SelfHealing;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    // #99's second-application precondition (#134): every number in benchmark-calibration.md §3/§4
    // came from one WPF tree (HandBrake). This mirrors that same measurement against a real WinForms
    // tree (ShareX v21.0.0) so the figures rest on two applications, not one - the same discipline
    // #97 applied to the LLM path and #98 applied to the whole-tree reconciliation question.
    public class ShareXAblationTests
    {
        private const string FixtureFileName = "ShareX_v21.0.0.tree.json";

        [Fact]
        public void ShareXFixture_ThresholdSweep()
        {
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "ShareX", "v21.0.0", FixtureFileName);

            Console.WriteLine($"ShareX v21.0.0: {dataset.Scenarios.Count} scenarios from {dataset.Scenarios.Select(s => s.OriginalAutomationId).Distinct().Count()} authored locators");
            Console.WriteLine("| MinConfidence | Precision | Recall | FalseHealRate | ManualReviewRate | CorrectHeals | FalseHeals | FalseOnRemoved | Missed | CorrectDeclines |");
            Console.WriteLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

            for (double conf = 0.50; conf <= 0.951; conf += 0.05)
            {
                var weights = new SimilarityWeights { MinimumConfidence = conf };
                var report = LocatorAblationHarness.Run(dataset, root, weights);
                var m = report.Metrics;
                var accepted = m.CorrectHeals + m.FalseHeals + m.FalseHealsOnRemoved;
                var precision = accepted == 0 ? 1.0 : (double)m.CorrectHeals / accepted;
                Console.WriteLine($"| {conf:F2} | {precision:P1} | {m.AutoHealRecall:P1} | {m.FalseHealRate:P1} | {m.ManualReviewRate:P1} | {m.CorrectHeals} | {m.FalseHeals} | {m.FalseHealsOnRemoved} | {m.MissedHeals} | {m.CorrectDeclines} |");
            }

            Console.WriteLine("\nPer-mutation breakdown at default weights (0.50):");
            var report050 = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.50 });
            foreach (LocatorMutationKind kind in Enum.GetValues(typeof(LocatorMutationKind)))
            {
                var subset = report050.Results.Where(r => r.MutationKind == kind).ToList();
                if (subset.Count == 0) continue;
                var correct = subset.Count(r => r.Outcome == AblationOutcome.CorrectHeal || r.Outcome == AblationOutcome.CorrectDecline);
                var falseHeals = subset.Count(r => r.Outcome == AblationOutcome.FalseHeal || r.Outcome == AblationOutcome.FalseHealOnRemoved);
                var missed = subset.Count(r => r.Outcome == AblationOutcome.MissedHeal);
                var minScore = subset.Count > 0 ? subset.Min(r => r.Score) : 0;
                var maxScore = subset.Count > 0 ? subset.Max(r => r.Score) : 0;
                var avgScore = subset.Count > 0 ? subset.Average(r => r.Score) : 0;
                Console.WriteLine($"  {kind,-20} (n={subset.Count,2}): Correct={correct,2}, False={falseHeals,2}, Missed={missed,2} | Score Range: [{minScore:F3} - {maxScore:F3}], Avg: {avgScore:F3}");
            }
        }

        [Fact]
        public void ShareXFixture_ThresholdSweep_ExcludingDataGridRows()
        {
            // 15 of ShareX's 29 authored locators (52%) are DataItem rows from the Hotkeys settings
            // grid ("Hotkey Row 1", "Description Row 2", ...) - a repeated-row pattern HandBrake's
            // fixture does not contain at all. Every one of those rows is structurally near-identical
            // to its siblings (same ControlType, same missing ClassName, position differing only by
            // row index), so they decline as Ambiguous even at a perfect top score - correctly, since
            // #78 already names DataGrid/DataItem as an inherently volatile locator class. Left in,
            // they drag the whole-application figure down in a way that measures this one grid's
            // density rather than the heuristic's general quality. This reruns the sweep with them
            // excluded, for an apples-to-apples comparison against HandBrake (which has none).
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "ShareX", "v21.0.0", FixtureFileName);
            var filtered = new LocatorAblationDataset
            {
                Scenarios = dataset.Scenarios
                    .Where(s => LocatorAblationGenerator.FindExpectedElement(root, s.OriginalAutomationId)?.ControlType != "DataItem")
                    .ToList(),
            };

            Console.WriteLine($"ShareX v21.0.0, DataGrid rows excluded: {filtered.Scenarios.Count} scenarios from {filtered.Scenarios.Select(s => s.OriginalAutomationId).Distinct().Count()} authored locators");
            Console.WriteLine("| MinConfidence | Precision | Recall | FalseHealRate | ManualReviewRate | CorrectHeals | FalseHeals | FalseOnRemoved | Missed | CorrectDeclines |");
            Console.WriteLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            for (double conf = 0.50; conf <= 0.951; conf += 0.05)
            {
                var report = LocatorAblationHarness.Run(filtered, root, new SimilarityWeights { MinimumConfidence = conf });
                var m = report.Metrics;
                var accepted = m.CorrectHeals + m.FalseHeals + m.FalseHealsOnRemoved;
                var precision = accepted == 0 ? 1.0 : (double)m.CorrectHeals / accepted;
                Console.WriteLine($"| {conf:F2} | {precision:P1} | {m.AutoHealRecall:P1} | {m.FalseHealRate:P1} | {m.ManualReviewRate:P1} | {m.CorrectHeals} | {m.FalseHeals} | {m.FalseHealsOnRemoved} | {m.MissedHeals} | {m.CorrectDeclines} |");
            }
        }

        [Fact]
        public void ShareXFixture_DefaultWeights_MatchesTheCommittedBaseline()
        {
            // Permanent record of the ShareX numbers reported in docs/benchmark-calibration.md §8, so
            // they cannot silently drift as the fixture or the heuristic change - the same convention
            // used for HandBrake's HandBrakeFixture_AbsenceSignalEvaluation_* tests.
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "ShareX", "v21.0.0", FixtureFileName);

            Assert.Equal(131, dataset.Scenarios.Count);
            Assert.Equal(29, dataset.Scenarios.Select(s => s.OriginalAutomationId).Distinct().Count());

            var report = LocatorAblationHarness.Run(dataset, root, new SimilarityWeights { MinimumConfidence = 0.50 });
            var m = report.Metrics;

            Assert.Equal(30, m.CorrectHeals);
            Assert.Equal(3, m.FalseHeals);
            Assert.Equal(8, m.FalseHealsOnRemoved);
            Assert.Equal(69, m.MissedHeals);
            Assert.Equal(21, m.CorrectDeclines);

            // RemovedElement scenarios classify strictly as CorrectDecline or FalseHealOnRemoved -
            // never MissedHeal, since there is no successor to miss (LocatorAblationHarness.Classify).
            // MissedHeal belongs to the other four mutation kinds and must not appear in this sum.
            Assert.Equal(
                report.Metrics.RemovalScenarios,
                report.Metrics.CorrectDeclines + report.Metrics.FalseHealsOnRemoved);
        }

        [Fact]
        public void ShareXFixture_MostMissedPerfectScoreRenames_AreDataGridRows()
        {
            // Why the raw recall figure (29.4%) is misleading on its own: 15 of ShareX's 29 authored
            // locators (52%) are DataItem rows from the Hotkeys settings grid ("Hotkey Row 1",
            // "Description Row 2", ...). Every one of those declines as Ambiguous even at a perfect
            // top score, because it is structurally near-identical to its row siblings - correctly,
            // per #78's DataGrid/DataItem volatility class, not a heuristic defect. 15 of the 16
            // renames that score 1.000 but still get declined are exactly these rows.
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "ShareX", "v21.0.0", FixtureFileName);
            var renames = dataset.Scenarios.Where(s => s.MutationKind == LocatorMutationKind.RenamedAutomationId).ToList();

            var missedPerfectScore = 0;
            var missedAndDataItem = 0;
            foreach (var scenario in renames)
            {
                var expected = LocatorAblationGenerator.FindExpectedElement(root, scenario.OriginalAutomationId)!;
                var mutated = LocatorAblationGenerator.ApplyMutation(root, scenario);
                var heal = SelfHealingResolver.Resolve(expected, mutated, new SimilarityWeights { MinimumConfidence = 0.50 }, log: _ => { });
                if (!heal.IsConfident && heal.Score >= 0.99)
                {
                    missedPerfectScore++;
                    if (expected.ControlType == "DataItem")
                    {
                        missedAndDataItem++;
                    }
                }
            }

            Assert.Equal(16, missedPerfectScore);
            Assert.Equal(15, missedAndDataItem);
        }

        [Fact]
        public void ShareXFixture_ExcludingDataGridRows_FalseHealOnRemovedRateIsWorseThanHandBrakes()
        {
            // The apples-to-apples comparison against HandBrake (which has zero DataItem locators):
            // with the grid-row confound removed, ShareX's false-heal-on-removed rate is 8/14 (57.1%)
            // against HandBrake's 17/42 (40.5%) at the same default weights. Recall converges toward
            // HandBrake once the confound is gone (71.4% vs 76.9%) - but the false-heal problem this
            // whole benchmark exists to measure is not better on a second application, it is worse.
            var root = LoadFixture();
            var dataset = LocatorAblationGenerator.Generate(root, "ShareX", "v21.0.0", FixtureFileName);
            var filtered = new LocatorAblationDataset
            {
                Scenarios = dataset.Scenarios
                    .Where(s => LocatorAblationGenerator.FindExpectedElement(root, s.OriginalAutomationId)?.ControlType != "DataItem")
                    .ToList(),
            };

            Assert.Equal(56, filtered.Scenarios.Count);
            Assert.Equal(14, filtered.Scenarios.Count(s => s.MutationKind == LocatorMutationKind.RemovedElement));

            var report = LocatorAblationHarness.Run(filtered, root, new SimilarityWeights { MinimumConfidence = 0.50 });
            var m = report.Metrics;

            // RemovedElement scenarios classify strictly as CorrectDecline or FalseHealOnRemoved -
            // never MissedHeal, since there is no successor to miss - so this sum is exactly the
            // removal-only denominator.
            Assert.Equal(8, m.FalseHealsOnRemoved);
            Assert.Equal(6, m.CorrectDeclines);
            Assert.Equal(14, m.FalseHealsOnRemoved + m.CorrectDeclines);

            var shareXRate = (double)m.FalseHealsOnRemoved / (m.FalseHealsOnRemoved + m.CorrectDeclines);
            const double handBrakeRate = 17.0 / 42.0;
            Assert.True(
                shareXRate > handBrakeRate,
                $"Expected ShareX's false-heal-on-removed rate ({shareXRate:P1}) to exceed HandBrake's ({handBrakeRate:P1}); " +
                "if this ever flips, the doc's cross-application comparison needs updating, not just this assertion.");
        }

        private static UiElementInfo LoadFixture()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureFileName);
            Assert.True(File.Exists(path), $"Ablation fixture not found at '{path}'.");
            var tree = UiTreeSerializer.FromJson(File.ReadAllText(path));
            Assert.NotNull(tree);
            return tree!;
        }
    }
}
