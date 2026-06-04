namespace Overlay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && args[1] == "--overlay")
        {
            var config = Config.Load();
            Application.Run(new OverlayForm(config));
            return;
        }

        Application.Run(new MainForm());
    }
}
