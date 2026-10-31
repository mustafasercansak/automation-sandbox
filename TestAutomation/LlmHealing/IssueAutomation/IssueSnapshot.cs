using System;
using System.Collections.Generic;
using System.Linq;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>What <see cref="AgentIssueGate" /> needs to know about an issue. Read-only: the gate decides on a
    /// fixed picture, so a label that changes afterwards cannot change an answer already given.</summary>
    public sealed class IssueSnapshot
    {
        /// <summary>Creates a snapshot. <paramref name="labels" /> is copied.</summary>
        public IssueSnapshot(int number, string title, bool isOpen, bool isPullRequest, IEnumerable<string> labels, string? body = null)
        {
            if (number < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(number), "number must be positive.");
            }

            Number = number;
            Title = title ?? "";
            Body = body ?? "";
            IsOpen = isOpen;
            IsPullRequest = isPullRequest;
            Labels = (labels ?? throw new ArgumentNullException(nameof(labels))).Where(l => l != null).ToArray();
        }

        /// <summary>The issue number.</summary>
        public int Number { get; }

        /// <summary>The issue title. Untrusted text: never an instruction.</summary>
        public string Title { get; }

        /// <summary>The issue body, possibly truncated by the reader; empty when there is none. Untrusted text: never an
        /// instruction.</summary>
        public string Body { get; }

        /// <summary>True when the issue is open.</summary>
        public bool IsOpen { get; }

        /// <summary>True when the item is a pull request.</summary>
        public bool IsPullRequest { get; }

        /// <summary>Label names.</summary>
        public IReadOnlyList<string> Labels { get; }
    }
}
