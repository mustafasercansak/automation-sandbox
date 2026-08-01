using FlaUI.Core;

using FlaUI.Core.AutomationElements;

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

            return new ApplicationConnector(app, new UIA3Automation());

        }



        public static ApplicationConnector Attach(string processName)

        {

            var app = Application.Attach(processName);

            return new ApplicationConnector(app, new UIA3Automation());

        }



        public Window GetMainWindow(TimeSpan? timeout = null)

        {

            return App.GetMainWindow(Automation, timeout ?? TimeSpan.FromSeconds(10))!;

        }



        public void Dispose()

        {

            Automation.Dispose();

            App.Dispose();

        }

    }

}
