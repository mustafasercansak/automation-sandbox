using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
  automation-sandbox-agent lessons build --repo <owner/name> --out <file> [--max-records <n>] [--before <yyyy-MM-dd>]

Options:
  --repo <owner/name>      The repository (required).
  --issue <number>         The issue number (required).
  --project-id <PVT_...>   The Projects (v2) board node id; or set PROJECT_ID.
  --routing-file <path>    JSON: which task kinds each model may serve, and defaults.
  --docs-root <dir>        A git checkout the docs task reads its source files from (only files git tracks, named by
                           ""Source: path"" lines in the issue). Without it a Task::docs issue fails before any model is asked.
  --allow-label <name>     A label the model may suggest (repeatable); default bug, enhancement, documentation, question.
  --lessons-file <path>    A lesson index (see ""lessons build""): the prompts of the drafting and reviewing models then also hold
                           the few past issues and pull requests that match the issue, quoted as data and named by number. Off
                           unless given; without it the prompts are exactly the same as before.
  --comment                After the run, post the report as one comment on the issue (needs GH_TOKEN or GITHUB_TOKEN
                           with permission to write issue comments). Off by default.
  --label-needs-human      When a task ran and the review blocked the proposal or the run failed, add the label
                           Agent::Needs-Human to the issue (needs GH_TOKEN or GITHUB_TOKEN with permission to write issues). It
                           adds that one label and nothing else, never removes one, and does nothing for an accepted run. Off by default.
  -h, --help               Show this help.

Environment (keys and tokens are never read from arguments):
  PROJECT_TOKEN            Read access to the project and its issues (required).
  GH_TOKEN / GITHUB_TOKEN  Optional for a public repository; required, with permission to write issue comments, for --comment.
  GEMINI_API_KEY [GEMINI_MODEL], ANTHROPIC_API_KEY [ANTHROPIC_MODEL], OPENAI_API_KEY [OPENAI_MODEL],
  MISTRAL_API_KEY + MISTRAL_MODEL, NVIDIA_API_KEY + NVIDIA_MODEL, OLLAMA_CLOUD_API_KEY + OLLAMA_CLOUD_MODEL,
  OLLAMA_MODEL [OLLAMA_HOST] (a local Ollama, no key): at least one model is required.

The issue needs Agent::Allowed, one Task::issue-triage, Task::ci-failure or Task::docs label, and the Ready column on the board. Choose the models with
Model::<name> (drafting) and Review::<name> (independent review) labels. The report is JSON on stdout; diagnostics go to
stderr. It changes no card, adds a label only with --label-needs-human, and posts a comment only with --comment.

lessons build reads the closed issues and pull requests of a repository from the GitHub API (read only; GH_TOKEN or GITHUB_TOKEN
is optional but an anonymous run is rate limited hard) and writes a plain JSON index file. --before keeps only items closed before
that date, for replaying a past defect without history the reviewer could not have had then.

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
            CancellationToken cancellationToken,
            Func<string, IReadOnlyCollection<string>>? listTrackedFiles = null,
            Action<string, string>? writeFile = null)
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

            if (options.Command == "lessons-build")
            {
                return await BuildLessonsAsync(options, Env("GH_TOKEN") ?? Env("GITHUB_TOKEN"), writeFile, stdout, stderr, httpClient, cancellationToken).ConfigureAwait(false);
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

            if (options.LabelNeedsHuman && issueToken == null)
            {
                await stderr.WriteLineAsync("--label-needs-human needs GH_TOKEN or GITHUB_TOKEN with permission to write issues (for a public repository, the public_repo scope).").ConfigureAwait(false);
                return 2;
            }

            var registry = TextModelRegistry.CreateFromEnvironment(httpClient, getEnv);
            if (registry.Models.Count == 0)
            {
                await stderr.WriteLineAsync("No model is configured. Set at least one of GEMINI_API_KEY, ANTHROPIC_API_KEY, OPENAI_API_KEY, MISTRAL_API_KEY with MISTRAL_MODEL, NVIDIA_API_KEY with NVIDIA_MODEL, OLLAMA_CLOUD_API_KEY with OLLAMA_CLOUD_MODEL, or OLLAMA_MODEL for a local Ollama.").ConfigureAwait(false);
                return 2;
            }

            LessonRetrievalLog? lessonLog = null;
            if (options.LessonsFile != null)
            {
                LessonIndex index;
                try
                {
                    index = LessonIndex.Parse(readFile(options.LessonsFile));
                }
                catch (FormatException ex)
                {
                    await stderr.WriteLineAsync("The lesson index is invalid: " + ex.Message).ConfigureAwait(false);
                    return 2;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    await stderr.WriteLineAsync("The lesson index could not be read.").ConfigureAwait(false);
                    return 2;
                }

                lessonLog = new LessonRetrievalLog();
                var wrapped = new TextModelRegistry();
                foreach (var model in registry.Models)
                {
                    wrapped.Add(new LessonTextModel(model, index, null, lessonLog));
                }

                registry = wrapped;
            }

            // Listing the tracked files runs git, so it is deferred until a docs issue needs it.
            DocsSourceContext? docsSources = null;
            if (options.DocsRoot != null && listTrackedFiles != null)
            {
                var root = options.DocsRoot;
                docsSources = new DocsSourceContext(root, () => listTrackedFiles(root));
            }

            AgentRunReport report;
            try
            {
                report = await AgentTriageRun.RunAsync(
                    options.Repository, options.Issue, projectId, projectToken, registry, config,
                    options.AllowedLabels.Count == 0 ? null : options.AllowedLabels,
                    issueToken, httpClient, cancellationToken, docsSources).ConfigureAwait(false);
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

            await stdout.WriteLineAsync(ToJson(report, lessonLog)).ConfigureAwait(false);
            await stderr.WriteLineAsync(AgentReportComment.ErrorSummary(report.Summary())).ConfigureAwait(false);
            if (lessonLog != null)
            {
                var used = lessonLog.Entries.SelectMany(e => e.Matches).Select(m => m.Record.Number).Distinct().OrderBy(n => n).ToList();
                await stderr.WriteLineAsync(used.Count == 0 ? "No past issue or pull request matched; the prompts were not changed." : "Lessons used: " + string.Join(", ", used.Select(n => "#" + n)) + ".").ConfigureAwait(false);
            }

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

            if (options.LabelNeedsHuman && report.NeedsHuman)
            {
                try
                {
                    var labelled = await IssueLabelWriter.AddNeedsHumanAsync(options.Repository, options.Issue, issueToken!, httpClient, cancellationToken).ConfigureAwait(false);
                    if (!labelled.Success)
                    {
                        await stderr.WriteLineAsync("Could not add the label: " + labelled.Error).ConfigureAwait(false);
                        return 2;
                    }

                    await stderr.WriteLineAsync("Added the label " + AgentLabels.NeedsHuman + " to the issue.").ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await stderr.WriteLineAsync("Cancelled.").ConfigureAwait(false);
                    return 2;
                }
            }

            return report.IsAccepted ? 0 : 1;
        }

        private static async Task<int> BuildLessonsAsync(AgentOptions options, string? token, Action<string, string>? writeFile, TextWriter stdout, TextWriter stderr, HttpClient? httpClient, CancellationToken cancellationToken)
        {
            LessonIndexBuildResult result;
            try
            {
                result = await LessonIndexBuilder.BuildAsync(
                    options.Repository, token,
                    new LessonIndexBuildOptions { MaxRecords = options.MaxRecords, ClosedBefore = options.Before },
                    httpClient, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await stderr.WriteLineAsync("Cancelled.").ConfigureAwait(false);
                return 2;
            }

            if (!result.Success)
            {
                await stderr.WriteLineAsync("The lesson index could not be built: " + result.Error).ConfigureAwait(false);
                return 2;
            }

            try
            {
                (writeFile ?? File.WriteAllText)(options.Output!, result.Index!.ToJson());
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                await stderr.WriteLineAsync("The lesson index could not be written.").ConfigureAwait(false);
                return 2;
            }

            var index = result.Index!;
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                repository = index.Repository,
                records = index.Records.Count,
                issues = index.Records.Count(r => r.Kind == LessonRecord.IssueKind),
                pullRequests = index.Records.Count(r => r.Kind == LessonRecord.PullRequestKind),
                detailFailures = result.DetailFailures,
                truncated = result.Truncated,
            }, JsonOptions)).ConfigureAwait(false);
            if (result.DetailFailures > 0 || result.Truncated)
            {
                await stderr.WriteLineAsync((result.Truncated ? "The build stopped at a limit, so older history is missing. " : "") + (result.DetailFailures > 0 ? result.DetailFailures + " pull request(s) are indexed without their files or review comments. " : "")).ConfigureAwait(false);
            }

            return 0;
        }

        // An explicit shape: the JSON report is the tool's contract, so nothing is serialised by accident.
        internal static string ToJson(AgentRunReport report, LessonRetrievalLog? lessons = null)
        {
            var triage = report.Triage;
            var proposal = triage?.Proposal;
            var ci = report.CiFailure;
            var docs = report.Docs;
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
                docs = docs == null ? null : new
                {
                    status = docs.Status.ToString(),
                    sources = docs.SourcePaths,
                    draftedBy = docs.DraftedBy,
                    reviewedBy = docs.ReviewedBy,
                    findings = docs.Findings,
                    error = Reduce(docs.Error),
                    proposal = docs.Proposal == null ? null : new
                    {
                        title = docs.Proposal.Title,
                        document = docs.Proposal.Document,
                        sourcesUsed = docs.Proposal.SourcesUsed,
                    },
                },
                lessons = lessons == null ? null : lessons.Entries.Select(e => new
                {
                    model = e.Model,
                    records = e.Matches.Select(m => new { kind = m.Record.Kind, number = m.Record.Number, score = Math.Round(m.Score, 3), fileMatch = m.FileMatch }).ToList(),
                }).ToList(),
                summary = AgentReportComment.ErrorSummary(report.Summary()),
            }, JsonOptions);
        }

        // A provider's response body can carry account identifiers; the report keeps the status and a short reason only.
        private static string? Reduce(string? error) => error == null ? null : AgentReportComment.ErrorSummary(error);
    }
}
