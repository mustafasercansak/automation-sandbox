using System;
using System.Collections.Generic;
using System.Linq;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>Label names of the agent protocol. A human opens a task with <see cref="Allowed" />; the agent owns
    /// <see cref="Working" />, <see cref="InReview" /> and <see cref="NeedsHuman" />; a human closes the door with
    /// <see cref="Rejected" />.</summary>
    public static class AgentLabels
    {
        /// <summary>Set by a human: the issue may be picked up.</summary>
        public const string Allowed = "Agent::Allowed";

        /// <summary>Set by a human: never pick this issue up, whatever else is set.</summary>
        public const string Rejected = "Agent::Rejected";

        /// <summary>Set by the agent: a task is running.</summary>
        public const string Working = "Agent::Working";

        /// <summary>Set by the agent: the result waits for a human.</summary>
        public const string InReview = "Agent::In-Review";

        /// <summary>Set by the agent: it stopped and needs a decision. It does not block a new run: a human adding
        /// <see cref="Allowed" /> again is the signal to continue.</summary>
        public const string NeedsHuman = "Agent::Needs-Human";

        /// <summary>Prefix of the label that names the task kind, for example <c>Task::docs</c>.</summary>
        public const string TaskPrefix = "Task::";
    }

    /// <summary>Why an issue is or is not eligible. Exactly one reason is reported, in the order the members are
    /// listed here.</summary>
    public static class GateReasons
    {
        /// <summary>The issue is eligible.</summary>
        public const string Eligible = "eligible";

        /// <summary>The issue carries <see cref="AgentLabels.Rejected" />; this is reported before every other reason.</summary>
        public const string Rejected = "rejected";

        /// <summary>The item is a pull request, not an issue.</summary>
        public const string PullRequest = "pull-request";

        /// <summary>The issue is not open.</summary>
        public const string Closed = "closed";

        /// <summary>A task is already running (<see cref="AgentLabels.Working" />).</summary>
        public const string AlreadyWorking = "already-working";

        /// <summary>A result already waits for review (<see cref="AgentLabels.InReview" />).</summary>
        public const string InReview = "in-review";

        /// <summary>No <see cref="AgentLabels.Allowed" /> label.</summary>
        public const string NotAllowed = "not-allowed";

        /// <summary>No <c>Task::</c> label.</summary>
        public const string TaskKindMissing = "task-kind-missing";

        /// <summary>More than one different <c>Task::</c> label.</summary>
        public const string TaskKindAmbiguous = "task-kind-ambiguous";

        /// <summary>The <c>Task::</c> label names no known task kind.</summary>
        public const string TaskKindUnknown = "task-kind-unknown";

        /// <summary>The project board status could not be determined (not supplied or unreadable).</summary>
        public const string BoardStatusUnknown = "board-status-unknown";

        /// <summary>The issue is on the board but not in the <c>Ready</c> column.</summary>
        public const string ProjectNotReady = "project-not-ready";
    }

    /// <summary>The gate's answer for one issue.</summary>
    public sealed class GateDecision
    {
        internal GateDecision(string reason, string? taskKind)
        {
            Reason = reason;
            TaskKind = taskKind;
        }

        /// <summary>True only when every condition holds; then <see cref="TaskKind" /> is set.</summary>
        public bool Eligible => Reason == GateReasons.Eligible;

        /// <summary>One of the <see cref="GateReasons" /> values.</summary>
        public string Reason { get; }

        /// <summary>The canonical task kind when eligible, otherwise null.</summary>
        public string? TaskKind { get; }
    }

    /// <summary>The conditions under which an issue may be picked up. Pure and fail-closed: anything missing,
    /// ambiguous or unreadable means "not eligible", and the first matching reason in the documented order is the
    /// only one reported.</summary>
    public static class AgentIssueGate
    {
        /// <summary>The project board column an issue must be in.</summary>
        public const string ReadyStatus = "Ready";

        /// <summary>Decides whether <paramref name="issue" /> may be picked up. <paramref name="boardStatus" /> is the
        /// issue's status on the project board, or null when it is not on the board or could not be read.</summary>
        public static GateDecision Evaluate(IssueSnapshot issue, string? boardStatus)
        {
            if (issue == null)
            {
                throw new ArgumentNullException(nameof(issue));
            }

            bool Has(string label) => issue.Labels.Any(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase));

            if (Has(AgentLabels.Rejected))
            {
                return Not(GateReasons.Rejected);
            }

            if (issue.IsPullRequest)
            {
                return Not(GateReasons.PullRequest);
            }

            if (!issue.IsOpen)
            {
                return Not(GateReasons.Closed);
            }

            if (Has(AgentLabels.Working))
            {
                return Not(GateReasons.AlreadyWorking);
            }

            if (Has(AgentLabels.InReview))
            {
                return Not(GateReasons.InReview);
            }

            if (!Has(AgentLabels.Allowed))
            {
                return Not(GateReasons.NotAllowed);
            }

            var names = issue.Labels
                .Where(l => l.TrimStart().StartsWith(AgentLabels.TaskPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.TrimStart().Substring(AgentLabels.TaskPrefix.Length).Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0)
            {
                return Not(GateReasons.TaskKindMissing);
            }

            if (names.Count > 1)
            {
                return Not(GateReasons.TaskKindAmbiguous);
            }

            var kind = TextTaskKinds.Normalize(names[0]);
            if (kind == null)
            {
                return Not(GateReasons.TaskKindUnknown);
            }

            if (boardStatus == null)
            {
                return Not(GateReasons.BoardStatusUnknown);
            }

            if (!string.Equals(boardStatus, ReadyStatus, StringComparison.Ordinal))
            {
                return Not(GateReasons.ProjectNotReady);
            }

            return new GateDecision(GateReasons.Eligible, kind);
        }

        private static GateDecision Not(string reason) => new GateDecision(reason, null);
    }
}
