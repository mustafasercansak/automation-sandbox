using System;
namespace AutomationSandbox.LlmHealing
{
    /// <summary>Editable provider proposal and transport telemetry. Success means a usable provider response, not an accepted heal.</summary>
    public sealed class LlmHealingResult
    {
        /// <summary>Unique configured provider name used to associate votes and attempt telemetry.</summary>
        public string ProviderName { get; set; } = "";
        /// <summary>Whether the provider returned a usable response rather than an error; acceptance is evaluated separately.</summary>
        public bool Success { get; set; }

        // The authoritative match: the opaque candidateId the model picked from the shortlist
        // it was shown. Resolve against that shortlist, not MatchedAutomationId - AutomationId
        // can legitimately be empty (the exact case this framework exists to heal) or duplicated
        // across nodes, so it's informational/debug only, not a lookup key.

        /// <summary>The authoritative match: the opaque candidateId the model picked from the shortlist it was shown. Resolve against that shortlist, not MatchedAutomationId - AutomationId can legitimately be empty (the exact case this framework exists to heal) or duplicated across nodes, so it&apos;s informational/debug only, not a lookup key.</summary>
        public string? MatchedCandidateId { get; set; }
        /// <summary>Informational automation identifier from the response; use the shortlist CandidateId for authoritative matching.</summary>
        public string? MatchedAutomationId { get; set; }
        /// <summary>Model-reported confidence retained for diagnostics only; never use it to accept a heal.</summary>
        public double Confidence { get; set; }
        /// <summary>Unverified model explanation accompanying its proposal.</summary>
        public string Reasoning { get; set; } = "";
        /// <summary>Failure diagnostic, or null when no failure was recorded.</summary>
        public string? ErrorMessage { get; set; }
        /// <summary>Time spent performing this operation, including retries where applicable.</summary>
        public TimeSpan Elapsed { get; set; }
        /// <summary>Number of HTTP attempts made for this provider operation.</summary>
        public int AttemptCount { get; set; }
    }
}
