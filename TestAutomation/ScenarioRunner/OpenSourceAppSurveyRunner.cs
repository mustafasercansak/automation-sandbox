using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Discovery;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using UiModel;

namespace ScenarioRunner
{
    public sealed class OpenSourceVersionCandidate
    {
        public OpenSourceVersionCandidate(string version, string downloadUrl, string executableRelativePath, string arguments = "")
        {
            Version = version;
            DownloadUrl = downloadUrl;
            ExecutableRelativePath = executableRelativePath;
            Arguments = arguments;
        }

        public string Version { get; }
        public string DownloadUrl { get; }
        public string ExecutableRelativePath { get; }
        public string Arguments { get; }
    }

    public sealed class OpenSourceAppCandidatePair
    {
        public OpenSourceAppCandidatePair(
            string appName,
            string toolkit,
            OpenSourceVersionCandidate olderVersion,
            OpenSourceVersionCandidate newerVersion)
        {
            AppName = appName;
            Toolkit = toolkit;
            OlderVersion = olderVersion;
            NewerVersion = newerVersion;
        }

        public string AppName { get; }
        public string Toolkit { get; }
        public OpenSourceVersionCandidate OlderVersion { get; }
        public OpenSourceVersionCandidate NewerVersion { get; }
    }

    public static class OpenSourceAppSurveyRunner
    {
        public static IReadOnlyList<OpenSourceAppCandidatePair> DefaultCandidatePairs => new[]
        {
            new OpenSourceAppCandidatePair(
                "ShareX",
                "WinForms",
                new OpenSourceVersionCandidate(
                    "v16.1.0",
                    "https://github.com/ShareX/ShareX/releases/download/v16.1.0/ShareX-16.1.0-portable.zip",
                    "ShareX.exe"),
                new OpenSourceVersionCandidate(
                    "v21.0.0",
                    "https://github.com/ShareX/ShareX/releases/download/v21.0.0/ShareX-21.0.0-portable-x64.zip",
                    "ShareX.exe")),

            new OpenSourceAppCandidatePair(
                "HandBrake",
                "WPF",
                new OpenSourceVersionCandidate(
                    "1.8.2",
                    "https://github.com/HandBrake/HandBrake/releases/download/1.8.2/HandBrake-1.8.2-x86_64-Win_GUI.zip",
                    "HandBrake.exe"),
                new OpenSourceVersionCandidate(
                    "1.11.2",
                    "https://github.com/HandBrake/HandBrake/releases/download/1.11.2/HandBrake-1.11.2-x86_64-Win_GUI.zip",
                    "HandBrake.exe")),
        };

        public static OpenSourceAppSurveyReport RunSurvey(
            string outputDirectory,
            IReadOnlyList<OpenSourceAppCandidatePair>? candidatePairs = null,
            Action<string>? log = null)
        {
            log ??= Console.WriteLine;
            candidatePairs ??= DefaultCandidatePairs;

            Directory.CreateDirectory(outputDirectory);
            var downloadsDir = Path.Combine(outputDirectory, "downloads");
            var extractedDir = Path.Combine(outputDirectory, "extracted");
            Directory.CreateDirectory(downloadsDir);
            Directory.CreateDirectory(extractedDir);

            var report = new OpenSourceAppSurveyReport
            {
                Timestamp = DateTimeOffset.UtcNow,
            };

            log($"[OpenSourceSurvey] Starting survey on {candidatePairs.Count} application pair(s).");
            log($"[OpenSourceSurvey] Output directory: {outputDirectory}");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AutomationSandbox-Survey/1.0");
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            foreach (var pair in candidatePairs)
            {
                log($"[OpenSourceSurvey] ========================================================");
                log($"[OpenSourceSurvey] Evaluating candidate pair: {pair.AppName} ({pair.Toolkit})");
                log($"[OpenSourceSurvey] Older: {pair.OlderVersion.Version} | Newer: {pair.NewerVersion.Version}");
                log($"[OpenSourceSurvey] ========================================================");

                var pairRecord = new AppPairSurveyRecord
                {
                    AppName = pair.AppName,
                    Toolkit = pair.Toolkit,
                };

                // Probe V1 (older)
                pairRecord.V1 = ProbeVersion(pair.AppName, pair.OlderVersion, downloadsDir, extractedDir, outputDirectory, httpClient, log);

                // Probe V2 (newer)
                pairRecord.V2 = ProbeVersion(pair.AppName, pair.NewerVersion, downloadsDir, extractedDir, outputDirectory, httpClient, log);

                // Load trees for deep diff comparison if both captured
                UiElementInfo? treeV1 = LoadTree(outputDirectory, pairRecord.V1.TreeJsonFileName);
                UiElementInfo? treeV2 = LoadTree(outputDirectory, pairRecord.V2.TreeJsonFileName);

                pairRecord.Diff = ApplicationTreeDiff.Compare(
                    new ApplicationSurveyRecord
                    {
                        AppName = $"{pair.AppName}_{pair.OlderVersion.Version}",
                        Launched = pairRecord.V1.Launched,
                        LaunchError = pairRecord.V1.Error,
                        Metrics = pairRecord.V1.Metrics,
                    },
                    new ApplicationSurveyRecord
                    {
                        AppName = $"{pair.AppName}_{pair.NewerVersion.Version}",
                        Launched = pairRecord.V2.Launched,
                        LaunchError = pairRecord.V2.Error,
                        Metrics = pairRecord.V2.Metrics,
                    },
                    treeV1,
                    treeV2);

                var (isViable, viabilityReason) = OpenSourceAppViabilityEvaluator.Evaluate(pairRecord.V1, pairRecord.V2, pairRecord.Diff);
                pairRecord.IsViableBenchmarkTarget = isViable;
                pairRecord.ViabilityReason = viabilityReason;

                log($"[OpenSourceSurvey] Pair assessment for {pair.AppName}: Viable={isViable} ({viabilityReason})");
                report.Pairs.Add(pairRecord);
            }

            // Save JSON report
            var reportJsonPath = Path.Combine(outputDirectory, "survey-report-open-source.json");
            var json = OpenSourceAppSurveySerializer.ToJson(report);
            File.WriteAllText(reportJsonPath, json);
            log($"[OpenSourceSurvey] Survey report written to: {reportJsonPath}");

            // Output Markdown summary
            var md = report.ToMarkdownSummary();
            log(md);

            var stepSummaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
            if (!string.IsNullOrEmpty(stepSummaryFile))
            {
                try
                {
                    File.AppendAllText(stepSummaryFile, md + Environment.NewLine);
                    log("[OpenSourceSurvey] Appended markdown summary to GITHUB_STEP_SUMMARY.");
                }
                catch (Exception ex)
                {
                    log($"[OpenSourceSurvey] Failed to write GITHUB_STEP_SUMMARY: {ex.Message}");
                }
            }

            return report;
        }

        private static AppVersionSurveyRecord ProbeVersion(
            string appName,
            OpenSourceVersionCandidate candidate,
            string downloadsDir,
            string extractedDir,
            string outputDirectory,
            HttpClient httpClient,
            Action<string> log)
        {
            var record = new AppVersionSurveyRecord
            {
                Version = candidate.Version,
                DownloadUrl = candidate.DownloadUrl,
                ExecutableRelativePath = candidate.ExecutableRelativePath,
                Arguments = candidate.Arguments,
            };

            var safeVersion = candidate.Version.Replace('/', '_').Replace('\\', '_');
            var zipFileName = $"{appName}_{safeVersion}.zip";
            var zipFilePath = Path.Combine(downloadsDir, zipFileName);
            var appExtractDir = Path.Combine(extractedDir, $"{appName}_{safeVersion}");

            // 1. Download
            try
            {
                if (!File.Exists(zipFilePath) || new FileInfo(zipFilePath).Length == 0)
                {
                    log($"[OpenSourceSurvey] Downloading {appName} {candidate.Version} from {candidate.DownloadUrl}...");
                    var response = httpClient.GetAsync(candidate.DownloadUrl).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();

                    using (var fs = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        response.Content.CopyToAsync(fs).GetAwaiter().GetResult();
                    }
                    log($"[OpenSourceSurvey] Downloaded {new FileInfo(zipFilePath).Length / 1024 / 1024} MB to {zipFilePath}");
                }
                record.Downloaded = true;
            }
            catch (Exception ex)
            {
                record.Downloaded = false;
                record.Error = $"Download failed: {ex.Message}";
                log($"[OpenSourceSurvey] ❌ Download failed for {appName} {candidate.Version}: {ex.Message}");
                return record;
            }

            // 2. Unpack
            try
            {
                if (!Directory.Exists(appExtractDir) || !Directory.EnumerateFileSystemEntries(appExtractDir).Any())
                {
                    log($"[OpenSourceSurvey] Unpacking {zipFilePath} to {appExtractDir}...");
                    Directory.CreateDirectory(appExtractDir);
                    ZipFile.ExtractToDirectory(zipFilePath, appExtractDir);
                }
            }
            catch (Exception ex)
            {
                record.Error = $"Unpack failed: {ex.Message}";
                log($"[OpenSourceSurvey] ❌ Unpack failed for {appName} {candidate.Version}: {ex.Message}");
                return record;
            }

            // 3. Locate executable
            var exeFullPath = Path.Combine(appExtractDir, candidate.ExecutableRelativePath);
            if (!File.Exists(exeFullPath))
            {
                // Search recursively in subdirectories if not at root
                var matched = Directory.GetFiles(appExtractDir, Path.GetFileName(candidate.ExecutableRelativePath), SearchOption.AllDirectories).FirstOrDefault();
                if (matched != null)
                {
                    exeFullPath = matched;
                }
                else
                {
                    record.Error = $"Executable '{candidate.ExecutableRelativePath}' not found in extracted directory.";
                    log($"[OpenSourceSurvey] ❌ Executable not found for {appName} {candidate.Version}.");
                    return record;
                }
            }

            // 4. Launch & Settle Capture
            Process? process = null;
            UIA3Automation? automation = null;
            FlaUI.Core.Application? app = null;

            try
            {
                log($"[OpenSourceSurvey] Launching '{exeFullPath}' {candidate.Arguments}...");
                var psi = new ProcessStartInfo
                {
                    FileName = exeFullPath,
                    Arguments = candidate.Arguments,
                    WorkingDirectory = Path.GetDirectoryName(exeFullPath),
                    UseShellExecute = false,
                };

                try
                {
                    process = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    record.Launched = false;
                    record.Error = $"Process.Start failed: {ex.Message}";
                    log($"[OpenSourceSurvey] ❌ Process.Start failed for {appName} {candidate.Version}: {ex.Message}");
                    return record;
                }

                if (process == null)
                {
                    record.Launched = false;
                    record.Error = "Process.Start returned null.";
                    return record;
                }

                // Initial process wait
                Thread.Sleep(2500);

                if (process.HasExited)
                {
                    var exitCode = process.ExitCode;
                    record.Launched = false;
                    record.Error = $"Process exited immediately with code 0x{exitCode:X8} ({exitCode}). Missing .NET Desktop Runtime or startup dependency.";
                    log($"[OpenSourceSurvey] ❌ {appName} {candidate.Version} {record.Error}");
                    return record;
                }

                automation = new UIA3Automation();

                try
                {
                    app = FlaUI.Core.Application.Attach(process.Id);
                }
                catch (Exception attachEx)
                {
                    record.Launched = false;
                    record.Error = $"FlaUI Attach failed: {attachEx.Message}";
                    log($"[OpenSourceSurvey] ❌ FlaUI attach failed for {appName} {candidate.Version}: {attachEx.Message}");
                    return record;
                }

                Window? window = null;
                try
                {
                    window = Retry.WhileNull(
                        () => app.GetMainWindow(automation, TimeSpan.FromSeconds(2)),
                        timeout: TimeSpan.FromSeconds(20)).Result;
                }
                catch (Exception winEx)
                {
                    record.Launched = false;
                    record.Error = $"GetMainWindow timed out: {winEx.Message}";
                    log($"[OpenSourceSurvey] ❌ Main window timeout for {appName} {candidate.Version}: {winEx.Message}");
                    return record;
                }

                if (window == null)
                {
                    record.Launched = false;
                    record.Error = "Main window was not found within 20s timeout.";
                    log($"[OpenSourceSurvey] ❌ Main window not found for {appName} {candidate.Version}.");
                    return record;
                }

                record.Launched = true;

                // 5. Dynamic Settle Discovery Loop
                log($"[OpenSourceSurvey] Performing dynamic settle capture on {appName} {candidate.Version}...");
                var discoveryOptions = new DiscoveryOptions
                {
                    MaxDepth = 25,
                    MaxElements = 5000,
                    IncludeOffscreen = true,
                    Timeout = TimeSpan.FromSeconds(25),
                };

                const int maxSettlePasses = 5;
                var passCounts = new List<int>();
                DiscoveryResult? settledResult = null;
                var sw = Stopwatch.StartNew();

                for (var pass = 1; pass <= maxSettlePasses; pass++)
                {
                    var result = UiTreeWalker.Discover(window, discoveryOptions);
                    settledResult = result;
                    var currentCount = result.CapturedCount;
                    passCounts.Add(currentCount);

                    log($"[OpenSourceSurvey]   Pass {pass}: captured {currentCount} nodes (elapsed: {result.Elapsed.TotalSeconds:F2}s)");

                    if (pass > 1 && currentCount == passCounts[pass - 2])
                    {
                        record.Settled = true;
                        break;
                    }

                    if (pass < maxSettlePasses)
                    {
                        Thread.Sleep(1000);
                    }
                }
                sw.Stop();

                record.SettlePassCount = passCounts.Count;
                record.SettleTelemetry = string.Join(" -> ", passCounts.Select((c, i) => $"Pass {i + 1}: {c}"));
                record.DiscoveryElapsed = sw.Elapsed;

                if (settledResult?.Root != null)
                {
                    var metrics = TreeMetricsCalculator.Calculate(settledResult.Root);
                    record.Metrics = metrics;

                    var treeFileName = $"{appName}_{safeVersion}.json";
                    var treePath = Path.Combine(outputDirectory, treeFileName);
                    var treeJson = UiTreeSerializer.ToJson(settledResult.Root);
                    File.WriteAllText(treePath, treeJson);
                    record.TreeJsonFileName = treeFileName;

                    log($"[OpenSourceSurvey] ✅ Captured {appName} {candidate.Version}: {metrics.TotalNodes} nodes, {ReportFormatting.Percent(metrics.EmptyAutomationIdFraction)} empty ID, settled in {record.SettlePassCount} passes ({record.SettleTelemetry})");
                }
                else
                {
                    record.Error = "Discovery returned null root element.";
                }
            }
            catch (Exception ex)
            {
                record.Launched = false;
                record.Error = $"Unexpected error: {ex.Message}";
                log($"[OpenSourceSurvey] ❌ Unexpected probe error for {appName} {candidate.Version}: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (app != null && !app.HasExited)
                    {
                        app.Close(killIfCloseFails: true);
                    }
                }
                catch { }

                try
                {
                    if (process != null && !process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch { }

                app?.Dispose();
                automation?.Dispose();
                process?.Dispose();
            }

            return record;
        }

        private static UiElementInfo? LoadTree(string dir, string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            var path = Path.Combine(dir, fileName);
            if (!File.Exists(path)) return null;
            try
            {
                return UiTreeSerializer.FromJson(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
    }
}
