using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>The outcome of <see cref="AgentIssueIntake.CheckAsync" />.</summary>
    public sealed class AgentIssueIntakeResult
    {
        internal AgentIssueIntakeResult(GateDecision decision, IssueSnapshot? issue, string? error)
        {
            Decision = decision;
            Issue = issue;
            Error = error;
        }

        /// <summary>The gate's decision; never eligible when a read failed.</summary>
        public GateDecision Decision { get; }

        /// <summary>The issue that was read, or null when it could not be read.</summary>
        public IssueSnapshot? Issue { get; }

        /// <summary>The reason a read failed, or null. Never contains a token.</summary>
        public string? Error { get; }
    }

    /// <summary>Reads an issue and its board card from GitHub and applies <see cref="AgentIssueGate" />. Every failure ends as
    /// "not eligible" with its reason; nothing is thrown for a read that goes wrong.</summary>
    public static class AgentIssueIntake
    {
        /// <summary>Checks issue <paramref name="number" /> of <paramref name="repository" />. The board is read only when the
        /// issue could pass the gate, so a rejected, running or unlabelled issue costs one request, not a
        /// paged board read. <paramref name="issueToken" /> is optional for a public repository;
        /// <paramref name="projectToken" /> needs read access to the project.</summary>
        public static async Task<AgentIssueIntakeResult> CheckAsync(
            string repository,
            int number,
            string projectId,
            string projectToken,
            string? issueToken = null,
            HttpClient? httpClient = null,
            CancellationToken cancellationToken = default)
        {
            var read = await GitHubIssueReader.ReadAsync(repository, number, issueToken, httpClient, cancellationToken).ConfigureAwait(false);
            if (!read.Success)
            {
                return new AgentIssueIntakeResult(new GateDecision(GateReasons.IssueUnreadable, null), null, read.Error);
            }

            var issue = read.Issue!;
            var early = AgentIssueGate.Evaluate(issue, AgentIssueGate.ReadyStatus);
            if (!early.Eligible)
            {
                return new AgentIssueIntakeResult(early, issue, null);
            }

            var board = await ProjectBoardReader.ReadStatusAsync(repository, number, projectId, projectToken, httpClient, cancellationToken).ConfigureAwait(false);
            var status = board.Outcome == BoardReadOutcome.Found ? board.Status : null;
            var note = board.Error ?? (board.Outcome == BoardReadOutcome.NotOnBoard ? "The issue is not on the project board."
                : status == null ? "The issue's card has no Status." : null);
            return new AgentIssueIntakeResult(AgentIssueGate.Evaluate(issue, status), issue, note);
        }
    }
}
