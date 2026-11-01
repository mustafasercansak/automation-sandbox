using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>How far a run got.</summary>
    public enum AgentRunStage
    {
        /// <summary>The gate stopped the issue (or it could not be read); no model was asked.</summary>
        NotEligible,

        /// <summary>The issue passed the gate but its task kind has no implementation yet; no model was asked.</summary>
        UnsupportedTask,

        /// <summary>Routing could not honour the labels; no model was asked.</summary>
        RoutingFailed,

        /// <summary>The task ran; <see cref="AgentRunReport.Triage" /> or <see cref="AgentRunReport.CiFailure" /> says whether it was accepted, blocked or failed.</summary>
        Completed,
    }

    /// <summary>What one run did and why it stopped. Plain data: it holds no token and no raw model answer.</summary>
    public sealed class AgentRunReport
    {
        internal AgentRunReport(AgentRunStage stage, GateDecision? gate, string? readError, string? taskKind, IReadOnlyList<string> routingProblems, TriageOutcome? triage, CiFailureOutcome? ciFailure = null)
        {
            Stage = stage;
            Gate = gate;
            ReadError = readError;
            TaskKind = taskKind;
            RoutingProblems = routingProblems;
            Triage = triage;
            CiFailure = ciFailure;
        }

        /// <summary>How far the run got.</summary>
        public AgentRunStage Stage { get; }

        /// <summary>The gate's decision.</summary>
        public GateDecision? Gate { get; }

        /// <summary>Why an issue or board read failed, or a note such as "not on the project board"; never a token.</summary>
        public string? ReadError { get; }

        /// <summary>The task kind of an eligible issue.</summary>
        public string? TaskKind { get; }

        /// <summary>Every reason routing stopped; empty otherwise.</summary>
        public IReadOnlyList<string> RoutingProblems { get; }

        /// <summary>The triage outcome when <see cref="Stage" /> is <see cref="AgentRunStage.Completed" />.</summary>
        public TriageOutcome? Triage { get; }

        /// <summary>The outcome when the task kind was <c>ci-failure</c> and <see cref="Stage" /> is <see cref="AgentRunStage.Completed" />.</summary>
        public CiFailureOutcome? CiFailure { get; }

        /// <summary>True when the run completed and its proposal was accepted (valid, and not blocked by the reviewer).</summary>
        public bool IsAccepted => Stage == AgentRunStage.Completed && (Triage?.Status ?? CiFailure?.Status) == TriageStatus.Accepted;

        /// <summary>One line that says how the run ended.</summary>
        public string Summary()
        {
            switch (Stage)
            {
                case AgentRunStage.NotEligible:
                    return $"Not eligible: {Gate?.Reason}" + (string.IsNullOrEmpty(ReadError) ? "" : $" ({ReadError})");
                case AgentRunStage.UnsupportedTask:
                    return $"Task kind \"{TaskKind}\" is not implemented yet; no model was asked.";
                case AgentRunStage.RoutingFailed:
                    return "Routing stopped the task: " + string.Join(" ", RoutingProblems);
                default:
                    if (CiFailure != null)
                    {
                        return $"CI failure {CiFailure.Status}" + (CiFailure.Error == null ? "" : ": " + CiFailure.Error);
                    }

                    return $"Triage {Triage!.Status}" + (Triage.Error == null ? "" : ": " + Triage.Error);
            }
        }
    }

    /// <summary>Runs an issue from GitHub to a reviewed triage proposal: read the issue and its board card and apply the
    /// gate, route the models from the issue's labels, run the triage pipeline. It only reads: it changes no label,
    /// comment or card. Every ordinary failure is a report, never an exception.</summary>
    public static class AgentTriageRun
    {
        /// <summary>Runs issue <paramref name="number" /> of <paramref name="repository" />. No model is asked unless the gate
        /// passed and routing succeeded. <paramref name="config" /> defaults to <see cref="TextModelRoutingConfig.Empty" />.</summary>
        public static async Task<AgentRunReport> RunAsync(
            string repository,
            int number,
            string projectId,
            string projectToken,
            TextModelRegistry registry,
            TextModelRoutingConfig? config = null,
            IReadOnlyCollection<string>? allowedLabels = null,
            string? issueToken = null,
            HttpClient? httpClient = null,
            CancellationToken cancellationToken = default)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            // Cancellation is checked here, between the stages, and does not rely on the HTTP stack: .NET Framework's
            // HttpClient does not throw for a token that is already cancelled, .NET does.
            cancellationToken.ThrowIfCancellationRequested();
            var intake = await AgentIssueIntake.CheckAsync(repository, number, projectId, projectToken, issueToken, httpClient, cancellationToken).ConfigureAwait(false);
            if (!intake.Decision.Eligible)
            {
                return new AgentRunReport(AgentRunStage.NotEligible, intake.Decision, intake.Error, null, new string[0], null);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var issue = intake.Issue!;
            var kind = intake.Decision.TaskKind!;
            if (kind != TextTaskKinds.IssueTriage && kind != TextTaskKinds.CiFailure)
            {
                return new AgentRunReport(AgentRunStage.UnsupportedTask, intake.Decision, intake.Error, kind, new string[0], null);
            }

            var routing = TextModelRouter.Route(kind, issue.Labels, registry, config ?? TextModelRoutingConfig.Empty);
            if (!routing.Success)
            {
                return new AgentRunReport(AgentRunStage.RoutingFailed, intake.Decision, intake.Error, kind, routing.Problems, null);
            }

            if (kind == TextTaskKinds.CiFailure)
            {
                var ci = await CiFailurePipeline.RunAsync(issue, routing, allowedLabels, cancellationToken).ConfigureAwait(false);
                return new AgentRunReport(AgentRunStage.Completed, intake.Decision, intake.Error, kind, new string[0], null, ci);
            }

            var triage = await TriagePipeline.RunAsync(issue, routing, allowedLabels, cancellationToken).ConfigureAwait(false);
            return new AgentRunReport(AgentRunStage.Completed, intake.Decision, intake.Error, kind, new string[0], triage);
        }
    }
}
