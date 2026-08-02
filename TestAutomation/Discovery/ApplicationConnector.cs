using System;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
namespace Discovery
{
    // Framework-agnostic connection point: whether the target is WinForms, WPF, or any
    // other Windows desktop app doesn't matter - UIA3 talks to it the same way from outside.
    public sealed class ApplicationConnector : IDisposable
    {
        public Application App { get; }
        public UIA3Automation Automation { get; }

        private ApplicationConnector(Application app, UIA3Automation automation)
        {
            App = app;
            Automation = automation;
        }

        public static ApplicationConnector Launch(string exePath)
        {
            var app = Application.Launch(exePath);
            app.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(10));
            app.WaitWhileBusy(TimeSpan.FromSeconds(10));
            return new ApplicationConnector(app, new UIA3Automation());
        }

        public static ApplicationConnector Attach(string processName)
        {
            var app = Application.Attach(processName);
            return new ApplicationConnector(app, new UIA3Automation());
        }

        public Window GetMainWindow(TimeSpan? timeout = null)
        {
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);
            App.WaitWhileBusy(TimeSpan.FromSeconds(5));
            var window = Retry.WhileNull(
                () => App.GetMainWindow(Automation, TimeSpan.FromSeconds(2)),
                timeout: effectiveTimeout
            ).Result;
            return window ?? throw new InvalidOperationException($"Main window was not found within {effectiveTimeout.TotalSeconds}s.");
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
