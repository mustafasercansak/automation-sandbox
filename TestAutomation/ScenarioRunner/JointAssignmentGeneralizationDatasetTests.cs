using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class JointAssignmentGeneralizationDatasetTests
    {
        [Theory]
        [MemberData(nameof(Fixtures))]
        public void FrozenSelection_GeneratesOneRotationPerEligibleLeaf(
            string applicationName,
            string sourceVersion,
            string fixtureFileName,
            string[] expectedEligibleIds,
            string expectedScenarioIdDigest)
        {
            var root = LoadFixture(fixtureFileName);

            var eligibleIds = JointAssignmentGeneralizationDataset.SelectEligibleLocatorIds(root);
            var dataset = JointAssignmentGeneralizationDataset.Generate(
                root,
                applicationName,
                sourceVersion,
                fixtureFileName);

            Assert.Equal(expectedEligibleIds, eligibleIds);
            Assert.Equal(expectedEligibleIds.Length, dataset.Scenarios.Count);

            for (var i = 0; i < dataset.Scenarios.Count; i++)
            {
                var scenario = dataset.Scenarios[i];
                Assert.Equal(LocatorMutationKind.MultiLocator, scenario.MutationKind);
                var mutations = Assert.IsType<List<LocatorAblationMutation>>(scenario.Mutations);
                Assert.Collection(
                    mutations,
                    removed =>
                    {
                        Assert.Equal(expectedEligibleIds[i], removed.OriginalAutomationId);
                        Assert.Equal(LocatorMutationKind.RemovedElement, removed.MutationKind);
                    },
                    renamed =>
                    {
                        Assert.Equal(expectedEligibleIds[(i + 1) % expectedEligibleIds.Length], renamed.OriginalAutomationId);
                        Assert.Equal(LocatorMutationKind.RenamedAutomationId, renamed.MutationKind);
                    },
                    shifted =>
                    {
                        Assert.Equal(expectedEligibleIds[(i + 2) % expectedEligibleIds.Length], shifted.OriginalAutomationId);
                        Assert.Equal(LocatorMutationKind.PositionShift, shifted.MutationKind);
                    });
            }

            Assert.Equal(
                dataset.Scenarios.Count,
                dataset.Scenarios.Select(s => s.ScenarioId).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(
                expectedScenarioIdDigest,
                Sha256(string.Join("\n", dataset.Scenarios.Select(s => s.ScenarioId)) + "\n"));
        }

        public static IEnumerable<object[]> Fixtures()
        {
            yield return new object[]
            {
                "HandBrake",
                "1.8.2",
                "HandBrake_1.8.2.tree.json",
                new[]
                {
                    "AboutHandBrake",
                    "ActivityWindow",
                    "AlignAVStart",
                    "Angles",
                    "Choose Source",
                    "Close",
                    "Container",
                    "Destination",
                    "DestinationBrowser",
                    "EndPoint",
                    "Maximize-Restore",
                    "Metadata",
                    "Minimize-Restore",
                    "PointToPointMode",
                    "Preferences",
                    "Preview",
                    "SelectPresetsButton",
                    "ShowQueue",
                    "Start",
                    "StartPoint",
                    "SystemMenuBar",
                    "Titles",
                    "WebOptimized",
                    "audioTab",
                    "chaptersTab",
                    "filtersTab",
                    "help",
                    "iPod5G",
                    "numberBox",
                    "pictureTab",
                    "presetMenu",
                    "presetbtn",
                    "queueMenu",
                    "statusBar",
                    "subtitlesTab",
                    "videoTab",
                },
                "459ab1b6f188a2a99510a57292726dfe71345cbd785fa0c504f273be68b13733",
            };
            yield return new object[]
            {
                "ShareX",
                "v21.0.0",
                "ShareX_v21.0.0.tree.json",
                new[]
                {
                    "4251744379",
                    "4265926980",
                    "4267017949",
                    "Close",
                    "Maximize-Restore",
                    "Minimize-Restore",
                    "SystemMenuBar",
                    "flpMain",
                    "tsMain",
                },
                "928dda00fa98420def578c47f9eeb8783a50d00d11156ddb50905cee7c5b869b",
            };
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)))
                    .Replace("-", "")
                    .ToLowerInvariant();
            }
        }

        private static UiElementInfo LoadFixture(string fixtureFileName)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureFileName);
            Assert.True(File.Exists(path), $"Ablation fixture not found at '{path}'.");
            var tree = UiTreeSerializer.FromJson(File.ReadAllText(path));
            Assert.NotNull(tree);
            return tree!;
        }
    }
}
