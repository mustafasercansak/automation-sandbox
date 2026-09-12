using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.UiModel;
namespace AutomationSandbox.LlmHealing
{
    /// <summary>Contract for a named fallback provider. Failures are reported as result diagnostics, and provider names must be unique within an evaluation.</summary>
    public interface ILlmHealingProvider
    {
        // Must be unique among the providers configured for a single run. Consensus
        // acceptance (#10) records the agreeing providers by this name, so two providers
        // sharing one would make a HealResult's AgreedProviders ambiguous - "OpenAI, OpenAI"
        // cannot be read as two independent votes. Every built-in provider takes a name
        // constructor parameter for exactly this case.
        /// <summary>Unique provider name used to associate independent votes and diagnostics within an evaluation.</summary>
        string Name { get; }

        // True when the provider has everything it needs to run (an API key
        // configured via environment variable, typically). Callers should skip
        // unavailable providers rather than let them fail with a confusing error -
        // this is what lets the evaluation harness run with whichever subset of
        // provider keys happen to be configured in the current environment.
        /// <summary>True when the provider has everything it needs to run (an API key configured via environment variable, typically). Callers should skip unavailable providers rather than let them fail with a confusing error - this is what lets the evaluation harness run with whichever subset of provider keys happen to be configured in the current environment.</summary>
        bool IsAvailable { get; }

        /// <summary>Proposes one candidate from the supplied shortlist and returns provider diagnostics; the caller still enforces shortlist membership and independent agreement.</summary>
        Task<LlmHealingResult> ResolveAsync(
            UiElementInfo expected,
            IReadOnlyList<CandidateScore> candidates,
            string? platform = null,
            CancellationToken cancellationToken = default);
    }
}
