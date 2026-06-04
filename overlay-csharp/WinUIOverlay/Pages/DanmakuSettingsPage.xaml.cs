using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Overlay.Services;

namespace Overlay.Pages;

public sealed partial class DanmakuSettingsPage : Page
{
    private readonly ConfigService _configService;

    public DanmakuSettingsPage(ConfigService configService)
    {
        InitializeComponent();
        _configService = configService;

        SlDmOpacity.ValueChanged += OnOpacityChanged;
        LoadConfig();
    }

    private void OnOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        LblDmOpacityVal.Text = $"{(int)e.NewValue}%";
    }

    public void LoadConfig()
    {
        var dm = _configService.Config.Danmaku;
        NudDmFontSize.Value = Math.Clamp((double)dm.FontSize, 12, 120);
        NudDmSpeed.Value = Math.Clamp((double)dm.Speed, 50, 2000);
        SlDmOpacity.Value = Math.Clamp(dm.Opacity * 100, 0, 100);
        LblDmOpacityVal.Text = $"{(int)SlDmOpacity.Value}%";
        NudDmTrackCount.Value = Math.Clamp(dm.TrackCount, 1, 50);
    }

    public float DmFontSize => (float)NudDmFontSize.Value;
    public float DmSpeed => (float)NudDmSpeed.Value;
    public float DmOpacity => (float)SlDmOpacity.Value / 100f;
    public int DmTrackCount => (int)NudDmTrackCount.Value;
}
