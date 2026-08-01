using System.Threading;

using System.Threading.Tasks;

using UiModel;



namespace LlmHealing

{

    public interface ILlmHealingProvider

    {

        string Name { get; }



        // True when the provider has everything it needs to run (an API key

        // configured via environment variable, typically). Callers should skip

        // unavailable providers rather than let them fail with a confusing error -

        // this is what lets the evaluation harness run with whichever subset of

        // provider keys happen to be configured in the current environment.

        bool IsAvailable { get; }



        Task<LlmHealingResult> ResolveAsync(UiElementInfo expected, IReadOnlyList<CandidateScore> candidates, CancellationToken cancellationToken = default);

    }

}
