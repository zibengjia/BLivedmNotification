using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Overlay.Pages;
using Overlay.Services;
using System.Runtime.InteropServices;
using Windows.UI;
using WinRT.Interop;

namespace Overlay;

public sealed partial class MainWindow : Window
{
    // --- Services ---
    private readonly ConfigService _configService;
    private readonly ProcessManager _processManager;
    private OverlayWindow? _overlayWindow;

    // --- Pages ---
    private BasicSettingsPage? _pageBasic;
    private DanmakuSettingsPage? _pageDanmaku;
    private SuperChatSettingsPage? _pageSc;
    private LogPage? _pageLog;

    // --- System tray (Win32 Shell_NotifyIcon) ---
    private IntPtr _hwnd;
    private IntPtr _originalWndProc;
    private bool _trayIconAdded;
    private const int WM_TRAY_CALLBACK = 0x400 + 1;
    private const int TRAY_ICON_ID = 1001;

    // --- Project root ---
    private string ProjectRoot => Path.GetDirectoryName(_configService.ConfigPath) ?? ".";

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public MainWindow()
    {
        InitializeComponent();

        // Init services
        _configService = new ConfigService();
        _processManager = new ProcessManager();
        _processManager.LogMessage += Log;
        _processManager.StatusChanged += OnBackendStatusChanged;

        // Window setup
        Title = "BLivedm Notification";
        _hwnd = WindowNative.GetWindowHandle(this);

        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // DPI-aware sizing: 680x600 logical pixels
        var dpi = GetDpiForWindow(_hwnd);
        var scale = dpi / 96.0;
        appWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)(680 * scale),
            (int)(600 * scale)));

        appWindow.SetIcon("Assets/AppIcon.ico");

        // Hook window proc for tray messages
        _originalWndProc = GetWindowLongPtr(_hwnd, GWLP_WNDPROC);
        _wndProcDelegate = MainWndProc;
        SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

        // Create pages
        _pageBasic = new BasicSettingsPage(_configService);
        _pageDanmaku = new DanmakuSettingsPage(_configService);
        _pageSc = new SuperChatSettingsPage(_configService);
        _pageLog = new LogPage();

        // Nav to first page
        NavView.SelectedItem = (NavigationViewItem)NavView.MenuItems[0];
        NavigateToPage("basic");

        // Lifecycle
        Closed += OnWindowClosed;
    }

    // ──────────────── Navigation ────────────────

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            NavigateToPage(tag);
    }

    private void NavigateToPage(string tag)
    {
        UIElement page = tag switch
        {
            "basic" => _pageBasic!,
            "danmaku" => _pageDanmaku!,
            "superchat" => _pageSc!,
            "about" => new AboutPage(),
            "log" => _pageLog!,
            _ => _pageBasic!,
        };
        PageContainer.Content = page;
    }

    // ──────────────── Config ────────────────

    private void OnSaveConfig(object sender, RoutedEventArgs e)
    {
        SaveConfig();
        Log("配置已保存。");
    }

    private void SaveConfig()
    {
        // Read from UI
        if (_pageBasic != null)
        {
            _configService.ApplyBasicSettings(
                _pageBasic.RoomId, _pageBasic.PipeName,
                _pageBasic.Sessdata, _pageBasic.DisplayIndex);
        }
        if (_pageDanmaku != null)
        {
            _configService.ApplyDanmakuSettings(
                _pageDanmaku.DmFontSize, _pageDanmaku.DmSpeed,
                _pageDanmaku.DmOpacity, _pageDanmaku.DmTrackCount);
        }
        if (_pageSc != null)
        {
            _configService.ApplySuperChatSettings(
                _pageSc.ScFontSize, _pageSc.ScDurationMs);
        }

        _configService.Save();
    }

    // ──────────────── Backend management ────────────────

    private void OnToggleBackend(object sender, RoutedEventArgs e)
    {
        if (_processManager.IsRunning)
            _processManager.Stop();
        else
            StartBackend();
    }

    private void StartBackend()
    {
        SaveConfig();
        var cfg = _configService.Config;
        var pythonCmd = _pageBasic?.PythonCmd ?? "python";
        _processManager.Start(ProjectRoot, cfg.RoomId, pythonCmd);
    }

    private void OnBackendStatusChanged(bool running)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LblBackend.Text = running ? "后端: 运行中" : "后端: 已停止";
            BackendDot.Fill = new SolidColorBrush(running ? Colors.Green : Colors.Red);
            BtnBackend.Content = running ? "停止后端" : "启动后端";
        });
    }

    // ──────────────── Overlay management ────────────────

    private void OnToggleOverlay(object sender, RoutedEventArgs e)
    {
        if (_overlayWindow != null)
            HideOverlay();
        else
            ShowOverlay();
    }

    private void ShowOverlay()
    {
        if (_overlayWindow != null) return;

        SaveConfig();

        try
        {
            _overlayWindow = new OverlayWindow(_configService.Config);
            _overlayWindow.OverlayClosed += OnOverlayClosed;
            _overlayWindow.Activate();

            UpdateOverlayStatus(true);
            HideToTray();
            Log("叠加层已显示。");
        }
        catch (Exception ex)
        {
            Log($"[ERR] 显示叠加层失败: {ex.Message}");
            _overlayWindow = null;
        }
    }

    private void HideOverlay()
    {
        if (_overlayWindow == null) return;

        _overlayWindow.OverlayClosed -= OnOverlayClosed;
        _overlayWindow.CloseOverlay();
        _overlayWindow = null;

        UpdateOverlayStatus(false);
        ShowMainWindow();
    }

    private void OnOverlayClosed()
    {
        _overlayWindow = null;
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateOverlayStatus(false);
            ShowMainWindow();
        });
    }

    private void UpdateOverlayStatus(bool visible)
    {
        LblOverlay.Text = visible ? "叠加层: 运行中" : "叠加层: 已隐藏";
        OverlayDot.Fill = new SolidColorBrush(visible ? Colors.Green : Colors.Red);
        BtnOverlay.Content = visible ? "隐藏叠加层" : "显示叠加层";
    }

    // ──────────────── Start / Stop all ────────────────

    private void OnStartAll(object sender, RoutedEventArgs e)
    {
        StartBackend();
        ShowOverlay();
    }

    private void OnStopAll(object sender, RoutedEventArgs e)
    {
        HideOverlay();
        _processManager.Stop();
    }

    // ──────────────── System tray ────────────────

    private void HideToTray()
    {
        if (!_trayIconAdded)
            AddTrayIcon();

        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Hide();

        TrayShowWindow(false);
    }

    private void ShowMainWindow()
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Show(true);

        TrayShowWindow(true);
        RemoveTrayIcon();
    }

    private void AddTrayIcon()
    {
        var data = new NOTIFYICONDATA();
        data.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>();
        data.hWnd = _hwnd;
        data.uID = TRAY_ICON_ID;
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_TRAY_CALLBACK;
        data.hIcon = LoadIcon(GetModuleHandle(null), 32512); // IDI_APPLICATION
        data.szTip = "BLivedm Notification";

        // Add right-click context menu items
        data.uVersion = 0;

        Shell_NotifyIcon(NIM_ADD, ref data);
        Shell_NotifyIcon(NIM_SETVERSION, ref data);

        _trayIconAdded = true;
    }

    private void RemoveTrayIcon()
    {
        if (!_trayIconAdded) return;

        var data = new NOTIFYICONDATA();
        data.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>();
        data.hWnd = _hwnd;
        data.uID = TRAY_ICON_ID;

        Shell_NotifyIcon(NIM_DELETE, ref data);
        _trayIconAdded = false;
    }

    private void TrayShowWindow(bool show)
    {
        ShowWindow(_hwnd, show ? SW_SHOW : SW_HIDE);
    }

    // ──────────────── Window proc for tray messages ────────────────

    private WndProcDelegate? _wndProcDelegate;
    private const int GWLP_WNDPROC = -4;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private IntPtr MainWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAY_CALLBACK)
        {
            uint uMsg = (uint)lParam;
            if (uMsg == WM_LBUTTONDBLCLK || uMsg == WM_RBUTTONUP)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ShowMainWindow();
                });
            }
            return IntPtr.Zero;
        }
        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    // ──────────────── Logging ────────────────

    public void Log(string message)
    {
        _pageLog?.Append(message);
    }

    // ──────────────── Window lifecycle ────────────────

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _processManager.Stop();
        _overlayWindow?.CloseOverlay();
        RemoveTrayIcon();

        if (_wndProcDelegate != null)
        {
            SetWindowLongPtr(_hwnd, GWLP_WNDPROC,
                GetWindowLongPtr(_hwnd, GWLP_WNDPROC));
        }
    }

    // ──────────────── Win32 P/Invoke ────────────────

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint cmd, ref NOTIFYICONDATA data);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // --- Tray icon constants ---
    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1;
    private const uint NIF_ICON = 2;
    private const uint NIF_TIP = 4;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_SHOWMINIMIZED = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
}
