using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UiModel;
using Xunit;

namespace ScenarioRunner
{
    public class ApplicationSurveyTests
    {
        [Fact]
        public void TreeMetricsCalculator_ComputesCorrectMetrics()
        {
            var root = new UiElementInfo
            {
                ControlType = "Window",
                AutomationId = "MainWin",
                Name = "Main Window",
                BoundingRectangle = new BoundingRectangle(0, 0, 800, 600),
            };

            var btnWithId = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btnSave",
                Name = "Save",
                BoundingRectangle = new BoundingRectangle(10, 10, 100, 30),
            };

            var btnEmptyId = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "",
                Name = "Cancel",
                BoundingRectangle = new BoundingRectangle(120, 10, 100, 30),
            };

            var labelNeither = new UiElementInfo
            {
                ControlType = "Text",
                AutomationId = "",
                Name = "",
                BoundingRectangle = new BoundingRectangle(0, 0, 0, 0), // unusable
            };

            root.Children.Add(btnWithId);
            root.Children.Add(btnEmptyId);
            btnEmptyId.Children.Add(labelNeither);

            var metrics = TreeMetricsCalculator.Calculate(root);

            Assert.Equal(4, metrics.TotalNodes);
            Assert.Equal(2, metrics.MaxDepth);

            Assert.Equal(2, metrics.EmptyAutomationIdCount);
            Assert.Equal(0.50, metrics.EmptyAutomationIdFraction, 2);

            Assert.Equal(1, metrics.EmptyNameCount);
            Assert.Equal(0.25, metrics.EmptyNameFraction, 2);

            Assert.Equal(1, metrics.NeitherIdNorNameCount);
            Assert.Equal(0.25, metrics.NeitherIdNorNameFraction, 2);

            Assert.Equal(1, metrics.UnusableBoundingRectangleCount);
            Assert.Equal(0.25, metrics.UnusableBoundingRectangleFraction, 2);

            Assert.Equal(1, metrics.ControlTypeDistribution["Window"]);
            Assert.Equal(2, metrics.ControlTypeDistribution["Button"]);
            Assert.Equal(1, metrics.ControlTypeDistribution["Text"]);
        }

        [Fact]
        public void ApplicationSurveySerializer_RoundTripsJson()
        {
            var report = new ApplicationSurveyReport
            {
                ImageName = "windows-2022",
                Timestamp = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero),
                Applications = new List<ApplicationSurveyRecord>
                {
                    new()
                    {
                        AppName = "notepad",
                        Executable = "notepad.exe",
                        Launched = true,
                        DiscoveryElapsed = TimeSpan.FromSeconds(1.23),
                        Metrics = new ApplicationTreeMetrics
                        {
                            TotalNodes = 42,
                            MaxDepth = 4,
                            EmptyAutomationIdCount = 10,
                            EmptyAutomationIdFraction = 10.0 / 42.0,
                            EmptyNameCount = 5,
                            EmptyNameFraction = 5.0 / 42.0,
                            NeitherIdNorNameCount = 2,
                            NeitherIdNorNameFraction = 2.0 / 42.0,
                            UnusableBoundingRectangleCount = 3,
                            UnusableBoundingRectangleFraction = 3.0 / 42.0,
                            ControlTypeDistribution = new Dictionary<string, int>
                            {
                                ["Window"] = 1,
                                ["Edit"] = 1,
                                ["MenuItem"] = 40,
                            },
                        },
                        TreeJsonFileName = "notepad.json",
                    },
                    new()
                    {
                        AppName = "wordpad",
                        Executable = "wordpad.exe",
                        Launched = false,
                        LaunchError = "Executable not found on system path",
                        DiscoveryElapsed = TimeSpan.Zero,
                    },
                },
            };

            var json = ApplicationSurveySerializer.ToJson(report);
            Assert.Contains("\"ImageName\": \"windows-2022\"", json);
            Assert.Contains("\"notepad\"", json);
            Assert.Contains("\"wordpad\"", json);

            var roundtripped = ApplicationSurveySerializer.FromJson(json);
            Assert.Equal(report.ImageName, roundtripped.ImageName);
            Assert.Equal(2, roundtripped.Applications.Count);
            Assert.Equal("notepad", roundtripped.Applications[0].AppName);
            Assert.True(roundtripped.Applications[0].Launched);
            Assert.Equal(42, roundtripped.Applications[0].Metrics!.TotalNodes);
            Assert.False(roundtripped.Applications[1].Launched);
            Assert.Equal("Executable not found on system path", roundtripped.Applications[1].LaunchError);
        }

        [Fact]
        public void ApplicationSurveyReport_GeneratesAccurateMarkdownSummary()
        {
            var report = new ApplicationSurveyReport
            {
                ImageName = "windows-2022",
                Timestamp = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero),
                Applications = new List<ApplicationSurveyRecord>
                {
                    new()
                    {
                        AppName = "notepad",
                        Executable = "notepad.exe",
                        Launched = true,
                        DiscoveryElapsed = TimeSpan.FromSeconds(0.85),
                        Metrics = new ApplicationTreeMetrics
                        {
                            TotalNodes = 50,
                            MaxDepth = 3,
                            EmptyAutomationIdCount = 20,
                            EmptyAutomationIdFraction = 0.40,
                            EmptyNameCount = 5,
                            EmptyNameFraction = 0.10,
                            NeitherIdNorNameCount = 2,
                            NeitherIdNorNameFraction = 0.04,
                            UnusableBoundingRectangleCount = 1,
                            UnusableBoundingRectangleFraction = 0.02,
                            ControlTypeDistribution = new Dictionary<string, int> { ["MenuItem"] = 40, ["Edit"] = 1, ["Window"] = 1 },
                        },
                    },
                },
            };

            var md = report.ToMarkdownSummary();
            Assert.Contains("## 🔍 Windows Application Survey: `windows-2022`", md);
            Assert.Contains("`notepad`", md);
            Assert.Contains("40.0%", md);
            Assert.Contains("MenuItem:40", md);
        }

        [Fact]
        public void ApplicationTreeDiff_DetectsIdenticalAndDriftedTrees()
        {
            var app2022 = new ApplicationSurveyRecord
            {
                AppName = "notepad",
                Launched = true,
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 30,
                    MaxDepth = 3,
                    EmptyAutomationIdCount = 5,
                    EmptyAutomationIdFraction = 5.0 / 30.0,
                    ControlTypeDistribution = new Dictionary<string, int> { ["Button"] = 10, ["Edit"] = 1 },
                },
            };

            var app2025Identical = new ApplicationSurveyRecord
            {
                AppName = "notepad",
                Launched = true,
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 30,
                    MaxDepth = 3,
                    EmptyAutomationIdCount = 5,
                    EmptyAutomationIdFraction = 5.0 / 30.0,
                    ControlTypeDistribution = new Dictionary<string, int> { ["Button"] = 10, ["Edit"] = 1 },
                },
            };

            var diffIdentical = ApplicationTreeDiff.Compare(app2022, app2025Identical, null, null);
            Assert.False(diffIdentical.HasStructuralDrift);
            Assert.Contains("Identical (30 nodes)", diffIdentical.DriftSignal);

            var app2025Drifted = new ApplicationSurveyRecord
            {
                AppName = "notepad",
                Launched = true,
                Metrics = new ApplicationTreeMetrics
                {
                    TotalNodes = 45,
                    MaxDepth = 4,
                    EmptyAutomationIdCount = 12,
                    EmptyAutomationIdFraction = 12.0 / 45.0,
                    ControlTypeDistribution = new Dictionary<string, int> { ["Button"] = 10, ["Edit"] = 1, ["TabItem"] = 14 },
                },
            };

            var diffDrifted = ApplicationTreeDiff.Compare(app2022, app2025Drifted, null, null);
            Assert.True(diffDrifted.HasStructuralDrift);
            Assert.Contains("+15 nodes", diffDrifted.DriftSignal);
            Assert.Contains(diffDrifted.Details, d => d.Contains("Total nodes changed: 30 → 45 (+15)"));
            Assert.Contains(diffDrifted.Details, d => d.Contains("ControlType 'TabItem': 0 → 14 (+14)"));
        }

        [Fact]
        public void CrossImageSurveyComparison_ProducesAccurateMarkdownAndDriftSignals()
        {
            var report2022 = new ApplicationSurveyReport
            {
                ImageName = "windows-2022",
                Timestamp = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero),
                Applications = new List<ApplicationSurveyRecord>
                {
                    new()
                    {
                        AppName = "notepad",
                        Launched = true,
                        Metrics = new ApplicationTreeMetrics
                        {
                            TotalNodes = 25,
                            MaxDepth = 2,
                            EmptyAutomationIdCount = 5,
                            EmptyAutomationIdFraction = 0.20,
                            ControlTypeDistribution = new Dictionary<string, int> { ["Window"] = 1, ["Edit"] = 1 },
                        },
                    },
                    new()
                    {
                        AppName = "wordpad",
                        Launched = true,
                        Metrics = new ApplicationTreeMetrics
                        {
                            TotalNodes = 80,
                            MaxDepth = 4,
                            EmptyAutomationIdCount = 20,
                            EmptyAutomationIdFraction = 0.25,
                            ControlTypeDistribution = new Dictionary<string, int> { ["Window"] = 1, ["Button"] = 50 },
                        },
                    },
                },
            };

            var report2025 = new ApplicationSurveyReport
            {
                ImageName = "windows-2025",
                Timestamp = new DateTimeOffset(2026, 8, 15, 10, 30, 0, TimeSpan.Zero),
                Applications = new List<ApplicationSurveyRecord>
                {
                    new()
                    {
                        AppName = "notepad",
                        Launched = true,
                        Metrics = new ApplicationTreeMetrics
                        {
                            TotalNodes = 65,
                            MaxDepth = 4,
                            EmptyAutomationIdCount = 30,
                            EmptyAutomationIdFraction = 30.0 / 65.0,
                            ControlTypeDistribution = new Dictionary<string, int> { ["Window"] = 1, ["Edit"] = 1, ["TabControl"] = 1 },
                        },
                    },
                    new()
                    {
                        AppName = "wordpad",
                        Launched = false,
                        LaunchError = "Executable not found (removed in Server 2025)",
                    },
                },
            };

            var md = CrossImageSurveyComparison.GenerateComparisonMarkdown(report2022, report2025);

            Assert.Contains("Windows Runner Images Application Benchmark Survey", md);
            Assert.Contains("`notepad`", md);
            Assert.Contains("`wordpad`", md);
            Assert.Contains("⚡ Drift (+40 nodes", md);
            Assert.Contains("⚠️ 2022 Only", md);
            Assert.Contains("Found **2 candidate(s)** with verified organic drift", md);
        }

        [Fact]
        public void RunLiveSurveyComparison()
        {
            var optIn = Environment.GetEnvironmentVariable("COMPARE_SURVEY_REPORTS");
            if (optIn != "1" && !string.Equals(optIn, "true", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[SurveyComparison] COMPARE_SURVEY_REPORTS=1 is not set - skipping live comparison.");
                return;
            }

            var path2022 = Environment.GetEnvironmentVariable("SURVEY_REPORT_2022")
                ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "survey-trees", "windows-2022", "survey-report-windows-2022.json");
            var path2025 = Environment.GetEnvironmentVariable("SURVEY_REPORT_2025")
                ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "survey-trees", "windows-2025", "survey-report-windows-2025.json");

            if (!File.Exists(path2022))
            {
                throw new FileNotFoundException(
                    $"[SurveyComparison] Survey report for windows-2022 not found at '{path2022}'. The compare step requires artifacts from both runner images; missing report indicates survey job failure or missing artifact download.");
            }

            if (!File.Exists(path2025))
            {
                throw new FileNotFoundException(
                    $"[SurveyComparison] Survey report for windows-2025 not found at '{path2025}'. The compare step requires artifacts from both runner images; missing report indicates survey job failure or missing artifact download.");
            }

            var report2022 = ApplicationSurveySerializer.FromJson(File.ReadAllText(path2022));
            var report2025 = ApplicationSurveySerializer.FromJson(File.ReadAllText(path2025));

            if (report2022.Applications.Count == 0 && report2025.Applications.Count == 0)
            {
                throw new InvalidOperationException("[SurveyComparison] Both survey reports contain 0 applications. An empty survey is an infrastructure defect.");
            }

            var treesDir2022 = Environment.GetEnvironmentVariable("TREES_DIR_2022") ?? Path.GetDirectoryName(path2022)!;
            var treesDir2025 = Environment.GetEnvironmentVariable("TREES_DIR_2025") ?? Path.GetDirectoryName(path2025)!;

            UiElementInfo? LoadTree(string image, string fileName)
            {
                var dir = string.Equals(image, "windows-2022", StringComparison.OrdinalIgnoreCase) ? treesDir2022 : treesDir2025;
                var fullPath = Path.Combine(dir, fileName);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        return UiTreeSerializer.FromJson(File.ReadAllText(fullPath));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SurveyComparison] Warning: failed to deserialize tree from {fullPath}: {ex.Message}");
                    }
                }
                return null;
            }

            var markdown = CrossImageSurveyComparison.GenerateComparisonMarkdown(report2022, report2025, LoadTree);

            Console.WriteLine(markdown);

            var stepSummaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
            if (!string.IsNullOrEmpty(stepSummaryFile))
            {
                try
                {
                    File.AppendAllText(stepSummaryFile, markdown + Environment.NewLine);
                    Console.WriteLine("[SurveyComparison] Appended comparison summary to GITHUB_STEP_SUMMARY.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SurveyComparison] Failed to write GITHUB_STEP_SUMMARY: {ex.Message}");
                }
            }
        }

        [Fact]
        public void RunLiveSurveyComparison_ThrowsFileNotFoundException_WhenReportIsMissing()
        {
            var missingPath2022 = Path.Combine(Path.GetTempPath(), "missing_2022_" + Guid.NewGuid().ToString("N") + ".json");
            var missingPath2025 = Path.Combine(Path.GetTempPath(), "missing_2025_" + Guid.NewGuid().ToString("N") + ".json");

            var prevOptIn = Environment.GetEnvironmentVariable("COMPARE_SURVEY_REPORTS");
            var prev2022 = Environment.GetEnvironmentVariable("SURVEY_REPORT_2022");
            var prev2025 = Environment.GetEnvironmentVariable("SURVEY_REPORT_2025");

            try
            {
                Environment.SetEnvironmentVariable("COMPARE_SURVEY_REPORTS", "1");
                Environment.SetEnvironmentVariable("SURVEY_REPORT_2022", missingPath2022);
                Environment.SetEnvironmentVariable("SURVEY_REPORT_2025", missingPath2025);

                var ex = Assert.Throws<FileNotFoundException>(() => new ApplicationSurveyTests().RunLiveSurveyComparison());
                Assert.Contains("missing_2022", ex.Message);
            }
            finally
            {
                Environment.SetEnvironmentVariable("COMPARE_SURVEY_REPORTS", prevOptIn);
                Environment.SetEnvironmentVariable("SURVEY_REPORT_2022", prev2022);
                Environment.SetEnvironmentVariable("SURVEY_REPORT_2025", prev2025);
            }
        }
    }
}
