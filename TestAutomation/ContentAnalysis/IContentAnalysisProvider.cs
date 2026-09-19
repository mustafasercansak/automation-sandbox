using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.ContentAnalysis
{
    /// <summary>An optional language-model review of captured text for issues the zero-dependency heuristics
    /// can't judge (grammar, meaning, tone). Implementations must never throw for missing configuration or a
    /// failed call - they degrade to an empty result, the same convention <c>LlmIntentPlanner</c> uses.</summary>
    public interface IContentAnalysisProvider
    {
        /// <summary>Whether required local provider configuration is present; this does not probe service
        /// reachability or quota.</summary>
        bool IsAvailable { get; }

        /// <summary>Reviews the supplied passages and returns any issues found; returns an empty list on
        /// missing configuration or a failed call rather than throwing.</summary>
        Task<IReadOnlyList<ContentIssue>> AnalyzeAsync(IReadOnlyList<ContentPassage> passages, CancellationToken cancellationToken = default);
    }
}
