using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LlmHealing;
using UiModel;

namespace SelfHealing
{
    public sealed class SelfHealingEngine
    {
        private readonly LocatorRepository? _repository;
        private readonly SimilarityWeights _weights;
        private readonly IReadOnlyList<ILlmHealingProvider> _llmProviders;

        public LocatorRepository? Repository => _repository;
        public SimilarityWeights Weights => _weights;
        public IReadOnlyList<ILlmHealingProvider> LlmProviders => _llmProviders;

        public SelfHealingEngine(
            LocatorRepository? repository = null,
            SimilarityWeights? weights = null,
            IEnumerable<ILlmHealingProvider>? llmProviders = null)
        {
            _repository = repository;
            _weights = weights ?? SimilarityWeights.Default;
            _weights.Validate();
            _llmProviders = llmProviders != null ? new List<ILlmHealingProvider>(llmProviders) : new List<ILlmHealingProvider>();
        }

        public async Task<HealResult> ResolveAndRecordAsync(
            string locatorKey,
            UiElementInfo expected,
            UiElementInfo currentTreeRoot,
            Action<string>? log = null,
            CancellationToken cancellationToken = default)
        {
            var healResult = await SelfHealingResolver.ResolveAsync(
                expected,
                currentTreeRoot,
                _llmProviders,
                _weights,
                log,
                cancellationToken).ConfigureAwait(false);

            if (healResult.IsConfident && healResult.Matched != null && _repository != null)
            {
                var entry = LocatorHealingHistoryEntryFactory.FromHealResult(healResult, expected);
                _repository.Upsert(locatorKey, healResult.Matched, entry);
            }

            return healResult;
        }

        public async Task<T> ExecuteWithHealingAsync<T>(
            string locatorKey,
            UiElementInfo expected,
            Func<UiElementInfo, Task<T>> action,
            Func<UiElementInfo> captureTreeRoot,
            string? testIntent = null,
            Action<string>? log = null,
            CancellationToken cancellationToken = default)
        {
            // Capture (clone) before mutating TestIntent below - expected and record.Snapshot
            // are caller/repository-owned objects, and UiElementInfo is a reference type, so
            // mutating them directly would leak this call's TestIntent back into whatever the
            // caller (or the repository's in-memory document) still holds a reference to.
            var target = UiElementSnapshot.Capture(expected);
            if (!string.IsNullOrWhiteSpace(testIntent))
            {
                target.TestIntent = testIntent!;
            }

            if (_repository != null)
            {
                var record = _repository.Find(locatorKey);
                if (record?.Snapshot != null)
                {
                    target = UiElementSnapshot.Capture(record.Snapshot);
                    if (!string.IsNullOrWhiteSpace(testIntent))
                    {
                        target.TestIntent = testIntent!;
                    }
                }
            }

            try
            {
                return await action(target).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[SelfHealingEngine] Initial action execution for locator '{locatorKey}' failed: {ex.Message}. Initiating self-healing...");
                var currentTree = captureTreeRoot();
                var healResult = await ResolveAndRecordAsync(locatorKey, target, currentTree, log, cancellationToken).ConfigureAwait(false);

                if (!healResult.IsConfident || healResult.Matched == null)
                {
                    throw new InvalidOperationException(
                        $"Self-healing failed to find a confident match for locator '{locatorKey}'. Best score: {healResult.Score:F2}", ex);
                }

                log?.Invoke($"[SelfHealingEngine] Healed locator '{locatorKey}' -> matched AutomationId='{healResult.Matched.AutomationId}'. Retrying action...");
                return await action(healResult.Matched).ConfigureAwait(false);
            }
        }
    }
}
