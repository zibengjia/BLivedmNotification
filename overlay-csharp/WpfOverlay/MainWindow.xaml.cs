using Microsoft.Win32;
using Overlay.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;

namespace Overlay;

public partial class MainWindow : FluentWindow
{
    private Config _config = null!;
    private readonly ProcessManager _processManager = new();
    private OverlayWindow? _overlay;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private bool _forceClose;

    private const int MaxLogLines = 1000;
    private readonly StringBuilder _logBuffer = new();
    private int _logLineCount;
    private bool _autoScroll = true;

    public MainWindow()
    {
        InitializeComponent();

        // Wire process manager events
        _processManager.StatusChanged += OnBackendStatusChanged;
        _processManager.LogMessage += Log;

        // Enumerate monitors into combo
        EnumerateMonitors();

        // Load saved settings
        LoadConfigToUI();

        // Init tray icon
        InitTrayIcon();

        // Wire auto-scroll toggle
        _autoScroll = AutoScrollToggle.IsChecked ?? true;
        AutoScrollToggle.Checked += (_, _) => _autoScroll = true;
        AutoScrollToggle.Unchecked += (_, _) => _autoScroll = false;
    }

    // ════════════════════════════════════════════════════════════
    //  Config I/O
    // ════════════════════════════════════════════════════════════

    private void LoadConfigToUI()
    {
        _config = Config.Load();
        RoomIdBox.Value = _config.RoomId;
        PipeNameBox.Text = _config.PipeName;
        if (!string.IsNullOrEmpty(_config.Sessdata))
            SessdataBox.Password = _config.Sessdata;
        DisplayCombo.SelectedIndex = Math.Clamp(_config.DisplayIndex, 0, DisplayCombo.Items.Count - 1);
        PythonPathBox.Text = _config.PythonPath;

        FontSizeSlider.Value = _config.Danmaku.FontSize;
        SpeedSlider.Value = _config.Danmaku.Speed;
        OpacitySlider.Value = _config.Danmaku.Opacity;
        TrackCountSlider.Value = _config.Danmaku.TrackCount;

        ScFontSizeSlider.Value = _config.SuperChat.FontSize;
        ScDurationSlider.Value = _config.SuperChat.DurationMs;

        // New settings
        SelectComboItem(FontWeightCombo, _config.Danmaku.FontWeight);
        SelectComboItem(PositionPriorityCombo, _config.Danmaku.PositionPriority);
        SelectComboItem(DensityCombo, _config.Danmaku.Density);
        ShadowEnabledToggle.IsChecked = _config.Danmaku.ShadowEnabled;
        ShadowOpacitySlider.Value = _config.Danmaku.ShadowOpacity;
        ShadowOffsetSlider.Value = _config.Danmaku.ShadowOffset;

        UpdateSliderLabels();
    }

    private void SaveConfigToFile()
    {
        _config.RoomId = (int)(RoomIdBox.Value ?? 0);
        _config.PipeName = PipeNameBox.Text.Trim();
        _config.Sessdata = SessdataBox.Password;
        _config.DisplayIndex = DisplayCombo.SelectedIndex >= 0 ? DisplayCombo.SelectedIndex : 0;
        _config.PythonPath = PythonPathBox.Text.Trim();

        _config.Danmaku.FontSize = (int)FontSizeSlider.Value;
        _config.Danmaku.Speed = (int)SpeedSlider.Value;
        _config.Danmaku.Opacity = (float)Math.Round(OpacitySlider.Value, 2);
        _config.Danmaku.TrackCount = (int)TrackCountSlider.Value;

        _config.SuperChat.FontSize = (int)ScFontSizeSlider.Value;
        _config.SuperChat.DurationMs = (int)ScDurationSlider.Value;

        // New settings
        _config.Danmaku.FontWeight = (FontWeightCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Normal";
        _config.Danmaku.PositionPriority = (PositionPriorityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Top";
        _config.Danmaku.Density = (DensityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Medium";
        _config.Danmaku.ShadowEnabled = ShadowEnabledToggle.IsChecked ?? true;
        _config.Danmaku.ShadowOpacity = (float)ShadowOpacitySlider.Value;
        _config.Danmaku.ShadowOffset = (float)ShadowOffsetSlider.Value;

        _config.Save();
        Log("配置已保存。");
    }

    private static void SelectComboItem(System.Windows.Controls.ComboBox combo, string value)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Content?.ToString() == value)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private void EnumerateMonitors()
    {
        DisplayCombo.Items.Clear();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            DisplayCombo.Items.Add(
                $"{screen.DeviceName} ({screen.Bounds.Width}×{screen.Bounds.Height})");
        }
        if (DisplayCombo.Items.Count > 0)
            DisplayCombo.SelectedIndex = 0;
    }

    // ════════════════════════════════════════════════════════════
    //  Backend (Python process)
    // ════════════════════════════════════════════════════════════

    private void OnBackendStatusChanged(bool running)
    {
        Dispatcher.Invoke(() =>
        {
            BackendStatus.Text = running ? "● 后端" : "○ 后端";
            BackendStatus.Foreground = running
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Gray;
            StartBackendBtn.IsEnabled = !running;
            StopBackendBtn.IsEnabled = running;
            UpdateStartAllButton();
        });
    }

    private void OnStartBackend(object sender, RoutedEventArgs e)
    {
        SaveConfigToFile();

        var projectRoot = GetProjectRoot();
        if (projectRoot == null)
        {
            Log("[ERR] 无法定位项目根目录（找不到 config.json）");
            return;
        }

        _processManager.Start(projectRoot, _config.RoomId, _config.PythonPath);
    }

    private void OnStopBackend(object sender, RoutedEventArgs e)
    {
        _processManager.Stop();
    }

    private string? GetProjectRoot()
    {
        // Walk up from executable directory looking for config.json
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "config.json")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }

    // ════════════════════════════════════════════════════════════
    //  Overlay (WPF transparent window)
    // ════════════════════════════════════════════════════════════

    private void OnShowOverlay(object sender, RoutedEventArgs e)
    {
        ShowOverlayWindow();
    }

    private void OnHideOverlay(object sender, RoutedEventArgs e)
    {
        HideOverlayWindow();
    }

    private void ShowOverlayWindow()
    {
        if (_overlay != null) return;

        SaveConfigToFile();
        _overlay = new OverlayWindow(_config);
        _overlay.OverlayClosed += OnOverlayClosed;
        _overlay.Show();

        OverlayStatus.Text = "● 覆盖层";
        OverlayStatus.Foreground = System.Windows.Media.Brushes.Green;
        ShowOverlayBtn.IsEnabled = false;
        HideOverlayBtn.IsEnabled = true;
        UpdateStartAllButton();
    }

    private void HideOverlayWindow()
    {
        if (_overlay == null) return;
        _overlay.OverlayClosed -= OnOverlayClosed;
        _overlay.CloseOverlay();
        _overlay = null;

        OverlayStatus.Text = "○ 覆盖层";
        OverlayStatus.Foreground = System.Windows.Media.Brushes.Gray;
        ShowOverlayBtn.IsEnabled = true;
        HideOverlayBtn.IsEnabled = false;
        UpdateStartAllButton();

        Show();
        Activate();
    }

    private void OnOverlayClosed()
    {
        Dispatcher.Invoke(() =>
        {
            _overlay = null;
            OverlayStatus.Text = "○ 覆盖层";
            OverlayStatus.Foreground = System.Windows.Media.Brushes.Gray;
            ShowOverlayBtn.IsEnabled = true;
            HideOverlayBtn.IsEnabled = false;
            UpdateStartAllButton();
            Show();
            Activate();
        });
    }

    // ════════════════════════════════════════════════════════════
    //  Start All / Stop All
    // ════════════════════════════════════════════════════════════

    private void OnStartAll(object sender, RoutedEventArgs e)
    {
        SaveConfigToFile();
        OnStartBackend(sender, e);
        ShowOverlayWindow();
    }

    private void OnStopAll(object sender, RoutedEventArgs e)
    {
        _processManager.Stop();
        HideOverlayWindow();
    }

    private void UpdateStartAllButton()
    {
        bool backendRunning = _processManager.IsRunning;
        bool overlayVisible = _overlay != null;

        StartAllBtn.IsEnabled = !backendRunning || !overlayVisible;
        StopAllBtn.IsEnabled = backendRunning || overlayVisible;
    }

    // ════════════════════════════════════════════════════════════
    //  Log
    // ════════════════════════════════════════════════════════════

    public void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            _logBuffer.Append($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            _logLineCount++;

            if (_logLineCount > MaxLogLines)
            {
                // Trim old lines
                var text = _logBuffer.ToString();
                var idx = text.IndexOf('\n', text.Length - 30000);
                if (idx > 0)
                {
                    _logBuffer.Remove(0, idx + 1);
                    _logLineCount = text.Count(c => c == '\n');
                }
            }

            LogBox.Text = _logBuffer.ToString();
            if (_autoScroll)
                LogBox.ScrollToEnd();
        });
    }

    private void OnClearLog(object sender, RoutedEventArgs e)
    {
        _logBuffer.Clear();
        _logLineCount = 0;
        LogBox.Text = "";
    }

    private void OnCopyLog(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_logBuffer.ToString());
            Log("日志已复制到剪贴板。");
        }
        catch (Exception ex)
        {
            Log($"[ERR] 复制失败: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Slider labels
    // ════════════════════════════════════════════════════════════

    private void OnDanmakuPreviewChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Safe guard: XAML loading may fire ValueChanged before fields are wired
        if (!IsLoaded) return;
        UpdateSliderLabels();
    }

    private void UpdateSliderLabels()
    {
        FontSizeLabel.Text = $"{(int)FontSizeSlider.Value}";
        SpeedLabel.Text = $"{(int)SpeedSlider.Value}";
        OpacityLabel.Text = $"{OpacitySlider.Value:F2}";
        TrackCountLabel.Text = $"{(int)TrackCountSlider.Value}";
        ScFontSizeLabel.Text = $"{(int)ScFontSizeSlider.Value}";
        ScDurationLabel.Text = $"{(int)ScDurationSlider.Value} ms";
        ShadowOpacityLabel.Text = $"{ShadowOpacitySlider.Value:F2}";
        ShadowOffsetLabel.Text = $"{ShadowOffsetSlider.Value:F1}";
    }

    // ════════════════════════════════════════════════════════════
    //  Shadow toggle
    // ════════════════════════════════════════════════════════════

    private void OnShadowToggled(object sender, RoutedEventArgs e)
    {
        bool enabled = ShadowEnabledToggle.IsChecked ?? false;
        ShadowOpacitySlider.IsEnabled = enabled;
        ShadowOffsetSlider.IsEnabled = enabled;
    }

    // ════════════════════════════════════════════════════════════
    //  SESSDATA show/hide
    // ════════════════════════════════════════════════════════════

    private void OnShowSessdata(object sender, RoutedEventArgs e)
    {
        // PasswordBox can't toggle visibility directly, this is a UX hint
        // For production, consider using a custom control
        Log("提示：SESSDATA 已保存，但在 UI 中始终以密码形式显示。");
    }

    // ════════════════════════════════════════════════════════════
    //  Browse Python
    // ════════════════════════════════════════════════════════════

    private void OnBrowsePython(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Python 可执行文件|python.exe;python3.exe;pythonw.exe;python3w.exe|所有文件|*.*",
            Title = "选择 Python 可执行文件",
        };
        if (dlg.ShowDialog() == true)
        {
            PythonPathBox.Text = dlg.FileName;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Tray Icon
    // ════════════════════════════════════════════════════════════

    private void InitTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "app.exe"),
            Text = "BLivedm Notification",
            Visible = true,
        };

        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindowFromTray());
        _trayMenu.Items.Add("全部启动", null, (_, _) => { Dispatcher.Invoke(OnStartAll, null, null!); });
        _trayMenu.Items.Add("全部停止", null, (_, _) => { Dispatcher.Invoke(OnStopAll, null, null!); });
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("退出", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip = _trayMenu;

        _trayIcon.DoubleClick += (_, _) => ShowMainWindowFromTray();
    }

    private void ShowMainWindowFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            Activate();
            WindowState = WindowState.Normal;
        });
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    // ════════════════════════════════════════════════════════════
    //  Close / Cleanup
    // ════════════════════════════════════════════════════════════

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose) return;

        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
    }

    private void ExitApp()
    {
        _forceClose = true;

        _processManager.Stop();
        _overlay?.CloseOverlay();
        _trayIcon?.Dispose();

        System.Windows.Application.Current.Shutdown();
    }
}
