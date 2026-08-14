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
            var tasks = available.Select(p => p.ResolveAsync(expected, candidates, platform, cancellationToken));
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }
}
