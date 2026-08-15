using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UiModel;
namespace LlmHealing
{
    // Runs every configured provider against the same broken-locator scenario in
    // parallel, so their answers can be compared side by side. Providers without a
    // configured API key are skipped rather than reported as failures.

    public static class LlmHealingEvaluator
    {
        public static async Task<IReadOnlyList<LlmHealingResult>> EvaluateAsync(
            IEnumerable<ILlmHealingProvider> providers,
            UiElementInfo expected,
            IReadOnlyList<CandidateScore> candidates,
            string? platform = null,
            CancellationToken cancellationToken = default)
        {
            var available = providers.Where(p => p.IsAvailable).ToList();
            var tasks = available.Select(p => EvaluateProviderAsync(p, expected, candidates, platform, cancellationToken));
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task<LlmHealingResult> EvaluateProviderAsync(
            ILlmHealingProvider provider,
            UiElementInfo expected,
            IReadOnlyList<CandidateScore> candidates,
            string? platform,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await provider.ResolveAsync(expected, candidates, platform, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(result.ProviderName))
                {
                    result.ProviderName = provider.Name;
                }
                return result;
            }
            catch (Exception ex)
            {
                // Providers are expected to return failures, but one implementation throwing
                // must not erase every other provider's vote or its own identity from telemetry.
                return new LlmHealingResult
                {
                    ProviderName = provider.Name,
                    Success = false,
                    ErrorMessage = ex.GetType().Name + ": " + ex.Message,
                };
            }
        }
    }
}
