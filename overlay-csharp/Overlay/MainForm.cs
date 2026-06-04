using System.Diagnostics;
using System.Text.Json;

namespace Overlay;

public class MainForm : Form
{
    // --- Config ---
    private Config _config = null!;
    private string _configPath;

    // --- Process management ---
    private Process? _pythonProcess;
    private OverlayForm? _overlayForm;

    // ========== Controls ==========

    // Main layout
    private TabControl _tabControl = null!;
    private Panel _bottomPanel = null!;
    private TextBox _txtLog = null!;

    // Tab 1 — Basic settings
    private NumericUpDown _nudRoomId = null!;
    private TextBox _txtPipeName = null!;
    private TextBox _txtSessdata = null!;
    private ComboBox _cboDisplay = null!;
    private TextBox _txtPythonCmd = null!;

    // Tab 2 — Danmaku
    private NumericUpDown _nudDmFontSize = null!;
    private NumericUpDown _nudDmSpeed = null!;
    private TrackBar _trkDmOpacity = null!;
    private Label _lblDmOpacityVal = null!;
    private NumericUpDown _nudDmTrackCount = null!;

    // Tab 3 — SuperChat
    private NumericUpDown _nudScFontSize = null!;
    private NumericUpDown _nudScDuration = null!;

    // Status bar
    private Label _lblBackendStatus = null!;
    private Button _btnBackendToggle = null!;
    private Label _lblOverlayStatus = null!;
    private Button _btnOverlayToggle = null!;

    // Buttons
    private Button _btnStartAll = null!;
    private Button _btnStopAll = null!;
    private Button _btnSave = null!;

    // System tray
    private NotifyIcon _notifyIcon = null!;
    private ContextMenuStrip _trayMenu = null!;

    // ============================================================

    public MainForm()
    {
        Text = "BLivedm Notification";
        Size = new Size(620, 660);
        MinimumSize = new Size(520, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        Icon = SystemIcons.Application;

        _configPath = Config.ResolveConfigPath();
        ReloadConfig();

        InitializeControls();
        LoadConfigToUI();

        // Diagnostic: show loaded config
        Log($"配置文件: {_configPath}");
        Log($"加载值: room_id={_config.RoomId}, sessdata={(string.IsNullOrEmpty(_config.Sessdata) ? "空" : "已设置")}, dm字号={_config.Danmaku.FontSize}, dm速度={_config.Danmaku.Speed}, dm不透明度={_config.Danmaku.Opacity}, 轨道数={_config.Danmaku.TrackCount}, SC字号={_config.SuperChat.FontSize}, SC停留={_config.SuperChat.DurationMs}ms");

        UpdateAllStatus();
    }

    // ──────────────── Control creation ────────────────

    private void InitializeControls()
    {
        // ---- TabControl ----
        _tabControl = new TabControl { Dock = DockStyle.Fill };
        _tabControl.TabPages.Add(CreateBasicTab());
        _tabControl.TabPages.Add(CreateDanmakuTab());
        _tabControl.TabPages.Add(CreateSuperChatTab());
        _tabControl.TabPages.Add(CreateAboutTab());
        Controls.Add(_tabControl);

        // ---- Bottom panel: status + buttons + log ----
        _bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 170 };
        Controls.Add(_bottomPanel);

        // Log textbox
        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(200, 200, 200),
            Font = new Font("Consolas", 9F),
            WordWrap = false,
        };
        _bottomPanel.Controls.Add(_txtLog);

        // Status panel
        var statusPanel = new Panel { Dock = DockStyle.Top, Height = 68, Padding = new Padding(10, 6, 10, 4) };
        _bottomPanel.Controls.Add(statusPanel);

        CreateStatusControls(statusPanel);

        // ---- System tray ----
        SetupTrayIcon();
    }

    // ──────────────── Tab pages ────────────────

    private TabPage CreateBasicTab()
    {
        var page = new TabPage("基本设置");
        var gb = CreateGroupBox(page, "连接与显示");

        int y = 24;
        CreateLabel(gb, "房间号 (Room ID):", 14, y);
        _nudRoomId = CreateNud(gb, 160, y - 1, 1, 99999999999, 510, 150);

        y += 32;
        CreateLabel(gb, "管道名称:", 14, y);
        _txtPipeName = CreateTextBox(gb, 160, y - 1, 300);

        y += 32;
        CreateLabel(gb, "SESSDATA (留空则匿名):", 14, y);
        _txtSessdata = CreateTextBox(gb, 160, y - 1, 350);
        _txtSessdata.PasswordChar = '*';

        y += 32;
        CreateLabel(gb, "显示器:", 14, y);
        _cboDisplay = new ComboBox
        {
            Location = new Point(160, y - 1),
            Size = new Size(300, 22),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        for (int i = 0; i < Screen.AllScreens.Length; i++)
        {
            var s = Screen.AllScreens[i];
            _cboDisplay.Items.Add($"显示器 {i + 1} ({s.Bounds.Width}x{s.Bounds.Height})");
        }
        gb.Controls.Add(_cboDisplay);

        y += 32;
        CreateLabel(gb, "Python 命令:", 14, y);
        _txtPythonCmd = CreateTextBox(gb, 160, y - 1, 200);
        _txtPythonCmd.Text = "python";

        return page;
    }

    private TabPage CreateDanmakuTab()
    {
        var page = new TabPage("弹幕设置");
        page.Padding = new Padding(24, 16, 24, 16);

        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(0),
        };
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(int row, string label, Control ctrl, Control? extra = null)
        {
            var lbl = new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            tlp.Controls.Add(lbl, 0, row);
            ctrl.Dock = DockStyle.Fill;
            ctrl.Margin = new Padding(0, 2, 4, 2);
            tlp.Controls.Add(ctrl, 1, row);
            if (extra != null)
            {
                extra.Dock = DockStyle.Fill;
                extra.Margin = new Padding(0, 2, 0, 2);
                tlp.Controls.Add(extra, 2, row);
            }
        }

        _nudDmFontSize = new NumericUpDown { Minimum = 12, Maximum = 120, Value = 28 };
        AddRow(0, "字号:", _nudDmFontSize);

        _nudDmSpeed = new NumericUpDown { Minimum = 50, Maximum = 2000, Value = 300 };
        AddRow(1, "速度 (px/s):", _nudDmSpeed);

        _trkDmOpacity = new TrackBar { Minimum = 0, Maximum = 100, Value = 90, TickFrequency = 10 };
        _lblDmOpacityVal = new Label { Text = "90%", TextAlign = ContentAlignment.MiddleLeft };
        _trkDmOpacity.ValueChanged += (_, _) => _lblDmOpacityVal.Text = $"{_trkDmOpacity.Value}%";
        AddRow(2, "不透明度:", _trkDmOpacity, _lblDmOpacityVal);

        _nudDmTrackCount = new NumericUpDown { Minimum = 1, Maximum = 50, Value = 12 };
        AddRow(3, "轨道数:", _nudDmTrackCount);

        page.Controls.Add(tlp);
        return page;
    }

    private TabPage CreateSuperChatTab()
    {
        var page = new TabPage("醒目留言");
        page.Padding = new Padding(24, 16, 24, 16);

        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(0),
        };
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        void AddRow(int row, string label, Control ctrl)
        {
            var lbl = new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            tlp.Controls.Add(lbl, 0, row);
            ctrl.Dock = DockStyle.Fill;
            ctrl.Margin = new Padding(0, 2, 0, 2);
            tlp.Controls.Add(ctrl, 1, row);
        }

        _nudScFontSize = new NumericUpDown { Minimum = 12, Maximum = 120, Value = 40 };
        AddRow(0, "字号:", _nudScFontSize);

        _nudScDuration = new NumericUpDown { Minimum = 1000, Maximum = 120000, Increment = 1000, Value = 15000 };
        AddRow(1, "停留时间 (ms):", _nudScDuration);

        page.Controls.Add(tlp);
        return page;
    }

    private TabPage CreateAboutTab()
    {
        var page = new TabPage("关于");
        var box = new Label
        {
            Text = "BLivedm Notification\n\n" +
                   "B站直播弹幕 Overlay\n" +
                   "Python (blivedm) → Named Pipe → C# (Direct2D)\n\n" +
                   $"版本: {Application.ProductVersion}\n" +
                   ".NET 8.0 Windows\n\n" +
                   "使用说明:\n" +
                   "1. 填写直播间 ID 和 SESSDATA\n" +
                   "2. 点击\"保存\"或\"全部启动\"\n" +
                   "3. 叠加层中按 ESC 返回",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(16),
            ForeColor = Color.FromArgb(60, 60, 60),
        };
        page.Controls.Add(box);
        return page;
    }

    // ──────────────── Status / button panel ────────────────

    private void CreateStatusControls(Panel parent)
    {
        // Row 1: backend status
        _lblBackendStatus = new Label
        {
            Text = "● 后端: 已停止",
            ForeColor = Color.Red,
            Location = new Point(14, 6),
            Size = new Size(180, 24),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        };
        parent.Controls.Add(_lblBackendStatus);

        _btnBackendToggle = new Button
        {
            Text = "启动后端",
            Location = new Point(200, 5),
            Size = new Size(100, 26),
        };
        _btnBackendToggle.Click += (_, _) => ToggleBackend();
        parent.Controls.Add(_btnBackendToggle);

        // Row 2: overlay status
        _lblOverlayStatus = new Label
        {
            Text = "● 叠加层: 已隐藏",
            ForeColor = Color.Red,
            Location = new Point(14, 36),
            Size = new Size(180, 24),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        };
        parent.Controls.Add(_lblOverlayStatus);

        _btnOverlayToggle = new Button
        {
            Text = "显示叠加层",
            Location = new Point(200, 35),
            Size = new Size(100, 26),
        };
        _btnOverlayToggle.Click += (_, _) => ToggleOverlay();
        parent.Controls.Add(_btnOverlayToggle);

        // Action buttons on the right
        _btnStartAll = new Button
        {
            Text = "全部启动",
            Location = new Point(340, 5),
            Size = new Size(100, 28),
            BackColor = Color.FromArgb(220, 255, 220),
        };
        _btnStartAll.Click += (_, _) => StartAll();
        parent.Controls.Add(_btnStartAll);

        _btnStopAll = new Button
        {
            Text = "全部停止",
            Location = new Point(340, 35),
            Size = new Size(100, 28),
            BackColor = Color.FromArgb(255, 220, 220),
        };
        _btnStopAll.Click += (_, _) => StopAll();
        parent.Controls.Add(_btnStopAll);

        _btnSave = new Button
        {
            Text = "保存配置",
            Location = new Point(460, 5),
            Size = new Size(100, 28),
        };
        _btnSave.Click += (_, _) => { SaveConfig(); Log("配置已保存。"); };
        parent.Controls.Add(_btnSave);
    }

    // ──────────────── System tray ────────────────

    private void SetupTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());
        _trayMenu.Items.Add("启动全部", null, (_, _) => StartAll());
        _trayMenu.Items.Add("停止全部", null, (_, _) => StopAll());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("退出", null, (_, _) => Application.Exit());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "BLivedm Notification",
            ContextMenuStrip = _trayMenu,
            Visible = false,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    // ──────────────── Config I/O ────────────────

    private void ReloadConfig()
    {
        _config = Config.Load(_configPath);
        _configPath = _config.ConfigPath ?? _configPath;
    }

    private void LoadConfigToUI()
    {
        _nudRoomId.Value = Math.Clamp(_config.RoomId, 1, 99999999999);

        _txtPipeName.Text = _config.PipeName;
        _txtSessdata.Text = _config.Sessdata ?? "";

        var di = _config.DisplayIndex;
        if (di >= 0 && di < _cboDisplay.Items.Count)
            _cboDisplay.SelectedIndex = di;
        else if (_cboDisplay.Items.Count > 0)
            _cboDisplay.SelectedIndex = 0;

        _nudDmFontSize.Value = Math.Clamp((decimal)_config.Danmaku.FontSize, 12, 120);
        _nudDmSpeed.Value = Math.Clamp((decimal)_config.Danmaku.Speed, 50, 2000);
        _trkDmOpacity.Value = Math.Clamp((int)(_config.Danmaku.Opacity * 100), 0, 100);
        _lblDmOpacityVal.Text = $"{_trkDmOpacity.Value}%";
        _nudDmTrackCount.Value = Math.Clamp(_config.Danmaku.TrackCount, 1, 50);

        _nudScFontSize.Value = Math.Clamp((decimal)_config.SuperChat.FontSize, 12, 120);
        _nudScDuration.Value = Math.Clamp(_config.SuperChat.DurationMs, 1000, 120000);
    }

    private void SaveConfig()
    {
        _config.RoomId = (int)_nudRoomId.Value;
        _config.PipeName = _txtPipeName.Text.Trim();
        _config.Sessdata = _txtSessdata.Text.Trim();
        _config.DisplayIndex = _cboDisplay.SelectedIndex >= 0
            ? _cboDisplay.SelectedIndex
            : 0;

        _config.Danmaku.FontSize = (float)_nudDmFontSize.Value;
        _config.Danmaku.Speed = (float)_nudDmSpeed.Value;
        _config.Danmaku.Opacity = _trkDmOpacity.Value / 100f;
        _config.Danmaku.TrackCount = (int)_nudDmTrackCount.Value;

        _config.SuperChat.FontSize = (float)_nudScFontSize.Value;
        _config.SuperChat.DurationMs = (int)_nudScDuration.Value;

        _config.Save(_configPath);

        // Re-read so ConfigPath etc are synced
        ReloadConfig();
    }

    // ──────────────── Process management ────────────────

    private void ToggleBackend()
    {
        if (_pythonProcess is { HasExited: false })
            StopBackend();
        else
            StartBackend();
    }

    private void ToggleOverlay()
    {
        if (_overlayForm != null && !_overlayForm.IsDisposed && _overlayForm.Visible)
            HideOverlay();
        else
            ShowOverlay();
    }

    private void StartBackend()
    {
        SaveConfig();

        // Derive project root from config.json location (more reliable than exe path)
        var projectRoot = Path.GetDirectoryName(_configPath);
        if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
        {
            Log("[ERR] 无法确定项目根目录 (config.json 路径无效)。");
            return;
        }

        var mainPy = Path.Combine(projectRoot, "py_overlay", "main.py");
        if (!File.Exists(mainPy))
        {
            Log($"[ERR] 找不到 {mainPy}");
            return;
        }

        var pythonCmd = _txtPythonCmd.Text.Trim();
        if (string.IsNullOrEmpty(pythonCmd))
            pythonCmd = "python";

        var roomId = (int)_nudRoomId.Value;

        var psi = new ProcessStartInfo
        {
            FileName = pythonCmd,
            Arguments = $"\"{mainPy}\" --room {roomId}",
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        try
        {
            _pythonProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _pythonProcess.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    BeginInvoke(() => Log(e.Data));
            };
            _pythonProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    BeginInvoke(() => Log($"[ERR] {e.Data}"));
            };
            _pythonProcess.Exited += (_, _) =>
            {
                BeginInvoke(() =>
                {
                    Log("后端进程已退出。");
                    UpdateBackendStatus(false);
                });
            };

            _pythonProcess.Start();
            _pythonProcess.BeginOutputReadLine();
            _pythonProcess.BeginErrorReadLine();

            Log($"后端已启动 (PID: {_pythonProcess.Id})");
            UpdateBackendStatus(true);
        }
        catch (Exception ex)
        {
            Log($"[ERR] 启动后端失败: {ex.Message}");
            _pythonProcess?.Dispose();
            _pythonProcess = null;
        }
    }

    private void StopBackend()
    {
        if (_pythonProcess is { HasExited: false })
        {
            try
            {
                _pythonProcess.Kill(entireProcessTree: true);
                Log("后端已停止。");
            }
            catch (Exception ex)
            {
                Log($"[ERR] 停止后端失败: {ex.Message}");
            }
            _pythonProcess.Dispose();
            _pythonProcess = null;
        }
        UpdateBackendStatus(false);
    }

    private void ShowOverlay()
    {
        SaveConfig();

        try
        {
            // Always recreate with latest config — close old form if hidden
            if (_overlayForm != null && !_overlayForm.IsDisposed)
            {
                _overlayForm.FormClosed -= OverlayFormClosed;
                _overlayForm.Close();
                _overlayForm.Dispose();
                _overlayForm = null;
            }

            _overlayForm = new OverlayForm(_config);
            _overlayForm.FormClosed += OverlayFormClosed;
            _overlayForm.Show();
            UpdateOverlayStatus(true);
            HideMainWindow();
            Log("叠加层已显示。");
        }
        catch (Exception ex)
        {
            Log($"[ERR] 显示叠加层失败: {ex.Message}");
        }
    }

    private void OverlayFormClosed(object? sender, FormClosedEventArgs e)
    {
        _overlayForm = null;
        BeginInvoke(() =>
        {
            UpdateOverlayStatus(false);
            ShowMainWindow();
        });
    }

    private void HideOverlay()
    {
        if (_overlayForm != null && !_overlayForm.IsDisposed)
        {
            // Close (not just hide) — ShowOverlay recreates fresh each time
            _overlayForm.FormClosed -= OverlayFormClosed;
            _overlayForm.Close();
            _overlayForm.Dispose();
            _overlayForm = null;
            UpdateOverlayStatus(false);
            ShowMainWindow();
        }
    }

    private void StartAll()
    {
        StartBackend();
        ShowOverlay();
    }

    private void StopAll()
    {
        HideOverlay();
        StopBackend();
    }

    // ──────────────── Status UI ────────────────

    private void UpdateBackendStatus(bool running)
    {
        _lblBackendStatus.Text = running ? "● 后端: 运行中" : "● 后端: 已停止";
        _lblBackendStatus.ForeColor = running ? Color.Green : Color.Red;
        _btnBackendToggle.Text = running ? "停止后端" : "启动后端";
    }

    private void UpdateOverlayStatus(bool visible)
    {
        _lblOverlayStatus.Text = visible ? "● 叠加层: 运行中" : "● 叠加层: 已隐藏";
        _lblOverlayStatus.ForeColor = visible ? Color.Green : Color.Red;
        _btnOverlayToggle.Text = visible ? "隐藏叠加层" : "显示叠加层";
    }

    private void UpdateAllStatus()
    {
        var backendAlive = _pythonProcess is { HasExited: false };
        var overlayVisible = _overlayForm != null && !_overlayForm.IsDisposed && _overlayForm.Visible;
        UpdateBackendStatus(backendAlive);
        UpdateOverlayStatus(overlayVisible);
    }

    // ──────────────── Window management ────────────────

    private void ShowMainWindow()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ShowMainWindow);
            return;
        }
        WindowState = FormWindowState.Normal;
        Show();
        BringToFront();
        _notifyIcon.Visible = false;
    }

    private void HideMainWindow()
    {
        if (InvokeRequired)
        {
            BeginInvoke(HideMainWindow);
            return;
        }
        Hide();
        _notifyIcon.Visible = true;
    }

    // ──────────────── Logging ────────────────

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _txtLog.AppendText($"[{timestamp}] {message}\r\n");
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    // ──────────────── Path resolution ────────────────

    // (project root derived from config.json path in StartBackend)

    // ──────────────── Window lifecycle ────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopBackend();
        if (_overlayForm != null && !_overlayForm.IsDisposed)
            _overlayForm.Close();
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
            _notifyIcon.Visible = true;
        }
    }

    // ──────────────── Helper factories ────────────────

    private static GroupBox CreateGroupBox(Control parent, string text)
    {
        var gb = new GroupBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 20, 12, 12),
        };
        parent.Controls.Add(gb);
        return gb;
    }

    private static Label CreateLabel(Control parent, string text, int x, int y)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(140, 22),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        parent.Controls.Add(lbl);
        return lbl;
    }

    private static NumericUpDown CreateNud(Control parent, int x, int y,
        decimal min, decimal max, decimal val, int width)
    {
        var nud = new NumericUpDown
        {
            Location = new Point(x, y),
            Size = new Size(width, 22),
            Minimum = min,
            Maximum = max,
            Value = val,
        };
        parent.Controls.Add(nud);
        return nud;
    }

    private static TextBox CreateTextBox(Control parent, int x, int y, int width)
    {
        var tb = new TextBox
        {
            Location = new Point(x, y),
            Size = new Size(width, 22),
        };
        parent.Controls.Add(tb);
        return tb;
    }
}
