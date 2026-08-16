using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using UiModel;

namespace ScenarioRunner
{
    // Ground-truth dataset for the #15 benchmark, produced by controlled ablation of a real captured
    // UI tree (#83). Natural locator drift is too rare to build a dataset from: eight ShareX releases
    // yielded two ambiguous candidates and five HandBrake releases none. Ablation inverts the problem —
    // we break a locator ourselves, so the correct answer is known by construction and no human
    // labelling judgement enters the data.
    public enum LocatorMutationKind
    {
        // Pure identifier break: the element survives under an opaque new AutomationId with no other changes.
        RenamedAutomationId,

        // Textual drift: AutomationId changes and Name/label is perturbed (e.g. edited text).
        NameDrift,

        // Layout shift: AutomationId changes and BoundingRectangle coordinates shift.
        PositionShift,

        // Compound refactor: AutomationId changes along with both Name perturbation and layout shift.
        CompoundDrift,

        // The element and its subtree are gone. The engine should decline rather than pick a neighbour.
        RemovedElement,
    }

    public enum LocatorExpectedOutcome
    {
        Successor,
        NoSuccessor,
    }

    // Identifies the correct element without relying on AutomationId, which is exactly what the
    // mutation destroys. ControlType/Name/ancestor path survive a rename and are what a human would
    // use to say "this is the same control".
    public sealed class ElementFingerprint
    {
        public string ControlType { get; set; } = "";
        public string Name { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string AncestorPath { get; set; } = "";

        public bool Matches(UiElementInfo? element, string ancestorPath)
        {
            return element != null &&
                string.Equals(ControlType, element.ControlType ?? "", StringComparison.Ordinal) &&
                string.Equals(Name, element.Name ?? "", StringComparison.Ordinal) &&
                string.Equals(ClassName, element.ClassName ?? "", StringComparison.Ordinal) &&
                string.Equals(AncestorPath, ancestorPath, StringComparison.Ordinal);
        }

        public override string ToString() =>
            $"{ControlType}['{Name}'] under {(string.IsNullOrEmpty(AncestorPath) ? "<root>" : AncestorPath)}";
    }

    public sealed class LocatorAblationScenario
    {
        public string ScenarioId { get; set; } = "";
        public string ApplicationName { get; set; } = "";
        public string SourceVersion { get; set; } = "";

        // The dataset stores the recipe, not a mutated copy of the tree. Regenerating the mutation from
        // the source tree keeps the file small and makes every scenario reproducible without re-running
        // capture — storing dozens of copies of a tree would do neither.
        public string SourceTreeFileName { get; set; } = "";

        public LocatorMutationKind MutationKind { get; set; }
        public LocatorExpectedOutcome ExpectedOutcome { get; set; }

        // The locator as the test knew it, before the mutation.
        public string OriginalAutomationId { get; set; } = "";

        // What the id becomes in the mutated tree. An opaque synthetic identifier (e.g. ablation-7f3a91)
        // so that LLM shortlist prompts do not leak the answer. Null for RemovedElement.
        public string? MutatedAutomationId { get; set; }

        // Parameterized mutations for text drift and coordinate shifts
        public string? MutatedName { get; set; }
        public double ShiftX { get; set; }
        public double ShiftY { get; set; }

        // The correct answer, or null when the correct answer is "do not heal".
        public ElementFingerprint? GroundTruth { get; set; }

        // How this scenario came to exist. "ablation" here; captured drift would say so instead, and
        // a hand-labelled scenario must carry the rationale for the label.
        public string Provenance { get; set; } = "ablation";
        public string? LabelRationale { get; set; }
    }

    public sealed class LocatorAblationDataset
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<LocatorAblationScenario> Scenarios { get; set; } = new();
    }

    public static class LocatorAblationDatasetSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static string ToJson(LocatorAblationDataset dataset) => JsonSerializer.Serialize(dataset, Options);

        public static LocatorAblationDataset FromJson(string json)
        {
            var dataset = JsonSerializer.Deserialize<LocatorAblationDataset>(json, Options)
                ?? throw new InvalidOperationException("Ablation dataset JSON did not deserialize.");

            if (dataset.SchemaVersion > LocatorAblationDataset.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Dataset schema v{dataset.SchemaVersion} is newer than this build supports (v{LocatorAblationDataset.CurrentSchemaVersion}).");
            }

            return dataset;
        }
    }
}
