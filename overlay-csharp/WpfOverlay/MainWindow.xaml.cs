using Microsoft.Win32;
using Overlay.Services;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Text.Json;
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
    private bool _restartingRoom;  // prevent recursive room switch restart

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
        HoverHideToggle.IsChecked = _config.Danmaku.HoverHideEnabled;

        RefreshRoomList();
        UpdateSliderLabels();
    }

    private void SaveConfigToFile()
    {
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
        _config.Danmaku.HoverHideEnabled = HoverHideToggle.IsChecked ?? false;
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

    private int GetSelectedRoomId()
    {
        var room = _config.CurrentRoom;
        return room.RoomId > 0 ? room.RoomId : _config.RoomId;
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

        var roomId = GetSelectedRoomId();
        Log($"启动后端 — 房间 {roomId}");
        _processManager.Start(projectRoot, roomId, _config.PythonPath);
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
    //  Room Management
    // ════════════════════════════════════════════════════════════

    private void RefreshRoomList()
    {
        RoomListBox.Items.Clear();
        for (int i = 0; i < _config.Rooms.Count; i++)
        {
            var room = _config.Rooms[i];
            var text = room.DisplayText;
            if (i == _config.SelectedRoomIndex)
                text = "✓ " + text;
            RoomListBox.Items.Add(text);
        }

        if (_config.Rooms.Count > 0)
        {
            RoomListBox.SelectedIndex = Math.Clamp(_config.SelectedRoomIndex, 0, _config.Rooms.Count - 1);
            // Sync label box with selected room
            var sel = _config.Rooms[_config.SelectedRoomIndex];
            RoomLabelBox.Text = sel.Label;
        }
    }

    private void OnSaveRoomLabel(object sender, RoutedEventArgs e)
    {
        if (_config.SelectedRoomIndex < 0 || _config.SelectedRoomIndex >= _config.Rooms.Count)
        {
            Log("[ERR] 请先选择一个房间");
            return;
        }

        _config.Rooms[_config.SelectedRoomIndex].Label = RoomLabelBox.Text.Trim();
        _config.Save();
        RefreshRoomList();
        Log($"备注已保存: {_config.Rooms[_config.SelectedRoomIndex].DisplayText}");
    }

    private void OnAddRoom(object sender, RoutedEventArgs e)
    {
        var roomId = (int)(NewRoomIdBox.Value ?? 0);
        if (roomId <= 0)
        {
            Log("[ERR] 请输入有效的房间号");
            return;
        }

        // Check for duplicates
        if (_config.Rooms.Any(r => r.RoomId == roomId))
        {
            Log($"房间 {roomId} 已在列表中");
            return;
        }

        _config.Rooms.Add(new Config.RoomEntry { RoomId = roomId, Label = "" });
        _config.Save();
        RefreshRoomList();
        NewRoomIdBox.Value = 0;
        Log($"已添加房间 {roomId}");
    }

    private async void OnFetchRoomName(object sender, RoutedEventArgs e)
    {
        var targetId = 0;
        if (RoomListBox.SelectedIndex >= 0 && RoomListBox.SelectedIndex < _config.Rooms.Count)
            targetId = _config.Rooms[RoomListBox.SelectedIndex].RoomId;

        if (targetId <= 0)
            targetId = (int)(NewRoomIdBox.Value ?? 0);

        if (targetId <= 0)
        {
            Log("[ERR] 请先在列表中选择一个房间，或在输入框中输入房间号");
            return;
        }

        await FetchAndUpdateRoomName(targetId);
    }

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" },
            { "Referer", "https://live.bilibili.com/" },
        }
    };

    private async Task FetchAndUpdateRoomName(int roomId)
    {
        try
        {
            Log($"正在获取房间 {roomId} 信息...");

            // Step 1: get room info (uid, uname, title)
            var getInfoUrl = $"https://api.live.bilibili.com/room/v1/Room/get_info?room_id={roomId}";
            var resp = await _httpClient.GetAsync(getInfoUrl);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body);

            if (json.RootElement.GetProperty("code").GetInt32() != 0)
            {
                Log($"[ERR] B站API错误: {json.RootElement.GetProperty("message").GetString()}");
                return;
            }

            var data = json.RootElement.GetProperty("data");
            var fetchedId = data.GetProperty("room_id").GetInt32();
            var uid = data.GetProperty("uid").GetInt64();
            var title = data.GetProperty("title").GetString() ?? "";
            var liveStatus = data.GetProperty("live_status").GetInt32();
            var uname = data.TryGetProperty("uname", out var unameProp) ? unameProp.GetString() ?? "" : "";

            Log($"房间 {fetchedId} uid={uid} uname='{uname}' title='{title}'");

            // Step 2: if uname empty, get from Master/info API
            if (string.IsNullOrEmpty(uname) && uid > 0)
            {
                Log($"通过 uid={uid} 获取主播名...");
                var masterUrl = $"https://api.live.bilibili.com/live_user/v1/Master/info?uid={uid}";
                var masterResp = await _httpClient.GetAsync(masterUrl);
                masterResp.EnsureSuccessStatusCode();
                var masterBody = await masterResp.Content.ReadAsStringAsync();
                var masterJson = JsonDocument.Parse(masterBody);

                if (masterJson.RootElement.GetProperty("code").GetInt32() == 0)
                {
                    uname = masterJson.RootElement.GetProperty("data")
                        .GetProperty("info").GetProperty("uname").GetString() ?? "";
                }
            }

            Log($"结果: uname='{uname}'");

            // Update the room entry in the list
            var index = _config.Rooms.FindIndex(r => r.RoomId == roomId);
            if (index >= 0)
            {
                _config.Rooms[index].Label = uname;
                _config.Rooms[index].RoomId = fetchedId;
                Log($"已更新房间[{index}] 备注='{uname}'");
            }
            else
            {
                Log($"[WARN] 房间列表中找不到 roomId={roomId}");
            }

            _config.Save();
            RefreshRoomList();

            var statusText = liveStatus switch
            {
                1 => "直播中",
                2 => "轮播",
                _ => "未开播",
            };
            Log($"✓ 房间 {fetchedId} → {uname} | {title} [{statusText}]");
        }
        catch (TaskCanceledException)
        {
            Log("[ERR] 获取房间信息超时（15秒）");
        }
        catch (HttpRequestException ex)
        {
            Log($"[ERR] HTTP请求失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log($"[ERR] 获取房间信息失败: {ex.Message}");
        }
    }

    private void OnRemoveRoom(object sender, RoutedEventArgs e)
    {
        if (RoomListBox.SelectedIndex < 0 || RoomListBox.SelectedIndex >= _config.Rooms.Count)
        {
            Log("[ERR] 请先选择要删除的房间");
            return;
        }

        var idx = RoomListBox.SelectedIndex;
        var room = _config.Rooms[idx];
        _config.Rooms.RemoveAt(idx);

        // Adjust selected index after removal
        if (idx <= _config.SelectedRoomIndex)
            _config.SelectedRoomIndex--;
        _config.SelectedRoomIndex = Math.Clamp(_config.SelectedRoomIndex, 0, Math.Max(0, _config.Rooms.Count - 1));

        if (_config.Rooms.Count > 0)
            _config.RoomId = _config.Rooms[_config.SelectedRoomIndex].RoomId;
        else
            _config.RoomId = 0;

        _config.Save();
        RefreshRoomList();
        Log($"已删除房间 {room.RoomId}");
    }

    private void OnRoomSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restartingRoom) return;
        if (RoomListBox.SelectedIndex < 0) return;

        var newIndex = RoomListBox.SelectedIndex;
        if (newIndex >= _config.Rooms.Count) return;

        // Don't re-select the same room
        if (newIndex == _config.SelectedRoomIndex) return;

        _config.SelectedRoomIndex = newIndex;
        _config.RoomId = _config.Rooms[newIndex].RoomId;
        _config.Save();
        RefreshRoomList();

        var room = _config.Rooms[newIndex];
        Log($"已切换到房间: {room.DisplayText}");

        // Auto-restart backend if running
        if (_processManager.IsRunning)
        {
            _restartingRoom = true;
            Log("后端正在切换房间...");
            var projectRoot = GetProjectRoot();
            if (projectRoot != null)
            {
                _processManager.Stop();
                _processManager.Start(projectRoot, room.RoomId, _config.PythonPath);
            }
            _restartingRoom = false;
        }
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
