using System;
using System.Collections.Generic;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class OpenSourceAppSurveyTests
    {
        [Fact]
        public void AppPairSurvey_EvaluatesViability_AccordingTo3Criteria()
        {
            var v1 = new AppVersionSurveyRecord
            {
                Version = "1.0.0",
                Downloaded = true,
                Launched = true,
                Settled = true,
                SettlePassCount = 2,
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 100,
                    EmptyAutomationIdCount = 45,
                    EmptyAutomationIdFraction = 0.45,
                },
            };

            var v2 = new AppVersionSurveyRecord
            {
                Version = "2.0.0",
                Downloaded = true,
                Launched = true,
                Settled = true,
                SettlePassCount = 2,
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 120,
                    EmptyAutomationIdCount = 50,
                    EmptyAutomationIdFraction = 50.0 / 120.0,
                },
            };

            var diffWithRemovedIds = new ApplicationTreeDiffResult
            {
                HasStructuralDrift = true,
                DriftSignal = "⚡ Drift (+20 nodes)",
                Details = new List<string>
                {
                    "Total nodes changed: 100 → 120 (+20)",
                    "AutomationIds removed in 2025: btnOldAction, tabLegacySettings",
                },
            };

            var (isViable, reason) = OpenSourceAppViabilityEvaluator.Evaluate(v1, v2, diffWithRemovedIds);
            Assert.True(isViable);
            Assert.Contains("verified removed AutomationIds", reason);
            Assert.Contains("45.0%", reason);
        }

        [Fact]
        public void AppPairSurvey_EvaluatesViability_RejectsUnsettledApps()
        {
            var v1 = new AppVersionSurveyRecord
            {
                Version = "1.0.0",
                Downloaded = true,
                Launched = true,
                Settled = false,
                SettlePassCount = 5,
                SettleTelemetry = "Pass 1: 12 -> Pass 2: 80 -> Pass 3: 150 -> Pass 4: 200 -> Pass 5: 250 (did not stabilize)",
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 250,
                    EmptyAutomationIdFraction = 0.50,
                },
            };

            var v2 = new AppVersionSurveyRecord
            {
                Version = "2.0.0",
                Downloaded = true,
                Launched = true,
                Settled = true,
                SettlePassCount = 2,
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 260,
                    EmptyAutomationIdFraction = 0.50,
                },
            };

            var diff = new ApplicationTreeDiffResult { HasStructuralDrift = true };
            var (isViable, reason) = OpenSourceAppViabilityEvaluator.Evaluate(v1, v2, diff);
            Assert.False(isViable);
            Assert.Contains("did not settle cleanly", reason);
        }

        [Fact]
        public void AppPairSurvey_EvaluatesViability_RejectsLowEmptyIdFraction()
        {
            var v1 = new AppVersionSurveyRecord
            {
                Version = "1.0.0",
                Downloaded = true,
                Launched = true,
                Settled = true,
                SettlePassCount = 2,
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 100,
                    EmptyAutomationIdCount = 5,
                    EmptyAutomationIdFraction = 0.05, // 5% empty -> 95% well identified
                },
            };

            var v2 = new AppVersionSurveyRecord
            {
                Version = "2.0.0",
                Downloaded = true,
                Launched = true,
                Settled = true,
                SettlePassCount = 2,
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 110,
                    EmptyAutomationIdCount = 6,
                    EmptyAutomationIdFraction = 0.054,
                },
            };

            var diff = new ApplicationTreeDiffResult
            {
                HasStructuralDrift = true,
                Details = new List<string> { "AutomationIds removed: btnAction" },
            };

            var (isViable, reason) = OpenSourceAppViabilityEvaluator.Evaluate(v1, v2, diff);
            Assert.False(isViable);
            Assert.Contains("Empty AutomationId fraction too low", reason);
        }

        [Fact]
        public void OpenSourceAppSurveyReport_GeneratesMarkdownWithReportFormatting()
        {
            var report = new OpenSourceAppSurveyReport
            {
                Timestamp = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero),
                Pairs = new List<AppPairSurveyRecord>
                {
                    new()
                    {
                        AppName = "ShareX",
                        Toolkit = "WinForms",
                        V1 = new AppVersionSurveyRecord
                        {
                            Version = "v16.1.0",
                            Launched = true,
                            Settled = true,
                            SettlePassCount = 2,
                            Metrics = new ApplicationTreeMetrics
                            {
                                TotalNodes = 85,
                                EmptyAutomationIdCount = 35,
                                EmptyAutomationIdFraction = 35.0 / 85.0,
                            },
                        },
                        V2 = new AppVersionSurveyRecord
                        {
                            Version = "v21.0.0",
                            Launched = true,
                            Settled = true,
                            SettlePassCount = 2,
                            Metrics = new ApplicationTreeMetrics
                            {
                                TotalNodes = 140,
                                EmptyAutomationIdCount = 60,
                                EmptyAutomationIdFraction = 60.0 / 140.0,
                            },
                        },
                        Diff = new ApplicationTreeDiffResult
                        {
                            HasStructuralDrift = true,
                            DriftSignal = "⚡ Drift (+55 nodes)",
                            Details = new List<string>
                            {
                                "AutomationIds removed in 2025: btnLegacyUpload",
                            },
                        },
                        IsViableBenchmarkTarget = true,
                        ViabilityReason = "Viable target with removed IDs and 42.9% empty IDs",
                    },
                },
            };

            var md = report.ToMarkdownSummary();
            Assert.Contains("Open-Source App Version Pairs Benchmark Survey", md);
            Assert.Contains("**ShareX**", md);
            Assert.Contains("`v16.1.0`", md);
            Assert.Contains("`v21.0.0`", md);
            Assert.Contains("2p settle", md);
            Assert.Contains("✅ **Viable**", md);
            Assert.Contains("41%", md); // 35/85 = 41.2% -> 41% formatted with 0 decimals
        }

        [Fact]
        public void OpenSourceAppSurveySerializer_RoundTripsJson()
        {
            var report = new OpenSourceAppSurveyReport
            {
                Timestamp = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero),
                Pairs = new List<AppPairSurveyRecord>
                {
                    new()
                    {
                        AppName = "HandBrake",
                        Toolkit = "WPF",
                        V1 = new AppVersionSurveyRecord
                        {
                            Version = "1.8.2",
                            DownloadUrl = "https://github.com/HandBrake/HandBrake/releases/download/1.8.2/HandBrake-1.8.2-x86_64-Win_GUI.zip",
                            ExecutableRelativePath = "HandBrake.exe",
                            Downloaded = true,
                            Launched = true,
                            Settled = true,
                            SettlePassCount = 2,
                            Metrics = new ApplicationTreeMetrics
                            {
                                TotalNodes = 200,
                                MaxDepth = 6,
                                EmptyAutomationIdCount = 120,
                                EmptyAutomationIdFraction = 0.60,
                            },
                        },
                        V2 = new AppVersionSurveyRecord
                        {
                            Version = "1.11.2",
                            DownloadUrl = "https://github.com/HandBrake/HandBrake/releases/download/1.11.2/HandBrake-1.11.2-x86_64-Win_GUI.zip",
                            ExecutableRelativePath = "HandBrake.exe",
                            Downloaded = true,
                            Launched = true,
                            Settled = true,
                            SettlePassCount = 3,
                            Metrics = new ApplicationTreeMetrics
                            {
                                TotalNodes = 240,
                                MaxDepth = 7,
                                EmptyAutomationIdCount = 140,
                                EmptyAutomationIdFraction = 140.0 / 240.0,
                            },
                        },
                        Diff = new ApplicationTreeDiffResult
                        {
                            HasStructuralDrift = true,
                            DriftSignal = "⚡ Drift (+40 nodes)",
                        },
                        IsViableBenchmarkTarget = true,
                        ViabilityReason = "Viable target with 60.0% empty IDs in WPF",
                    },
                },
            };

            var json = OpenSourceAppSurveySerializer.ToJson(report);
            Assert.Contains("\"HandBrake\"", json);
            Assert.Contains("\"1.8.2\"", json);
            Assert.Contains("\"1.11.2\"", json);

            var roundtripped = OpenSourceAppSurveySerializer.FromJson(json);
            Assert.Single(roundtripped.Pairs);
            Assert.Equal("HandBrake", roundtripped.Pairs[0].AppName);
            Assert.Equal("1.8.2", roundtripped.Pairs[0].V1.Version);
            Assert.True(roundtripped.Pairs[0].IsViableBenchmarkTarget);
        }
    }
}
