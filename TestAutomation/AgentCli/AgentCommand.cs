using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;

namespace AutomationSandbox.AgentCli
{
    internal static class AgentCommand
    {
        public const string Usage = @"automation-sandbox-agent: run one GitHub issue through the issue-triage agent (read-only).

Usage:
  automation-sandbox-agent triage --repo <owner/name> --issue <number> [options]

Options:
  --repo <owner/name>      The repository (required).
  --issue <number>         The issue number (required).
  --project-id <PVT_...>   The Projects (v2) board node id; or set PROJECT_ID.
  --routing-file <path>    JSON: which task kinds each model may serve, and defaults.
  --allow-label <name>     A label the model may suggest (repeatable); default bug, enhancement, documentation, question.
  --comment                After the run, post the report as one comment on the issue (needs GH_TOKEN or GITHUB_TOKEN
                           with permission to write issue comments). Off by default.
  -h, --help               Show this help.

Environment (keys and tokens are never read from arguments):
  PROJECT_TOKEN            Read access to the project and its issues (required).
  GH_TOKEN / GITHUB_TOKEN  Optional for a public repository; required, with permission to write issue comments, for --comment.
  GEMINI_API_KEY [GEMINI_MODEL], ANTHROPIC_API_KEY [ANTHROPIC_MODEL], OPENAI_API_KEY [OPENAI_MODEL],
  MISTRAL_API_KEY + MISTRAL_MODEL, NVIDIA_API_KEY + NVIDIA_MODEL, OLLAMA_CLOUD_API_KEY + OLLAMA_CLOUD_MODEL,
  OLLAMA_MODEL [OLLAMA_HOST] (a local Ollama, no key): at least one model is required.

The issue needs Agent::Allowed, one Task::issue-triage label, and the Ready column on the board. Choose the models with
Model::<name> (drafting) and Review::<name> (independent review) labels. The report is JSON on stdout; diagnostics go to
stderr. It changes no label or card, and posts a comment only with --comment.

Exit codes: 0 the proposal was accepted; 1 the issue is not eligible, routing stopped, or the proposal was blocked or failed
(the report says why); 2 the command could not run (bad arguments, no token, no model, or the comment could not be posted).";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public static async Task<int> RunAsync(
            string[] args,
            Func<string, string?> getEnv,
            Func<string, string> readFile,
            TextWriter stdout,
            TextWriter stderr,
            HttpClient? httpClient,
            CancellationToken cancellationToken)
        {
            AgentOptions options;
            try
            {
                options = AgentOptions.Parse(args);
            }
            catch (AgentUsageException ex)
            {
                await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
                await stderr.WriteLineAsync("Run with --help for usage.").ConfigureAwait(false);
                return 2;
            }

            if (options.ShowHelp)
            {
                await stdout.WriteLineAsync(Usage).ConfigureAwait(false);
                return 0;
            }

            string? Env(string key)
            {
                var value = getEnv(key);
                return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
            }

            var projectId = options.ProjectId ?? Env("PROJECT_ID");
            if (projectId == null || !ProjectBoardReader.IsValidProjectId(projectId))
            {
                await stderr.WriteLineAsync("A project board id is required: --project-id PVT_... or the PROJECT_ID environment variable.").ConfigureAwait(false);
                return 2;
            }

            var projectToken = Env("PROJECT_TOKEN");
            if (projectToken == null)
            {
                await stderr.WriteLineAsync("PROJECT_TOKEN is required (read access to the project and its issues).").ConfigureAwait(false);
                return 2;
            }

            var config = TextModelRoutingConfig.Empty;
            if (options.RoutingFile != null)
            {
                try
                {
                    config = TextModelRoutingConfig.Parse(readFile(options.RoutingFile));
                }
                catch (FormatException ex)
                {
                    await stderr.WriteLineAsync("The routing file is invalid: " + ex.Message).ConfigureAwait(false);
                    return 2;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    await stderr.WriteLineAsync("The routing file could not be read.").ConfigureAwait(false);
                    return 2;
                }
            }

            var issueToken = Env("GH_TOKEN") ?? Env("GITHUB_TOKEN");
            if (options.Comment && issueToken == null)
            {
                await stderr.WriteLineAsync("--comment needs GH_TOKEN or GITHUB_TOKEN with permission to write issue comments (for a public repository, the public_repo scope).").ConfigureAwait(false);
                return 2;
            }

            var registry = TextModelRegistry.CreateFromEnvironment(httpClient, getEnv);
            if (registry.Models.Count == 0)
            {
                await stderr.WriteLineAsync("No model is configured. Set at least one of GEMINI_API_KEY, ANTHROPIC_API_KEY, OPENAI_API_KEY, MISTRAL_API_KEY with MISTRAL_MODEL, NVIDIA_API_KEY with NVIDIA_MODEL, OLLAMA_CLOUD_API_KEY with OLLAMA_CLOUD_MODEL, or OLLAMA_MODEL for a local Ollama.").ConfigureAwait(false);
                return 2;
            }

            AgentRunReport report;
            try
            {
                report = await AgentTriageRun.RunAsync(
                    options.Repository, options.Issue, projectId, projectToken, registry, config,
                    options.AllowedLabels.Count == 0 ? null : options.AllowedLabels,
                    issueToken, httpClient, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await stderr.WriteLineAsync("Cancelled.").ConfigureAwait(false);
                return 2;
            }
            catch (Exception ex)
            {
                // Only the type: an exception message could carry a URL or text from a response.
                await stderr.WriteLineAsync("Unexpected error: " + ex.GetType().Name).ConfigureAwait(false);
                return 2;
            }

            await stdout.WriteLineAsync(ToJson(report)).ConfigureAwait(false);
            await stderr.WriteLineAsync(AgentReportComment.ErrorSummary(report.Summary())).ConfigureAwait(false);

            // Only when the issue was read: an unreadable issue has nothing to comment on.
            if (options.Comment && report.Gate!.Reason != GateReasons.IssueUnreadable)
            {
                try
                {
                    var posted = await IssueCommentWriter.PostAsync(options.Repository, options.Issue, issueToken!, AgentReportComment.Build(report), httpClient, cancellationToken).ConfigureAwait(false);
                    if (!posted.Success)
                    {
                        await stderr.WriteLineAsync("Could not post the comment: " + posted.Error).ConfigureAwait(false);
                        return 2;
                    }

                    await stderr.WriteLineAsync("Posted the report as a comment on the issue.").ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await stderr.WriteLineAsync("Cancelled.").ConfigureAwait(false);
                    return 2;
                }
            }

            return report.IsAccepted ? 0 : 1;
        }

        // An explicit shape: the JSON report is the tool's contract, so nothing is serialised by accident.
        internal static string ToJson(AgentRunReport report)
        {
            var triage = report.Triage;
            var proposal = triage?.Proposal;
            var ci = report.CiFailure;
            return JsonSerializer.Serialize(new
            {
                stage = report.Stage.ToString(),
                gate = report.Gate == null ? null : new { eligible = report.Gate.Eligible, reason = report.Gate.Reason, taskKind = report.Gate.TaskKind },
                taskKind = report.TaskKind,
                readError = Reduce(report.ReadError),
                routingProblems = report.RoutingProblems,
                triage = triage == null ? null : new
                {
                    status = triage.Status.ToString(),
                    draftedBy = triage.DraftedBy,
                    reviewedBy = triage.ReviewedBy,
                    findings = triage.Findings,
                    error = Reduce(triage.Error),
                    proposal = proposal == null ? null : new
                    {
                        category = proposal.Category,
                        summary = proposal.Summary,
                        missingInformation = proposal.MissingInformation,
                        suggestedLabels = proposal.SuggestedLabels,
                    },
                },
                ciFailure = ci == null ? null : new
                {
                    status = ci.Status.ToString(),
                    category = ci.Classification.Category,
                    matchedCategories = ci.Classification.MatchedCategories,
                    retryable = ci.Classification.Retryable,
                    draftedBy = ci.DraftedBy,
                    reviewedBy = ci.ReviewedBy,
                    findings = ci.Findings,
                    error = Reduce(ci.Error),
                    proposal = ci.Proposal == null ? null : new
                    {
                        title = ci.Proposal.Title,
                        body = ci.Proposal.Body,
                        suggestedLabels = ci.Proposal.SuggestedLabels,
                    },
                },
                summary = AgentReportComment.ErrorSummary(report.Summary()),
            }, JsonOptions);
        }

        // A provider's response body can carry account identifiers; the report keeps the status and a short reason only.
        private static string? Reduce(string? error) => error == null ? null : AgentReportComment.ErrorSummary(error);
    }
}
