using System;
using System.Collections.Generic;
using AutomationSandbox.SelfHealing;
using Xunit;

namespace ScenarioRunner
{
    public class HealingReportSummaryTests
    {
        [Fact]
        public void Summarize_EmptyDocument_ReturnsZeroedSummaryWithoutDividingByZero()
        {
            var summary = HealingReportSummary.Summarize(new HealingReportDocument());

            Assert.Equal(0, summary.TotalEvents);
            Assert.Equal(0, summary.AcceptedCount);
            Assert.Equal(0, summary.DeclinedCount);
            Assert.Equal(0.0, summary.AcceptanceRate);
            Assert.Empty(summary.CountsByOutcome);
            Assert.Empty(summary.LocatorsWithProviderErrors);
        }

        [Fact]
        public void Summarize_ThrowsOnNullDocument()
        {
            Assert.Throws<ArgumentNullException>(() => HealingReportSummary.Summarize(null!));
        }

        [Fact]
        public void Summarize_CountsAcceptedAndDeclinedEvents()
        {
            var document = new HealingReportDocument
            {
                Events = new List<HealingReportEntry>
                {
                    new HealingReportEntry { LocatorKey = "a", Source = "heuristic", Outcome = HealingReportEntry.AcceptedOutcome },
                    new HealingReportEntry { LocatorKey = "b", Source = "heuristic", Outcome = HealingReportEntry.AmbiguousOutcome },
                    new HealingReportEntry { LocatorKey = "c", Source = "heuristic", Outcome = HealingReportEntry.LowEvidenceOutcome },
                },
            };

            var summary = HealingReportSummary.Summarize(document);

            Assert.Equal(3, summary.TotalEvents);
            Assert.Equal(1, summary.AcceptedCount);
            Assert.Equal(2, summary.DeclinedCount);
            Assert.Equal(1.0 / 3.0, summary.AcceptanceRate, 6);
        }

        [Fact]
        public void Summarize_NullOutcome_IsGroupedUnderAcceptedOutcome_NotUnspecified()
        {
            // A null Outcome means "this build predates the Outcome field and recorded
            // accepted heals only" - HealingReportEntry.IsAccepted treats it as accepted,
            // and the outcome grouping must agree rather than silently reclassifying
            // legacy history as "unspecified".
            var document = new HealingReportDocument
            {
                Events = new List<HealingReportEntry>
                {
                    new HealingReportEntry { LocatorKey = "legacy", Source = "heuristic", Outcome = null },
                },
            };

            var summary = HealingReportSummary.Summarize(document);

            Assert.Equal(1, summary.AcceptedCount);
            Assert.Equal(1, summary.CountsByOutcome[HealingReportEntry.AcceptedOutcome]);
            Assert.False(summary.CountsByOutcome.ContainsKey(HealingReportEntry.UnspecifiedOutcome));
        }

        [Fact]
        public void Summarize_CountsAcceptedWithLlm_WhenSourceIsNotTheHeuristicLiteral()
        {
            var document = new HealingReportDocument
            {
                Events = new List<HealingReportEntry>
                {
                    new HealingReportEntry { LocatorKey = "a", Source = "heuristic", Outcome = HealingReportEntry.AcceptedOutcome },
                    new HealingReportEntry { LocatorKey = "b", Source = "Gemini", Outcome = HealingReportEntry.AcceptedOutcome },
                    new HealingReportEntry { LocatorKey = "c", Source = "llm", Outcome = HealingReportEntry.AcceptedOutcome },
                    // Declined entries never count toward AcceptedWithLlmCount even with a non-heuristic Source.
                    new HealingReportEntry { LocatorKey = "d", Source = "Gemini", Outcome = HealingReportEntry.NoConsensusOutcome },
                },
            };

            var summary = HealingReportSummary.Summarize(document);

            Assert.Equal(3, summary.AcceptedCount);
            Assert.Equal(2, summary.AcceptedWithLlmCount);
        }

        [Fact]
        public void Summarize_CountsDivergedFromHeuristic_OnlyAmongAcceptedEvents()
        {
            var document = new HealingReportDocument
            {
                Events = new List<HealingReportEntry>
                {
                    new HealingReportEntry { LocatorKey = "a", Source = "Gemini", Outcome = HealingReportEntry.AcceptedOutcome, DivergedFromHeuristic = true },
                    // A declined entry can still carry DivergedFromHeuristic = true from an earlier
                    // heuristic comparison; it must not inflate the count since it was never applied.
                    new HealingReportEntry { LocatorKey = "b", Source = "Gemini", Outcome = HealingReportEntry.NoConsensusOutcome, DivergedFromHeuristic = true },
                },
            };

            var summary = HealingReportSummary.Summarize(document);

            Assert.Equal(1, summary.DivergedFromHeuristicCount);
        }

        [Fact]
        public void Summarize_CollectsDistinctSortedLocatorsWithProviderErrors()
        {
            var document = new HealingReportDocument
            {
                Events = new List<HealingReportEntry>
                {
                    new HealingReportEntry
                    {
                        LocatorKey = "zebra",
                        Source = "heuristic",
                        Outcome = HealingReportEntry.ProviderErrorOutcome,
                        ProviderErrors = new Dictionary<string, string> { ["Gemini"] = "timeout" },
                    },
                    new HealingReportEntry
                    {
                        LocatorKey = "alpha",
                        Source = "heuristic",
                        Outcome = HealingReportEntry.ProviderErrorOutcome,
                        ProviderErrors = new Dictionary<string, string> { ["Gemini"] = "timeout" },
                    },
                    // Same locator failing twice must appear once, not twice.
                    new HealingReportEntry
                    {
                        LocatorKey = "alpha",
                        Source = "heuristic",
                        Outcome = HealingReportEntry.ProviderErrorOutcome,
                        ProviderErrors = new Dictionary<string, string> { ["Claude"] = "rate limited" },
                    },
                    new HealingReportEntry { LocatorKey = "clean", Source = "heuristic", Outcome = HealingReportEntry.AcceptedOutcome },
                },
            };

            var summary = HealingReportSummary.Summarize(document);

            Assert.Equal(new[] { "alpha", "zebra" }, summary.LocatorsWithProviderErrors);
        }
    }
}
