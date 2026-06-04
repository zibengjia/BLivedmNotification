using Microsoft.UI.Xaml.Controls;
using Overlay.Services;
using System.Runtime.InteropServices;

namespace Overlay.Pages;

public sealed partial class BasicSettingsPage : Page
{
    private readonly ConfigService _configService;

    public BasicSettingsPage(ConfigService configService)
    {
        InitializeComponent();
        _configService = configService;
        LoadMonitors();
        LoadConfig();
    }

    private void LoadMonitors()
    {
        CboDisplay.Items.Clear();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    int w = mi.rcMonitor.right - mi.rcMonitor.left;
                    int h = mi.rcMonitor.bottom - mi.rcMonitor.top;
                    CboDisplay.Items.Add($"显示器 {CboDisplay.Items.Count + 1} ({w}x{h})");
                }
                return true;
            }, IntPtr.Zero);

        if (CboDisplay.Items.Count == 0)
        {
            CboDisplay.Items.Add("显示器 1 (主显示器)");
        }
    }

    public void LoadConfig()
    {
        var cfg = _configService.Config;
        NudRoomId.Value = Math.Clamp(cfg.RoomId, 1, 99999999999);
        TxtPipeName.Text = cfg.PipeName;
        TxtSessdata.Password = cfg.Sessdata ?? "";

        var di = cfg.DisplayIndex;
        if (di >= 0 && di < CboDisplay.Items.Count)
            CboDisplay.SelectedIndex = di;
        else if (CboDisplay.Items.Count > 0)
            CboDisplay.SelectedIndex = 0;
    }

    public int RoomId => (int)NudRoomId.Value;
    public string PipeName => TxtPipeName.Text.Trim();
    public string Sessdata => TxtSessdata.Password;
    public int DisplayIndex => CboDisplay.SelectedIndex >= 0 ? CboDisplay.SelectedIndex : 0;
    public string PythonCmd => TxtPythonCmd.Text.Trim();

    // --- Win32 monitor enumeration ---
    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
