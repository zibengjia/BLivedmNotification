using Overlay;

namespace Overlay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var config = Config.Load();
        var form = new OverlayForm(config);
        Application.Run(form);
    }
}
