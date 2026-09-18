using System.Runtime.CompilerServices;
using AutomationSandbox.LlmHealing;
using AutomationSandbox.UiModel;

namespace AutomationSandbox.SelfHealing
{
    /// <summary>Outcome of optional one-candidate ownership reconciliation.</summary>
    public enum BatchReconciliationDisposition
    {
        /// <summary>Single-locator acceptance already declined this request.</summary>
        BaselineDecline,
        /// <summary>No other accepted request claimed this live node.</summary>
        PreservedUncontested,
        /// <summary>This claim won under the source-specific separation rule.</summary>
        WonContention,
        /// <summary>A sufficiently stronger same-source claim won ownership.</summary>
        DeclinedByStrongerClaim,
        /// <summary>Contention could not be resolved unambiguously, including mixed heuristic and LLM claims.</summary>
        DeclinedAmbiguousContention,
    }

    /// <summary>Logical locator key and expected evidence for one request in an opt-in batch.</summary>
    public sealed class BatchHealingRequest
    {
        /// <summary>Associates a logical locator key with the expected snapshot for one batch request.</summary>
        public BatchHealingRequest(string locatorKey, UiElementInfo expected)
        {
            if (string.IsNullOrWhiteSpace(locatorKey))
            {
                throw new ArgumentException("locatorKey must not be null or empty.", nameof(locatorKey));
            }

            LocatorKey = locatorKey;
            Expected = expected ?? throw new ArgumentNullException(nameof(expected));
        }

        /// <summary>Logical repository key identifying a locator independently of its current automation identifier.</summary>
        public string LocatorKey { get; }
        /// <summary>Stored evidence for the element whose locator needs resolution.</summary>
        public UiElementInfo Expected { get; }
    }

    /// <summary>One independently resolved locator and its ownership-reconciliation outcome.</summary>
    public sealed class BatchHealingItemResult
    {
        internal BatchHealingItemResult(BatchHealingRequest request, HealResult result)
        {
            Request = request;
            Result = result;
            WasIndependentlyConfident = result.IsConfident;
        }

        /// <summary>Original batch request associated with this outcome.</summary>
        public BatchHealingRequest Request { get; }
        /// <summary>Resolution evidence after ownership reconciliation.</summary>
        public HealResult Result { get; }
        /// <summary>Whether single-locator acceptance passed before ownership reconciliation.</summary>
        public bool WasIndependentlyConfident { get; }
        /// <summary>Opaque pre-order tree path identifying the candidate within this captured tree only.</summary>
        public string? CandidateIdentity => Result.CandidateIdentity;
        /// <summary>Ownership decision applied to the independently proposed candidate.</summary>
        public BatchReconciliationDisposition ReconciliationDisposition =>
            Result.ReconciliationDisposition ?? BatchReconciliationDisposition.BaselineDecline;
    }

    /// <summary>Batch outcomes after reconciling accepted claims against one shared captured tree.</summary>
    public sealed class BatchHealingResult
    {
        internal BatchHealingResult(IReadOnlyList<BatchHealingItemResult> items)
        {
            Items = items;
        }

        /// <summary>Results in request order.</summary>
        public IReadOnlyList<BatchHealingItemResult> Items { get; }
        /// <summary>Number of live nodes claimed by more than one independently accepted request.</summary>
        public int ContestedCandidateCount => Items
            .Where(i => i.WasIndependentlyConfident && i.CandidateIdentity != null)
            .GroupBy(i => i.CandidateIdentity!, StringComparer.Ordinal)
            .Count(g => g.Count() > 1);
        /// <summary>Number of previously accepted claims declined by ownership reconciliation.</summary>
        public int ReconciliationDeclineCount => Items.Count(i => i.Result.RejectedByReconciliation);
    }

    /// <summary>Deterministic locator resolution with optional independent-provider fallback and explicit batch ownership reconciliation.</summary>
    public static partial class SelfHealingResolver
    {
        /// <summary>
        /// Independently resolves several stale locators against one captured tree, then
        /// applies opt-in one-to-one ownership reconciliation to already accepted top claims.
        /// Existing single-locator resolution behavior is unchanged.
        /// </summary>
        public static BatchHealingResult ResolveBatch(
            IEnumerable<BatchHealingRequest> requests,
            UiElementInfo currentTreeRoot,
            SimilarityWeights? weights = null,
            Action<string>? log = null)
        {
            var input = ValidateBatchInput(requests, currentTreeRoot);
            var w = weights ?? SimilarityWeights.Default;
            w.Validate();

            var items = new List<BatchHealingItemResult>(input.Count);
            foreach (var request in input)
            {
                items.Add(new BatchHealingItemResult(
                    request,
                    Resolve(request.Expected, currentTreeRoot, w, log)));
            }

            return Reconcile(items, currentTreeRoot, w, log);
        }

        /// <summary>
        /// Async batch resolution retains the existing per-locator LLM shortlist,
        /// hallucination guard and consensus rules. Provider failures remain isolated in
        /// each locator's HealResult telemetry; cancellation never returns a partial batch.
        /// </summary>
        public static async Task<BatchHealingResult> ResolveBatchAsync(
            IEnumerable<BatchHealingRequest> requests,
            UiElementInfo currentTreeRoot,
            IEnumerable<ILlmHealingProvider>? llmProviders = null,
            SimilarityWeights? weights = null,
            Action<string>? log = null,
            string? platform = null,
            CancellationToken cancellationToken = default)
        {
            var input = ValidateBatchInput(requests, currentTreeRoot);
            var w = weights ?? SimilarityWeights.Default;
            w.Validate();
            var providers = llmProviders?.ToList();

            var items = new List<BatchHealingItemResult>(input.Count);
            foreach (var request in input)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ResolveAsync(
                    request.Expected,
                    currentTreeRoot,
                    providers,
                    w,
                    log,
                    platform,
                    cancellationToken).ConfigureAwait(false);
                items.Add(new BatchHealingItemResult(request, result));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Reconcile(items, currentTreeRoot, w, log);
        }

        private static List<BatchHealingRequest> ValidateBatchInput(
            IEnumerable<BatchHealingRequest> requests,
            UiElementInfo currentTreeRoot)
        {
            if (requests == null)
            {
                throw new ArgumentNullException(nameof(requests));
            }

            if (currentTreeRoot == null)
            {
                throw new ArgumentNullException(nameof(currentTreeRoot));
            }

            var input = requests.ToList();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < input.Count; i++)
            {
                var request = input[i];
                if (request == null)
                {
                    throw new ArgumentException("Batch requests must not contain null entries.", nameof(requests));
                }

                if (!keys.Add(request.LocatorKey))
                {
                    throw new ArgumentException(
                        $"Batch locator keys must be unique; '{request.LocatorKey}' appears more than once.",
                        nameof(requests));
                }
            }

            return input;
        }

        private static BatchHealingResult Reconcile(
            List<BatchHealingItemResult> items,
            UiElementInfo currentTreeRoot,
            SimilarityWeights weights,
            Action<string>? log)
        {
            var candidateIdentities = BuildCandidateIdentities(currentTreeRoot);
            foreach (var item in items)
            {
                if (!item.WasIndependentlyConfident || item.Result.Matched == null)
                {
                    item.Result.ReconciliationDisposition = BatchReconciliationDisposition.BaselineDecline;
                    continue;
                }

                if (!candidateIdentities.TryGetValue(item.Result.Matched, out var identity))
                {
                    throw new InvalidOperationException(
                        $"Resolved candidate for locator '{item.Request.LocatorKey}' does not belong to the batch tree.");
                }

                item.Result.CandidateIdentity = identity;
                item.Result.ReconciliationDisposition = BatchReconciliationDisposition.PreservedUncontested;
            }

            var contentions = items
                .Where(i => i.WasIndependentlyConfident && i.CandidateIdentity != null)
                .GroupBy(i => i.CandidateIdentity!, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            foreach (var contention in contentions)
            {
                var allHeuristic = contention.All(i => i.Result.Source == HealSource.Heuristic);
                var allLlm = contention.All(i => i.Result.Source == HealSource.Llm);

                if (allHeuristic)
                {
                    var ranked = contention
                        .OrderByDescending(i => i.Result.Score)
                        .ThenBy(i => i.Request.LocatorKey, StringComparer.Ordinal)
                        .ToList();
                    var ownershipMargin = ranked[0].Result.Score - ranked[1].Result.Score;
                    var wonByMargin = CandidateMargin.HasSufficientMargin(
                        ranked[0].Result.Score,
                        ranked[1].Result.Score,
                        weights.MinimumCandidateMargin);

                    ApplyContentionOutcome(
                        ranked,
                        wonByMargin,
                        $"[SelfHealing] Batch candidate '{contention.Key}' assigned to '{ranked[0].Request.LocatorKey}' " +
                            $"with ownership margin {ownershipMargin:F3}; {ranked.Count - 1} weaker claim(s) declined.",
                        $"[SelfHealing] Batch candidate '{contention.Key}' has ambiguous ownership margin " +
                            $"{ownershipMargin:F3} below {weights.MinimumCandidateMargin:F3}; all {ranked.Count} claims declined.",
                        log);
                }
                else if (allLlm)
                {
                    var ranked = contention
                        .OrderByDescending(i => i.Result.AgreedProviders.Count)
                        .ThenBy(i => i.Request.LocatorKey, StringComparer.Ordinal)
                        .ToList();
                    var voteMargin = ranked[0].Result.AgreedProviders.Count - ranked[1].Result.AgreedProviders.Count;

                    ApplyContentionOutcome(
                        ranked,
                        voteMargin >= 1,
                        $"[SelfHealing] Batch candidate '{contention.Key}' assigned to '{ranked[0].Request.LocatorKey}' " +
                            $"by LLM consensus vote margin ({ranked[0].Result.AgreedProviders.Count} vs {ranked[1].Result.AgreedProviders.Count}); " +
                            $"{ranked.Count - 1} weaker claim(s) declined.",
                        $"[SelfHealing] Batch candidate '{contention.Key}' has ambiguous LLM consensus tie " +
                            $"({ranked[0].Result.AgreedProviders.Count} votes each); all {ranked.Count} claims declined.",
                        log);
                }
                else
                {
                    // Mixed contention between Heuristic and LLM consensus claims. Because heuristic score
                    // and LLM consensus quorum operate on fundamentally incommensurable scales, neither can
                    // establish a valid structural score margin over the other. All claimants are declined
                    // as ambiguous for manual review.
                    var ranked = contention
                        .OrderBy(i => i.Request.LocatorKey, StringComparer.Ordinal)
                        .ToList();

                    foreach (var claimant in ranked)
                    {
                        RejectClaim(claimant, BatchReconciliationDisposition.DeclinedAmbiguousContention);
                    }

                    log?.Invoke(
                        $"[SelfHealing] Batch candidate '{contention.Key}' has mixed-source contention between heuristic " +
                        $"and LLM consensus claims; all {ranked.Count} claims declined as ambiguous.");
                }
            }

            return new BatchHealingResult(items);
        }

        // Shared by the allHeuristic and allLlm contention branches, which differ only in how
        // ranked[]/margin are computed - not in what happens once a winner is (or isn't) decided.
        private static void ApplyContentionOutcome(
            List<BatchHealingItemResult> ranked,
            bool wonByMargin,
            string wonLogMessage,
            string ambiguousLogMessage,
            Action<string>? log)
        {
            if (wonByMargin)
            {
                ranked[0].Result.ReconciliationDisposition = BatchReconciliationDisposition.WonContention;
                foreach (var loser in ranked.Skip(1))
                {
                    RejectClaim(loser, BatchReconciliationDisposition.DeclinedByStrongerClaim);
                }

                log?.Invoke(wonLogMessage);
            }
            else
            {
                foreach (var claimant in ranked)
                {
                    RejectClaim(claimant, BatchReconciliationDisposition.DeclinedAmbiguousContention);
                }

                log?.Invoke(ambiguousLogMessage);
            }
        }

        private static void RejectClaim(
            BatchHealingItemResult item,
            BatchReconciliationDisposition disposition)
        {
            item.Result.RejectedByReconciliation = true;
            item.Result.ReconciliationDisposition = disposition;
            item.Result.ResolutionStatus = HealResolutionStatus.OwnershipConflict;
        }

        private static Dictionary<UiElementInfo, string> BuildCandidateIdentities(UiElementInfo root)
        {
            var identities = new Dictionary<UiElementInfo, string>(UiElementReferenceComparer.Instance);
            AddCandidateIdentity(root, "r", identities);
            return identities;
        }

        private static void AddCandidateIdentity(
            UiElementInfo node,
            string identity,
            Dictionary<UiElementInfo, string> identities)
        {
            if (identities.ContainsKey(node))
            {
                return;
            }

            identities[node] = identity;

            for (var i = 0; i < node.Children.Count; i++)
            {
                AddCandidateIdentity(node.Children[i], identity + "/" + i, identities);
            }
        }

        private sealed class UiElementReferenceComparer : IEqualityComparer<UiElementInfo>
        {
            public static readonly UiElementReferenceComparer Instance = new UiElementReferenceComparer();

            public bool Equals(UiElementInfo? x, UiElementInfo? y) => ReferenceEquals(x, y);

            public int GetHashCode(UiElementInfo obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
