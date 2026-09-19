using System;
using System.Collections.Generic;
using System.Linq;

namespace AutomationSandbox.SelfHealing
{
    // A CI/dashboard consumer wants a handful of numbers - accepted vs. declined, which
    // outcome dominated, whether any provider is failing - without parsing the HTML report
    // or writing its own LINQ over Events every time. HealingReportHtmlRenderer computes
    // similar aggregates inline for display; this exposes the same kind of aggregate as
    // reusable, testable data instead.
    /// <summary>Aggregate counts computed from a HealingReportDocument's events, for CI gates and dashboards that need numbers without parsing the HTML report.</summary>
    public sealed class HealingReportSummary
    {
        /// <summary>Total recorded events, accepted and declined.</summary>
        public int TotalEvents { get; set; }

        /// <summary>Events accepted under HealingReportEntry.IsAccepted, including legacy entries with no recorded Outcome.</summary>
        public int AcceptedCount { get; set; }

        /// <summary>Accepted events whose Source was not the literal &quot;heuristic&quot; - an LLM provider name or a legacy LLM label.</summary>
        public int AcceptedWithLlmCount { get; set; }

        /// <summary>Events not accepted: ambiguous, low-evidence, no-consensus, and every other decline reason.</summary>
        public int DeclinedCount { get; set; }

        /// <summary>Accepted events whose LLM pick diverged from the heuristic winner (#6).</summary>
        public int DivergedFromHeuristicCount { get; set; }

        /// <summary>Event counts grouped by HealingReportEntry.Outcome; a null Outcome (legacy accepted-only entries) is grouped under HealingReportEntry.AcceptedOutcome, matching HealingReportEntry.IsAccepted&apos;s own treatment of null.</summary>
        public IReadOnlyDictionary<string, int> CountsByOutcome { get; set; } = new Dictionary<string, int>();

        /// <summary>Distinct LocatorKey values with at least one recorded provider error, in ordinal order.</summary>
        public IReadOnlyList<string> LocatorsWithProviderErrors { get; set; } = Array.Empty<string>();

        /// <summary>Fraction of events accepted, from zero to one; zero when there are no events rather than dividing by zero.</summary>
        public double AcceptanceRate => TotalEvents == 0 ? 0.0 : (double)AcceptedCount / TotalEvents;

        /// <summary>Computes a summary from every event recorded in the document, in a single pass.</summary>
        public static HealingReportSummary Summarize(HealingReportDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var countsByOutcome = new Dictionary<string, int>(StringComparer.Ordinal);
            var locatorsWithProviderErrors = new SortedSet<string>(StringComparer.Ordinal);
            var accepted = 0;
            var acceptedWithLlm = 0;
            var diverged = 0;

            foreach (var e in document.Events)
            {
                // A null Outcome means "this build predates the Outcome field and recorded
                // accepted heals only" (HealingReportEntry.IsAccepted treats it the same way),
                // not "unspecified" - HealingReportEntry.UnspecifiedOutcome is a distinct,
                // deliberately-chosen label for a different situation.
                var outcome = e.Outcome ?? HealingReportEntry.AcceptedOutcome;
                countsByOutcome[outcome] = countsByOutcome.TryGetValue(outcome, out var count) ? count + 1 : 1;

                if (e.IsAccepted)
                {
                    accepted++;
                    if (!string.IsNullOrEmpty(e.Source) && !string.Equals(e.Source, "heuristic", StringComparison.Ordinal))
                    {
                        acceptedWithLlm++;
                    }

                    if (e.DivergedFromHeuristic)
                    {
                        diverged++;
                    }
                }

                if (e.ProviderErrors != null && e.ProviderErrors.Count > 0 && !string.IsNullOrEmpty(e.LocatorKey))
                {
                    locatorsWithProviderErrors.Add(e.LocatorKey);
                }
            }

            return new HealingReportSummary
            {
                TotalEvents = document.Events.Count,
                AcceptedCount = accepted,
                AcceptedWithLlmCount = acceptedWithLlm,
                DeclinedCount = document.Events.Count - accepted,
                DivergedFromHeuristicCount = diverged,
                CountsByOutcome = countsByOutcome,
                LocatorsWithProviderErrors = locatorsWithProviderErrors.ToList(),
            };
        }
    }
}
