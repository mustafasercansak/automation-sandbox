using AutomationSandbox.SelfHealing;
using AutomationSandbox.UiModel;

namespace AutomationSandbox.SelfHealingCli;

/// <summary>Makes a <see cref="HealResult"/> small enough to print or to hand to a model: it keeps the decision and its evidence and drops what makes
/// the output many times larger than its input (#620), namely the child subtree every element carries and every scored node listed as a candidate.
/// Shared by <c>automation-sandbox-heal --compact</c> and the MCP <c>heal_locator</c> tool (#668), which both need the same shape; the MCP tool's answer
/// is cut at a fixed length, and without this the cut removed <c>IsConfident</c>, the acceptance gate.</summary>
internal static class HealResultCompactor
{
    /// <summary>How many of the best candidates are kept. <see cref="HealResult.CandidateCount"/> still reports the full number.</summary>
    public const int CandidateLimit = 5;

    /// <summary>Compacts the result in place. Elements are shallow-copied, so the live tree handed to the resolver is not modified. Decide on the
    /// full result first (<see cref="HealResult.IsConfident"/>): what is printed must not change the decision.</summary>
    public static void Compact(HealResult result)
    {
        result.Matched = WithoutChildren(result.Matched);
        result.HeuristicMatched = WithoutChildren(result.HeuristicMatched);
        if (result.Candidates is not null)
        {
            result.Candidates = result.Candidates
                .OrderByDescending(candidate => candidate.TotalScore)
                .Take(CandidateLimit)
                .Select(candidate => new CandidateScore
                {
                    Candidate = WithoutChildren(candidate.Candidate)!,
                    TotalScore = candidate.TotalScore,
                    Components = candidate.Components,
                    EvidenceCoverage = candidate.EvidenceCoverage,
                    CandidateId = candidate.CandidateId,
                })
                .ToList();
        }
    }

    private static UiElementInfo? WithoutChildren(UiElementInfo? element) =>
        element is null
            ? null
            : new UiElementInfo
            {
                ControlType = element.ControlType,
                Name = element.Name,
                AutomationId = element.AutomationId,
                ClassName = element.ClassName,
                BoundingRectangle = element.BoundingRectangle,
                ParentControlType = element.ParentControlType,
                ParentAutomationId = element.ParentAutomationId,
                SiblingIndex = element.SiblingIndex,
                SiblingCount = element.SiblingCount,
                TestIntent = element.TestIntent,
                ChildControlTypeSignature = element.ChildControlTypeSignature,
            };
}
