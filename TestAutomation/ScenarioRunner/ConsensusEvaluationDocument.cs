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
            sb.AppendLine($"- **Consensus Rate:** {Summary.ConsensusCount}/{Summary.TotalScenarios} ({((double)Summary.ConsensusCount / Math.Max(1, Summary.TotalScenarios) * 100):F0}%)");
            sb.AppendLine();

            sb.AppendLine("### 📋 Scenario Results");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Platform | Ground Truth | Consensus Winner | Heuristic Candidate | Status | Agreed Providers |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

            foreach (var s in Scenarios)
            {
                var status = s.ConsensusReached
                    ? (s.IsCorrect == true ? "✅ Consensus (Correct)" : "⚠️ Consensus (Mismatch)")
                    : (s.AgreedProviders.Count == 0 && s.ProviderAttempts.Count(p => p.Value > 0) < 2
                        ? "🔌 Insufficient Providers"
                        : "⚔️ Split Vote / Disagreement");

                var winner = s.ConsensusReached && !string.IsNullOrEmpty(s.ConsensusWinnerAutomationId)
                    ? $"`{s.ConsensusWinnerAutomationId}`"
                    : "*(none)*";

                var heuristic = !string.IsNullOrEmpty(s.HeuristicCandidateAutomationId)
                    ? $"`{s.HeuristicCandidateAutomationId}`"
                    : "*(none)*";

                var agreed = s.AgreedProviders.Count > 0
                    ? string.Join(", ", s.AgreedProviders)
                    : "*(none)*";

                sb.AppendLine($"| `{s.ScenarioName}` | `{s.Platform}` | `{s.GroundTruthAutomationId}` | {winner} | {heuristic} | {status} | {agreed} |");
            }

            sb.AppendLine();
            sb.AppendLine("### 📊 Provider Health & Telemetry");
            sb.AppendLine();
            sb.AppendLine("| Provider | Successful Picks | Errors / Dissent | Total HTTP Attempts |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");

            foreach (var p in ConfiguredProviders)
            {
                Summary.TotalProviderSuccesses.TryGetValue(p, out var successes);
                Summary.TotalProviderFailures.TryGetValue(p, out var failures);
                Summary.TotalProviderAttempts.TryGetValue(p, out var attempts);

                sb.AppendLine($"| `{p}` | {successes} | {failures} | {attempts} |");
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
        public string GroundTruthAutomationId { get; set; } = string.Empty;
        public string? ConsensusWinnerAutomationId { get; set; }
        public string? HeuristicCandidateAutomationId { get; set; }
        public bool ConsensusReached { get; set; }
        public bool? IsCorrect { get; set; }
        public List<string> AgreedProviders { get; set; } = new();
        public Dictionary<string, int> ProviderAttempts { get; set; } = new();
    }

    public class ConsensusEvaluationSummary
    {
        public int TotalScenarios { get; set; }
        public int ConsensusCount { get; set; }
        public int CorrectCount { get; set; }
        public int SplitVoteCount { get; set; }
        public int InsufficientProvidersCount { get; set; }
        public Dictionary<string, int> TotalProviderAttempts { get; set; } = new();
        public Dictionary<string, int> TotalProviderSuccesses { get; set; } = new();
        public Dictionary<string, int> TotalProviderFailures { get; set; } = new();
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
