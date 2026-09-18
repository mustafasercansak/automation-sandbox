using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using AutomationSandbox.UiModel;

namespace AutomationSandbox.SelfHealing
{
    /// <summary>Editable versioned collection of healing attempts for persistence and offline analysis.</summary>
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
        /// <summary>Newest document schema version this build can read and write.</summary>
        public const int CurrentSchemaVersion = 8;
        /// <summary>Version of the persisted document shape; newer unsupported versions are rejected by the reader.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        /// <summary>Timestamp when this report document was generated.</summary>
        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Recorded resolution attempts, including accepted and declined proposals.</summary>
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

    /// <summary>Persisted candidate score and available evidence, including candidates below the shortlist threshold.</summary>
    public sealed class HealingReportCandidate
    {
        /// <summary>Captured automation identifier. It may be empty or duplicated and must not be treated as a unique node identity.</summary>
        public string AutomationId { get; set; } = "";
        /// <summary>Human-readable accessible name of the element; empty when unavailable.</summary>
        public string Name { get; set; } = "";
        /// <summary>UI Automation control type used for structural matching, such as Button or Edit; empty when unavailable.</summary>
        public string ControlType { get; set; } = "";
        /// <summary>Weighted structural similarity after excluding signals with no evidence; a score is not a probability of correctness.</summary>
        public double TotalScore { get; set; }
        /// <summary>Fraction of configured signal weight backed by non-null evidence, from zero to one.</summary>
        public double EvidenceCoverage { get; set; }
        /// <summary>Per-signal structural similarities used to explain the weighted score.</summary>
        public ScoreComponents? Components { get; set; }
    }

    /// <summary>Editable persisted resolution attempt, proposal, execution outcome, and optional provider telemetry.</summary>
    public sealed class HealingReportEntry
    {
        // Renamed from HeuristicReviewMargin: that name collided with the runner-up margin
        // gate (issue #4). This one is different - it flags heuristic matches whose score
        // sits barely above the confidence threshold, i.e. proximity to the threshold, not
        // distance from the runner-up.
        private const double ReviewProximityBand = 0.10;

        /// <summary>Compatibility review label for a heuristic acceptance.</summary>
        public const string AcceptedStatus = "accepted";
        /// <summary>Compatibility review label for an LLM-assisted acceptance.</summary>
        public const string AcceptedWithLlmStatus = "accepted-with-llm";
        /// <summary>Compatibility review label for a proposal requiring manual review.</summary>
        public const string ManualReviewStatus = "manual-review";

        /// <summary>Outcome label for a heal accepted and verified by successful action execution.</summary>
        public const string AcceptedOutcome = "accepted";
        /// <summary>Outcome label for an accepted proposal without action execution verification.</summary>
        public const string AcceptedUnverifiedOutcome = "accepted-unverified";
        /// <summary>Outcome label for an accepted proposal whose retried action failed.</summary>
        public const string RetryFailedOutcome = "retry-failed";
        /// <summary>Outcome label for insufficient separation between competing candidates.</summary>
        public const string AmbiguousOutcome = "ambiguous";
        /// <summary>Outcome label for insufficient observed signal weight.</summary>
        public const string LowEvidenceOutcome = "low-evidence";
        /// <summary>Outcome label for a proposal below the structural score threshold.</summary>
        public const string LowConfidenceOutcome = "low-confidence";
        /// <summary>Outcome label for an empty resolution shortlist.</summary>
        public const string NoCandidatesOutcome = "no-candidates";
        /// <summary>Outcome label for a provider fallback without an unambiguous independent-agreement quorum.</summary>
        public const string NoConsensusOutcome = "no-consensus";
        /// <summary>Outcome label for a failed provider fallback.</summary>
        public const string ProviderErrorOutcome = "provider-error";
        /// <summary>Outcome label for a claim rejected by candidate-ownership reconciliation.</summary>
        public const string OwnershipConflictOutcome = "ownership-conflict";
        /// <summary>Outcome label for a proposal recorded without applying it.</summary>
        public const string ObservedOutcome = "observed";
        /// <summary>Outcome label for a proposal routed to human review.</summary>
        public const string ManualReviewOutcome = "manual-review";
        /// <summary>Outcome label for an engine policy that refuses to apply a proposed heal.</summary>
        public const string FailClosedOutcome = "fail-closed";
        /// <summary>Outcome label for an unclassified decision path; keep distinct from measured rejection reasons.</summary>
        public const string UnspecifiedOutcome = "unspecified";

        /// <summary>Timestamp at which the resolution event was recorded.</summary>
        public DateTimeOffset HealedAt { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Logical repository key identifying a locator independently of its current automation identifier.</summary>
        public string LocatorKey { get; set; } = "";
        /// <summary>Resolution mechanism that proposed the candidate.</summary>
        public string Source { get; set; } = "";
        /// <summary>Compatibility review label retained alongside the more precise outcome.</summary>
        public string ReviewStatus { get; set; } = "";

        // Null on entries upgraded from schema v6 and earlier. Those reports contained
        // accepted heals only, so IsAccepted deliberately treats a null Outcome as accepted.
        /// <summary>Execution or resolution outcome; null on legacy entries that recorded accepted heals only.</summary>
        public string? Outcome { get; set; }
        /// <summary>Platform label recorded with the document, normally web or desktop.</summary>
        public string? Platform { get; set; }

        // Null on single-locator attempts and entries upgraded from schema v7 or earlier.
        // CandidateIdentity is an opaque path within one captured tree, not a reusable locator.
        /// <summary>Opaque pre-order tree path identifying the candidate within this captured tree only.</summary>
        public string? CandidateIdentity { get; set; }
        /// <summary>Ownership decision applied to the independently proposed candidate.</summary>
        public string? ReconciliationDisposition { get; set; }

        /// <summary>Whether this entry represents an accepted outcome, including legacy entries with no outcome field.</summary>
        [JsonIgnore]
        public bool IsAccepted => Outcome == null ||
            Outcome == AcceptedOutcome ||
            Outcome == AcceptedUnverifiedOutcome;

        /// <summary>Structural similarity score; it is not a calibrated correctness probability.</summary>
        public double Score { get; set; }
        /// <summary>Minimum structural score required for heuristic acceptance.</summary>
        public double ConfidenceThreshold { get; set; }
        /// <summary>Number of candidates considered by this result&apos;s resolution or matching stage.</summary>
        public int CandidateCount { get; set; }
        /// <summary>Provider-reported confidence retained for diagnostics only; it never controls acceptance.</summary>
        public double? LlmConfidence { get; set; }
        /// <summary>Provider name or names recorded for an LLM proposal; absent for heuristic-only resolution.</summary>
        public string? LlmProviderName { get; set; }
        /// <summary>Provider explanation retained for diagnostics; not independently verified evidence.</summary>
        public string? LlmReasoning { get; set; }

        // Providers that agreed on this pick (issue #10). Null - not an empty list - on
        // entries upgraded from a v4 report: "this build did not record it", as opposed to
        // "nobody agreed", which an empty list would claim. Same distinction EvidenceCoverage
        // makes for v1 upgrades.

        /// <summary>Providers that agreed on this pick (issue #10). Null - not an empty list - on entries upgraded from a v4 report: &quot;this build did not record it&quot;, as opposed to &quot;nobody agreed&quot;, which an empty list would claim. Same distinction EvidenceCoverage makes for v1 upgrades.</summary>
        public List<string>? AgreedProviders { get; set; }

        // Telemetry for resilience/retries (#11): how many HTTP attempts each evaluated provider made.
        // Null on entries upgraded from a v5 report.
        /// <summary>Telemetry for resilience/retries (#11): how many HTTP attempts each evaluated provider made. Null on entries upgraded from a v5 report.</summary>
        public Dictionary<string, int>? ProviderAttempts { get; set; }

        // Provider name -> failure detail. Null on entries upgraded from schema v6 and earlier.
        /// <summary>Provider name -&gt; failure detail. Null on entries upgraded from schema v6 and earlier.</summary>
        public Dictionary<string, string>? ProviderErrors { get; set; }

        /// <summary>Locator evidence before this resolution attempt.</summary>
        public UiElementInfo? PreviousSnapshot { get; set; }
        /// <summary>Snapshot retained for an accepted heal; may be absent when acceptance was not recorded.</summary>
        public UiElementInfo? AcceptedSnapshot { get; set; }

        // Best proposed match when it was not accepted (for example ambiguous or retry-failed).
        // Null on accepted entries and on reports upgraded from schema v6 and earlier.
        /// <summary>Snapshot of the proposed node, including proposals that were subsequently declined.</summary>
        public UiElementInfo? ProposedSnapshot { get; set; }
        /// <summary>Per-signal explanation of the proposed candidate&apos;s structural score.</summary>
        public ScoreComponents? ScoreBreakdown { get; set; }

        // Null on entries upgraded from a v1 report: "unknown", not "no evidence" - a 0.0
        // would be misread as thin evidence by offline threshold sweeps.

        /// <summary>Fraction of configured signal weight backed by non-null evidence, from zero to one.</summary>
        public double? EvidenceCoverage { get; set; }

        // Second-best candidate score at decision time (null = no runner-up). Persisted so
        // the margin gate's behavior is auditable offline, not just the final verdict.

        /// <summary>Second-best candidate score at decision time (null = no runner-up). Persisted so the margin gate&apos;s behavior is auditable offline, not just the final verdict.</summary>
        public double? RunnerUpScore { get; set; }

        // Heuristic winner baseline and divergence tracking (issue #6)
        /// <summary>Whether the LLM proposed a different candidate from the heuristic winner.</summary>
        public bool DivergedFromHeuristic { get; set; }
        /// <summary>Snapshot of the heuristic winner before provider fallback.</summary>
        public UiElementInfo? HeuristicSnapshot { get; set; }
        /// <summary>Structural score of the heuristic winner before fallback, if retained.</summary>
        public double? HeuristicScore { get; set; }

        /// <summary>Ranked candidate evidence retained by this result or report.</summary>
        public List<HealingReportCandidate>? Candidates { get; set; }

        /// <summary>Creates a compatibility accepted-heal report entry from the previous and accepted snapshots and resolver evidence.</summary>
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

        /// <summary>Creates a report entry for an explicitly classified attempt, preserving the proposal and available diagnostics.</summary>
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

        /// <summary>Maps the resolver classification to the report outcome vocabulary without treating unspecified as low confidence.</summary>
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
