using Microsoft.UI.Xaml;
using System.IO;

namespace Overlay;

public partial class App : Application
{
    private const string LogFile = "WinUIOverlay_crash.log";

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            File.WriteAllText(LogFile, $"FATAL: {e.ExceptionObject}\n{e.IsTerminating}");
        TaskScheduler.UnobservedTaskException += (s, e) =>
            File.WriteAllText(LogFile, $"TASK: {e.Exception}");
        Current.UnhandledException += (s, e) =>
        {
            File.WriteAllText(LogFile, $"UI: {e.Exception}\n{e.Handled}");
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Length > 1 && cmdArgs[1] == "--overlay")
            {
                var config = Config.Load();
                var overlay = new OverlayWindow(config);
                overlay.Activate();
            }
            else
            {
                var mainWindow = new MainWindow();
                mainWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(LogFile, $"LAUNCH: {ex}");
            throw;
        }
    }
}
