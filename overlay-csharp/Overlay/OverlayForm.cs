using System.Runtime.InteropServices;

namespace Overlay;

public class OverlayForm : Form
{
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOPMOST = 0x8;
    private const int WS_EX_NOACTIVATE = 0x8000000;
    private const int GWL_EXSTYLE = -20;

    private readonly DanmakuEngine _engine;
    private readonly DanmakuRenderer _renderer;
    private readonly PipeClient _pipe;
    private readonly Config _config;
    private System.Windows.Forms.Timer? _animTimer;
    private bool _initialized;

    public OverlayForm(Config config)
    {
        _config = config;
        _engine = new DanmakuEngine(config);
        _renderer = new DanmakuRenderer(config);
        _pipe = new PipeClient(config.PipeName);

        ConfigureWindow();

        // Pipe message handler (called from pipe thread)
        _pipe.OnMessage += msg => _engine.HandleMessage(msg);
        _pipe.OnError += msg => Console.Error.WriteLine($"Pipe: {msg}");
        _ = Task.Run(() => _pipe.ConnectAsync());
    }

    private void ConfigureWindow()
    {
        var screen = Screen.AllScreens[_config.DisplayIndex];
        Bounds = screen.Bounds;

        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = false;
        TopMost = true;

        TransparencyKey = Color.Black;
        BackColor = Color.Black;

        Load += OnLoad;
    }

    private void OnLoad(object? sender, EventArgs e)
    {
        // Apply extended window styles for layered + transparent + topmost
        var exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
        exStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
        SetWindowLong(Handle, GWL_EXSTYLE, exStyle);

        // Init Direct2D
        _renderer.Initialize(Handle, Width, Height);
        _engine.ScreenWidth = Width;
        _engine.ScreenHeight = Height;

        // Use DirectWrite for accurate text measurement
        _engine.MeasureTextWidth = _renderer.MeasureText;

        // Animation timer: 60fps update + repaint
        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += (_, _) =>
        {
            _engine.Update();
            Invalidate(); // triggers OnPaint
        };
        _animTimer.Start();

        _initialized = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_initialized) return;

        var items = _engine.GetItems();
        _renderer.Render(items);
    }

    /// <summary>
    /// Suppress background painting to avoid flicker.
    /// D2D renders everything in OnPaint — no GDI background needed.
    /// </summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // no-op
    }

    protected override void WndProc(ref Message m)
    {
        // WM_ERASEBKGND — suppress to avoid background flash before D2D render
        if (m.Msg == 0x0014)
            return;
        base.WndProc(ref m);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_initialized)
        {
            _renderer?.Resize(Width, Height);
            _engine.ScreenWidth = Width;
            _engine.ScreenHeight = Height;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _animTimer?.Stop();
        _animTimer?.Dispose();
        _pipe.Disconnect();
        _renderer.Dispose();
        base.OnFormClosed(e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
