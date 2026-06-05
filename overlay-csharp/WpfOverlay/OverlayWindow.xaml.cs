using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Overlay;

/// <summary>
/// Transparent fullscreen overlay window for danmaku display.
/// Uses WPF's AllowsTransparency for clean per-pixel alpha.
/// </summary>
public sealed partial class OverlayWindow : Window
{
    private readonly Config _config;
    private DanmakuEngine _engine = null!;
    private DanmakuRenderer _renderer = null!;
    private PipeClient _pipe = null!;
    private bool _initialized;
    private bool _closing;

    public event Action? OverlayClosed;

    public OverlayWindow(Config config)
    {
        _config = config;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        KeyDown += OnKeyDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (_initialized) return;
        LogDebug("OnSourceInitialized start");

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            LogDebug($"HWND={hwnd}");
            if (hwnd == IntPtr.Zero) return;

            // Apply click-through styles
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            LogDebug($"GWL_EXSTYLE before: 0x{exStyle:X8}");
            exStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
            exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            LogDebug($"GWL_EXSTYLE after: 0x{exStyle:X8}");

            // Fullscreen on target monitor
            var screens = System.Windows.Forms.Screen.AllScreens;
            var displayIndex = Math.Clamp(_config.DisplayIndex, 0, screens.Length - 1);
            var screen = screens[displayIndex];
            LogDebug($"Screen {displayIndex}: {screen.Bounds}");
            Left = screen.Bounds.X;
            Top = screen.Bounds.Y;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;

            // Init engine + renderer
            _engine = new DanmakuEngine(_config)
            {
                ScreenWidth = screen.Bounds.Width,
                ScreenHeight = screen.Bounds.Height,
            };
            _renderer = new DanmakuRenderer(_config);
            _engine.MeasureTextWidth = (text, fontSize) =>
                _renderer.MeasureText(text, fontSize);
            LogDebug("Engine+Renderer OK");

            // Wire renderer into visual tree
            DanmakuCanvas.Children.Add(_renderer);

            // Setup animation loop
            CompositionTarget.Rendering += OnRendering;

            // Start pipe client (background thread, let it fail quietly)
            _pipe = new PipeClient(_config.PipeName);
            // ⚠️ All pipe callbacks run on a background thread.
            // Engine.HandleMessage → MeasureTextWidth → renderer.MeasureText → FormattedText
            // requires UI thread. Dispatch to UI thread to avoid cross-thread exception.
            _pipe.OnMessage += msg => _ = Dispatcher.InvokeAsync(() =>
            {
                LogDebug($"Pipe msg received");
                _engine.HandleMessage(msg);
            });
            _pipe.OnError += msg => LogDebug($"Pipe err: {msg}");
            _pipe.OnConnected += () => LogDebug("Pipe connected event");
            _pipe.OnDisconnected += () => LogDebug("Pipe disconnected event");
            _ = Task.Run(() => _pipe.ConnectAsync());
            LogDebug("Pipe client starting");

            _initialized = true;
            LogDebug("OnSourceInitialized OK");
        }
        catch (Exception ex)
        {
            LogDebug($"OnSourceInitialized ERROR: {ex}");
            // Don't close — window stays visible (transparent) for debugging
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_closing) return;

        _engine.Update();
        _renderer.Sync(_engine.GetItems());
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            CloseOverlay();
    }

    public void CloseOverlay()
    {
        if (_closing) return;
        _closing = true;

        CompositionTarget.Rendering -= OnRendering;
        _pipe?.Disconnect();
        _renderer?.Clear();
        DanmakuCanvas.Children.Clear();
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!_closing)
        {
            _closing = true;
            CompositionTarget.Rendering -= OnRendering;
            _pipe?.Disconnect();
            _renderer?.Clear();
        }
        OverlayClosed?.Invoke();
    }

    // ---- Debug logging ----
    private static void LogDebug(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(
                "overlay_debug.log",
                $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    // ---- Win32 P/Invoke ----
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
}
