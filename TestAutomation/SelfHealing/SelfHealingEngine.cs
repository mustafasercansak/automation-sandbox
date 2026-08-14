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
        private readonly IHealingReportSink? _reportSink;

        public LocatorRepository? Repository => _repository;
        public SimilarityWeights Weights => _weights;
        public IReadOnlyList<ILlmHealingProvider> LlmProviders => _llmProviders;

        public SelfHealingEngine(
            LocatorRepository? repository = null,
            SimilarityWeights? weights = null,
            IEnumerable<ILlmHealingProvider>? llmProviders = null,
            IHealingReportSink? reportSink = null)
        {
            _repository = repository;
            _weights = weights ?? SimilarityWeights.Default;
            _weights.Validate();
            _llmProviders = llmProviders != null ? new List<ILlmHealingProvider>(llmProviders) : new List<ILlmHealingProvider>();
            _reportSink = reportSink ?? HealingReportFileSink.FromEnvironment();
        }

        public async Task<HealResult> ResolveAndRecordAsync(
            string locatorKey,
            UiElementInfo expected,
            UiElementInfo currentTreeRoot,
            Action<string>? log = null,
            string? platform = null,
            CancellationToken cancellationToken = default)
        {
            var healResult = await SelfHealingResolver.ResolveAsync(
                expected,
                currentTreeRoot,
                _llmProviders,
                _weights,
                log,
                platform,
                cancellationToken).ConfigureAwait(false);

            if (healResult.IsConfident && healResult.Matched != null)
            {
                var matchedSnapshot = UiElementSnapshot.Capture(healResult.Matched);
                if (string.IsNullOrWhiteSpace(matchedSnapshot.TestIntent) && !string.IsNullOrWhiteSpace(expected.TestIntent))
                {
                    matchedSnapshot.TestIntent = expected.TestIntent;
                }

                if (_repository != null)
                {
                    var entry = LocatorHealingHistoryEntryFactory.FromHealResult(healResult, expected);
                    _repository.Upsert(locatorKey, matchedSnapshot, entry, platform: platform);
                }

                _reportSink?.Record(HealingReportEntry.FromHealResult(locatorKey, expected, matchedSnapshot, healResult));
            }

            return healResult;
        }

        // Exact type names that mark an exception as a locator/element-resolution failure.
        // Matching by name rather than by type identity is deliberate: this assembly is
        // FlaUI-free (it multi-targets netstandard2.0/net8.0 and runs cross-platform), so it
        // cannot reference FlaUI's ElementNotAvailableException, and name matching also
        // covers Playwright/Selenium-style and hand-rolled locator exceptions without
        // depending on any of them. Matching is exact rather than substring - a substring
        // check would also treat an unrelated backend/state exception that merely happens to
        // contain one of these words (e.g. a hypothetical "ElementNotFoundInCacheException"
        // raised by something other than locator resolution) as healable, which is exactly
        // the misclassification risk this policy exists to prevent. Callers running
        // non-idempotent actions (e.g. placing an order) should still pass their own
        // shouldHeal policy to ExecuteWithHealingAsync rather than relying on this default.
        private static readonly HashSet<string> LocatorResolutionExceptionTypeNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "ElementNotFoundException",
                "ElementNotAvailableException",
                "LocatorNotFoundException",
                "NoSuchElementException",
            };

        public static bool IsLocatorResolutionException(Exception exception)
        {
            return LocatorResolutionExceptionTypeNames.Contains(exception.GetType().Name);
        }

        public async Task<T> ExecuteWithHealingAsync<T>(
            string locatorKey,
            UiElementInfo expected,
            Func<UiElementInfo, Task<T>> action,
            Func<UiElementInfo> captureTreeRoot,
            string? testIntent = null,
            Action<string>? log = null,
            string? platform = null,
            CancellationToken cancellationToken = default,
            Func<Exception, bool>? shouldHeal = null)
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
                // Classify before touching the tree: re-running the whole action on the
                // strength of an unrelated failure (assertion, timeout, backend error) is
                // dangerous for non-idempotent actions - if the click already succeeded
                // server-side and only a later parse threw, a blind retry would duplicate
                // the side effect. Anything the policy does not accept bubbles up untouched.
                var attemptHealing = shouldHeal?.Invoke(ex) ?? IsLocatorResolutionException(ex);
                log?.Invoke(attemptHealing
                    ? $"[SelfHealingEngine] Action for locator '{locatorKey}' threw {ex.GetType().Name} ('{ex.Message}'), classified as a locator-resolution failure. Initiating self-healing..."
                    : $"[SelfHealingEngine] Action for locator '{locatorKey}' threw {ex.GetType().Name} ('{ex.Message}'), classified as a non-locator failure. Rethrowing without healing or retrying the action.");
                if (!attemptHealing)
                {
                    throw;
                }

                var currentTree = captureTreeRoot();
                var healResult = await ResolveAndRecordAsync(locatorKey, target, currentTree, log, platform, cancellationToken).ConfigureAwait(false);

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
