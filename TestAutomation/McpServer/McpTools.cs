using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.AgentCli;
using AutomationSandbox.LlmHealing;
using AutomationSandbox.SelfHealing;
using AutomationSandbox.UiModel;

namespace AutomationSandbox.Mcp
{
    /// <summary>The tools the MCP server offers. All of them only read: none changes a label, a card, a file or an issue, and none
    /// takes a credential as an argument or returns one. Tokens and model keys come from the server's own environment, which the
    /// caller of a tool cannot read or set. Every argument is untrusted: it is type-checked, bounded and validated before use.</summary>
    internal static class McpTools
    {
        /// <summary>Longest accepted JSON document for the heal tool, in characters.</summary>
        public const int MaxDocumentLength = 400000;

        /// <summary>Longest accepted CI log, in characters. The classifier reads only the end of a longer one anyway.</summary>
        public const int MaxLogLength = 400000;

        private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true };

        public static IReadOnlyList<McpTool> Create(Func<string, string?> getEnv, HttpClient? httpClient)
        {
            return new[]
            {
                new McpTool(
                    "heal_locator",
                    "Find the live UI element that a stale locator now refers to. Takes the stale element and a freshly captured UI tree as JSON (the shape of UiTreeSerializer.ToJson) and returns the heal decision with its score breakdown. isConfident is the engine's own acceptance gate: do not treat a match as safe when it is false. Nothing is changed.",
                    "{\"type\":\"object\",\"properties\":{\"expected\":{\"type\":\"string\",\"description\":\"The stale element as one UiElementInfo JSON object.\"},\"tree\":{\"type\":\"string\",\"description\":\"The live UI tree as UiElementInfo JSON.\"},\"profile\":{\"type\":\"string\",\"enum\":[\"balanced\",\"conservative\",\"aggressive\"],\"description\":\"Threshold profile; default balanced.\"}},\"required\":[\"expected\",\"tree\"],\"additionalProperties\":false}",
                    (args, ct) => Task.FromResult(HealLocator(args))),
                new McpTool(
                    "classify_ci_log",
                    "Sort a failed CI log into a category (permissions, dependency, build, test, timeout, infrastructure or unknown) with deterministic rules, no model. Returns the category, every matching category, whether a plain re-run may fix it, a few evidence lines and an excerpt. Credential shapes in the log are masked in the answer.",
                    "{\"type\":\"object\",\"properties\":{\"log\":{\"type\":\"string\",\"description\":\"The CI log text.\"}},\"required\":[\"log\"],\"additionalProperties\":false}",
                    (args, ct) => Task.FromResult(ClassifyCiLog(args))),
                new McpTool(
                    "check_issue_eligibility",
                    "Read a GitHub issue and its project board card and say whether the agent may process it (the Agent label gate). Reads only. The server uses its own read tokens; none is taken or returned.",
                    IssueSchema(),
                    (args, ct) => CheckEligibilityAsync(args, getEnv, httpClient, ct)),
                new McpTool(
                    "triage_issue_dry_run",
                    "Run the read-only triage agent on one eligible issue: a model drafts a proposal and a second model checks it. Nothing is posted, labelled or moved; the proposal is returned for a person to read. It asks the configured models, so it costs quota.",
                    IssueSchema(),
                    (args, ct) => TriageAsync(args, getEnv, httpClient, ct)),
            };
        }

        private static string IssueSchema() =>
            "{\"type\":\"object\",\"properties\":{\"repository\":{\"type\":\"string\",\"description\":\"owner/name\"},\"issue\":{\"type\":\"integer\",\"minimum\":1},\"project_id\":{\"type\":\"string\",\"description\":\"The Projects (v2) node id (PVT_...); optional when the server has PROJECT_ID set.\"}},\"required\":[\"repository\",\"issue\"],\"additionalProperties\":false}";

        private static McpToolResult HealLocator(JsonElement args)
        {
            if (!TryGetString(args, "expected", MaxDocumentLength, true, out var expectedText, out var problem) ||
                !TryGetString(args, "tree", MaxDocumentLength, true, out var treeText, out problem) ||
                !TryGetString(args, "profile", 20, false, out var profileText, out problem))
            {
                return McpToolResult.Error(problem!);
            }

            var profile = ThresholdProfile.Balanced;
            if (profileText != null && !Enum.TryParse(profileText, true, out profile))
            {
                return McpToolResult.Error("profile must be balanced, conservative or aggressive.");
            }

            UiElementInfo expected;
            UiElementInfo tree;
            try
            {
                expected = UiTreeSerializer.FromJson(expectedText!);
                tree = UiTreeSerializer.FromJson(treeText!);
            }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException)
            {
                return McpToolResult.Error("expected and tree must be JSON shaped like UiTreeSerializer.ToJson's output.");
            }

            if (expected == null || tree == null)
            {
                return McpToolResult.Error("expected and tree must be JSON shaped like UiTreeSerializer.ToJson's output.");
            }

            // The resolver's log callback is dropped: only the result goes back, never a side channel.
            var result = SelfHealingResolver.Resolve(expected, tree, SimilarityWeights.FromProfile(profile), log: _ => { });
            return McpToolResult.Ok(JsonSerializer.Serialize(result, Indented));
        }

        private static McpToolResult ClassifyCiLog(JsonElement args)
        {
            if (!TryGetString(args, "log", MaxLogLength, true, out var log, out var problem))
            {
                return McpToolResult.Error(problem!);
            }

            var c = CiLogClassifier.Classify(log);
            return McpToolResult.Ok(JsonSerializer.Serialize(new
            {
                category = c.Category,
                matchedCategories = c.MatchedCategories,
                retryable = c.Retryable,
                evidence = c.Evidence,
                excerpt = c.Excerpt,
                truncated = c.Truncated,
            }, Indented));
        }

        private static async Task<McpToolResult> CheckEligibilityAsync(JsonElement args, Func<string, string?> getEnv, HttpClient? httpClient, CancellationToken ct)
        {
            var inputs = ReadIssueInputs(args, getEnv);
            if (inputs.Problem != null)
            {
                return McpToolResult.Error(inputs.Problem);
            }

            var result = await AgentIssueIntake.CheckAsync(inputs.Repository!, inputs.Issue, inputs.ProjectId!, inputs.ProjectToken!, inputs.IssueToken, httpClient, ct).ConfigureAwait(false);
            return McpToolResult.Ok(JsonSerializer.Serialize(new
            {
                eligible = result.Decision.Eligible,
                reason = result.Decision.Reason,
                taskKind = result.Decision.TaskKind,
                error = result.Error == null ? null : AgentReportComment.ErrorSummary(result.Error),
            }, Indented));
        }

        private static async Task<McpToolResult> TriageAsync(JsonElement args, Func<string, string?> getEnv, HttpClient? httpClient, CancellationToken ct)
        {
            var inputs = ReadIssueInputs(args, getEnv);
            if (inputs.Problem != null)
            {
                return McpToolResult.Error(inputs.Problem);
            }

            var registry = TextModelRegistry.CreateFromEnvironment(httpClient, getEnv);
            if (registry.Models.Count == 0)
            {
                return McpToolResult.Error("The server has no model configured, so it cannot triage.");
            }

            var report = await AgentTriageRun.RunAsync(
                inputs.Repository!, inputs.Issue, inputs.ProjectId!, inputs.ProjectToken!, registry, TextModelRoutingConfig.Empty,
                null, inputs.IssueToken, httpClient, ct).ConfigureAwait(false);

            // The same explicit report shape the command line prints: provider errors are already reduced to a status and a short reason.
            return McpToolResult.Ok(AgentCommand.ToJson(report));
        }

        private sealed class IssueInputs
        {
            public string? Problem { get; set; }

            public string? Repository { get; set; }

            public int Issue { get; set; }

            public string? ProjectId { get; set; }

            public string? ProjectToken { get; set; }

            public string? IssueToken { get; set; }
        }

        private static IssueInputs ReadIssueInputs(JsonElement args, Func<string, string?> getEnv)
        {
            var inputs = new IssueInputs();
            if (!TryGetString(args, "repository", 140, true, out var repository, out var problem) ||
                !TryGetString(args, "project_id", 100, false, out var projectId, out problem))
            {
                inputs.Problem = problem;
                return inputs;
            }

            if (!GitHubIssueReader.IsValidRepository(repository))
            {
                inputs.Problem = "repository must look like owner/name.";
                return inputs;
            }

            if (!args.TryGetProperty("issue", out var issue) || issue.ValueKind != JsonValueKind.Number || !issue.TryGetInt32(out var number) || number < 1)
            {
                inputs.Problem = "issue must be a positive whole number.";
                return inputs;
            }

            string? Env(string key)
            {
                var value = getEnv(key);
                return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
            }

            projectId = projectId ?? Env("PROJECT_ID");
            if (projectId == null || !ProjectBoardReader.IsValidProjectId(projectId))
            {
                inputs.Problem = "A project board id (PVT_...) is required: pass project_id, or set PROJECT_ID on the server.";
                return inputs;
            }

            var projectToken = Env("PROJECT_TOKEN");
            if (projectToken == null)
            {
                inputs.Problem = "The server has no PROJECT_TOKEN, so it cannot read the project board.";
                return inputs;
            }

            inputs.Repository = repository;
            inputs.Issue = number;
            inputs.ProjectId = projectId;
            inputs.ProjectToken = projectToken;
            inputs.IssueToken = Env("GH_TOKEN") ?? Env("GITHUB_TOKEN");
            return inputs;
        }

        // A string argument: present and a string (or absent and optional), not longer than the limit.
        private static bool TryGetString(JsonElement args, string name, int maxLength, bool required, out string? value, out string? problem)
        {
            value = null;
            problem = null;
            if (!args.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            {
                if (required)
                {
                    problem = name + " is required.";
                    return false;
                }

                return true;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                problem = name + " must be a string.";
                return false;
            }

            var text = element.GetString()!;
            if (text.Length > maxLength)
            {
                problem = name + " is longer than " + maxLength + " characters.";
                return false;
            }

            value = text;
            return true;
        }
    }
}
