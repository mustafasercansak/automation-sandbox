using System;



namespace LlmHealing

{

    public sealed class LlmHealingResult

    {

        public string ProviderName { get; set; } = "";

        public bool Success { get; set; }



        // The authoritative match: the opaque candidateId the model picked from the shortlist

        // it was shown. Resolve against that shortlist, not MatchedAutomationId - AutomationId

        // can legitimately be empty (the exact case this framework exists to heal) or duplicated

        // across nodes, so it's informational/debug only, not a lookup key.

        public string? MatchedCandidateId { get; set; }

        public string? MatchedAutomationId { get; set; }

        public double Confidence { get; set; }

        public string Reasoning { get; set; } = "";

        public string? ErrorMessage { get; set; }

        public TimeSpan Elapsed { get; set; }

    }

}
