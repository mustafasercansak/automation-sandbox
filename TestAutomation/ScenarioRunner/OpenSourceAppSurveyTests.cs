using System;
using System.Collections.Generic;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class OpenSourceAppSurveyTests
    {
        [Fact]
        public void AppChainSurvey_EvaluatesMultipleHops_AndDeduplicatesBrokenLocators()
        {
            var v1 = new AppVersionSurveyRecord
            {
                Version = "1.0.0",
                Downloaded = true,
                Launched = true,
                Settled = true,
                WindowTitle = "App v1.0.0",
                RootClassName = "MainForm",
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
                WindowTitle = "App v2.0.0",
                RootClassName = "MainForm",
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 115,
                    EmptyAutomationIdCount = 50,
                    EmptyAutomationIdFraction = 50.0 / 115.0,
                },
            };

            var v3 = new AppVersionSurveyRecord
            {
                Version = "3.0.0",
                Downloaded = true,
                Launched = true,
                Settled = true,
                WindowTitle = "App v3.0.0",
                RootClassName = "MainForm",
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 130,
                    EmptyAutomationIdCount = 55,
                    EmptyAutomationIdFraction = 55.0 / 130.0,
                },
            };

            var diff1to2 = new ApplicationTreeDiffResult
            {
                HasStructuralDrift = true,
                DriftSignal = "⚡ Drift (+15 nodes)",
                Details = new List<string>
                {
                    "AutomationIds removed in 2025: btnSaveOld, btnExportLegacy",
                },
            };

            var diff2to3 = new ApplicationTreeDiffResult
            {
                HasStructuralDrift = true,
                DriftSignal = "⚡ Drift (+15 nodes)",
                Details = new List<string>
                {
                    "AutomationIds removed in 2025: btnExportLegacy, tabPreferencesOld",
                },
            };

            var hop1 = OpenSourceAppViabilityEvaluator.EvaluateHop(v1, v2, diff1to2);
            var hop2 = OpenSourceAppViabilityEvaluator.EvaluateHop(v2, v3, diff2to3);

            Assert.True(hop1.IsViableHop);
            Assert.Equal(2, hop1.RemovedAutomationIds.Count);
            Assert.Contains("btnSaveOld", hop1.RemovedAutomationIds);
            Assert.Contains("btnExportLegacy", hop1.RemovedAutomationIds);

            Assert.True(hop2.IsViableHop);
            Assert.Equal(2, hop2.RemovedAutomationIds.Count);
            Assert.Contains("btnExportLegacy", hop2.RemovedAutomationIds);
            Assert.Contains("tabPreferencesOld", hop2.RemovedAutomationIds);

            var chain = new AppChainSurveyRecord
            {
                AppName = "SampleApp",
                Toolkit = "WinForms",
                Versions = new List<AppVersionSurveyRecord> { v1, v2, v3 },
                Hops = new List<AppHopSurveyRecord> { hop1, hop2 },
            };

            OpenSourceAppViabilityEvaluator.EvaluateChain(chain);

            Assert.True(chain.IsViableBenchmarkTarget);
            Assert.Equal(4, chain.TotalCumulativeBrokenLocatorsCount);
            Assert.Equal(3, chain.TotalDistinctBrokenLocatorsCount);
            Assert.Equal(new[] { "btnExportLegacy", "btnSaveOld", "tabPreferencesOld" }, chain.DistinctRemovedAutomationIds);
        }

        [Fact]
        public void AppHopSurvey_FlagsSuspectCapture_OnOrderOfMagnitudeNodeDrop()
        {
            var v1 = new AppVersionSurveyRecord
            {
                Version = "1.8.2",
                Downloaded = true,
                Launched = true,
                Settled = true,
                WindowTitle = "HandBrake",
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 149,
                    EmptyAutomationIdCount = 105,
                    EmptyAutomationIdFraction = 105.0 / 149.0,
                },
            };

            var v2 = new AppVersionSurveyRecord
            {
                Version = "1.11.2",
                Downloaded = true,
                Launched = true,
                Settled = true,
                WindowTitle = "shellView",
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 7, // 149 -> 7 is a ~21.3x drop (splash/unhydrated shell capture)
                    EmptyAutomationIdCount = 2,
                    EmptyAutomationIdFraction = 2.0 / 7.0,
                },
            };

            var diff = new ApplicationTreeDiffResult
            {
                HasStructuralDrift = true,
                DriftSignal = "⚡ Drift (-142 nodes)",
                Details = new List<string>
                {
                    "Total nodes changed: 149 → 7 (-142)",
                    "AutomationIds removed: shellView, SystemMenuBar, MainViewModel",
                },
            };

            var hop = OpenSourceAppViabilityEvaluator.EvaluateHop(v1, v2, diff);

            Assert.True(hop.IsSuspectCapture);
            Assert.False(hop.IsViableHop);
            Assert.Contains("Order-of-magnitude node drop: 149 → 7", hop.SuspectReason);
            Assert.Contains("21.3x decrease", hop.SuspectReason);
        }

        [Fact]
        public void AppHopSurvey_EvaluatesViability_RejectsUnsettledApps()
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
            var hop = OpenSourceAppViabilityEvaluator.EvaluateHop(v1, v2, diff);

            Assert.False(hop.IsViableHop);
            Assert.Contains("did not settle cleanly", hop.ViabilityReason);
        }

        [Fact]
        public void AppHopSurvey_EvaluatesViability_RejectsLowEmptyIdFraction()
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

            var hop = OpenSourceAppViabilityEvaluator.EvaluateHop(v1, v2, diff);

            Assert.False(hop.IsViableHop);
            Assert.Contains("Empty AutomationId fraction too low", hop.ViabilityReason);
        }

        [Fact]
        public void AppVersionSurveyRecord_TracksWindowDiagnostics_AndHydrationTimeout()
        {
            var v = new AppVersionSurveyRecord
            {
                Version = "v21.0.0",
                Downloaded = true,
                Launched = true,
                Settled = true,
                HydrationTimedOut = false,
                SettlePassCount = 2,
                WindowTitle = "ShareX - Screen Capture",
                RootClassName = "MainForm",
                RootControlType = "Window",
                WindowSelectionReason = "Selected window 'ShareX - Screen Capture' (140 nodes) over 2 candidates: [Title='ShareX - Screen Capture', Nodes=140], [Title='', Nodes=5]",
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 140,
                    EmptyAutomationIdCount = 60,
                    EmptyAutomationIdFraction = 60.0 / 140.0,
                },
            };

            Assert.Equal("ShareX - Screen Capture", v.WindowTitle);
            Assert.Equal("MainForm", v.RootClassName);
            Assert.False(v.HydrationTimedOut);
            Assert.Contains("140 nodes", v.WindowSelectionReason);
        }

        [Fact]
        public void OpenSourceAppSurveyReport_GeneratesMarkdownWithHopsAndBrokenLocators()
        {
            var report = new OpenSourceAppSurveyReport
            {
                Timestamp = new DateTimeOffset(2026, 8, 15, 14, 0, 0, TimeSpan.Zero),
                Chains = new List<AppChainSurveyRecord>
                {
                    new()
                    {
                        AppName = "ShareX",
                        Toolkit = "WinForms",
                        Versions = new List<AppVersionSurveyRecord>
                        {
                            new()
                            {
                                Version = "v16.1.0",
                                Launched = true,
                                Settled = true,
                                SettlePassCount = 2,
                                WindowTitle = "ShareX",
                                RootClassName = "MainForm",
                                Metrics = new ApplicationTreeMetrics
                                {
                                    TotalNodes = 85,
                                    EmptyAutomationIdCount = 35,
                                    EmptyAutomationIdFraction = 35.0 / 85.0,
                                },
                                WindowSelectionReason = "Single window",
                            },
                            new()
                            {
                                Version = "v17.0.0",
                                Launched = true,
                                Settled = true,
                                SettlePassCount = 2,
                                WindowTitle = "ShareX",
                                RootClassName = "MainForm",
                                Metrics = new ApplicationTreeMetrics
                                {
                                    TotalNodes = 95,
                                    EmptyAutomationIdCount = 40,
                                    EmptyAutomationIdFraction = 40.0 / 95.0,
                                },
                                WindowSelectionReason = "Single window",
                            },
                            new()
                            {
                                Version = "v21.0.0",
                                Launched = true,
                                Settled = true,
                                SettlePassCount = 2,
                                WindowTitle = "ShareX",
                                RootClassName = "MainForm",
                                Metrics = new ApplicationTreeMetrics
                                {
                                    TotalNodes = 140,
                                    EmptyAutomationIdCount = 60,
                                    EmptyAutomationIdFraction = 60.0 / 140.0,
                                },
                                WindowSelectionReason = "Selected from 2 candidates",
                            },
                        },
                        Hops = new List<AppHopSurveyRecord>
                        {
                            new()
                            {
                                FromVersion = "v16.1.0",
                                ToVersion = "v17.0.0",
                                Diff = new ApplicationTreeDiffResult { HasStructuralDrift = true, DriftSignal = "⚡ Drift (+10 nodes)" },
                                RemovedLocators = new List<SurveyLocatorElementRecord> { new() { AutomationId = "btnLegacyUpload1", ControlType = "Button" } },
                                IsViableHop = true,
                                ViabilityReason = "1 removed ID",
                            },
                            new()
                            {
                                FromVersion = "v17.0.0",
                                ToVersion = "v21.0.0",
                                Diff = new ApplicationTreeDiffResult { HasStructuralDrift = true, DriftSignal = "⚡ Drift (+45 nodes)" },
                                RemovedLocators = new List<SurveyLocatorElementRecord>
                                {
                                    new() { AutomationId = "btnLegacyUpload2", ControlType = "Button" },
                                    new() { AutomationId = "btnLegacyUpload1", ControlType = "Button" },
                                },
                                IsViableHop = true,
                                ViabilityReason = "2 removed IDs",
                            },
                        },
                        DistinctRemovedAutomationIds = new List<string> { "btnLegacyUpload1", "btnLegacyUpload2" },
                        IsViableBenchmarkTarget = true,
                        BenchmarkRecommendation = "Viable target: 2 distinct broken locators across 2 viable hops",
                    },
                },
            };

            var md = report.ToMarkdownSummary();

            Assert.Contains("Open-Source App Version Chains Benchmark Survey", md);
            Assert.Contains("`ShareX` Version Chain (WinForms)", md);
            Assert.Contains("Distinct Broken Locators (Deduplicated):** **2**", md);
            Assert.Contains("Cumulative Broken Locators:** 3", md);
            Assert.Contains("`v16.1.0` → `v17.0.0`", md);
            Assert.Contains("`v17.0.0` → `v21.0.0`", md);
            Assert.Contains("`btnLegacyUpload1`", md);
            Assert.Contains("`btnLegacyUpload2`", md);
            Assert.Contains("Window Diagnostics & Capture-Readiness", md);
        }

        [Fact]
        public void OpenSourceAppSurveySerializer_RoundTripsJson()
        {
            var report = new OpenSourceAppSurveyReport
            {
                Timestamp = new DateTimeOffset(2026, 8, 15, 14, 0, 0, TimeSpan.Zero),
                Chains = new List<AppChainSurveyRecord>
                {
                    new()
                    {
                        AppName = "HandBrake",
                        Toolkit = "WPF",
                        Versions = new List<AppVersionSurveyRecord>
                        {
                            new()
                            {
                                Version = "1.8.2",
                                DownloadUrl = "https://github.com/HandBrake/HandBrake/releases/download/1.8.2/HandBrake-1.8.2-x86_64-Win_GUI.zip",
                                ExecutableRelativePath = "HandBrake.exe",
                                Downloaded = true,
                                Launched = true,
                                Settled = true,
                                SettlePassCount = 2,
                                WindowTitle = "HandBrake",
                                RootClassName = "Window",
                                Metrics = new ApplicationTreeMetrics
                                {
                                    TotalNodes = 149,
                                    EmptyAutomationIdCount = 105,
                                    EmptyAutomationIdFraction = 105.0 / 149.0,
                                },
                            },
                        },
                        DistinctRemovedAutomationIds = new List<string> { "btnSourceLegacy" },
                        IsViableBenchmarkTarget = true,
                        BenchmarkRecommendation = "1 broken locator",
                    },
                },
            };

            var json = OpenSourceAppSurveySerializer.ToJson(report);
            Assert.Contains("\"HandBrake\"", json);
            Assert.Contains("\"1.8.2\"", json);
            Assert.Contains("\"btnSourceLegacy\"", json);

            var roundtripped = OpenSourceAppSurveySerializer.FromJson(json);
            Assert.Single(roundtripped.Chains);
            Assert.Equal("HandBrake", roundtripped.Chains[0].AppName);
            Assert.Equal("1.8.2", roundtripped.Chains[0].Versions[0].Version);
            Assert.Equal(1, roundtripped.Chains[0].TotalDistinctBrokenLocatorsCount);
        }

        [Fact]
        public void AppChainSurvey_ExcludesIdsFromSuspectHops_FromDistinctAndCumulativeTotals()
        {
            // Reproduces run 31883560197: HandBrake 1.8.2 (149 nodes) was a real capture, while 1.9.2
            // captured a 7-node update dialog. The ids "removed" across that hop describe the difference
            // between a window and a dialog, so they must not enter the benchmark dataset.
            var real = new AppVersionSurveyRecord
            {
                Version = "1.8.2",
                Downloaded = true,
                Launched = true,
                Settled = true,
                WindowTitle = "HandBrake",
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 149,
                    EmptyAutomationIdCount = 106,
                    EmptyAutomationIdFraction = 0.711,
                },
            };

            var dialog = new AppVersionSurveyRecord
            {
                Version = "1.9.2",
                Downloaded = true,
                Launched = true,
                Settled = true,
                HydrationTimedOut = true,
                WindowTitle = "Check for updates?",
                RootClassName = "#32770",
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 7,
                    EmptyAutomationIdCount = 1,
                    EmptyAutomationIdFraction = 0.143,
                },
            };

            var later = new AppVersionSurveyRecord
            {
                Version = "1.11.2",
                Downloaded = true,
                Launched = true,
                Settled = true,
                WindowTitle = "HandBrake",
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 160,
                    EmptyAutomationIdCount = 112,
                    EmptyAutomationIdFraction = 0.70,
                },
            };

            var suspectDiff = new ApplicationTreeDiffResult
            {
                HasStructuralDrift = true,
                DriftSignal = "⚡ Drift (-142 nodes)",
                Details = new List<string>
                {
                    "AutomationIds removed in 1.9.2: shellView, SystemMenuBar, MainViewModel",
                },
            };

            var newest = new AppVersionSurveyRecord
            {
                Version = "1.12.0",
                Downloaded = true,
                Launched = true,
                Settled = true,
                WindowTitle = "HandBrake",
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 171,
                    EmptyAutomationIdCount = 120,
                    EmptyAutomationIdFraction = 0.70,
                },
            };

            var riseOutDiff = new ApplicationTreeDiffResult
            {
                HasStructuralDrift = true,
                DriftSignal = "⚡ Drift (+153 nodes)",
                Details = new List<string>
                {
                    "AutomationIds removed in 1.11.2: errorText",
                },
            };

            var realDiff = new ApplicationTreeDiffResult
            {
                HasStructuralDrift = true,
                DriftSignal = "⚡ Drift (+11 nodes)",
                Details = new List<string>
                {
                    "AutomationIds removed in 1.12.0: btnQueueLegacy",
                },
            };

            var suspectHop = OpenSourceAppViabilityEvaluator.EvaluateHop(real, dialog, suspectDiff);
            var riseOutHop = OpenSourceAppViabilityEvaluator.EvaluateHop(dialog, later, riseOutDiff);
            var viableHop = OpenSourceAppViabilityEvaluator.EvaluateHop(later, newest, realDiff);

            Assert.True(suspectHop.IsSuspectCapture);
            Assert.False(suspectHop.IsViableHop);
            Assert.Equal(3, suspectHop.RemovedAutomationIds.Count);
            Assert.True(viableHop.IsViableHop);

            var chain = new AppChainSurveyRecord
            {
                AppName = "HandBrake",
                Toolkit = "WPF",
                Versions = new List<AppVersionSurveyRecord> { real, dialog, later, newest },
                Hops = new List<AppHopSurveyRecord> { suspectHop, riseOutHop, viableHop },
            };

            OpenSourceAppViabilityEvaluator.EvaluateChain(chain);

            // Only the hop between two sound captures survives: the drop into the dialog and the rise
            // back out of it are both excluded, in both directions.
            Assert.Equal(new[] { "btnQueueLegacy" }, chain.DistinctRemovedAutomationIds);
            Assert.Equal(1, chain.TotalDistinctBrokenLocatorsCount);
            Assert.Equal(1, chain.TotalCumulativeBrokenLocatorsCount);
            Assert.DoesNotContain("shellView", chain.DistinctRemovedAutomationIds);
            Assert.DoesNotContain("errorText", chain.DistinctRemovedAutomationIds);
        }

        [Fact]
        public void AppChainSurvey_ExcludesHopsRisingOutOfAnUnreliableCapture()
        {
            // Run 31885249314 node counts: 149, 149, 149, 23, 149. The 23-node capture was HandBrake's
            // "An Unknown Error has occurred." window. The drop into it was rejected, but the rise out of
            // it was accepted and harvested five scrollbar ids as if a maintainer had removed them.
            var chain = HandBrakeShapedChain(
                out var dropHop,
                out var riseHop,
                riseRemovedIds: "errorText, VerticalScrollBar, PART_LineUpButton, PageUp, PageDown");

            // Before the chain-level pass, the rising hop looks perfectly viable.
            Assert.False(dropHop.IsViableHop);
            Assert.True(riseHop.IsViableHop);
            Assert.Equal(5, riseHop.RemovedAutomationIds.Count);

            OpenSourceAppViabilityEvaluator.EvaluateChain(chain);

            var unreliable = chain.Versions.Single(v => v.IsUnreliableCapture);
            Assert.Equal("1.9.2", unreliable.Version);
            Assert.Contains("23 node(s)", unreliable.CaptureReliabilityReason);

            Assert.False(riseHop.IsViableHop);
            Assert.True(riseHop.IsSuspectCapture);
            Assert.Contains("1.9.2", riseHop.SuspectReason);

            Assert.Empty(chain.DistinctRemovedAutomationIds);
            Assert.Equal(0, chain.TotalCumulativeBrokenLocatorsCount);
            Assert.False(chain.IsViableBenchmarkTarget);
        }

        [Fact]
        public void AppChainSurvey_KeepsLegitimateGrowth_WhenNoCaptureIsFarBelowMedian()
        {
            // ShareX's real node counts. 34 -> 63 is a 1.9x spread across the chain and must stay viable;
            // the reliability rule has to separate organic growth from a capture that is not the app.
            var nodeCounts = new[] { 34, 34, 34, 59, 59, 59, 63, 63 };
            var versions = new List<AppVersionSurveyRecord>();
            for (var i = 0; i < nodeCounts.Length; i++)
            {
                versions.Add(new AppVersionSurveyRecord
                {
                    Version = $"v{i}",
                    Downloaded = true,
                    Launched = true,
                    Settled = true,
                    Metrics = new ApplicationTreeMetrics
                    {
                        TotalNodes = nodeCounts[i],
                        EmptyAutomationIdFraction = 0.49,
                    },
                });
            }

            var growthHop = OpenSourceAppViabilityEvaluator.EvaluateHop(
                versions[2],
                versions[3],
                new ApplicationTreeDiffResult
                {
                    HasStructuralDrift = true,
                    DriftSignal = "⚡ Drift (+25 nodes)",
                    Details = new List<string> { "AutomationIds removed in v3: pThumbnailView, lblThumbnailViewTip" },
                });

            var chain = new AppChainSurveyRecord
            {
                AppName = "ShareX",
                Toolkit = "WinForms",
                Versions = versions,
                Hops = new List<AppHopSurveyRecord> { growthHop },
            };

            OpenSourceAppViabilityEvaluator.EvaluateChain(chain);

            Assert.DoesNotContain(chain.Versions, v => v.IsUnreliableCapture);
            Assert.True(growthHop.IsViableHop);
            Assert.Equal(new[] { "lblThumbnailViewTip", "pThumbnailView" }, chain.DistinctRemovedAutomationIds);
        }

        [Fact]
        public void AppChainSurvey_MarksHydrationTimeoutUnreliable_EvenNearTheMedian()
        {
            var chain = new AppChainSurveyRecord
            {
                AppName = "StubApp",
                Toolkit = "WPF",
                Versions = new List<AppVersionSurveyRecord>
                {
                    new AppVersionSurveyRecord
                    {
                        Version = "1.0",
                        Downloaded = true,
                        Launched = true,
                        Settled = true,
                        Metrics = new ApplicationTreeMetrics { TotalNodes = 40, EmptyAutomationIdFraction = 0.5 },
                    },
                    new AppVersionSurveyRecord
                    {
                        Version = "1.1",
                        Downloaded = true,
                        Launched = true,
                        Settled = true,
                        HydrationTimedOut = true,
                        Metrics = new ApplicationTreeMetrics { TotalNodes = 38, EmptyAutomationIdFraction = 0.5 },
                    },
                },
            };

            OpenSourceAppViabilityEvaluator.EvaluateChain(chain);

            Assert.False(chain.Versions[0].IsUnreliableCapture);
            Assert.True(chain.Versions[1].IsUnreliableCapture);
            Assert.Contains("Hydration timed out", chain.Versions[1].CaptureReliabilityReason);
        }

        [Fact]
        public void MarkdownSummary_RendersUnreliableCapture_InReadinessTable()
        {
            var chain = HandBrakeShapedChain(out _, out _, riseRemovedIds: "errorText");
            OpenSourceAppViabilityEvaluator.EvaluateChain(chain);

            var report = new OpenSourceAppSurveyReport { Chains = { chain } };
            var md = report.ToMarkdownSummary();

            Assert.Contains("⚠️ Unreliable capture", md);
            Assert.Contains("chain median", md);
        }

        // 149, 149, 149, 23, 149 with a hop into and a hop out of the 23-node capture.
        private static AppChainSurveyRecord HandBrakeShapedChain(
            out AppHopSurveyRecord dropHop,
            out AppHopSurveyRecord riseHop,
            string riseRemovedIds)
        {
            AppVersionSurveyRecord Capture(string version, int nodes, double emptyFraction) =>
                new AppVersionSurveyRecord
                {
                    Version = version,
                    Downloaded = true,
                    Launched = true,
                    Settled = true,
                    Metrics = new ApplicationTreeMetrics
                    {
                        TotalNodes = nodes,
                        EmptyAutomationIdFraction = emptyFraction,
                    },
                };

            var v161 = Capture("1.6.1", 149, 0.711);
            var v173 = Capture("1.7.3", 149, 0.711);
            var v182 = Capture("1.8.2", 149, 0.711);
            var v192 = Capture("1.9.2", 23, 0.522);
            var v1112 = Capture("1.11.2", 149, 0.705);

            dropHop = OpenSourceAppViabilityEvaluator.EvaluateHop(
                v182,
                v192,
                new ApplicationTreeDiffResult
                {
                    HasStructuralDrift = true,
                    DriftSignal = "⚡ Drift (-126 nodes)",
                    Details = new List<string> { "AutomationIds removed in 1.9.2: shellView, MainViewModel" },
                });

            riseHop = OpenSourceAppViabilityEvaluator.EvaluateHop(
                v192,
                v1112,
                new ApplicationTreeDiffResult
                {
                    HasStructuralDrift = true,
                    DriftSignal = "⚡ Drift (+126 nodes)",
                    Details = new List<string> { $"AutomationIds removed in 1.11.2: {riseRemovedIds}" },
                });

            return new AppChainSurveyRecord
            {
                AppName = "HandBrake",
                Toolkit = "WPF",
                Versions = new List<AppVersionSurveyRecord> { v161, v173, v182, v192, v1112 },
                Hops = new List<AppHopSurveyRecord> { dropHop, riseHop },
            };
        }

        [Fact]
        public void MarkdownSummary_RendersLaunchDiagnostics_InReadinessTable()
        {
            var report = new OpenSourceAppSurveyReport
            {
                Chains =
                {
                    new AppChainSurveyRecord
                    {
                        AppName = "HandBrake",
                        Toolkit = "WPF",
                        Versions =
                        {
                            new AppVersionSurveyRecord
                            {
                                Version = "1.9.2",
                                Launched = true,
                                Settled = true,
                                SettlePassCount = 2,
                                WindowTitle = "HandBrake",
                                WindowSelectionReason = "Selected window 'HandBrake'",
                                LaunchDiagnostics =
                                {
                                    "DOTNET_ROLL_FORWARD=LatestMajor applied",
                                    "🪟 Dismissed startup dialog 'Check for updates?' via 'No'",
                                },
                                Metrics = new ApplicationTreeMetrics { TotalNodes = 149 },
                            },
                        },
                    },
                },
            };

            var md = report.ToMarkdownSummary();

            Assert.Contains("DOTNET_ROLL_FORWARD=LatestMajor applied", md);
            Assert.Contains("Dismissed startup dialog 'Check for updates?' via 'No'", md);
        }

        [Fact]
        public void VolatileLocatorClassifier_ExcludesReassignedNumericGridCells()
        {
            // Real captured elements from ShareX run 31888358528
            var cell1 = new SurveyLocatorElementRecord
            {
                AutomationId = "4255429698",
                ControlType = "Edit",
                Name = "Hotkey Row 4",
                AncestorPath = "Window['MainForm'] > Table['DataGridView'] > Unknown['Row 4']",
            };

            var header1 = new SurveyLocatorElementRecord
            {
                AutomationId = "429021196",
                ControlType = "Header",
                Name = "",
                AncestorPath = "Window['MainForm'] > Table['DataGridView'] > Unknown['Top Row']",
            };

            var header2 = new SurveyLocatorElementRecord
            {
                AutomationId = "4214081900",
                ControlType = "Header",
                Name = "",
                AncestorPath = "Window['MainForm'] > Table['DataGridView'] > Unknown['Top Row']",
            };

            Assert.True(VolatileLocatorClassifier.IsVolatile(cell1, out var reason1));
            Assert.Contains("Numeric ID (4255429698) in dynamic container", reason1);

            Assert.True(VolatileLocatorClassifier.IsVolatile(header1, out var reason2));
            Assert.Contains("Purely numeric ID (429021196) on container child element", reason2);

            Assert.True(VolatileLocatorClassifier.IsVolatile(header2, out var reason3));
            Assert.Contains("Purely numeric ID (4214081900) on container child element", reason3);
        }

        [Fact]
        public void VolatileLocatorClassifier_PreservesAuthoredControlNames()
        {
            // Real developer-authored WinForms controls from ShareX v15.0.0
            var panel = new SurveyLocatorElementRecord
            {
                AutomationId = "pThumbnailView",
                ControlType = "Pane",
                Name = "",
                ClassName = "Panel",
                AncestorPath = "Window['MainForm']",
            };

            var label = new SurveyLocatorElementRecord
            {
                AutomationId = "lblThumbnailViewTip",
                ControlType = "Text",
                Name = "Drag and drop images here...",
                ClassName = "Label",
                AncestorPath = "Window['MainForm'] > Pane['pThumbnailView']",
            };

            Assert.False(VolatileLocatorClassifier.IsVolatile(panel, out _));
            Assert.False(VolatileLocatorClassifier.IsVolatile(label, out _));
        }

        [Fact]
        public void VolatileLocatorClassifier_PreservesHandBrakeStableNamedIds()
        {
            // Stable developer-authored WPF identifiers in HandBrake
            var mainVm = new SurveyLocatorElementRecord
            {
                AutomationId = "MainViewModel",
                ControlType = "Custom",
                Name = "",
                ClassName = "MainViewModel",
                AncestorPath = "Window['HandBrake']",
            };

            var shell = new SurveyLocatorElementRecord
            {
                AutomationId = "shellView",
                ControlType = "Custom",
                Name = "",
                ClassName = "ShellView",
                AncestorPath = "Window['HandBrake']",
            };

            var grid = new SurveyLocatorElementRecord
            {
                AutomationId = "SourceGrid",
                ControlType = "Pane",
                Name = "",
                ClassName = "Grid",
                AncestorPath = "Window['HandBrake'] > Custom['MainViewModel']",
            };

            Assert.False(VolatileLocatorClassifier.IsVolatile(mainVm, out _));
            Assert.False(VolatileLocatorClassifier.IsVolatile(shell, out _));
            Assert.False(VolatileLocatorClassifier.IsVolatile(grid, out _));
        }

        [Fact]
        public void EvaluateHop_WithFullTrees_ExcludesVolatileGridIds_AndKeepsValidBrokenLocators()
        {
            // Tree 1 (v15.0.0): contains pThumbnailView, lblThumbnailViewTip, and 14 numeric DataGridView cells
            var gridTable1 = new UiElementInfo { ControlType = "Table", Name = "DataGridView", AutomationId = "dgvHotkeys" };
            for (var r = 0; r < 7; r++)
            {
                var row = new UiElementInfo { ControlType = "Unknown", Name = $"Row {r}" };
                row.Children.Add(new UiElementInfo { ControlType = "Edit", Name = $"Hotkey Row {r}", AutomationId = $"425542969{r}" });
                row.Children.Add(new UiElementInfo { ControlType = "Edit", Name = $"Description Row {r}", AutomationId = $"42902119{r}" });
                gridTable1.Children.Add(row);
            }

            var tree1 = new UiElementInfo { ControlType = "Window", Name = "MainForm", AutomationId = "MainForm" };
            var thumbnailPanel = new UiElementInfo { ControlType = "Pane", AutomationId = "pThumbnailView", Name = "" };
            thumbnailPanel.Children.Add(new UiElementInfo { ControlType = "Text", AutomationId = "lblThumbnailViewTip", Name = "Drag and drop..." });
            tree1.Children.Add(thumbnailPanel);
            tree1.Children.Add(gridTable1);

            // Tree 2 (v16.0.1): pThumbnailView removed, lblThumbnailViewTip removed, grid has new reassigned cell IDs
            var gridTable2 = new UiElementInfo { ControlType = "Table", Name = "DataGridView", AutomationId = "dgvHotkeys" };
            for (var r = 0; r < 7; r++)
            {
                var row = new UiElementInfo { ControlType = "Unknown", Name = $"Row {r}" };
                row.Children.Add(new UiElementInfo { ControlType = "Edit", Name = $"Hotkey Row {r}", AutomationId = $"525542969{r}" });
                row.Children.Add(new UiElementInfo { ControlType = "Edit", Name = $"Description Row {r}", AutomationId = $"52902119{r}" });
                gridTable2.Children.Add(row);
            }

            var tree2 = new UiElementInfo { ControlType = "Window", Name = "MainForm", AutomationId = "MainForm" };
            tree2.Children.Add(gridTable2);

            var v1 = new AppVersionSurveyRecord
            {
                Version = "v15.0.0",
                Downloaded = true,
                Launched = true,
                Settled = true,
                Metrics = new ApplicationTreeMetrics { TotalNodes = 50, EmptyAutomationIdFraction = 0.50 },
            };

            var v2 = new AppVersionSurveyRecord
            {
                Version = "v16.0.1",
                Downloaded = true,
                Launched = true,
                Settled = true,
                Metrics = new ApplicationTreeMetrics { TotalNodes = 50, EmptyAutomationIdFraction = 0.50 },
            };

            var diff = new ApplicationTreeDiffResult { HasStructuralDrift = true };
            var hop = OpenSourceAppViabilityEvaluator.EvaluateHop(v1, v2, diff, tree1, tree2);

            // Assert: Exactly 2 valid broken locators survive (pThumbnailView, lblThumbnailViewTip)
            Assert.True(hop.IsViableHop);
            Assert.Equal(2, hop.RemovedLocators.Count);
            Assert.Contains(hop.RemovedLocators, r => r.AutomationId == "pThumbnailView");
            Assert.Contains(hop.RemovedLocators, r => r.AutomationId == "lblThumbnailViewTip");

            // Assert: Exactly 28 grid cell IDs (14 removed from tree1 + 14 added to tree2) are classified as excluded
            Assert.Equal(28, hop.ExcludedLocators.Count);
            Assert.Equal(14, hop.ExcludedLocators.Count(ex => ex.AutomationId.StartsWith("42")));
            Assert.Equal(14, hop.ExcludedLocators.Count(ex => ex.AutomationId.StartsWith("52")));
            Assert.All(hop.ExcludedLocators, ex => Assert.True(ex.IsExcluded));
            Assert.All(hop.ExcludedLocators, ex => Assert.Contains("dynamic container", ex.ExclusionReason, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void OpenSourceAppSurveyReport_RendersAuditedExcludedVolatileIdentifiersTable()
        {
            var report = new OpenSourceAppSurveyReport
            {
                Timestamp = new DateTimeOffset(2026, 8, 15, 17, 0, 0, TimeSpan.Zero),
                Chains =
                {
                    new AppChainSurveyRecord
                    {
                        AppName = "ShareX",
                        Toolkit = "WinForms",
                        Versions =
                        {
                            new AppVersionSurveyRecord { Version = "v15.0.0", Launched = true, Settled = true, Metrics = new ApplicationTreeMetrics { TotalNodes = 50 } },
                            new AppVersionSurveyRecord { Version = "v16.0.1", Launched = true, Settled = true, Metrics = new ApplicationTreeMetrics { TotalNodes = 50 } },
                        },
                        Hops =
                        {
                            new AppHopSurveyRecord
                            {
                                FromVersion = "v15.0.0",
                                ToVersion = "v16.0.1",
                                IsViableHop = true,
                                RemovedLocators =
                                {
                                    new SurveyLocatorElementRecord { AutomationId = "pThumbnailView", ControlType = "Pane", Name = "", AncestorPath = "Window['MainForm']" },
                                    new SurveyLocatorElementRecord { AutomationId = "lblThumbnailViewTip", ControlType = "Text", Name = "Tip", AncestorPath = "Window['MainForm'] > Pane['pThumbnailView']" },
                                },
                                ExcludedLocators =
                                {
                                    new SurveyLocatorElementRecord
                                    {
                                        AutomationId = "4255429698",
                                        ControlType = "Edit",
                                        Name = "Hotkey Row 4",
                                        AncestorPath = "Window['MainForm'] > Table['DataGridView'] > Unknown['Row 4']",
                                        IsExcluded = true,
                                        ExclusionReason = "Numeric ID in dynamic container",
                                    },
                                },
                            },
                        },
                        DistinctRemovedAutomationIds = { "lblThumbnailViewTip", "pThumbnailView" },
                        IsViableBenchmarkTarget = true,
                    },
                },
            };

            var md = report.ToMarkdownSummary();

            Assert.Contains("### 🛡️ Audited Excluded Volatile Identifiers", md);
            Assert.Contains("`4255429698`", md);
            Assert.Contains("`Edit`", md);
            Assert.Contains("'Hotkey Row 4'", md);
            Assert.Contains("_Numeric ID in dynamic container_", md);
            Assert.Contains("Excluded Volatile Identifiers:** 1", md);
            Assert.Contains("Distinct Broken Locators (Deduplicated):** **2**", md);
        }
    }
}
