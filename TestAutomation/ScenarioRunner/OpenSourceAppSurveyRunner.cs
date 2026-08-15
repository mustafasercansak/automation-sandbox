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

    public sealed class OpenSourceAppVersionChain
    {
        public OpenSourceAppVersionChain(
            string appName,
            string toolkit,
            IReadOnlyList<OpenSourceVersionCandidate> versions)
        {
            AppName = appName;
            Toolkit = toolkit;
            Versions = versions;
        }

        public string AppName { get; }
        public string Toolkit { get; }
        public IReadOnlyList<OpenSourceVersionCandidate> Versions { get; }
    }

    public static class OpenSourceAppSurveyRunner
    {
        public static IReadOnlyList<OpenSourceAppVersionChain> DefaultVersionChains => new[]
        {
            new OpenSourceAppVersionChain(
                "ShareX",
                "WinForms",
                new[]
                {
                    new OpenSourceVersionCandidate("v13.7.0", "https://github.com/ShareX/ShareX/releases/download/v13.7.0/ShareX-13.7.0-portable.zip", "ShareX.exe"),
                    new OpenSourceVersionCandidate("v14.1.0", "https://github.com/ShareX/ShareX/releases/download/v14.1.0/ShareX-14.1.0-portable.zip", "ShareX.exe"),
                    new OpenSourceVersionCandidate("v15.0.0", "https://github.com/ShareX/ShareX/releases/download/v15.0.0/ShareX-15.0.0-portable.zip", "ShareX.exe"),
                    new OpenSourceVersionCandidate("v16.0.1", "https://github.com/ShareX/ShareX/releases/download/v16.0.1/ShareX-16.0.1-portable.zip", "ShareX.exe"),
                    new OpenSourceVersionCandidate("v16.1.0", "https://github.com/ShareX/ShareX/releases/download/v16.1.0/ShareX-16.1.0-portable.zip", "ShareX.exe"),
                    new OpenSourceVersionCandidate("v17.0.0", "https://github.com/ShareX/ShareX/releases/download/v17.0.0/ShareX-17.0.0-portable.zip", "ShareX.exe"),
                    new OpenSourceVersionCandidate("v19.0.2", "https://github.com/ShareX/ShareX/releases/download/v19.0.2/ShareX-19.0.2-portable.zip", "ShareX.exe"),
                    new OpenSourceVersionCandidate("v21.0.0", "https://github.com/ShareX/ShareX/releases/download/v21.0.0/ShareX-21.0.0-portable-x64.zip", "ShareX.exe"),
                }),

            new OpenSourceAppVersionChain(
                "HandBrake",
                "WPF",
                new[]
                {
                    new OpenSourceVersionCandidate("1.6.1", "https://github.com/HandBrake/HandBrake/releases/download/1.6.1/HandBrake-1.6.1-x86_64-Win_GUI.zip", "HandBrake.exe"),
                    new OpenSourceVersionCandidate("1.7.3", "https://github.com/HandBrake/HandBrake/releases/download/1.7.3/HandBrake-1.7.3-x86_64-Win_GUI.zip", "HandBrake.exe"),
                    new OpenSourceVersionCandidate("1.8.2", "https://github.com/HandBrake/HandBrake/releases/download/1.8.2/HandBrake-1.8.2-x86_64-Win_GUI.zip", "HandBrake.exe"),
                    new OpenSourceVersionCandidate("1.9.2", "https://github.com/HandBrake/HandBrake/releases/download/1.9.2/HandBrake-1.9.2-x86_64-Win_GUI.zip", "HandBrake.exe"),
                    new OpenSourceVersionCandidate("1.11.2", "https://github.com/HandBrake/HandBrake/releases/download/1.11.2/HandBrake-1.11.2-x86_64-Win_GUI.zip", "HandBrake.exe"),
                }),
        };

        public static OpenSourceAppSurveyReport RunSurvey(
            string outputDirectory,
            IReadOnlyList<OpenSourceAppVersionChain>? versionChains = null,
            Action<string>? log = null)
        {
            log ??= Console.WriteLine;
            versionChains ??= DefaultVersionChains;

            Directory.CreateDirectory(outputDirectory);
            var downloadsDir = Path.Combine(outputDirectory, "downloads");
            var extractedDir = Path.Combine(outputDirectory, "extracted");
            Directory.CreateDirectory(downloadsDir);
            Directory.CreateDirectory(extractedDir);

            var report = new OpenSourceAppSurveyReport
            {
                Timestamp = DateTimeOffset.UtcNow,
            };

            log($"[OpenSourceSurvey] Starting survey on {versionChains.Count} version chain(s).");
            log($"[OpenSourceSurvey] Output directory: {outputDirectory}");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AutomationSandbox-Survey/1.0");
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            foreach (var chain in versionChains)
            {
                log($"[OpenSourceSurvey] ========================================================");
                log($"[OpenSourceSurvey] Probing Version Chain: {chain.AppName} ({chain.Toolkit}) - {chain.Versions.Count} releases");
                log($"[OpenSourceSurvey] Range: {chain.Versions.First().Version} → {chain.Versions.Last().Version}");
                log($"[OpenSourceSurvey] ========================================================");

                var chainRecord = new AppChainSurveyRecord
                {
                    AppName = chain.AppName,
                    Toolkit = chain.Toolkit,
                };

                // 1. Probe every version in the chain
                foreach (var versionCandidate in chain.Versions)
                {
                    var versionRecord = ProbeVersion(chain.AppName, versionCandidate, downloadsDir, extractedDir, outputDirectory, httpClient, log);
                    chainRecord.Versions.Add(versionRecord);
                }

                // 2. Evaluate consecutive hops (V_i -> V_{i+1})
                for (var i = 0; i < chainRecord.Versions.Count - 1; i++)
                {
                    var v1 = chainRecord.Versions[i];
                    var v2 = chainRecord.Versions[i + 1];

                    UiElementInfo? treeV1 = LoadTree(outputDirectory, v1.TreeJsonFileName);
                    UiElementInfo? treeV2 = LoadTree(outputDirectory, v2.TreeJsonFileName);

                    var diff = ApplicationTreeDiff.Compare(
                        new ApplicationSurveyRecord
                        {
                            AppName = $"{chain.AppName}_{v1.Version}",
                            Launched = v1.Launched,
                            LaunchError = v1.Error,
                            Metrics = v1.Metrics,
                        },
                        new ApplicationSurveyRecord
                        {
                            AppName = $"{chain.AppName}_{v2.Version}",
                            Launched = v2.Launched,
                            LaunchError = v2.Error,
                            Metrics = v2.Metrics,
                        },
                        treeV1,
                        treeV2);

                    var hopRecord = OpenSourceAppViabilityEvaluator.EvaluateHop(v1, v2, diff);
                    chainRecord.Hops.Add(hopRecord);
                    log($"[OpenSourceSurvey] Hop '{hopRecord.FromVersion}' → '{hopRecord.ToVersion}': Viable={hopRecord.IsViableHop}, Removed IDs={hopRecord.RemovedAutomationIds.Count} ({hopRecord.ViabilityReason})");
                }

                // 3. Evaluate chain totals & deduplication
                OpenSourceAppViabilityEvaluator.EvaluateChain(chainRecord);
                log($"[OpenSourceSurvey] Chain summary for {chain.AppName}: {chainRecord.TotalDistinctBrokenLocatorsCount} distinct broken locators, {chainRecord.TotalCumulativeBrokenLocatorsCount} cumulative across {chainRecord.Hops.Count} hops. ({chainRecord.BenchmarkRecommendation})");

                report.Chains.Add(chainRecord);
            }

            // Clean up empty helper directories if needed
            try { if (Directory.Exists(downloadsDir) && !Directory.EnumerateFileSystemEntries(downloadsDir).Any()) Directory.Delete(downloadsDir); } catch { }
            try { if (Directory.Exists(extractedDir) && !Directory.EnumerateFileSystemEntries(extractedDir).Any()) Directory.Delete(extractedDir); } catch { }

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

                // Older releases in a chain target runtimes that are long out of support and absent from
                // CI images (HandBrake 1.6.1/1.7.3 ask for .NET 6). Rolling forward to the newest installed
                // major lets them start instead of showing a host error dialog. Recorded, not hidden: a
                // capture made under a different runtime major is still a fact about the capture.
                psi.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";
                record.LaunchDiagnostics.Add("DOTNET_ROLL_FORWARD=LatestMajor applied");

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

                // 5. Robust Window Selection & Hydration Loop
                // Rule: Visibility + size (W>=200, H>=150) -> highest node count -> non-empty title tie-breaker
                log($"[OpenSourceSurvey] Acquiring main window and polling visual tree hydration for {appName} {candidate.Version}...");
                Window? chosenWindow = null;
                var hydrationSw = Stopwatch.StartNew();
                var hydrationMaxWaitSeconds = 15.0;
                var dismissedDialogTitles = new HashSet<string>(StringComparer.Ordinal);
                const int maxDialogDismissals = 3;
                var discoveryOptions = new DiscoveryOptions
                {
                    MaxDepth = 25,
                    MaxElements = 5000,
                    IncludeOffscreen = true,
                    Timeout = TimeSpan.FromSeconds(25),
                };

                var candidatesLog = new List<string>();

                while (hydrationSw.Elapsed.TotalSeconds < hydrationMaxWaitSeconds)
                {
                    var topLevelWindows = app.GetAllTopLevelWindows(automation);
                    var viableWindows = new List<(Window Win, int NodeCount, string Title, UiElementInfo? Root)>();

                    foreach (var win in topLevelWindows)
                    {
                        try
                        {
                            var rect = win.BoundingRectangle;
                            if (rect.Width < 200 || rect.Height < 150) continue;

                            var probeResult = UiTreeWalker.Discover(win, new DiscoveryOptions
                            {
                                MaxDepth = 10,
                                MaxElements = 500,
                                Timeout = TimeSpan.FromSeconds(5),
                            });

                            viableWindows.Add((win, probeResult.CapturedCount, win.Title, probeResult.Root));
                        }
                        catch { }
                    }

                    if (viableWindows.Count > 0)
                    {
                        // Order by node count descending, then non-empty title descending
                        var best = viableWindows
                            .OrderByDescending(w => w.NodeCount)
                            .ThenByDescending(w => !string.IsNullOrEmpty(w.Title))
                            .First();

                        chosenWindow = best.Win;
                        candidatesLog = viableWindows.Select(w => $"[Title='{w.Title}', Nodes={w.NodeCount}]").ToList();

                        // If visual tree has substantive content (> 15 nodes), consider hydrated
                        if (best.NodeCount > 15)
                        {
                            break;
                        }

                        // A small #32770 window is a modal startup dialog standing in front of the app,
                        // not the app. Capturing it yields a 7-node tree whose diff against a real window
                        // fabricates removed AutomationIds. Diagnose it, then get it out of the way.
                        var dialogRoot = best.Root;
                        if (dialogRoot != null && WindowReadinessHeuristics.IsBlockingDialog(dialogRoot.ClassName, best.NodeCount))
                        {
                            var dialogTexts = CollectDescendantValues(dialogRoot, e => e.Name);

                            if (WindowReadinessHeuristics.TryDetectMissingRuntime(dialogTexts, out var missingRuntime))
                            {
                                record.Launched = false;
                                record.Error = $"Missing runtime: application requires {missingRuntime}, which is not installed and could not be rolled forward.";
                                record.LaunchDiagnostics.Add($"❌ Host error dialog '{best.Title}' reported missing {missingRuntime}");
                                log($"[OpenSourceSurvey] ❌ {appName} {candidate.Version}: {record.Error}");
                                return record;
                            }

                            var dialogKey = best.Title ?? "";
                            if (!dismissedDialogTitles.Contains(dialogKey) && dismissedDialogTitles.Count < maxDialogDismissals)
                            {
                                var buttonNames = CollectDescendantValues(
                                    dialogRoot,
                                    e => string.Equals(e.ControlType, "Button", StringComparison.OrdinalIgnoreCase) ? e.Name : null);
                                var dismissButton = WindowReadinessHeuristics.SelectDismissButtonName(buttonNames);

                                if (dismissButton != null && TryInvokeButton(best.Win, dismissButton))
                                {
                                    dismissedDialogTitles.Add(dialogKey);
                                    record.LaunchDiagnostics.Add($"🪟 Dismissed startup dialog '{dialogKey}' via '{dismissButton}'");
                                    log($"[OpenSourceSurvey] 🪟 Dismissed startup dialog '{dialogKey}' for {appName} {candidate.Version} via '{dismissButton}' button.");

                                    // The real main window has not been created yet; give it its own budget.
                                    chosenWindow = null;
                                    hydrationMaxWaitSeconds = hydrationSw.Elapsed.TotalSeconds + 15.0;
                                    Thread.Sleep(1500);
                                    continue;
                                }

                                record.LaunchDiagnostics.Add($"⚠️ Blocking dialog '{dialogKey}' has no recognised dismiss button");
                            }
                        }
                    }

                    Thread.Sleep(1000);
                }

                if (chosenWindow == null)
                {
                    try
                    {
                        chosenWindow = app.GetMainWindow(automation, TimeSpan.FromSeconds(2));
                    }
                    catch { }
                }

                if (chosenWindow == null)
                {
                    record.Launched = false;
                    record.Error = "No usable top-level window found within timeout.";
                    log($"[OpenSourceSurvey] ❌ No usable window found for {appName} {candidate.Version}.");
                    return record;
                }

                record.Launched = true;
                record.WindowTitle = chosenWindow.Title;
                record.RootClassName = chosenWindow.ClassName;
                record.RootControlType = chosenWindow.ControlType.ToString();

                var candidatesSummary = candidatesLog.Count > 0 ? string.Join(", ", candidatesLog) : "Single window";
                record.WindowSelectionReason = $"Selected window '{chosenWindow.Title}' (Class='{chosenWindow.ClassName}') from {candidatesLog.Count} candidates: {candidatesSummary}";

                // 6. Dynamic Settle Discovery Loop
                log($"[OpenSourceSurvey] Running settle loop on '{record.WindowTitle}' for {appName} {candidate.Version}...");
                const int maxSettlePasses = 5;
                var passCounts = new List<int>();
                DiscoveryResult? settledResult = null;
                var sw = Stopwatch.StartNew();

                for (var pass = 1; pass <= maxSettlePasses; pass++)
                {
                    var result = UiTreeWalker.Discover(chosenWindow, discoveryOptions);
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

                    if (metrics.TotalNodes <= 15)
                    {
                        record.HydrationTimedOut = true;
                        record.WindowSelectionReason += " | ⚠️ Hydration timed out (node count <= 15)";
                        log($"[OpenSourceSurvey] ⚠️ Hydration warning for {appName} {candidate.Version}: node count remained at {metrics.TotalNodes} <= 15");
                    }

                    var treeFileName = $"{appName}_{safeVersion}.json";
                    var treePath = Path.Combine(outputDirectory, treeFileName);
                    var treeJson = UiTreeSerializer.ToJson(settledResult.Root);
                    File.WriteAllText(treePath, treeJson);
                    record.TreeJsonFileName = treeFileName;

                    log($"[OpenSourceSurvey] ✅ Captured {appName} {candidate.Version}: {metrics.TotalNodes} nodes, {ReportFormatting.Percent(metrics.EmptyAutomationIdFraction)} empty ID, window='{record.WindowTitle}' ({record.SettlePassCount}p settle)");
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

                // Clean up disk: remove extracted folder and zip file after probe
                try
                {
                    if (Directory.Exists(appExtractDir))
                    {
                        Directory.Delete(appExtractDir, recursive: true);
                    }
                }
                catch { }

                try
                {
                    if (File.Exists(zipFilePath))
                    {
                        File.Delete(zipFilePath);
                    }
                }
                catch { }
            }

            return record;
        }

        // Flattens a probed dialog tree into the values a heuristic needs (all names, or button names only).
        private static List<string?> CollectDescendantValues(UiElementInfo root, Func<UiElementInfo, string?> selector)
        {
            var values = new List<string?>();
            var stack = new Stack<UiElementInfo>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                var value = selector(current);
                if (!string.IsNullOrEmpty(value))
                {
                    values.Add(value);
                }

                foreach (var child in current.Children)
                {
                    stack.Push(child);
                }
            }

            return values;
        }

        private static bool TryInvokeButton(Window window, string buttonName)
        {
            try
            {
                var button = window.FindFirstDescendant(cf =>
                    cf.ByName(buttonName).And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)));

                if (button == null)
                {
                    return false;
                }

                try
                {
                    button.AsButton().Invoke();
                }
                catch
                {
                    // Win32 buttons that do not expose InvokePattern still respond to a synthetic click.
                    button.AsButton().Click();
                }

                return true;
            }
            catch
            {
                return false;
            }
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
