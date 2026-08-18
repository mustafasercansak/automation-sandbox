using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LlmHealing;
using SelfHealing;
using UiModel;

namespace ScenarioRunner
{
    // What the engine did with a scenario whose correct answer is known.
    public enum AblationOutcome
    {
        // Rename scenarios: the engine accepted a match and it is the right element.
        CorrectHeal,

        // Rename scenarios: the engine accepted a match and it is the wrong element. The dangerous one —
        // the test goes green against something nobody intended.
        FalseHeal,

        // Rename scenarios: the engine declined, so a human has to look. Safe but not free.
        MissedHeal,

        // Removal scenarios: the engine declined, which is correct because there is no successor.
        CorrectDecline,

        // Removal scenarios: the engine accepted a neighbour for an element that no longer exists.
        FalseHealOnRemoved,
    }

    // How the independent providers behaved on one scenario. This — not the heal outcome — is what
    // #97 asks about: when the element is gone there is no correct answer, so the hypothesis is that
    // providers scatter. Agreement on a decoy would be the opposite, and worse than a heuristic miss,
    // because consensus is what the engine trusts most.
    public enum ConsensusVotePattern
    {
        // The LLM path never ran; the heuristic answered on its own.
        NotEvaluated,

        // Every provider that answered named the same candidate.
        Unanimous,

        // Providers answered but named different candidates.
        Scattered,

        // Every provider declined to name a candidate.
        AllDeclined,

        // Some answered, some declined. Not consensus, but not scatter either.
        Partial,

        // Fewer than MinimumProvidersForConsensus providers produced a usable answer, so there were
        // not enough independent opinions to call anything consensus. Measurement noise, not a finding.
        //
        // #109: this used to fire when *any* provider failed, which discarded scenarios that two
        // other providers had answered completely. With one provider out of quota that rule threw
        // away every scenario in a run and reported the absence of data as unusable data.
        ProviderFailure,
    }

    public sealed class AblationScenarioResult
    {
        public string ScenarioId { get; set; } = "";
        public LocatorMutationKind MutationKind { get; set; }
        public AblationOutcome Outcome { get; set; }
        public string OriginalAutomationId { get; set; } = "";

        public bool EngineAccepted { get; set; }
        public double Score { get; set; }
        public double EvidenceCoverage { get; set; }
        public int CandidateCount { get; set; }
        public HealResolutionStatus ResolutionStatus { get; set; }
        public HealSource Source { get; set; } = HealSource.Heuristic;
        public IReadOnlyList<string> AgreedProviders { get; set; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, int> ProviderAttempts { get; set; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, string> ProviderErrors { get; set; } = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string?> ProviderVotes { get; set; } = new Dictionary<string, string?>();
        public IReadOnlyDictionary<string, LlmHealingResult> ProviderResults { get; set; } = new Dictionary<string, LlmHealingResult>();

        // How many providers returned a usable answer for this scenario, counting declines as
        // answers. Without it a two-provider observation is indistinguishable from a three-provider
        // one in the report, and "unanimous" means something different in each case.
        public int RespondingProviders { get; set; }
        public double? LlmConfidence { get; set; }
        public string? LlmReasoning { get; set; }

        // The raw score vector, kept so thresholds can be swept offline without re-running the harness
        // (#15 asks for this directly).
        public IReadOnlyList<CandidateScore> Candidates { get; set; } = new List<CandidateScore>();

        public ConsensusVotePattern VotePattern { get; set; } = ConsensusVotePattern.NotEvaluated;

        public string? ExpectedElement { get; set; }
        public string? MatchedElement { get; set; }
    }

    public sealed class ProviderParticipation
    {
        public int Answered { get; set; }
        public int Failed { get; set; }

        // One representative message rather than all of them: a provider that is out of quota
        // returns the same 429 for every scenario, and printing it 42 times hides the other provider.
        public string? SampleError { get; set; }
    }

    public sealed class AblationMetrics
    {
        public int SuccessorScenarios { get; set; }
        public int RenameScenarios { get; set; }
        public int NameDriftScenarios { get; set; }
        public int PositionShiftScenarios { get; set; }
        public int CompoundDriftScenarios { get; set; }

        public int CorrectHeals { get; set; }
        public int FalseHeals { get; set; }
        public int MissedHeals { get; set; }

        public int RemovalScenarios { get; set; }
        public int CorrectDeclines { get; set; }
        public int FalseHealsOnRemoved { get; set; }

        public int LlmHeals { get; set; }
        public int ConsensusDeclines { get; set; }
        // Resolutions that ended with no usable provider answer at all. This is a property of the
        // resolution, not of the providers: if one provider answers everything, this stays 0 even
        // when every other provider failed every request. Name it for what it counts - #112 was
        // filed because "Provider errors | 0" was printed for a run in which 65 provider calls
        // failed and two of three providers never answered once.
        public int ResolutionsWithoutAnyProviderAnswer { get; set; }

        // Per-provider participation, which is what a reader needs in order to know how much of the
        // provider set the result actually rests on.
        public IReadOnlyDictionary<string, ProviderParticipation> ProviderParticipation { get; set; } =
            new Dictionary<string, ProviderParticipation>();

        // Of the locators that survived (with rename, drift, or shift), how many were found.
        public double AutoHealRecall => SuccessorScenarios == 0 ? 0.0 : (double)CorrectHeals / SuccessorScenarios;

        // Of everything the engine accepted, how much was the true element.
        public double Precision
        {
            get
            {
                var accepted = CorrectHeals + FalseHeals + FalseHealsOnRemoved;
                return accepted == 0 ? 1.0 : (double)CorrectHeals / accepted;
            }
        }

        // Of everything the engine accepted, how much was wrong (misdirected or accepted on deleted element).
        public double FalseHealRate
        {
            get
            {
                var accepted = CorrectHeals + FalseHeals + FalseHealsOnRemoved;
                return accepted == 0 ? 0.0 : (double)(FalseHeals + FalseHealsOnRemoved) / accepted;
            }
        }

        // How often a human is asked to decide (declined because of low confidence, low evidence, or ambiguity).
        public double ManualReviewRate
        {
            get
            {
                var total = SuccessorScenarios + RemovalScenarios;
                return total == 0 ? 0.0 : (double)(MissedHeals + CorrectDeclines) / total;
            }
        }
    }

    public sealed class AblationRunReport
    {
        public List<AblationScenarioResult> Results { get; set; } = new();
        public AblationMetrics Metrics { get; set; } = new();
    }

    public static class LocatorAblationHarness
    {
        // Matches the engine's own acceptance rule (#10, #19): consensus needs two independent
        // providers naming the same candidate. Below this there is nothing to agree or disagree
        // about, so the scenario yields no measurement either way.
        public const int MinimumProvidersForConsensus = 2;

        public static AblationRunReport Run(
            LocatorAblationDataset dataset,
            UiElementInfo sourceRoot,
            SimilarityWeights? weights = null)
        {
            if (dataset == null)
            {
                throw new ArgumentNullException(nameof(dataset));
            }

            var report = new AblationRunReport();

            foreach (var scenario in dataset.Scenarios)
            {
                var expected = LocatorAblationGenerator.FindExpectedElement(sourceRoot, scenario.OriginalAutomationId);
                if (expected == null)
                {
                    throw new InvalidOperationException(
                        $"Scenario '{scenario.ScenarioId}' references AutomationId '{scenario.OriginalAutomationId}', absent from the source tree.");
                }

                var mutatedRoot = LocatorAblationGenerator.ApplyMutation(sourceRoot, scenario);
                var heal = SelfHealingResolver.Resolve(expected, mutatedRoot, weights, log: _ => { });

                var accepted = heal.IsConfident && heal.Matched != null;
                var matchedPath = heal.Matched == null
                    ? null
                    : LocatorAblationGenerator.AncestorPathOf(mutatedRoot, heal.Matched);

                var result = new AblationScenarioResult
                {
                    ScenarioId = scenario.ScenarioId,
                    MutationKind = scenario.MutationKind,
                    OriginalAutomationId = scenario.OriginalAutomationId,
                    EngineAccepted = accepted,
                    Score = heal.Score,
                    EvidenceCoverage = heal.EvidenceCoverage,
                    CandidateCount = heal.CandidateCount,
                    ResolutionStatus = heal.ResolutionStatus,
                    Source = heal.Source,
                    AgreedProviders = heal.AgreedProviders ?? Array.Empty<string>(),
                    ProviderAttempts = heal.ProviderAttempts ?? new Dictionary<string, int>(),
                    ProviderErrors = heal.ProviderErrors ?? new Dictionary<string, string>(),
                    LlmConfidence = heal.LlmConfidence,
                    LlmReasoning = heal.LlmReasoning,
                    Candidates = heal.Candidates ?? new List<CandidateScore>(),
                    ExpectedElement = scenario.GroundTruth?.ToString(),
                    MatchedElement = heal.Matched == null
                        ? null
                        : LocatorAblationGenerator.Fingerprint(heal.Matched, matchedPath ?? "").ToString(),
                    Outcome = Classify(scenario, heal, accepted, matchedPath),
                };

                report.Results.Add(result);
            }

            report.Metrics = Summarize(report.Results);
            return report;
        }

        public static async Task<AblationRunReport> RunAsync(
            LocatorAblationDataset dataset,
            UiElementInfo sourceRoot,
            IEnumerable<ILlmHealingProvider>? llmProviders = null,
            SimilarityWeights? weights = null,
            string? platform = null,
            Func<LocatorAblationScenario, bool>? scenarioFilter = null,
            CancellationToken cancellationToken = default,
            TimeSpan? scenarioPacing = null,
            TimeSpan? maxRetryAfter = null,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
        {
            if (dataset == null)
            {
                throw new ArgumentNullException(nameof(dataset));
            }

            var report = new AblationRunReport();
            var providersList = llmProviders?.ToList();

            // #110: Groq answers with Retry-After values of 11-13s under load, above the transport's
            // 10s interactive ceiling, so every rate-limited request failed fast instead of waiting.
            //
            // #127: raising that ceiling alone did nothing, because the total timeout that wraps
            // every attempt was still the interactive 35s default. A wait this override now tells
            // the transport to honour could itself exceed the total timeout, cancelling the call
            // before the wait completed - indistinguishable in the report from a genuinely dead
            // endpoint. TotalTimeoutOverride must widen in step, sized from each provider's own
            // per-attempt timeout and retry count so a fully rate-limited run of retries still fits:
            // (attempts * per-attempt timeout) + (retries * honoured wait) + margin.
            if (maxRetryAfter.HasValue && providersList != null)
            {
                foreach (var http in providersList.OfType<HttpLlmHealingProvider>())
                {
                    http.MaxRetryAfterOverride = maxRetryAfter;

                    var attempts = http.MaxRetries + 1;
                    var worstCase = TimeSpan.FromTicks(http.Timeout.Ticks * attempts)
                        + TimeSpan.FromTicks(maxRetryAfter.Value.Ticks * http.MaxRetries)
                        + TimeSpan.FromSeconds(10);
                    http.TotalTimeoutOverride = worstCase;
                }
            }

            // Waiting after being told to is strictly worse than not being told to. Defaults to zero
            // so the offline suite, which runs mock providers over no network, pays nothing for it.
            var pacing = scenarioPacing ?? TimeSpan.Zero;
            var delay = delayAsync ?? Task.Delay;
            var pacedScenarios = 0;

            foreach (var scenario in dataset.Scenarios)
            {
                if (scenarioFilter != null && !scenarioFilter(scenario))
                {
                    continue;
                }

                // After the first evaluated scenario, not before it - a leading delay buys nothing
                // because no request has been made yet.
                if (pacing > TimeSpan.Zero && providersList != null && pacedScenarios++ > 0)
                {
                    await delay(pacing, cancellationToken).ConfigureAwait(false);
                }

                var expected = LocatorAblationGenerator.FindExpectedElement(sourceRoot, scenario.OriginalAutomationId);
                if (expected == null)
                {
                    throw new InvalidOperationException(
                        $"Scenario '{scenario.ScenarioId}' references AutomationId '{scenario.OriginalAutomationId}', absent from the source tree.");
                }

                var mutatedRoot = LocatorAblationGenerator.ApplyMutation(sourceRoot, scenario);
                var recorders = providersList?.Select(p => new RecordingProvider(p)).ToList();
                var heal = await SelfHealingResolver.ResolveAsync(
                    expected,
                    mutatedRoot,
                    recorders,
                    weights,
                    log: _ => { },
                    platform: platform ?? "windows-desktop",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var accepted = heal.IsConfident && heal.Matched != null;
                var matchedPath = heal.Matched == null
                    ? null
                    : LocatorAblationGenerator.AncestorPathOf(mutatedRoot, heal.Matched);

                var providerVotes = new SortedDictionary<string, string?>(StringComparer.Ordinal);
                var providerResults = new SortedDictionary<string, LlmHealingResult>(StringComparer.Ordinal);
                if (recorders != null)
                {
                    foreach (var rec in recorders)
                    {
                        if (rec.LastResult != null)
                        {
                            providerVotes[rec.Name] = rec.LastResult.Success ? rec.LastResult.MatchedCandidateId : null;
                            providerResults[rec.Name] = rec.LastResult;
                        }
                    }
                }

                var result = new AblationScenarioResult
                {
                    ScenarioId = scenario.ScenarioId,
                    MutationKind = scenario.MutationKind,
                    OriginalAutomationId = scenario.OriginalAutomationId,
                    EngineAccepted = accepted,
                    Score = heal.Score,
                    EvidenceCoverage = heal.EvidenceCoverage,
                    CandidateCount = heal.CandidateCount,
                    ResolutionStatus = heal.ResolutionStatus,
                    Source = heal.Source,
                    AgreedProviders = heal.AgreedProviders ?? Array.Empty<string>(),
                    ProviderAttempts = heal.ProviderAttempts ?? new Dictionary<string, int>(),
                    ProviderErrors = heal.ProviderErrors ?? new Dictionary<string, string>(),
                    ProviderVotes = providerVotes,
                    ProviderResults = providerResults,
                    RespondingProviders = CountRespondingProviders(providerResults),
                    VotePattern = ClassifyVotePattern(providerVotes, providerResults),
                    LlmConfidence = heal.LlmConfidence,
                    LlmReasoning = heal.LlmReasoning,
                    Candidates = heal.Candidates ?? new List<CandidateScore>(),
                    ExpectedElement = scenario.GroundTruth?.ToString(),
                    MatchedElement = heal.Matched == null
                        ? null
                        : LocatorAblationGenerator.Fingerprint(heal.Matched, matchedPath ?? "").ToString(),
                    Outcome = Classify(scenario, heal, accepted, matchedPath),
                };

                report.Results.Add(result);
            }

            report.Metrics = Summarize(report.Results);
            return report;
        }

        private sealed class RecordingProvider : ILlmHealingProvider
        {
            private readonly ILlmHealingProvider _inner;

            public RecordingProvider(ILlmHealingProvider inner)
            {
                _inner = inner;
            }

            public string Name => _inner.Name;
            public bool IsAvailable => _inner.IsAvailable;
            public LlmHealingResult? LastResult { get; private set; }

            public async Task<LlmHealingResult> ResolveAsync(
                UiElementInfo expected,
                IReadOnlyList<CandidateScore> candidates,
                string? platform = null,
                CancellationToken cancellationToken = default)
            {
                LastResult = await _inner.ResolveAsync(expected, candidates, platform, cancellationToken).ConfigureAwait(false);
                return LastResult;
            }
        }

        private static AblationOutcome Classify(
            LocatorAblationScenario scenario,
            HealResult heal,
            bool accepted,
            string? matchedPath)
        {
            if (scenario.ExpectedOutcome == LocatorExpectedOutcome.NoSuccessor)
            {
                return accepted ? AblationOutcome.FalseHealOnRemoved : AblationOutcome.CorrectDecline;
            }

            if (!accepted)
            {
                return AblationOutcome.MissedHeal;
            }

            var isGroundTruth = heal.Matched != null &&
                (string.Equals(heal.Matched.AutomationId, scenario.MutatedAutomationId, StringComparison.Ordinal) ||
                 (scenario.GroundTruth != null && scenario.GroundTruth.Matches(heal.Matched, matchedPath ?? "")));

            return isGroundTruth ? AblationOutcome.CorrectHeal : AblationOutcome.FalseHeal;
        }


        // Reads the raw votes rather than the consensus verdict. HealResult only reports whether two
        // providers agreed; it cannot say whether the others scattered, declined, or failed - and that
        // distinction is the entire question in #97.
        private static ConsensusVotePattern ClassifyVotePattern(
            IReadOnlyDictionary<string, string?> votes,
            IReadOnlyDictionary<string, LlmHealingResult> results)
        {
            if (votes.Count == 0)
            {
                return ConsensusVotePattern.NotEvaluated;
            }

            // Classify from the results rather than the vote map. A failed provider is written into
            // the vote map as null, which is the same value a decline produces, so the vote map alone
            // cannot tell "the request errored" from "the model said none of these". That distinction
            // is the entire subject of #97: a decline on a removal scenario is the predicted signal,
            // and folding failures into it would manufacture the result the run exists to test.
            var answered = new List<string>();
            var declined = 0;
            var responding = CountRespondingProviders(results);

            foreach (var entry in results)
            {
                if (entry.Value == null || !entry.Value.Success)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(entry.Value.MatchedCandidateId))
                {
                    declined++;
                }
                else
                {
                    answered.Add(entry.Value.MatchedCandidateId!);
                }
            }

            // Consensus is defined as two or more independent providers naming the same candidate
            // (#10, #19). Two responses are therefore a complete observation, and a third provider's
            // HTTP failure says something about that provider's quota, not about whether the two
            // that answered agreed.
            if (responding < MinimumProvidersForConsensus)
            {
                return ConsensusVotePattern.ProviderFailure;
            }

            if (answered.Count == 0)
            {
                return ConsensusVotePattern.AllDeclined;
            }

            if (declined > 0)
            {
                return ConsensusVotePattern.Partial;
            }

            var distinct = new HashSet<string>(answered, StringComparer.Ordinal).Count;
            return distinct == 1 ? ConsensusVotePattern.Unanimous : ConsensusVotePattern.Scattered;
        }

        // Providers that returned a usable answer. A decline counts: "none of these candidates is
        // the element" is a considered response, not a missing one.
        internal static int CountRespondingProviders(IReadOnlyDictionary<string, LlmHealingResult> results)
        {
            var responding = 0;
            foreach (var entry in results)
            {
                if (entry.Value != null && entry.Value.Success)
                {
                    responding++;
                }
            }

            return responding;
        }

        // Scenarios where the LLM actually answered. A run where every provider failed produces results
        // but no measurement, and the difference has to be visible to the caller, not just in prose.
        public static int UsableConsensusScenarios(IEnumerable<AblationScenarioResult> results) =>
            results.Count(r => r.VotePattern != ConsensusVotePattern.NotEvaluated &&
                               r.VotePattern != ConsensusVotePattern.ProviderFailure);

        public static AblationMetrics Summarize(IEnumerable<AblationScenarioResult> results)
        {
            var all = results.ToList();
            var successors = all.Where(r => r.MutationKind != LocatorMutationKind.RemovedElement).ToList();
            return new AblationMetrics
            {
                SuccessorScenarios = successors.Count,
                RenameScenarios = all.Count(r => r.MutationKind == LocatorMutationKind.RenamedAutomationId),
                NameDriftScenarios = all.Count(r => r.MutationKind == LocatorMutationKind.NameDrift),
                PositionShiftScenarios = all.Count(r => r.MutationKind == LocatorMutationKind.PositionShift),
                CompoundDriftScenarios = all.Count(r => r.MutationKind == LocatorMutationKind.CompoundDrift),
                CorrectHeals = all.Count(r => r.Outcome == AblationOutcome.CorrectHeal),
                FalseHeals = all.Count(r => r.Outcome == AblationOutcome.FalseHeal),
                MissedHeals = all.Count(r => r.Outcome == AblationOutcome.MissedHeal),
                RemovalScenarios = all.Count(r => r.MutationKind == LocatorMutationKind.RemovedElement),
                CorrectDeclines = all.Count(r => r.Outcome == AblationOutcome.CorrectDecline),
                FalseHealsOnRemoved = all.Count(r => r.Outcome == AblationOutcome.FalseHealOnRemoved),
                LlmHeals = all.Count(r => r.Source == HealSource.Llm && r.EngineAccepted),
                ConsensusDeclines = all.Count(r => r.ResolutionStatus == HealResolutionStatus.NoConsensus),
                ResolutionsWithoutAnyProviderAnswer = all.Count(r => r.ResolutionStatus == HealResolutionStatus.ProviderError),
                ProviderParticipation = SummarizeParticipation(all),
            };
        }

        private static IReadOnlyDictionary<string, ProviderParticipation> SummarizeParticipation(
            IEnumerable<AblationScenarioResult> results)
        {
            var participation = new SortedDictionary<string, ProviderParticipation>(StringComparer.Ordinal);

            foreach (var result in results)
            {
                foreach (var entry in result.ProviderResults)
                {
                    if (!participation.TryGetValue(entry.Key, out var counts))
                    {
                        counts = new ProviderParticipation();
                        participation[entry.Key] = counts;
                    }

                    if (entry.Value != null && entry.Value.Success)
                    {
                        counts.Answered++;
                    }
                    else
                    {
                        counts.Failed++;
                        counts.SampleError ??= entry.Value?.ErrorMessage;
                    }
                }
            }

            return participation;
        }

        public static string ToMarkdownSummary(AblationRunReport report, string datasetName)
        {
            var m = report.Metrics;
            var lines = new List<string>
            {
                $"### Locator ablation benchmark — {datasetName}",
                "",
                $"- Scenarios: **{report.Results.Count}** ({m.SuccessorScenarios} successor [{m.RenameScenarios} rename, {m.NameDriftScenarios} text drift, {m.PositionShiftScenarios} pos shift, {m.CompoundDriftScenarios} compound], {m.RemovalScenarios} removal)",
                "",
                "| Metric | Value | Reading |",
                "| :--- | ---: | :--- |",
                $"| Precision | {ReportFormatting.Percent(m.Precision)} | of everything accepted, how much was correct |",
                $"| Auto-heal recall | {ReportFormatting.Percent(m.AutoHealRecall)} | surviving locators found again |",
                $"| False-heal rate | {ReportFormatting.Percent(m.FalseHealRate)} | of everything accepted, how much was wrong |",
                $"| Manual-review rate | {ReportFormatting.Percent(m.ManualReviewRate)} | how often a human is asked |",
                "",
                "| Outcome | Count |",
                "| :--- | ---: |",
                $"| Correct heal | {m.CorrectHeals} |",
                $"| False heal (wrong element) | {m.FalseHeals} |",
                $"| Missed heal (declined, successor existed) | {m.MissedHeals} |",
                $"| Correct decline (no successor) | {m.CorrectDeclines} |",
                $"| False heal on removed element | {m.FalseHealsOnRemoved} |",
            };

            if (m.LlmHeals > 0 || m.ConsensusDeclines > 0 || m.ResolutionsWithoutAnyProviderAnswer > 0)
            {
                lines.Add("");
                lines.Add("| LLM Consensus Telemetry | Count |");
                lines.Add("| :--- | ---: |");
                lines.Add($"| LLM-accepted heals | {m.LlmHeals} |");
                lines.Add($"| Disagreement / No-consensus declines | {m.ConsensusDeclines} |");
                lines.Add($"| Resolutions with no provider answer at all | {m.ResolutionsWithoutAnyProviderAnswer} |");
            }

            if (m.ProviderParticipation.Count > 0)
            {
                // #112. Without this the reader cannot tell a three-provider result from a
                // one-provider result, and the run that prompted this had two of its three providers
                // fail every single request while the summary reported no errors.
                lines.Add("");
                lines.Add("#### Provider participation");
                lines.Add("");
                lines.Add("| Provider | Answered | Failed | Sample error |");
                lines.Add("| :--- | ---: | ---: | :--- |");
                foreach (var entry in m.ProviderParticipation)
                {
                    var sample = string.IsNullOrEmpty(entry.Value.SampleError)
                        ? "—"
                        : "`" + ReportFormatting.Truncate(entry.Value.SampleError!.Replace("\n", " "), 90) + "`";
                    lines.Add($"| `{entry.Key}` | {entry.Value.Answered} | {entry.Value.Failed} | {sample} |");
                }
            }

            var evaluated = report.Results.Where(r => r.VotePattern != ConsensusVotePattern.NotEvaluated).ToList();
            if (evaluated.Count > 0)
            {
                lines.Add("");
                lines.Add("#### Provider vote patterns");
                lines.Add("");
                lines.Add("Read from the raw per-provider votes, not from the consensus verdict. On removed elements");
                lines.Add("there is no correct answer, so scattering is the hypothesis and unanimity on a decoy is the");
                lines.Add("failure mode worth knowing about.");
                lines.Add("");
                lines.Add("| Mutation | Unanimous | Scattered | All declined | Partial | Provider failure |");
                lines.Add("| :--- | ---: | ---: | ---: | ---: | ---: |");

                foreach (var group in evaluated.GroupBy(r => r.MutationKind).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal))
                {
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "| `{0}` | {1} | {2} | {3} | {4} | {5} |",
                        group.Key,
                        group.Count(r => r.VotePattern == ConsensusVotePattern.Unanimous),
                        group.Count(r => r.VotePattern == ConsensusVotePattern.Scattered),
                        group.Count(r => r.VotePattern == ConsensusVotePattern.AllDeclined),
                        group.Count(r => r.VotePattern == ConsensusVotePattern.Partial),
                        group.Count(r => r.VotePattern == ConsensusVotePattern.ProviderFailure)));
                }

                var failures = evaluated.Count(r => r.VotePattern == ConsensusVotePattern.ProviderFailure);
                var usable = evaluated.Count - failures;
                var removed = evaluated.Where(r => r.MutationKind == LocatorMutationKind.RemovedElement).ToList();
                var usableRemoved = removed.Count(r => r.VotePattern != ConsensusVotePattern.ProviderFailure);

                lines.Add("");
                if (usable == 0)
                {
                    // Zero unanimous votes because nobody voted is not the same as zero unanimous votes
                    // because they scattered, and printing the second reading of the first situation is how
                    // a failed run gets quoted as a finding.
                    lines.Add("> [!WARNING]");
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "> **No usable data.** All {0} evaluated scenarios ended in provider failure, so this run answers nothing. The counts above describe the heuristic path only; the consensus question in #97 remains open.",
                        evaluated.Count));
                }
                else if (usableRemoved == 0)
                {
                    lines.Add("> [!WARNING]");
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "> **Inconclusive for #97.** {0} of {1} scenarios produced votes, but every removed-element scenario failed, and those are the ones the question is about.",
                        usable,
                        evaluated.Count));
                }
                else
                {
                    var unanimousOnDecoy = removed.Count(r => r.VotePattern == ConsensusVotePattern.Unanimous);
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "**Answer to the #97 question:** of {0} removed elements with usable votes, the providers agreed unanimously {1} time(s) — every one of those is agreement on an element that does not exist.",
                        usableRemoved,
                        unanimousOnDecoy));

                    if (failures > 0)
                    {
                        lines.Add("");
                        lines.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "_{0} of {1} evaluated scenarios failed at the provider and are excluded from that reading._",
                            failures,
                            evaluated.Count));
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
