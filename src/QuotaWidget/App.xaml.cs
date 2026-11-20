using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using QuotaWidget.Cli;

namespace QuotaWidget;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0)
        {
            AttachConsole(-1);
            int exitCode = CliRunner.RunAsync(e.Args).GetAwaiter().GetResult();
            Shutdown(exitCode);
            return;
        }

        const string mutexName = "Local\\QuotaWidgetSingleInstanceMutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch
        {
            // Ignore on shutdown
        }

        base.OnExit(e);
    }
}
