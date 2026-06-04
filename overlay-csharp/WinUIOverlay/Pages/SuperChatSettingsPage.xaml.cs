using Microsoft.UI.Xaml.Controls;
using Overlay.Services;

namespace Overlay.Pages;

public sealed partial class SuperChatSettingsPage : Page
{
    private readonly ConfigService _configService;

    public SuperChatSettingsPage(ConfigService configService)
    {
        InitializeComponent();
        _configService = configService;
        LoadConfig();
    }

    public void LoadConfig()
    {
        var sc = _configService.Config.SuperChat;
        NudScFontSize.Value = Math.Clamp((double)sc.FontSize, 12, 120);
        NudScDuration.Value = Math.Clamp(sc.DurationMs, 1000, 120000);
    }

    public float ScFontSize => (float)NudScFontSize.Value;
    public int ScDurationMs => (int)NudScDuration.Value;
}
