using System.IO;
using System.Windows;

namespace Overlay;

public partial class App : System.Windows.Application
{
    private const string LogFile = "WpfOverlay_crash.log";

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            File.WriteAllText(LogFile, $"FATAL: {e.ExceptionObject}\nIsTerminating={e.IsTerminating}");

        DispatcherUnhandledException += (s, e) =>
        {
            File.WriteAllText(LogFile, $"DISPATCHER: {e.Exception}");
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
            File.WriteAllText(LogFile, $"TASK: {e.Exception}");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Log args for debugging
        var args = string.Join(" ", e.Args);
        File.WriteAllText(LogFile, $"ARGS: [{args}] count={e.Args.Length}\n");

        try
        {
            // Check args: both CLI args and raw command line
            bool overlayMode = e.Args.Contains("--overlay");
            if (!overlayMode)
            {
                var raw = Environment.CommandLine;
                overlayMode = raw.Contains("--overlay");
            }

            if (overlayMode)
            {
                File.AppendAllText(LogFile, "Starting in OVERLAY mode\n");
                var config = Config.Load();
                var overlay = new OverlayWindow(config);
                overlay.OverlayClosed += () => Dispatcher.Invoke(Shutdown);
                overlay.Show();
            }
            else
            {
                File.AppendAllText(LogFile, "Starting in LAUNCHER mode\n");
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText(LogFile, $"ONSTARTUP: {ex}");
            throw;
        }
    }
}
