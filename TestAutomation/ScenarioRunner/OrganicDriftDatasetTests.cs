using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutomationSandbox.SelfHealing;
using AutomationSandbox.UiModel;
using Xunit;
using Xunit.Abstractions;

namespace ScenarioRunner
{
    // #663: the first organic drift dataset. Two genuine releases of a third-party application (Swagger UI 4.19.1 and 5.17.14, captured
    // by examples/js-playwright-capture/organic-drift-capture.mjs from the unmodified npm packages), every interactive element of the old
    // version in three page states as a stored locator, and a reviewed ground truth for each. Nothing here was renamed or deleted to create
    // drift, and no element was left out after a result was seen. The ground truth is reviewer judgement, not independent confirmation,
    // and this is one application pair: it is not the external validation #401 asks for.
    public class OrganicDriftDatasetTests
    {
        private readonly ITestOutputHelper _output;

        public OrganicDriftDatasetTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string DatasetPath(params string[] parts)
        {
            var all = new List<string> { AppContext.BaseDirectory, "Fixtures", "organic-drift", "swagger-ui" };
            all.AddRange(parts);
            return Path.Combine(all.ToArray());
        }

        private static JsonDocument LoadGroundTruth()
        {
            var path = DatasetPath("ground-truth.json");
            Assert.True(File.Exists(path), $"Organic drift ground truth not found at '{path}'.");
            return JsonDocument.Parse(File.ReadAllText(path));
        }

        private static UiElementInfo LoadTree(string version, string state)
        {
            var path = DatasetPath("trees", version, state + ".json");
            Assert.True(File.Exists(path), $"Captured tree not found at '{path}'.");
            return UiTreeSerializer.FromJson(File.ReadAllText(path));
        }

        private static List<UiElementInfo> PreOrder(UiElementInfo root)
        {
            var result = new List<UiElementInfo>();
            var stack = new Stack<UiElementInfo>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                result.Add(node);
                for (var i = node.Children.Count - 1; i >= 0; i--)
                {
                    stack.Push(node.Children[i]);
                }
            }

            return result;
        }

        private static List<UiElementInfo> Interactive(UiElementInfo root, HashSet<string> controlTypes)
        {
            return PreOrder(root).Where(node => controlTypes.Contains(node.ControlType)).ToList();
        }

        private static HashSet<string> InteractiveTypes(JsonDocument truth)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in truth.RootElement.GetProperty("interactiveControlTypes").EnumerateArray())
            {
                set.Add(type.GetString()!);
            }

            return set;
        }

        // The stored locator is the old element as the capture reported it, without its subtree: what a suite records once and
        // keeps, like the --expected file of the heal CLI.
        private static UiElementInfo StoredLocator(UiElementInfo node)
        {
            var copy = UiTreeSerializer.FromJson(UiTreeSerializer.ToJson(node));
            copy.Children = new List<UiElementInfo>();
            return copy;
        }

        private sealed class Measurement
        {
            public int Locators;
            public int CorrectHeals;
            public int WrongHeals;
            public int DeclinedWithTarget;
            public int DeclinedWithTargetAsTopCandidate;
            public int DeclinedRemoved;
            public readonly List<string> Wrong = new List<string>();
            public readonly List<string> Declined = new List<string>();
        }

        private static Measurement Measure(ThresholdProfile profile)
        {
            var measurement = new Measurement();
            var weights = SimilarityWeights.FromProfile(profile);
            using (var truth = LoadGroundTruth())
            {
                var types = InteractiveTypes(truth);
                var pair = truth.RootElement.GetProperty("pair");
                var oldVersion = pair.GetProperty("old").GetString()!;
                var newVersion = pair.GetProperty("new").GetString()!;

                foreach (var state in truth.RootElement.GetProperty("states").EnumerateArray())
                {
                    var stateName = state.GetProperty("name").GetString()!;
                    var oldNodes = Interactive(LoadTree(oldVersion, stateName), types);
                    var newRoot = LoadTree(newVersion, stateName);
                    var newNodes = Interactive(newRoot, types);

                    foreach (var entry in state.GetProperty("entries").EnumerateArray())
                    {
                        var oldIndex = entry.GetProperty("old").GetInt32();
                        var target = entry.GetProperty("new");
                        var expectedNode = target.ValueKind == JsonValueKind.Null ? null : newNodes[target.GetInt32()];
                        var label = $"{stateName}#{oldIndex} '{oldNodes[oldIndex].Name}'";

                        var result = SelfHealingResolver.Resolve(StoredLocator(oldNodes[oldIndex]), newRoot, weights, log: _ => { });
                        measurement.Locators++;

                        if (result.IsConfident)
                        {
                            if (expectedNode != null && ReferenceEquals(result.Matched, expectedNode))
                            {
                                measurement.CorrectHeals++;
                            }
                            else
                            {
                                measurement.WrongHeals++;
                                measurement.Wrong.Add($"{label} -> '{result.Matched?.Name}' ({result.Matched?.ClassName})");
                            }
                        }
                        else if (expectedNode == null)
                        {
                            measurement.DeclinedRemoved++;
                        }
                        else
                        {
                            measurement.DeclinedWithTarget++;
                            if (ReferenceEquals(result.Matched, expectedNode))
                            {
                                measurement.DeclinedWithTargetAsTopCandidate++;
                            }

                            measurement.Declined.Add($"{label} [{result.ResolutionStatus}]");
                        }
                    }
                }
            }

            return measurement;
        }

        [Fact]
        public void EveryInteractiveElementOfTheOldVersionHasOneReviewedEntry()
        {
            using (var truth = LoadGroundTruth())
            {
                var types = InteractiveTypes(truth);
                var pair = truth.RootElement.GetProperty("pair");
                var oldVersion = pair.GetProperty("old").GetString()!;
                var newVersion = pair.GetProperty("new").GetString()!;
                var states = truth.RootElement.GetProperty("states").EnumerateArray().ToList();
                Assert.Equal(3, states.Count);

                foreach (var state in states)
                {
                    var stateName = state.GetProperty("name").GetString()!;
                    var oldCount = Interactive(LoadTree(oldVersion, stateName), types).Count;
                    var newCount = Interactive(LoadTree(newVersion, stateName), types).Count;
                    var entries = state.GetProperty("entries").EnumerateArray().ToList();

                    // None dropped: the old version's elements are exactly the entries, in order, once each.
                    Assert.Equal(oldCount, entries.Count);
                    Assert.Equal(Enumerable.Range(0, oldCount), entries.Select(e => e.GetProperty("old").GetInt32()));

                    var targets = entries.Where(e => e.GetProperty("new").ValueKind != JsonValueKind.Null).Select(e => e.GetProperty("new").GetInt32()).ToList();
                    Assert.Equal(targets.Count, targets.Distinct().Count());
                    Assert.All(targets, t => Assert.InRange(t, 0, newCount - 1));

                    // Every new element is either a target or listed as new-only: nothing unaccounted for.
                    var newOnly = state.GetProperty("newOnly").EnumerateArray().Select(e => e.GetInt32()).ToList();
                    Assert.Equal(newCount, targets.Count + newOnly.Count);
                    Assert.Empty(targets.Intersect(newOnly));
                }
            }
        }

        [Fact]
        public void GroundTruthEntriesNameTheElementsTheyPointAt()
        {
            using (var truth = LoadGroundTruth())
            {
                var types = InteractiveTypes(truth);
                var pair = truth.RootElement.GetProperty("pair");
                var oldVersion = pair.GetProperty("old").GetString()!;
                var newVersion = pair.GetProperty("new").GetString()!;

                foreach (var state in truth.RootElement.GetProperty("states").EnumerateArray())
                {
                    var stateName = state.GetProperty("name").GetString()!;
                    var oldNodes = Interactive(LoadTree(oldVersion, stateName), types);
                    var newNodes = Interactive(LoadTree(newVersion, stateName), types);

                    foreach (var entry in state.GetProperty("entries").EnumerateArray())
                    {
                        var oldNode = oldNodes[entry.GetProperty("old").GetInt32()];
                        Assert.Equal(entry.GetProperty("oldName").GetString(), oldNode.Name);
                        Assert.Equal(entry.GetProperty("oldClass").GetString(), oldNode.ClassName);

                        var target = entry.GetProperty("new");
                        if (target.ValueKind != JsonValueKind.Null)
                        {
                            var newNode = newNodes[target.GetInt32()];
                            Assert.Equal(entry.GetProperty("newName").GetString(), newNode.Name);
                            Assert.Equal(entry.GetProperty("newClass").GetString(), newNode.ClassName);
                        }
                    }
                }
            }
        }

        [Fact]
        public void ManifestRecordsHashesForEveryReleaseAndTheSameVersionsAsTheGroundTruth()
        {
            using (var manifest = JsonDocument.Parse(File.ReadAllText(DatasetPath("manifest.json"))))
            using (var truth = LoadGroundTruth())
            {
                var releases = manifest.RootElement.GetProperty("releases").EnumerateArray().ToList();
                foreach (var release in releases)
                {
                    var sha256 = release.GetProperty("sha256").GetString()!;
                    Assert.Equal(64, sha256.Length);
                    Assert.All(sha256, c => Assert.True(Uri.IsHexDigit(c)));
                    Assert.StartsWith("sha512-", release.GetProperty("integrity").GetString()!, StringComparison.Ordinal);
                    Assert.StartsWith("https://registry.npmjs.org/", release.GetProperty("tarball").GetString()!, StringComparison.Ordinal);
                }

                var versions = releases.Select(r => r.GetProperty("version").GetString()).ToList();
                var pair = truth.RootElement.GetProperty("pair");
                Assert.Contains(pair.GetProperty("old").GetString(), versions);
                Assert.Contains(pair.GetProperty("new").GetString(), versions);
            }
        }

        // The figures below were measured once on the frozen dataset (2026-11-07) and are pinned so they cannot drift silently. They are
        // not tuned: the profiles and the dataset were fixed before the first result. Read them with their limits: 50 locators from one
        // application pair, mild drift (three operation buttons renamed, three look-alike buttons added), and no element was removed, so
        // the dataset says nothing about a deleted element healing onto a neighbour. The declined cases are identical look-alikes (the same
        // button repeated in each operation block), where declining is the safe answer.
        [Theory]
        [InlineData(ThresholdProfile.Conservative, 34, 0, 16)]
        [InlineData(ThresholdProfile.Balanced, 41, 0, 9)]
        [InlineData(ThresholdProfile.Aggressive, 43, 0, 7)]
        public void MeasuredOutcomeOnTheRealReleasePair_IsPinnedPerProfile(ThresholdProfile profile, int correct, int wrong, int declined)
        {
            var m = Measure(profile);
            _output.WriteLine(
                $"{profile}: locators={m.Locators} correct={m.CorrectHeals} wrong={m.WrongHeals} " +
                $"declined(target exists)={m.DeclinedWithTarget} (top candidate was the target in {m.DeclinedWithTargetAsTopCandidate}) " +
                $"declined(removed)={m.DeclinedRemoved}");
            foreach (var line in m.Wrong.Select(w => "  WRONG    " + w).Concat(m.Declined.Select(d => "  DECLINED " + d)))
            {
                _output.WriteLine(line);
            }

            Assert.Equal(50, m.Locators);
            Assert.Equal(correct, m.CorrectHeals);
            Assert.Equal(wrong, m.WrongHeals);
            Assert.Equal(declined, m.DeclinedWithTarget);
            Assert.Equal(0, m.DeclinedRemoved);
            Assert.Equal(m.Locators, m.CorrectHeals + m.WrongHeals + m.DeclinedWithTarget + m.DeclinedRemoved);
        }

        [Fact]
        public void Balanced_DeclinesOnlyBecauseTwoCandidatesAreTooClose_AndTheRightOneWasAlwaysOnTop()
        {
            var m = Measure(ThresholdProfile.Balanced);

            Assert.NotEmpty(m.Declined);
            Assert.All(m.Declined, d => Assert.Contains("[Ambiguous]", d));
            Assert.Equal(m.DeclinedWithTarget, m.DeclinedWithTargetAsTopCandidate);
        }

        [Fact]
        public void TheLookAlikeButtonAddedByTheNewRelease_NeverWinsAConfidentHeal()
        {
            // In 5.17.14 the accessible name "get /pets" moved from the operation's summary button to a new "opblock-control-arrow"
            // button. Matching on the name alone would pick that new button; the engine must not.
            foreach (var profile in new[] { ThresholdProfile.Conservative, ThresholdProfile.Balanced, ThresholdProfile.Aggressive })
            {
                using (var truth = LoadGroundTruth())
                {
                    var types = InteractiveTypes(truth);
                    var weights = SimilarityWeights.FromProfile(profile);
                    var oldNodes = Interactive(LoadTree("4.19.1", "initial"), types);
                    var newRoot = LoadTree("5.17.14", "initial");

                    foreach (var oldNode in oldNodes.Where(n => n.ClassName == "opblock-summary-control"))
                    {
                        var result = SelfHealingResolver.Resolve(StoredLocator(oldNode), newRoot, weights, log: _ => { });
                        Assert.False(result.IsConfident && result.Matched?.ClassName == "opblock-control-arrow", $"{profile}: healed '{oldNode.Name}' onto the look-alike button.");
                    }
                }
            }
        }
    }
}
