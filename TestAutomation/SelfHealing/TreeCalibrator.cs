using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UiModel;

namespace SelfHealing
{
    /// <summary>
    /// Calibration metrics for a specific threshold profile or confidence level.
    /// </summary>
    public sealed class ProfileCalibrationResult
    {
        public ThresholdProfile Profile { get; set; }
        public double ConfidenceThreshold { get; set; }
        public double MarginThreshold { get; set; }
        public double EvidenceWeightThreshold { get; set; }

        public int SuccessorScenarios { get; set; }
        public int RemovalScenarios { get; set; }

        public int CorrectHeals { get; set; }
        public int FalseHeals { get; set; }
        public int MissedHeals { get; set; }
        public int CorrectDeclines { get; set; }
        public int FalseHealsOnRemoved { get; set; }

        public double Recall => SuccessorScenarios == 0 ? 0.0 : (double)CorrectHeals / SuccessorScenarios;

        public double Precision
        {
            get
            {
                var totalAccepted = CorrectHeals + FalseHeals + FalseHealsOnRemoved;
                return totalAccepted == 0 ? 1.0 : (double)CorrectHeals / totalAccepted;
            }
        }

        public double FalseHealRate
        {
            get
            {
                var totalAccepted = CorrectHeals + FalseHeals + FalseHealsOnRemoved;
                return totalAccepted == 0 ? 0.0 : (double)(FalseHeals + FalseHealsOnRemoved) / totalAccepted;
            }
        }

        public double ManualReviewRate
        {
            get
            {
                var total = SuccessorScenarios + RemovalScenarios;
                return total == 0 ? 0.0 : (double)(MissedHeals + CorrectDeclines) / total;
            }
        }
    }

    /// <summary>
    /// Comprehensive calibration report summarizing threshold performance and recommending an optimal profile.
    /// </summary>
    public sealed class TreeCalibrationReport
    {
        public string ApplicationName { get; set; } = "Application";
        public int TotalTreeElements { get; set; }
        public int ProbedElementsCount { get; set; }
        public int TotalScenariosEvaluated { get; set; }
        public IReadOnlyList<ProfileCalibrationResult> ProfileResults { get; set; } = new List<ProfileCalibrationResult>();
        public ThresholdProfile RecommendedProfile { get; set; }
        public string RecommendationReasoning { get; set; } = string.Empty;

        /// <summary>
        /// Formats the calibration report as a readable markdown document with actionable recommendations.
        /// </summary>
        public string ToMarkdownReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# UI Tree Calibration Report: {ApplicationName}");
            sb.AppendLine();
            sb.AppendLine($"- **Tree Elements:** {TotalTreeElements}");
            sb.AppendLine($"- **Probed Controls:** {ProbedElementsCount}");
            sb.AppendLine($"- **Evaluated Perturbations:** {TotalScenariosEvaluated}");
            sb.AppendLine();
            sb.AppendLine($"## 🏆 Recommended Profile: **{RecommendedProfile}**");
            sb.AppendLine();
            sb.AppendLine(RecommendationReasoning);
            sb.AppendLine();
            sb.AppendLine("### Profile Performance Comparison");
            sb.AppendLine();
            sb.AppendLine("| Profile | Min Confidence | Precision | Auto-Heal Recall | False Heal Rate | Manual Review Rate |");
            sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :---: |");

            foreach (var result in ProfileResults)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "| **{0}** | {1:0.00} | {2:P1} | {3:P1} | {4:P1} | {5:P1} |",
                    result.Profile,
                    result.ConfidenceThreshold,
                    result.Precision,
                    result.Recall,
                    result.FalseHealRate,
                    result.ManualReviewRate));
            }

            sb.AppendLine();
            sb.AppendLine("### Recommended Configuration");
            sb.AppendLine();
            sb.AppendLine("```csharp");
            sb.AppendLine($"// Initialize SelfHealingEngine with the recommended {RecommendedProfile} profile:");
            sb.AppendLine($"var engine = SelfHealingEngine.Create(ThresholdProfile.{RecommendedProfile});");
            sb.AppendLine("```");
            sb.AppendLine();

            return sb.ToString();
        }

        public override string ToString() => ToMarkdownReport();
    }

    /// <summary>
    /// Calibrates an application UI tree against synthetic mutations to recommend an optimal <see cref="ThresholdProfile"/>.
    /// </summary>
    public static class TreeCalibrator
    {
        private enum ProbeMutationKind
        {
            Rename,
            NameDrift,
            PositionShift,
            CompoundDrift,
            Removed
        }

        private sealed class CalibrationProbe
        {
            public UiElementInfo OriginalElement { get; set; } = new UiElementInfo();
            public ProbeMutationKind MutationKind { get; set; }
            public bool ExpectsSuccessor => MutationKind != ProbeMutationKind.Removed;
            public UiElementInfo ExpectedTarget { get; set; } = new UiElementInfo();
            public UiElementInfo MutatedTreeRoot { get; set; } = new UiElementInfo();
        }

        /// <summary>
        /// Analyzes the supplied UI tree, runs synthetic locator refactor probes, and produces a calibration report.
        /// </summary>
        public static TreeCalibrationReport Calibrate(
            UiElementInfo root,
            string applicationName = "Application",
            int maxProbedElements = 50)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            var allElements = Flatten(root).ToList();
            var eligibleElements = allElements
                .Where(e => !string.IsNullOrEmpty(e.AutomationId) && e != root)
                .Take(maxProbedElements)
                .ToList();

            if (eligibleElements.Count == 0)
            {
                // Fallback: take elements with non-empty Name if AutomationId is sparse
                eligibleElements = allElements
                    .Where(e => !string.IsNullOrEmpty(e.Name) && e != root)
                    .Take(maxProbedElements)
                    .ToList();
            }

            var probes = GenerateProbes(root, eligibleElements);

            var profiles = new[]
            {
                ThresholdProfile.Aggressive,
                ThresholdProfile.Balanced,
                ThresholdProfile.Conservative
            };

            var profileResults = new List<ProfileCalibrationResult>();

            foreach (var profile in profiles)
            {
                var weights = SimilarityWeights.FromProfile(profile);
                var result = EvaluateProbesForProfile(profile, weights, probes);
                profileResults.Add(result);
            }

            var (recommended, reasoning) = SelectRecommendation(profileResults);

            return new TreeCalibrationReport
            {
                ApplicationName = applicationName,
                TotalTreeElements = allElements.Count,
                ProbedElementsCount = eligibleElements.Count,
                TotalScenariosEvaluated = probes.Count,
                ProfileResults = profileResults,
                RecommendedProfile = recommended,
                RecommendationReasoning = reasoning
            };
        }

        private static (ThresholdProfile profile, string reasoning) SelectRecommendation(
            IReadOnlyList<ProfileCalibrationResult> results)
        {
            var balanced = results.FirstOrDefault(r => r.Profile == ThresholdProfile.Balanced);
            var conservative = results.FirstOrDefault(r => r.Profile == ThresholdProfile.Conservative);
            var aggressive = results.FirstOrDefault(r => r.Profile == ThresholdProfile.Aggressive);

            if (balanced == null || conservative == null || aggressive == null)
            {
                return (ThresholdProfile.Balanced, "Defaulted to Balanced profile based on standard operating parameters.");
            }

            if (conservative.FalseHealRate == 0.0 && balanced.FalseHealRate > 0.15)
            {
                return (
                    ThresholdProfile.Conservative,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Conservative profile recommended: the application UI tree contains high structural similarity or sibling density causing a {0:P1} false-heal rate under Balanced. Conservative drops false heals to {1:P1} with {2:P1} recall.",
                        balanced.FalseHealRate,
                        conservative.FalseHealRate,
                        conservative.Recall));
            }

            if (aggressive.FalseHealRate <= 0.05 && aggressive.Recall >= 0.85)
            {
                return (
                    ThresholdProfile.Aggressive,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Aggressive profile recommended: the application UI elements are uniquely identifiable and structural decoy risk is minimal (false-heal rate {0:P1}). Aggressive maximizes autonomous recall to {1:P1}.",
                        aggressive.FalseHealRate,
                        aggressive.Recall));
            }

            return (
                ThresholdProfile.Balanced,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Balanced profile recommended: provides optimal balance between high auto-heal recall ({0:P1}) and low false-heal risk ({1:P1}, precision {2:P1}).",
                    balanced.Recall,
                    balanced.FalseHealRate,
                    balanced.Precision));
        }

        private static ProfileCalibrationResult EvaluateProbesForProfile(
            ThresholdProfile profile,
            SimilarityWeights weights,
            IReadOnlyList<CalibrationProbe> probes)
        {
            var result = new ProfileCalibrationResult
            {
                Profile = profile,
                ConfidenceThreshold = weights.MinimumConfidence,
                MarginThreshold = weights.MinimumCandidateMargin,
                EvidenceWeightThreshold = weights.MinimumEvidenceWeight
            };

            foreach (var probe in probes)
            {
                var heal = SelfHealingResolver.Resolve(
                    probe.ExpectedTarget,
                    probe.MutatedTreeRoot,
                    weights,
                    log: _ => { });

                var isAccepted = heal.IsConfident && heal.Matched != null;

                if (probe.ExpectsSuccessor)
                {
                    result.SuccessorScenarios++;

                    if (isAccepted)
                    {
                        // Check if matched element is the true successor (same object reference or same mutated tag)
                        if (IsTargetSuccessor(heal.Matched, probe.OriginalElement))
                        {
                            result.CorrectHeals++;
                        }
                        else
                        {
                            result.FalseHeals++;
                        }
                    }
                    else
                    {
                        result.MissedHeals++;
                    }
                }
                else
                {
                    result.RemovalScenarios++;

                    if (isAccepted)
                    {
                        result.FalseHealsOnRemoved++;
                    }
                    else
                    {
                        result.CorrectDeclines++;
                    }
                }
            }

            return result;
        }

        private static bool IsTargetSuccessor(UiElementInfo? matched, UiElementInfo original)
        {
            if (matched == null) return false;
            if (matched.AutomationId != null && matched.AutomationId.StartsWith("calib-mutated-"))
            {
                return true;
            }
            return ReferenceEquals(matched, original) ||
                   (matched.ControlType == original.ControlType &&
                    matched.Name == original.Name &&
                    matched.BoundingRectangle == original.BoundingRectangle);
        }

        private static List<CalibrationProbe> GenerateProbes(
            UiElementInfo sourceRoot,
            IReadOnlyList<UiElementInfo> targetElements)
        {
            var probes = new List<CalibrationProbe>();

            for (var i = 0; i < targetElements.Count; i++)
            {
                var target = targetElements[i];

                // 1. Rename mutation
                {
                    var mutatedTree = CloneTree(sourceRoot);
                    var matchedInClone = FindCorrespondingElement(mutatedTree, target);
                    if (matchedInClone != null)
                    {
                        matchedInClone.AutomationId = $"calib-mutated-rename-{i}";
                        probes.Add(new CalibrationProbe
                        {
                            OriginalElement = target,
                            ExpectedTarget = CloneElementShallow(target),
                            MutatedTreeRoot = mutatedTree,
                            MutationKind = ProbeMutationKind.Rename
                        });
                    }
                }

                // 2. Name drift mutation (if Name is non-empty)
                if (!string.IsNullOrEmpty(target.Name))
                {
                    var mutatedTree = CloneTree(sourceRoot);
                    var matchedInClone = FindCorrespondingElement(mutatedTree, target);
                    if (matchedInClone != null)
                    {
                        matchedInClone.AutomationId = $"calib-mutated-namedrift-{i}";
                        matchedInClone.Name = target.Name + " (Updated)";
                        probes.Add(new CalibrationProbe
                        {
                            OriginalElement = target,
                            ExpectedTarget = CloneElementShallow(target),
                            MutatedTreeRoot = mutatedTree,
                            MutationKind = ProbeMutationKind.NameDrift
                        });
                    }
                }

                // 3. Position shift mutation (if BoundingRectangle is valid)
                if (target.BoundingRectangle.Width > 0 && target.BoundingRectangle.Height > 0)
                {
                    var mutatedTree = CloneTree(sourceRoot);
                    var matchedInClone = FindCorrespondingElement(mutatedTree, target);
                    if (matchedInClone != null)
                    {
                        matchedInClone.AutomationId = $"calib-mutated-posshift-{i}";
                        matchedInClone.BoundingRectangle = new BoundingRectangle(
                            target.BoundingRectangle.X + 50,
                            target.BoundingRectangle.Y + 30,
                            target.BoundingRectangle.Width,
                            target.BoundingRectangle.Height);

                        probes.Add(new CalibrationProbe
                        {
                            OriginalElement = target,
                            ExpectedTarget = CloneElementShallow(target),
                            MutatedTreeRoot = mutatedTree,
                            MutationKind = ProbeMutationKind.PositionShift
                        });
                    }
                }

                // 4. Removal / Decoy scenario
                {
                    var mutatedTree = CloneTree(sourceRoot);
                    var removed = RemoveElement(mutatedTree, target);
                    if (removed)
                    {
                        probes.Add(new CalibrationProbe
                        {
                            OriginalElement = target,
                            ExpectedTarget = CloneElementShallow(target),
                            MutatedTreeRoot = mutatedTree,
                            MutationKind = ProbeMutationKind.Removed
                        });
                    }
                }
            }

            return probes;
        }

        private static UiElementInfo CloneElementShallow(UiElementInfo element)
        {
            return new UiElementInfo
            {
                AutomationId = element.AutomationId,
                Name = element.Name,
                ControlType = element.ControlType,
                ParentControlType = element.ParentControlType,
                ParentAutomationId = element.ParentAutomationId,
                BoundingRectangle = element.BoundingRectangle,
                SiblingIndex = element.SiblingIndex,
                SiblingCount = element.SiblingCount,
                ClassName = element.ClassName,
                TestIntent = element.TestIntent
            };
        }

        private static UiElementInfo CloneTree(UiElementInfo root)
        {
            var json = UiTreeSerializer.ToJson(root);
            return UiTreeSerializer.FromJson(json);
        }

        private static UiElementInfo? FindCorrespondingElement(UiElementInfo treeRoot, UiElementInfo target)
        {
            // AutomationId is checked in a dedicated first pass across the whole tree so that it always
            // wins over the structural fallback below, regardless of traversal order. Interleaving the two
            // checks per-node let a structurally-identical sibling (same ControlType/Name/BoundingRectangle,
            // common for separators or unlabeled rows) shadow the real AutomationId match if it happened to
            // come first in the flattened order.
            if (!string.IsNullOrEmpty(target.AutomationId))
            {
                foreach (var node in Flatten(treeRoot))
                {
                    if (node.AutomationId == target.AutomationId)
                    {
                        return node;
                    }
                }
            }

            foreach (var node in Flatten(treeRoot))
            {
                if (node.ControlType == target.ControlType &&
                    node.Name == target.Name &&
                    node.BoundingRectangle == target.BoundingRectangle)
                {
                    return node;
                }
            }
            return null;
        }

        private static bool RemoveElement(UiElementInfo treeRoot, UiElementInfo target)
        {
            var match = FindCorrespondingElement(treeRoot, target);
            return match != null && RemoveByReference(treeRoot, match);
        }

        private static bool RemoveByReference(UiElementInfo current, UiElementInfo match)
        {
            for (var i = 0; i < current.Children.Count; i++)
            {
                var child = current.Children[i];
                if (ReferenceEquals(child, match))
                {
                    current.Children.RemoveAt(i);
                    return true;
                }

                if (RemoveByReference(child, match))
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<UiElementInfo> Flatten(UiElementInfo root)
        {
            yield return root;
            foreach (var child in root.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
