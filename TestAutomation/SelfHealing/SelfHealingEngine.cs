using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LlmHealing;
using UiModel;

namespace SelfHealing
{
    public sealed class SelfHealingEngine
    {
        /// <summary>
        /// The <see cref="Exception.Data"/> key that contains the exception thrown by a failed
        /// action retry while the original locator-resolution failure remains the inner exception.
        /// </summary>
        public const string RetryExceptionDataKey = "RetryException";

        private readonly LocatorRepository? _repository;
        private readonly SimilarityWeights _weights;
        private readonly IReadOnlyList<ILlmHealingProvider> _llmProviders;
        private readonly IHealingReportSink? _reportSink;
        private readonly HealingMode _mode;
        private readonly bool _reconcileAgainstRepository;

        public LocatorRepository? Repository => _repository;
        public SimilarityWeights Weights => _weights;
        public IReadOnlyList<ILlmHealingProvider> LlmProviders => _llmProviders;
        public IHealingReportSink? ReportSink => _reportSink;
        public HealingMode Mode => _mode;

        /// <summary>
        /// When true and a repository is configured, a confident heuristic match is cross-checked
        /// against the rest of the repository (#370): if the winning candidate is already the
        /// current, confidently-resolving identity of another authored locator, this locator's
        /// claim is declined as an <see cref="HealResolutionStatus.OwnershipConflict"/> instead of
        /// silently re-pointing onto an element that belongs to a different test. Off by default;
        /// costs one extra heuristic resolution per other repository entry, only on a heal attempt.
        /// </summary>
        public bool ReconcileAgainstRepository => _reconcileAgainstRepository;

        public SelfHealingEngine(
            LocatorRepository? repository = null,
            SimilarityWeights? weights = null,
            IEnumerable<ILlmHealingProvider>? llmProviders = null,
            IHealingReportSink? reportSink = null,
            HealingMode mode = HealingMode.Review,
            bool reconcileAgainstRepository = false)
        {
            _repository = repository;
            _weights = weights ?? SimilarityWeights.Default;
            _weights.Validate();
            _llmProviders = llmProviders != null ? new List<ILlmHealingProvider>(llmProviders) : new List<ILlmHealingProvider>();
            _reportSink = reportSink ?? HealingReportFileSink.FromEnvironment();
            _mode = mode;
            _reconcileAgainstRepository = reconcileAgainstRepository;
        }

        /// <summary>
        /// Creates a <see cref="SelfHealingEngine"/> configured with a preset <see cref="ThresholdProfile"/>.
        /// </summary>
        public static SelfHealingEngine Create(
            ThresholdProfile profile,
            LocatorRepository? repository = null,
            IEnumerable<ILlmHealingProvider>? llmProviders = null,
            IHealingReportSink? reportSink = null,
            HealingMode mode = HealingMode.Review,
            bool reconcileAgainstRepository = false)
        {
            return new SelfHealingEngine(
                repository: repository,
                weights: SimilarityWeights.FromProfile(profile),
                llmProviders: llmProviders,
                reportSink: reportSink,
                mode: mode,
                reconcileAgainstRepository: reconcileAgainstRepository);
        }

        /// <summary>
        /// Resolves a candidate and immediately persists a confident match without proving it
        /// through an action. Use <see cref="ExecuteWithHealingAsync{T}"/> when persistence must
        /// happen only after the healed action succeeds.
        /// </summary>
        public async Task<HealResult> ResolveAndRecordAsync(
            string locatorKey,
            UiElementInfo expected,
            UiElementInfo currentTreeRoot,
            Action<string>? log = null,
            string? platform = null,
            CancellationToken cancellationToken = default)
        {
            if (_mode == HealingMode.FailClosed)
            {
                log?.Invoke($"[SelfHealingEngine] ResolveAndRecordAsync called for locator '{locatorKey}', but mode is FailClosed. Resolution skipped.");
                var failResult = new HealResult
                {
                    ResolutionStatus = HealResolutionStatus.Unspecified
                };
                RecordResolutionAttempt(
                    locatorKey,
                    expected,
                    failResult,
                    HealingReportEntry.FailClosedOutcome,
                    platform);
                return failResult;
            }

            var healResult = await ResolveAsync(
                locatorKey,
                expected,
                currentTreeRoot,
                log,
                platform,
                cancellationToken).ConfigureAwait(false);

            var confidentMatch = healResult.IsConfident && healResult.Matched != null;
            if (_mode == HealingMode.AutoHeal && confidentMatch)
            {
                PersistAcceptedHeal(locatorKey, expected, healResult, platform);
            }

            RecordResolutionAttempt(
                locatorKey,
                expected,
                healResult,
                confidentMatch
                    ? OutcomeForConfidentMatch(_mode, verifiedByRetry: false)
                    : HealingReportEntry.OutcomeFromResolutionStatus(healResult.ResolutionStatus),
                platform);

            return healResult;
        }

        /// <summary>
        /// Maps a healing mode to the report outcome recorded when a confident candidate match was
        /// found, so <see cref="ResolveAndRecordAsync"/> and <see cref="ExecuteWithHealingAsync{T}"/>
        /// share a single source of truth instead of independently duplicating this mapping.
        /// </summary>
        private static string OutcomeForConfidentMatch(HealingMode mode, bool verifiedByRetry)
        {
            switch (mode)
            {
                case HealingMode.AutoHeal:
                    return verifiedByRetry ? HealingReportEntry.AcceptedOutcome : HealingReportEntry.AcceptedUnverifiedOutcome;
                case HealingMode.Observe:
                    return HealingReportEntry.ObservedOutcome;
                case HealingMode.Review:
                    return HealingReportEntry.ManualReviewOutcome;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "FailClosed does not resolve confident matches.");
            }
        }

        private async Task<HealResult> ResolveAsync(
            string locatorKey,
            UiElementInfo expected,
            UiElementInfo currentTreeRoot,
            Action<string>? log,
            string? platform,
            CancellationToken cancellationToken)
        {
            var result = await SelfHealingResolver.ResolveAsync(
                expected,
                currentTreeRoot,
                _llmProviders,
                _weights,
                log,
                platform,
                cancellationToken).ConfigureAwait(false);

            return ApplyRepositoryOwnershipReconciliation(locatorKey, result, currentTreeRoot, log);
        }

        // #370: a confident match whose winning candidate is already the current, confidently
        // resolving identity of a *different* authored locator is almost always a false heal -
        // the classic "the element I was pointing at was deleted, so I healed onto the neighbour
        // that belongs to another test" failure. Re-resolving every other repository entry against
        // the same live tree and rejecting the claim when a stronger sibling already owns the node
        // is the one structural signal that separates this case from a genuine drift, because it
        // brings in evidence the single-locator scorer never sees: what the rest of the suite
        // still resolves to. Opt-in (ReconcileAgainstRepository); heuristic-only, so no extra LLM
        // traffic; a no-op when the repository holds fewer than two locators.
        private HealResult ApplyRepositoryOwnershipReconciliation(
            string locatorKey,
            HealResult result,
            UiElementInfo currentTreeRoot,
            Action<string>? log)
        {
            if (!_reconcileAgainstRepository || _repository == null)
            {
                return result;
            }

            if (!result.IsConfident || result.Matched == null)
            {
                return result;
            }

            List<LocatorRecord> others;
            try
            {
                others = _repository.Load().Locators
                    .Where(r => r?.Snapshot != null
                        && !string.IsNullOrEmpty(r.LocatorKey)
                        && !string.Equals(r.LocatorKey, locatorKey, StringComparison.Ordinal))
                    .ToList();
            }
            catch (Exception ex)
            {
                log?.Invoke($"[SelfHealingEngine] Repository reconciliation skipped for locator '{locatorKey}': could not load the repository ({ex.GetType().Name}).");
                return result;
            }

            foreach (var other in others)
            {
                var owner = SelfHealingResolver.Resolve(other.Snapshot, currentTreeRoot, _weights, log: null);
                if (!owner.IsConfident || !ReferenceEquals(owner.Matched, result.Matched))
                {
                    continue;
                }

                // Another locator still resolves confidently onto this exact node. Keep the claim
                // only if this locator beats that owner by the same margin the scorer already
                // requires between competing candidates; otherwise the node is spoken for.
                if (result.Source == HealSource.Heuristic
                    && CandidateMargin.HasSufficientMargin(result.Score, owner.Score, _weights.MinimumCandidateMargin))
                {
                    continue;
                }

                result.RejectedByReconciliation = true;
                result.ReconciliationDisposition = BatchReconciliationDisposition.DeclinedByStrongerClaim;
                result.ResolutionStatus = HealResolutionStatus.OwnershipConflict;
                log?.Invoke(
                    $"[SelfHealingEngine] Repository reconciliation declined locator '{locatorKey}': its winning candidate " +
                    $"(score {result.Score:F3}) is the current identity of authored locator '{other.LocatorKey}' " +
                    $"(score {owner.Score:F3}). Routing to review instead of healing onto another test's element.");
                return result;
            }

            return result;
        }

        private void PersistAcceptedHeal(
            string locatorKey,
            UiElementInfo expected,
            HealResult healResult,
            string? platform)
        {
            if (!healResult.IsConfident || healResult.Matched == null)
            {
                throw new InvalidOperationException("Only a confident matched heal can be committed.");
            }

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
        }

        private void RecordResolutionAttempt(
            string locatorKey,
            UiElementInfo expected,
            HealResult healResult,
            string outcome,
            string? platform)
        {
            _reportSink?.Record(HealingReportEntry.FromResolutionAttempt(locatorKey, expected, healResult, outcome, platform));
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

        /// <summary>
        /// Executes an action and, after a locator-resolution failure, retries it with a healed
        /// candidate. Repository history and accepted-heal reporting are committed only when the
        /// retry succeeds.
        /// </summary>
        /// <remarks>
        /// If the retry fails, the original locator-resolution exception remains the returned
        /// exception's <see cref="Exception.InnerException"/> and the retry exception, including
        /// its stack trace, is available from <see cref="RetryExceptionDataKey"/> in
        /// <see cref="Exception.Data"/>.
        /// </remarks>
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
                    ? $"[SelfHealingEngine] Action for locator '{locatorKey}' threw {ex.GetType().Name} ('{ex.Message}'), classified as a locator-resolution failure. Mode is {_mode}."
                    : $"[SelfHealingEngine] Action for locator '{locatorKey}' threw {ex.GetType().Name} ('{ex.Message}'), classified as a non-locator failure. Rethrowing without healing or retrying the action.");
                if (!attemptHealing)
                {
                    throw;
                }

                if (_mode == HealingMode.FailClosed)
                {
                    log?.Invoke($"[SelfHealingEngine] Healing mode is FailClosed. Action will not be retried and healing discovery will not be executed.");
                    RecordResolutionAttempt(
                        locatorKey,
                        target,
                        new HealResult { ResolutionStatus = HealResolutionStatus.Unspecified },
                        HealingReportEntry.FailClosedOutcome,
                        platform);
                    throw;
                }

                var currentTree = captureTreeRoot();
                var healResult = await ResolveAsync(locatorKey, target, currentTree, log, platform, cancellationToken).ConfigureAwait(false);

                if (!healResult.IsConfident || healResult.Matched == null)
                {
                    RecordResolutionAttempt(
                        locatorKey,
                        target,
                        healResult,
                        HealingReportEntry.OutcomeFromResolutionStatus(healResult.ResolutionStatus),
                        platform);
                    throw new InvalidOperationException(
                        $"Self-healing failed to find a confident match for locator '{locatorKey}'. Best score: {healResult.Score:F2}", ex);
                }

                if (_mode == HealingMode.Observe)
                {
                    log?.Invoke($"[SelfHealingEngine] Observe mode: evaluated locator '{locatorKey}'. Best candidate: AutomationId='{healResult.Matched?.AutomationId}', Score={healResult.Score:F2}, IsConfident={healResult.IsConfident}. Action will not be retried and heal will not be persisted.");
                    RecordResolutionAttempt(
                        locatorKey,
                        target,
                        healResult,
                        OutcomeForConfidentMatch(_mode, verifiedByRetry: false),
                        platform);
                    throw new InvalidOperationException(
                        $"Self-healing observed candidate '{healResult.Matched?.AutomationId}' (score: {healResult.Score:F2}) for locator '{locatorKey}', but healing mode is Observe. Action was not retried and locator was not persisted.",
                        ex);
                }

                if (_mode == HealingMode.Review)
                {
                    log?.Invoke($"[SelfHealingEngine] Review mode: evaluated locator '{locatorKey}'. Best candidate: AutomationId='{healResult.Matched?.AutomationId}', Score={healResult.Score:F2}, IsConfident={healResult.IsConfident}. Routing to manual review without auto-persisting or retrying.");
                    RecordResolutionAttempt(
                        locatorKey,
                        target,
                        healResult,
                        OutcomeForConfidentMatch(_mode, verifiedByRetry: false),
                        platform);
                    throw new InvalidOperationException(
                        $"Self-healing resolved candidate '{healResult.Matched?.AutomationId}' (score: {healResult.Score:F2}) for locator '{locatorKey}'. Healing mode is Review: candidate routed to review without executing or persisting.",
                        ex);
                }

                // Mode is AutoHeal:
                log?.Invoke($"[SelfHealingEngine] Healed locator '{locatorKey}' -> matched AutomationId='{healResult.Matched.AutomationId}'. Retrying action...");
                T result;
                try
                {
                    result = await action(healResult.Matched).ConfigureAwait(false);
                }
                catch (Exception retryException)
                {
                    log?.Invoke($"[SelfHealingEngine] Retried action for locator '{locatorKey}' threw {retryException.GetType().Name} ('{retryException.Message}'). The proposed heal was not persisted.");
                    RecordResolutionAttempt(
                        locatorKey,
                        target,
                        healResult,
                        HealingReportEntry.RetryFailedOutcome,
                        platform);
                    var failure = new InvalidOperationException(
                        $"Self-healing matched locator '{locatorKey}', but the retried action failed with {retryException.GetType().Name}: {retryException.Message}",
                        ex);
                    failure.Data[RetryExceptionDataKey] = retryException;
                    throw failure;
                }

                // The action is the proof that the proposed element works. Persisting before this
                // point would turn a failed retry into the repository's new baseline and report an
                // unproven match as accepted on every later run.
                PersistAcceptedHeal(locatorKey, target, healResult, platform);
                RecordResolutionAttempt(
                    locatorKey,
                    target,
                    healResult,
                    OutcomeForConfidentMatch(_mode, verifiedByRetry: true),
                    platform);
                return result;
            }
        }
    }
}
