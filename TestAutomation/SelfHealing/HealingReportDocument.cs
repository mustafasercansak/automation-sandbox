using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using UiModel;

namespace SelfHealing
{
    public sealed class HealingReportDocument
    {
        // v8 (issue #144): batch-resolution entries can carry snapshot-local CandidateIdentity
        // and ReconciliationDisposition ownership telemetry.
        // v7 (issue #82): every resolution attempt carries an explicit Outcome, Platform,
        // ProposedSnapshot and ProviderErrors. Older entries keep these fields null because
        // the build that wrote them did not observe those values.
        // v6 (issue #11): entries carry ProviderAttempts - tracking how many attempts each
        // evaluated provider made (resilience, retry counts, quota audits).
        // v5 (issue #10): entries carry AgreedProviders - which providers reached consensus
        // on an LLM pick, the evidence behind the acceptance decision itself.
        // v4 (issue #6): entries carry DivergedFromHeuristic, HeuristicSnapshot, HeuristicScore
        // for full explainability when an LLM pick diverges from the heuristic winner.
        // v3: entries carry RunnerUpScore (margin gate, issue #4).
        // v2: added EvidenceCoverage and Candidates (#3).
        // Older reports upgrade in place; only newer-than-current schemas are rejected.
        public const int CurrentSchemaVersion = 8;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<HealingReportEntry> Events { get; set; } = new List<HealingReportEntry>();

        /// <summary>
        /// Accepted and accepted-unverified entries, including every legacy entry written
        /// before schema v7 when reports contained accepted heals only.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<HealingReportEntry> AcceptedEvents => Events.Where(e => e.IsAccepted).ToList();
    }

    // Slim per-candidate record for the report: enough to re-run offline threshold sweeps
    // (#15 benchmark) without persisting the whole UiElementInfo tree per candidate. Null
    // signals inside Components are the "no evidence" markers.

    public sealed class HealingReportCandidate
    {
        public string AutomationId { get; set; } = "";
        public string Name { get; set; } = "";
        public string ControlType { get; set; } = "";
        public double TotalScore { get; set; }
        public double EvidenceCoverage { get; set; }
        public ScoreComponents? Components { get; set; }
    }

    public sealed class HealingReportEntry
    {
        // Renamed from HeuristicReviewMargin: that name collided with the runner-up margin
        // gate (issue #4). This one is different - it flags heuristic matches whose score
        // sits barely above the confidence threshold, i.e. proximity to the threshold, not
        // distance from the runner-up.
        private const double ReviewProximityBand = 0.10;

        public const string AcceptedStatus = "accepted";
        public const string AcceptedWithLlmStatus = "accepted-with-llm";
        public const string ManualReviewStatus = "manual-review";

        public const string AcceptedOutcome = "accepted";
        public const string AcceptedUnverifiedOutcome = "accepted-unverified";
        public const string RetryFailedOutcome = "retry-failed";
        public const string AmbiguousOutcome = "ambiguous";
        public const string LowEvidenceOutcome = "low-evidence";
        public const string LowConfidenceOutcome = "low-confidence";
        public const string NoCandidatesOutcome = "no-candidates";
        public const string NoConsensusOutcome = "no-consensus";
        public const string ProviderErrorOutcome = "provider-error";
        public const string OwnershipConflictOutcome = "ownership-conflict";
        public const string ObservedOutcome = "observed";
        public const string ManualReviewOutcome = "manual-review";
        public const string FailClosedOutcome = "fail-closed";
        public const string UnspecifiedOutcome = "unspecified";

        public DateTimeOffset HealedAt { get; set; } = DateTimeOffset.UtcNow;
        public string LocatorKey { get; set; } = "";
        public string Source { get; set; } = "";
        public string ReviewStatus { get; set; } = "";

        // Null on entries upgraded from schema v6 and earlier. Those reports contained
        // accepted heals only, so IsAccepted deliberately treats a null Outcome as accepted.
        public string? Outcome { get; set; }
        public string? Platform { get; set; }

        // Null on single-locator attempts and entries upgraded from schema v7 or earlier.
        // CandidateIdentity is an opaque path within one captured tree, not a reusable locator.
        public string? CandidateIdentity { get; set; }
        public string? ReconciliationDisposition { get; set; }

        [JsonIgnore]
        public bool IsAccepted => Outcome == null ||
            Outcome == AcceptedOutcome ||
            Outcome == AcceptedUnverifiedOutcome;

        public double Score { get; set; }
        public double ConfidenceThreshold { get; set; }
        public int CandidateCount { get; set; }
        public double? LlmConfidence { get; set; }
        public string? LlmProviderName { get; set; }
        public string? LlmReasoning { get; set; }

        // Providers that agreed on this pick (issue #10). Null - not an empty list - on
        // entries upgraded from a v4 report: "this build did not record it", as opposed to
        // "nobody agreed", which an empty list would claim. Same distinction EvidenceCoverage
        // makes for v1 upgrades.

        public List<string>? AgreedProviders { get; set; }

        // Telemetry for resilience/retries (#11): how many HTTP attempts each evaluated provider made.
        // Null on entries upgraded from a v5 report.
        public Dictionary<string, int>? ProviderAttempts { get; set; }

        // Provider name -> failure detail. Null on entries upgraded from schema v6 and earlier.
        public Dictionary<string, string>? ProviderErrors { get; set; }

        public UiElementInfo? PreviousSnapshot { get; set; }
        public UiElementInfo? AcceptedSnapshot { get; set; }

        // Best proposed match when it was not accepted (for example ambiguous or retry-failed).
        // Null on accepted entries and on reports upgraded from schema v6 and earlier.
        public UiElementInfo? ProposedSnapshot { get; set; }
        public ScoreComponents? ScoreBreakdown { get; set; }

        // Null on entries upgraded from a v1 report: "unknown", not "no evidence" - a 0.0
        // would be misread as thin evidence by offline threshold sweeps.

        public double? EvidenceCoverage { get; set; }

        // Second-best candidate score at decision time (null = no runner-up). Persisted so
        // the margin gate's behavior is auditable offline, not just the final verdict.

        public double? RunnerUpScore { get; set; }

        // Heuristic winner baseline and divergence tracking (issue #6)
        public bool DivergedFromHeuristic { get; set; }
        public UiElementInfo? HeuristicSnapshot { get; set; }
        public double? HeuristicScore { get; set; }

        public List<HealingReportCandidate>? Candidates { get; set; }

        public static HealingReportEntry FromHealResult(
            string locatorKey,
            UiElementInfo previousSnapshot,
            UiElementInfo acceptedSnapshot,
            HealResult result)
        {
            if (string.IsNullOrWhiteSpace(locatorKey))
            {
                throw new ArgumentException("locatorKey must not be null or empty.", nameof(locatorKey));
            }

            if (previousSnapshot == null)
            {
                throw new ArgumentNullException(nameof(previousSnapshot));
            }

            if (acceptedSnapshot == null)
            {
                throw new ArgumentNullException(nameof(acceptedSnapshot));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return Create(
                locatorKey,
                previousSnapshot,
                acceptedSnapshot,
                proposedSnapshot: null,
                result,
                AcceptedUnverifiedOutcome,
                platform: null);
        }

        public static HealingReportEntry FromResolutionAttempt(
            string locatorKey,
            UiElementInfo previousSnapshot,
            HealResult result,
            string outcome,
            string? platform = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var proposedSnapshot = result.Matched == null ? null : UiElementSnapshot.Capture(result.Matched);
            if (proposedSnapshot != null &&
                string.IsNullOrWhiteSpace(proposedSnapshot.TestIntent) &&
                !string.IsNullOrWhiteSpace(previousSnapshot.TestIntent))
            {
                proposedSnapshot.TestIntent = previousSnapshot.TestIntent;
            }
            var acceptedSnapshot = IsAcceptedOutcome(outcome) ? proposedSnapshot : null;
            return Create(locatorKey, previousSnapshot, acceptedSnapshot, IsAcceptedOutcome(outcome) ? null : proposedSnapshot, result, outcome, platform);
        }

        public static string OutcomeFromResolutionStatus(HealResolutionStatus status)
        {
            switch (status)
            {
                case HealResolutionStatus.Ambiguous:
                    return AmbiguousOutcome;
                case HealResolutionStatus.LowEvidence:
                    return LowEvidenceOutcome;
                case HealResolutionStatus.NoConsensus:
                    return NoConsensusOutcome;
                case HealResolutionStatus.ProviderError:
                    return ProviderErrorOutcome;
                case HealResolutionStatus.OwnershipConflict:
                    return OwnershipConflictOutcome;
                case HealResolutionStatus.NoCandidates:
                    return NoCandidatesOutcome;
                case HealResolutionStatus.LowConfidence:
                    return LowConfidenceOutcome;
                case HealResolutionStatus.Unspecified:
                    return UnspecifiedOutcome;
                case HealResolutionStatus.Confident:
                    return AcceptedUnverifiedOutcome;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status));
            }
        }

        private static HealingReportEntry Create(
            string locatorKey,
            UiElementInfo previousSnapshot,
            UiElementInfo? acceptedSnapshot,
            UiElementInfo? proposedSnapshot,
            HealResult result,
            string outcome,
            string? platform)
        {
            if (string.IsNullOrWhiteSpace(locatorKey))
            {
                throw new ArgumentException("locatorKey must not be null or empty.", nameof(locatorKey));
            }

            if (previousSnapshot == null)
            {
                throw new ArgumentNullException(nameof(previousSnapshot));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (string.IsNullOrWhiteSpace(outcome))
            {
                throw new ArgumentException("outcome must not be null or empty.", nameof(outcome));
            }

            Dictionary<string, int>? providerAttempts = null;
            if (result.ProviderAttempts != null && result.ProviderAttempts.Count > 0)
            {
                var sorted = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var kvp in result.ProviderAttempts)
                {
                    sorted[kvp.Key] = kvp.Value;
                }
                providerAttempts = new Dictionary<string, int>(sorted);
            }

            Dictionary<string, string>? providerErrors = null;
            if (result.ProviderErrors != null)
            {
                var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var kvp in result.ProviderErrors)
                {
                    sorted[kvp.Key] = kvp.Value;
                }
                providerErrors = new Dictionary<string, string>(sorted);
            }

            return new HealingReportEntry
            {
                LocatorKey = locatorKey,
                Source = result.Source == HealSource.Llm ? result.LlmProviderName ?? "llm" : "heuristic",
                ReviewStatus = IsAcceptedOutcome(outcome) ? ClassifyReviewStatus(result) : ManualReviewStatus,
                Outcome = outcome,
                Platform = platform,
                CandidateIdentity = result.CandidateIdentity,
                ReconciliationDisposition = result.ReconciliationDisposition?.ToString(),
                Score = result.Score,
                ConfidenceThreshold = result.ConfidenceThreshold,
                CandidateCount = result.CandidateCount,
                LlmConfidence = result.LlmConfidence,
                LlmProviderName = result.LlmProviderName,
                AgreedProviders = result.AgreedProviders.Count == 0 ? null : new List<string>(result.AgreedProviders),
                ProviderAttempts = providerAttempts,
                ProviderErrors = providerErrors,
                LlmReasoning = result.LlmReasoning,
                PreviousSnapshot = UiElementSnapshot.Capture(previousSnapshot),
                AcceptedSnapshot = acceptedSnapshot == null ? null : UiElementSnapshot.Capture(acceptedSnapshot),
                ProposedSnapshot = proposedSnapshot == null ? null : UiElementSnapshot.Capture(proposedSnapshot),
                ScoreBreakdown = result.ScoreBreakdown,
                EvidenceCoverage = result.EvidenceCoverage,
                RunnerUpScore = result.RunnerUpScore,
                DivergedFromHeuristic = result.DivergedFromHeuristic,
                HeuristicSnapshot = result.HeuristicMatched is null ? null : UiElementSnapshot.Capture(result.HeuristicMatched),
                HeuristicScore = result.HeuristicScore,
                Candidates = result.Candidates?
                    .Select(c => new HealingReportCandidate
                    {
                        AutomationId = c.Candidate.AutomationId,
                        Name = c.Candidate.Name,
                        ControlType = c.Candidate.ControlType,
                        TotalScore = c.TotalScore,
                        EvidenceCoverage = c.EvidenceCoverage,
                        Components = c.Components,
                    })
                    .ToList(),
            };
        }

        private static bool IsAcceptedOutcome(string outcome)
        {
            return outcome == AcceptedOutcome || outcome == AcceptedUnverifiedOutcome;
        }

        private static string ClassifyReviewStatus(HealResult result)
        {
            if (!result.IsConfident)
            {
                return ManualReviewStatus;
            }

            if (result.Source == HealSource.Llm)
            {
                return AcceptedWithLlmStatus;
            }

            return result.Score - result.ConfidenceThreshold <= ReviewProximityBand
                ? ManualReviewStatus
                : AcceptedStatus;
        }
    }
}
