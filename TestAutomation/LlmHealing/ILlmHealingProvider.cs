using System.Threading;
using System.Threading.Tasks;
using UiModel;
namespace LlmHealing
{
    public interface ILlmHealingProvider
    {
        // Must be unique among the providers configured for a single run. Consensus
        // acceptance (#10) records the agreeing providers by this name, so two providers
        // sharing one would make a HealResult's AgreedProviders ambiguous - "OpenAI, OpenAI"
        // cannot be read as two independent votes. Every built-in provider takes a name
        // constructor parameter for exactly this case.
        string Name { get; }

        // True when the provider has everything it needs to run (an API key
        // configured via environment variable, typically). Callers should skip
        // unavailable providers rather than let them fail with a confusing error -
        // this is what lets the evaluation harness run with whichever subset of
        // provider keys happen to be configured in the current environment.
        bool IsAvailable { get; }

        Task<LlmHealingResult> ResolveAsync(
            UiElementInfo expected,
            IReadOnlyList<CandidateScore> candidates,
            string? platform = null,
            CancellationToken cancellationToken = default);
    }
}
