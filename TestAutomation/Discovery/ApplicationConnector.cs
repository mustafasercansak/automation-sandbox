using System;
using System.Diagnostics;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
namespace Discovery
{
    // Framework-agnostic connection point: whether the target is WinForms, WPF, or any
    // other Windows desktop app doesn't matter - UIA3 talks to it the same way from outside.
    public sealed class ApplicationConnector : IDisposable
    {
        private static readonly TimeSpan DefaultMainWindowTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan LookupSlice = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan LookupInterval = TimeSpan.FromMilliseconds(100);

        public Application App { get; }
        public UIA3Automation Automation { get; }

        private readonly int _processId;

        private ApplicationConnector(Application app, UIA3Automation automation)
        {
            App = app;
            Automation = automation;
            _processId = app.ProcessId;
        }

        public static ApplicationConnector Launch(string exePath)
        {
            var app = Application.Launch(exePath);
            return new ApplicationConnector(app, new UIA3Automation());
        }

        public static ApplicationConnector Attach(string processName)
        {
            var app = Application.Attach(processName);
            return new ApplicationConnector(app, new UIA3Automation());
        }

        public Window GetMainWindow(TimeSpan? timeout = null)
        {
            var effectiveTimeout = timeout ?? DefaultMainWindowTimeout;
            if (effectiveTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Main-window timeout must be greater than zero.");
            }

            var stopwatch = Stopwatch.StartNew();
            Exception? lastUiaError = null;
            var lastTopLevelWindowCount = 0;
            var sawNativeMainWindowHandle = false;

            while (stopwatch.Elapsed < effectiveTimeout)
            {
                if (App.HasExited)
                {
                    throw ApplicationStartupDiagnostics.CreateFailure(
                        _processId,
                        stopwatch.Elapsed,
                        hasExited: true,
                        exitCode: App.ExitCode,
                        sawNativeMainWindowHandle: sawNativeMainWindowHandle,
                        topLevelWindowCount: lastTopLevelWindowCount,
                        lastUiaError: lastUiaError);
                }

                try
                {
                    sawNativeMainWindowHandle |= App.MainWindowHandle != IntPtr.Zero;
                    var mainWindow = App.GetMainWindow(Automation, LookupSlice);
                    if (mainWindow != null)
                    {
                        return mainWindow;
                    }
                }
                catch (Exception ex)
                {
                    lastUiaError = ex;
                }

                // FlaUI's main-window lookup depends on Process.MainWindowHandle. Under CI
                // contention that handle can briefly remain zero even after UIA can see the
                // WPF window. Accept only an unambiguous, same-process top-level window so a
                // splash screen or unrelated dialog can never be mistaken for the main UI.
                try
                {
                    var topLevelWindows = App.GetAllTopLevelWindows(Automation);
                    lastTopLevelWindowCount = topLevelWindows.Length;
                    if (topLevelWindows.Length == 1)
                    {
                        return topLevelWindows[0];
                    }
                }
                catch (Exception ex)
                {
                    lastUiaError = ex;
                }

                Thread.Sleep(LookupInterval);
            }

            var hasExited = App.HasExited;
            throw ApplicationStartupDiagnostics.CreateFailure(
                _processId,
                stopwatch.Elapsed,
                hasExited: hasExited,
                exitCode: hasExited ? App.ExitCode : (int?)null,
                sawNativeMainWindowHandle: sawNativeMainWindowHandle,
                topLevelWindowCount: lastTopLevelWindowCount,
                lastUiaError: lastUiaError);
        }

        public void Dispose()
        {
            try
            {
                if (!App.HasExited)
                {
                    App.Close(killIfCloseFails: true);
                }
            }
            finally
            {
                Automation.Dispose();
                App.Dispose();
            }
        }
    }
}
