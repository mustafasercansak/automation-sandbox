using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Discovery;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using UiModel;

namespace ScenarioRunner
{
    public sealed class SurveyCandidate
    {
        public SurveyCandidate(string name, string executable, string arguments = "", string processName = "")
        {
            Name = name;
            Executable = executable;
            Arguments = arguments;
            ProcessName = string.IsNullOrEmpty(processName) ? Path.GetFileNameWithoutExtension(executable) : processName;
        }

        public string Name { get; }
        public string Executable { get; }
        public string Arguments { get; }
        public string ProcessName { get; }
    }

    public static class WindowsAppSurveyRunner
    {
        public static IReadOnlyList<SurveyCandidate> DefaultCandidates => new[]
        {
            new SurveyCandidate("notepad", "notepad.exe"),
            new SurveyCandidate("mspaint", "mspaint.exe"),
            new SurveyCandidate("regedit", "regedit.exe"),
            new SurveyCandidate("taskmgr", "taskmgr.exe"),
            new SurveyCandidate("charmap", "charmap.exe"),
            new SurveyCandidate("osk", "osk.exe"),
            new SurveyCandidate("msinfo32", "msinfo32.exe"),
            new SurveyCandidate("dxdiag", "dxdiag.exe"),
            new SurveyCandidate("cleanmgr", "cleanmgr.exe"),
            new SurveyCandidate("control", "control.exe"),
            new SurveyCandidate("services.msc", "mmc.exe", "services.msc", "mmc"),
            new SurveyCandidate("eventvwr.msc", "mmc.exe", "eventvwr.msc", "mmc"),
            new SurveyCandidate("compmgmt.msc", "mmc.exe", "compmgmt.msc", "mmc"),
            new SurveyCandidate("diskmgmt.msc", "mmc.exe", "diskmgmt.msc", "mmc"),
            new SurveyCandidate("wordpad", "wordpad.exe"),
        };

        public static ApplicationSurveyReport RunSurvey(
            string imageName,
            string outputDirectory,
            IReadOnlyList<SurveyCandidate>? candidates = null,
            Action<string>? log = null)
        {
            log ??= Console.WriteLine;
            candidates ??= DefaultCandidates;

            Directory.CreateDirectory(outputDirectory);

            var report = new ApplicationSurveyReport
            {
                ImageName = imageName,
                Timestamp = DateTimeOffset.UtcNow,
            };

            log($"[WindowsAppSurvey] Starting survey on '{imageName}' with {candidates.Count} candidate applications.");
            log($"[WindowsAppSurvey] Trees will be written to: {outputDirectory}");

            foreach (var candidate in candidates)
            {
                log($"[WindowsAppSurvey] === Probing '{candidate.Name}' ({candidate.Executable} {candidate.Arguments}) ===");
                var record = ProbeCandidate(candidate, outputDirectory, log);
                report.Applications.Add(record);
            }

            // Save survey report JSON
            var reportJsonPath = Path.Combine(outputDirectory, $"survey-report-{imageName}.json");
            var json = ApplicationSurveySerializer.ToJson(report);
            File.WriteAllText(reportJsonPath, json);
            log($"[WindowsAppSurvey] Survey report written to: {reportJsonPath}");

            // Output markdown summary
            var md = report.ToMarkdownSummary();
            log(md);

            var stepSummaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
            if (!string.IsNullOrEmpty(stepSummaryFile))
            {
                try
                {
                    File.AppendAllText(stepSummaryFile, md + Environment.NewLine);
                    log("[WindowsAppSurvey] Appended markdown summary to GITHUB_STEP_SUMMARY.");
                }
                catch (Exception ex)
                {
                    log($"[WindowsAppSurvey] Failed to write GITHUB_STEP_SUMMARY: {ex.Message}");
                }
            }

            return report;
        }

        private static ApplicationSurveyRecord ProbeCandidate(
            SurveyCandidate candidate,
            string outputDirectory,
            Action<string> log)
        {
            var record = new ApplicationSurveyRecord
            {
                AppName = candidate.Name,
                Executable = candidate.Executable,
                Arguments = candidate.Arguments,
            };

            Process? process = null;
            UIA3Automation? automation = null;
            FlaUI.Core.Application? app = null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate.Executable,
                    Arguments = candidate.Arguments,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal,
                };

                try
                {
                    process = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    record.Launched = false;
                    record.LaunchError = $"Process.Start failed: {ex.Message}";
                    log($"[WindowsAppSurvey] ❌ Failed to start '{candidate.Name}': {ex.Message}");
                    return record;
                }

                if (process == null)
                {
                    record.Launched = false;
                    record.LaunchError = "Process.Start returned null.";
                    log($"[WindowsAppSurvey] ❌ Process.Start returned null for '{candidate.Name}'.");
                    return record;
                }

                // Allow process time to initialize
                Thread.Sleep(2000);

                automation = new UIA3Automation();

                // Attach via FlaUI
                try
                {
                    app = !process.HasExited
                        ? FlaUI.Core.Application.Attach(process.Id)
                        : FlaUI.Core.Application.Attach(candidate.ProcessName);
                }
                catch (Exception attachEx)
                {
                    log($"[WindowsAppSurvey] Attach by PID/name exception ({attachEx.Message}), attempting name lookup...");
                    try
                    {
                        app = FlaUI.Core.Application.Attach(candidate.ProcessName);
                    }
                    catch (Exception ex)
                    {
                        record.Launched = false;
                        record.LaunchError = $"Attach failed: {ex.Message}";
                        log($"[WindowsAppSurvey] ❌ Attach failed for '{candidate.Name}': {ex.Message}");
                        return record;
                    }
                }

                if (app == null)
                {
                    record.Launched = false;
                    record.LaunchError = "FlaUI.Core.Application.Attach returned null.";
                    log($"[WindowsAppSurvey] ❌ FlaUI attach returned null for '{candidate.Name}'.");
                    return record;
                }

                // Get Main Window
                Window? window = null;
                try
                {
                    window = Retry.WhileNull(
                        () => app.GetMainWindow(automation, TimeSpan.FromSeconds(2)),
                        timeout: TimeSpan.FromSeconds(15)).Result;
                }
                catch (Exception winEx)
                {
                    record.Launched = false;
                    record.LaunchError = $"GetMainWindow timed out or threw: {winEx.Message}";
                    log($"[WindowsAppSurvey] ❌ GetMainWindow failed for '{candidate.Name}': {winEx.Message}");
                    return record;
                }

                if (window == null)
                {
                    record.Launched = false;
                    record.LaunchError = "Main window was not found within timeout.";
                    log($"[WindowsAppSurvey] ❌ Main window was not found for '{candidate.Name}'.");
                    return record;
                }

                // Discover UI tree
                log($"[WindowsAppSurvey] Capturing UI tree for '{candidate.Name}'...");
                var discoveryOptions = new DiscoveryOptions
                {
                    MaxDepth = 25,
                    MaxElements = 5000,
                    IncludeOffscreen = true,
                    Timeout = TimeSpan.FromSeconds(25),
                };

                var discoveryResult = UiTreeWalker.Discover(window, discoveryOptions);
                record.Launched = true;
                record.DiscoveryElapsed = discoveryResult.Elapsed;

                // Calculate metrics
                var metrics = TreeMetricsCalculator.Calculate(discoveryResult.Root);
                record.Metrics = metrics;

                // Serialize and save tree JSON
                var treeFileName = $"{candidate.Name}.json";
                var treePath = Path.Combine(outputDirectory, treeFileName);
                var treeJson = UiTreeSerializer.ToJson(discoveryResult.Root);
                File.WriteAllText(treePath, treeJson);
                record.TreeJsonFileName = treeFileName;

                log($"[WindowsAppSurvey] ✅ Captured '{candidate.Name}': {metrics.TotalNodes} nodes, max depth {metrics.MaxDepth}, {metrics.EmptyAutomationIdFraction:P1} empty ID in {discoveryResult.Elapsed.TotalSeconds:F2}s");
            }
            catch (Exception ex)
            {
                record.Launched = false;
                record.LaunchError = $"Unexpected error during probe: {ex.Message}";
                log($"[WindowsAppSurvey] ❌ Unexpected error during probe for '{candidate.Name}': {ex.Message}");
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
    }
}
