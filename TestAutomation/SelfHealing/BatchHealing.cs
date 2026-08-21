using System.Runtime.CompilerServices;
using LlmHealing;
using UiModel;

namespace SelfHealing
{
    public enum BatchReconciliationDisposition
    {
        BaselineDecline,
        PreservedUncontested,
        WonContention,
        DeclinedByStrongerClaim,
        DeclinedAmbiguousContention,
    }

    public sealed class BatchHealingRequest
    {
        public BatchHealingRequest(string locatorKey, UiElementInfo expected)
        {
            if (string.IsNullOrWhiteSpace(locatorKey))
            {
                throw new ArgumentException("locatorKey must not be null or empty.", nameof(locatorKey));
            }

            LocatorKey = locatorKey;
            Expected = expected ?? throw new ArgumentNullException(nameof(expected));
        }

        public string LocatorKey { get; }
        public UiElementInfo Expected { get; }
    }

    public sealed class BatchHealingItemResult
    {
        internal BatchHealingItemResult(BatchHealingRequest request, HealResult result)
        {
            Request = request;
            Result = result;
            WasIndependentlyConfident = result.IsConfident;
        }

        public BatchHealingRequest Request { get; }
        public HealResult Result { get; }
        public bool WasIndependentlyConfident { get; }
        public string? CandidateIdentity => Result.CandidateIdentity;
        public BatchReconciliationDisposition ReconciliationDisposition =>
            Result.ReconciliationDisposition ?? BatchReconciliationDisposition.BaselineDecline;
    }

    public sealed class BatchHealingResult
    {
        internal BatchHealingResult(IReadOnlyList<BatchHealingItemResult> items)
        {
            Items = items;
        }

        public IReadOnlyList<BatchHealingItemResult> Items { get; }
        public int ContestedCandidateCount => Items
            .Where(i => i.WasIndependentlyConfident && i.CandidateIdentity != null)
            .GroupBy(i => i.CandidateIdentity!, StringComparer.Ordinal)
            .Count(g => g.Count() > 1);
        public int ReconciliationDeclineCount => Items.Count(i => i.Result.RejectedByReconciliation);
    }

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
                var ranked = contention
                    .OrderByDescending(i => i.Result.Score)
                    .ThenBy(i => i.Request.LocatorKey, StringComparer.Ordinal)
                    .ToList();
                var ownershipMargin = ranked[0].Result.Score - ranked[1].Result.Score;

                if (CandidateMargin.HasSufficientMargin(
                    ranked[0].Result.Score,
                    ranked[1].Result.Score,
                    weights.MinimumCandidateMargin))
                {
                    ranked[0].Result.ReconciliationDisposition = BatchReconciliationDisposition.WonContention;
                    foreach (var loser in ranked.Skip(1))
                    {
                        RejectClaim(loser, BatchReconciliationDisposition.DeclinedByStrongerClaim);
                    }

                    log?.Invoke(
                        $"[SelfHealing] Batch candidate '{contention.Key}' assigned to '{ranked[0].Request.LocatorKey}' " +
                        $"with ownership margin {ownershipMargin:F3}; {ranked.Count - 1} weaker claim(s) declined.");
                }
                else
                {
                    foreach (var claimant in ranked)
                    {
                        RejectClaim(claimant, BatchReconciliationDisposition.DeclinedAmbiguousContention);
                    }

                    log?.Invoke(
                        $"[SelfHealing] Batch candidate '{contention.Key}' has ambiguous ownership margin " +
                        $"{ownershipMargin:F3} below {weights.MinimumCandidateMargin:F3}; all {ranked.Count} claims declined.");
                }
            }

            return new BatchHealingResult(items);
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
