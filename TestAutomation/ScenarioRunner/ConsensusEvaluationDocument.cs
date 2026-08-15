using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ScenarioRunner
{
    public class ConsensusEvaluationDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public List<string> ConfiguredProviders { get; set; } = new();
        public List<ScenarioEvaluationRecord> Scenarios { get; set; } = new();
        public ConsensusEvaluationSummary Summary { get; set; } = new();

        public string ToMarkdownStepSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## 🤖 Multi-Provider Consensus Evaluation Summary");
            sb.AppendLine();
            sb.AppendLine($"- **Timestamp (UTC):** `{Timestamp:yyyy-MM-dd HH:mm:ss} UTC`");
            sb.AppendLine($"- **Configured Providers ({ConfiguredProviders.Count}):** {string.Join(", ", ConfiguredProviders.Select(p => $"`{p}`"))}");
            sb.AppendLine($"- **Consensus Rate:** {Summary.ConsensusCount}/{Summary.TotalScenarios} ({ReportFormatting.PercentOfTotal(Summary.ConsensusCount, Math.Max(1, Summary.TotalScenarios))})");

            if (Summary.DecidableConsensusCount > 0)
            {
                sb.AppendLine($"- **Accuracy (on Decidable Consensus):** {Summary.CorrectCount}/{Summary.DecidableConsensusCount} ({ReportFormatting.PercentOfTotal(Summary.CorrectCount, Summary.DecidableConsensusCount)})");
            }

            if (Summary.UndecidableScenariosCount > 0)
            {
                sb.AppendLine($"- **Consensus on Undecidable:** {Summary.UndecidableConsensusCount}/{Summary.UndecidableScenariosCount}");
            }

            sb.AppendLine();

            sb.AppendLine("### 📋 Scenario Results");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Platform | Ground Truth | Consensus Winner | Heuristic Candidate | Status | Agreed Providers |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

            foreach (var s in Scenarios)
            {
                var isDecidable = !string.IsNullOrEmpty(s.GroundTruthAutomationId);
                string status;
                if (s.ConsensusReached)
                {
                    if (isDecidable)
                    {
                        status = s.IsCorrect == true ? "✅ Consensus (Correct)" : "⚠️ Consensus (Mismatch)";
                    }
                    else
                    {
                        status = "ℹ️ Consensus (Undecidable)";
                    }
                }
                else if (!isDecidable)
                {
                    status = "✅ No Consensus (Expected)";
                }
                else if (s.Outcome == ConsensusOutcome.NoProviderAnswered)
                {
                    status = "🔌 No Provider Answered";
                }
                else if (s.Outcome == ConsensusOutcome.TooFewUsableVotes)
                {
                    status = "🪫 Too Few Usable Votes";
                }
                else
                {
                    status = "⚔️ Models Disagreed";
                }

                var gt = isDecidable
                    ? $"`{s.GroundTruthAutomationId}`"
                    : "*(undecidable)*";

                var winner = s.ConsensusReached && !string.IsNullOrEmpty(s.ConsensusWinnerAutomationId)
                    ? $"`{s.ConsensusWinnerAutomationId}`"
                    : "*(none)*";

                var heuristic = !string.IsNullOrEmpty(s.HeuristicCandidateAutomationId)
                    ? $"`{s.HeuristicCandidateAutomationId}`"
                    : "*(none)*";

                var agreed = s.AgreedProviders.Count > 0
                    ? string.Join(", ", s.AgreedProviders)
                    : "*(none)*";

                sb.AppendLine($"| `{s.ScenarioName}` | `{s.Platform}` | {gt} | {winner} | {heuristic} | {status} | {agreed} |");
            }

            sb.AppendLine();
            sb.AppendLine("### 📊 Provider Health & Telemetry");
            sb.AppendLine();
            // "Answered" is the provider's own health; "In Consensus" depends on its peers.
            // Keeping them in separate columns is the point of #56 - a provider that answers
            // while everyone else fails is healthy, and the old single "Successful Picks"
            // column reported it as broken.
            sb.AppendLine("| Provider | Answered | Failed | Vote Discarded | In Consensus | HTTP Attempts |");
            sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :---: |");

            foreach (var p in ConfiguredProviders)
            {
                Summary.TotalProviderAnswered.TryGetValue(p, out var answered);
                Summary.TotalProviderFailed.TryGetValue(p, out var failed);
                Summary.TotalProviderDiscarded.TryGetValue(p, out var discarded);
                Summary.TotalProviderInConsensus.TryGetValue(p, out var inConsensus);
                Summary.TotalProviderAttempts.TryGetValue(p, out var attempts);

                sb.AppendLine($"| `{p}` | {answered} | {failed} | {discarded} | {inConsensus} | {attempts} |");
            }

            var failures = Scenarios
                .SelectMany(s => s.ProviderResults.Select(r => new { s.ScenarioName, Result = r }))
                .Where(x => x.Result.Outcome != ProviderOutcome.Answered && !string.IsNullOrEmpty(x.Result.Error))
                .ToList();

            if (failures.Count > 0)
            {
                // Without the error text a nightly can only report *that* a provider dropped
                // out, never why - which is what made the first live run unreadable.
                sb.AppendLine();
                sb.AppendLine("### ⚠️ Provider Failures");
                sb.AppendLine();
                sb.AppendLine("| Provider | Scenario | Outcome | Attempts | Error |");
                sb.AppendLine("| :--- | :--- | :--- | :---: | :--- |");
                foreach (var f in failures)
                {
                    var error = f.Result.Error!.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");
                    if (error.Length > 220)
                    {
                        error = error.Substring(0, 220) + "…";
                    }

                    sb.AppendLine($"| `{f.Result.ProviderName}` | `{f.ScenarioName}` | {f.Result.Outcome} | {f.Result.AttemptCount} | {error} |");
                }
            }

            if (Summary.AgreementMatrix.Count > 0 && ConfiguredProviders.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("### 🤝 Pairwise Agreement Matrix");
                sb.AppendLine();
                var providerList = ConfiguredProviders;
                sb.Append("| Provider |");
                foreach (var p in providerList)
                {
                    sb.Append($" {p} |");
                }
                sb.AppendLine();
                sb.Append("| :--- |");
                foreach (var _ in providerList)
                {
                    sb.Append(" :---: |");
                }
                sb.AppendLine();

                foreach (var row in providerList)
                {
                    sb.Append($"| **{row}** |");
                    foreach (var col in providerList)
                    {
                        if (row == col)
                        {
                            sb.Append(" - |");
                        }
                        else
                        {
                            var count = Summary.AgreementMatrix.TryGetValue(row, out var sub) && sub.TryGetValue(col, out var c) ? c : 0;
                            sb.Append($" {count} |");
                        }
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }

    public class ScenarioEvaluationRecord
    {
        public string ScenarioName { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string? GroundTruthAutomationId { get; set; }
        public string? ConsensusWinnerAutomationId { get; set; }
        public string? HeuristicCandidateAutomationId { get; set; }
        public bool ConsensusReached { get; set; }
        public bool? IsCorrect { get; set; }
        public List<string> AgreedProviders { get; set; } = new();
        public Dictionary<string, int> ProviderAttempts { get; set; } = new();

        // What each provider actually did on this scenario (#56). ProviderAttempts alone is
        // ambiguous: an attempt count of 1 means either "answered on the first try" or "failed
        // fast on a non-transient status", and the first live run could not be read because of it.
        public List<ProviderOutcomeRecord> ProviderResults { get; set; } = new();

        // Why no consensus formed, when none did. "Nobody answered" and "the models disagreed"
        // are opposite findings and must not collapse into one bucket.
        public string Outcome { get; set; } = ConsensusOutcome.ConsensusReached;
    }

    public static class ConsensusOutcome
    {
        public const string ConsensusReached = "consensus";
        public const string Disagreement = "disagreement";
        public const string TooFewUsableVotes = "too-few-usable-votes";
        public const string NoProviderAnswered = "no-provider-answered";
    }

    public sealed class ProviderOutcomeRecord
    {
        public string ProviderName { get; set; } = string.Empty;

        // "answered" - returned a usable pick. "failed" - errored, timed out, or was unavailable.
        // "discarded" - answered, but named a candidate outside the shortlist it was sent, so the
        // hallucination guard dropped the vote before counting.
        public string Outcome { get; set; } = string.Empty;
        public string? MatchedCandidateId { get; set; }
        public double? Confidence { get; set; }
        public double ElapsedMs { get; set; }
        public int AttemptCount { get; set; }
        public bool AgreedWithConsensus { get; set; }
        public string? Error { get; set; }
    }

    public static class ProviderOutcome
    {
        public const string Answered = "answered";
        public const string Failed = "failed";
        public const string Discarded = "discarded";
    }

    public class ConsensusEvaluationSummary
    {
        public int TotalScenarios { get; set; }
        public int ConsensusCount { get; set; }
        public int CorrectCount { get; set; }
        public int DecidableScenariosCount { get; set; }
        public int DecidableConsensusCount { get; set; }
        public int UndecidableScenariosCount { get; set; }
        public int UndecidableConsensusCount { get; set; }
        public int SplitVoteCount { get; set; }
        public int InsufficientProvidersCount { get; set; }
        public int NoProviderAnsweredCount { get; set; }
        public Dictionary<string, int> TotalProviderAttempts { get; set; } = new();

        // Answered vs agreed are deliberately separate (#56). A provider that returns a usable
        // pick has done its job even when its peers fail and no consensus forms; counting that
        // as a failure - which the first version did - reports a healthy provider as broken.
        public Dictionary<string, int> TotalProviderAnswered { get; set; } = new();
        public Dictionary<string, int> TotalProviderFailed { get; set; } = new();
        public Dictionary<string, int> TotalProviderDiscarded { get; set; } = new();
        public Dictionary<string, int> TotalProviderInConsensus { get; set; } = new();
        public Dictionary<string, Dictionary<string, int>> AgreementMatrix { get; set; } = new();
    }

    public static class ConsensusEvaluationSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public static string ToJson(ConsensusEvaluationDocument doc) => JsonSerializer.Serialize(doc, Options);

        public static ConsensusEvaluationDocument FromJson(string json) =>
            JsonSerializer.Deserialize<ConsensusEvaluationDocument>(json, Options)
            ?? throw new JsonException("Failed to deserialize ConsensusEvaluationDocument.");
    }
}
