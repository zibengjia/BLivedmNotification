using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.IO;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Overlay;

/// <summary>
/// Transparent fullscreen overlay window using WinUI 3 + Win2D.
/// Renders danmaku via CanvasControl, click-through via WS_EX_TRANSPARENT.
/// </summary>
public sealed partial class OverlayWindow : Window
{
    private readonly Config _config;
    private readonly DanmakuEngine _engine;
    private readonly Win2DRenderer _renderer;
    private readonly PipeClient _pipe;
    private DispatcherTimer? _animTimer;
    private bool _initialized;
    private bool _closing;

    // Win32 window style constants
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private const int HOTKEY_ID_ESCAPE = 9000;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_ESCAPE = 0x1B;

    public event Action? OverlayClosed;

    public OverlayWindow(Config config)
    {
        _config = config;
        _engine = new DanmakuEngine(config);
        _renderer = new Win2DRenderer(config);
        _pipe = new PipeClient(config.PipeName);

        InitializeComponent();

        // Custom transparent backdrop — prevents WinUI from painting opaque white
        SystemBackdrop = new TransparentBackdrop();

        // Set up CanvasControl
        DanmakuCanvas.Draw += OnCanvasDraw;

        // Pipe message handler
        _pipe.OnMessage += msg => _engine.HandleMessage(msg);
        _pipe.OnError += msg => System.Diagnostics.Debug.WriteLine($"Pipe: {msg}");

        // Window setup deferred to Loaded event (HWND available)
        RootGrid.Loaded += OnWindowLoaded;
        Closed += OnClosed;
    }

    private static void OverlayLog(string msg)
    {
        try { File.AppendAllText("overlay_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
        catch { }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;

        var hwnd = WindowNative.GetWindowHandle(this);
        OverlayLog($"HWND={hwnd}");

        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        OverlayLog("AppWindow OK");

        // ─── 1. Hook window proc early (for WM_ERASEBKGND) ───
        _originalOverlayWndProc = GetWindowLongPtr(hwnd, GWLP_WNDPROC);
        _overlayWndProcDelegate = OverlayWndProc;
        SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_overlayWndProcDelegate));
        OverlayLog("WndProc hooked");

        // ─── 2. OverlappedPresenter — remove title bar & borders ───
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            OverlayLog("Presenter configured");
        }

        // ─── 3. Set fullscreen on target monitor ───
        IntPtr hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf<MONITORINFO>();
        GetMonitorInfo(hMonitor, ref mi);

        int width = mi.rcMonitor.right - mi.rcMonitor.left;
        int height = mi.rcMonitor.bottom - mi.rcMonitor.top;
        int left = mi.rcMonitor.left;
        int top = mi.rcMonitor.top;
        OverlayLog($"Monitor: {left},{top} {width}x{height}");

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(left, top, width, height));

        // ─── 4. TransparentBackdrop already set in constructor ───

        // ─── 5. Clean Win32 standard window styles ───
        const int GWL_STYLE = -16;
        const uint WS_CAPTION = 0x00C00000;
        const uint WS_THICKFRAME = 0x00040000;
        const uint WS_MINIMIZEBOX = 0x00020000;
        const uint WS_MAXIMIZEBOX = 0x00010000;
        const uint WS_SYSMENU = 0x00080000;
        const uint BorderlessMask = WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;

        uint style = GetWindowLongPtrUint(hwnd, GWL_STYLE);
        OverlayLog($"GWL_STYLE before: 0x{style:X8}");
        SetWindowLong(hwnd, GWL_STYLE, style & ~BorderlessMask);
        style = GetWindowLongPtrUint(hwnd, GWL_STYLE);
        OverlayLog($"GWL_STYLE after: 0x{style:X8}");

        // ─── 6. Set extended styles: WS_EX_LAYERED | WS_EX_TOOLWINDOW ───
        uint exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        OverlayLog($"GWL_EXSTYLE before: 0x{exStyle:X8}");
        exStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        OverlayLog($"GWL_EXSTYLE after: 0x{exStyle:X8}");

        // ─── 7. SetLayeredWindowAttributes ───
        bool lwaOk = SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        OverlayLog($"SetLayeredWindowAttributes: {lwaOk} (LastErr={Marshal.GetLastPInvokeError()})");

        // ─── 7b. ApplyAccent — override WinUI composition ───
        // WinUI 3's DirectComposition root visual paints opaque white.
        // This call tells DWM to force per-pixel transparency, overriding
        // whatever the WinUI composition tree outputs.
        // ACCENT_ENABLE_TRANSPARENTGRADIENT (state=2) gives true per-pixel alpha.
        // Fallback: try ACCENT_ENABLE_ACRYLICBLURBEHIND (state=4) if this doesn't work.
        int accentResult = ApplyAccent(hwnd, ACCENT_ENABLE_TRANSPARENTGRADIENT, 0x00000000);
        OverlayLog($"Accent applied (state=2 gradient=0x00000000) result={accentResult} LastErr={Marshal.GetLastPInvokeError()}");

        // ─── 8. DWM — remove rounded corners & border outline ───
        uint cornerPreference = 1; // DWMWCP_DONOTROUND
        int dwm1 = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(uint));
        uint borderColor = 0xFFFFFFFE; // DWMWA_COLOR_NONE
        int dwm2 = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
        OverlayLog($"DWM corner={dwm1} border={dwm2}");

        // ─── 9. Apply styles via SWP_FRAMECHANGED ───
        SetWindowPos(hwnd, HWND_TOPMOST, left, top, width, height,
            SWP_NOACTIVATE | SWP_FRAMECHANGED);
        OverlayLog("SWP_FRAMECHANGED done");

        // ─── 10. Init engine dimensions ───
        _engine.ScreenWidth = width;
        _engine.ScreenHeight = height;

        // ─── 11. Provide text measurement ───
        _engine.MeasureTextWidth = (text, fontSize) =>
            _renderer.MeasureText(DanmakuCanvas, text, fontSize);

        // ─── 12. Start animation timer (60fps) ───
        _animTimer = new DispatcherTimer();
        _animTimer.Interval = TimeSpan.FromMilliseconds(16);
        _animTimer.Tick += OnTimerTick;
        _animTimer.Start();

        // ─── 13. Register hotkey for Escape ───
        RegisterHotKey(hwnd, HOTKEY_ID_ESCAPE, MOD_NOREPEAT, VK_ESCAPE);
        OverlayLog("Hotkey registered");

        // ─── 14. Start pipe connection ───
        _ = Task.Run(() => _pipe.ConnectAsync());

        // ─── 15. Enable click-through (after everything is set up) ───
        uint finalExStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        finalExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, finalExStyle);

        OverlayLog("Overlay initialized OK");
        _initialized = true;
    }

    private void OnTimerTick(object? sender, object e)
    {
        _engine.Update();
        DanmakuCanvas.Invalidate(); // Triggers redraw
    }

    private void OnCanvasDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var items = _engine.GetItems();
        _renderer.Render(args.DrawingSession, items);
    }

    /// <summary>
    /// Start pipe + overlay (called from launcher).
    /// </summary>
    public void Start()
    {
        // Already started in OnWindowLoaded
    }

    /// <summary>
    /// Close overlay and clean up.
    /// </summary>
    public void CloseOverlay()
    {
        if (_closing) return;
        _closing = true;

        UnregisterHotKey(WindowNative.GetWindowHandle(this), HOTKEY_ID_ESCAPE);

        _animTimer?.Stop();
        _pipe.Disconnect();
        _renderer.Dispose();
        _engine.GetItems().Clear();

        Close();
        OverlayClosed?.Invoke();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (!_closing)
        {
            _closing = true;
            _animTimer?.Stop();
            _pipe.Disconnect();
            _renderer.Dispose();
            OverlayClosed?.Invoke();
        }
    }

    // ---- Window subclassing for hotkey + background erase ----
    private IntPtr _originalOverlayWndProc;
    private WndProcDelegate? _overlayWndProcDelegate;
    private const int GWLP_WNDPROC = -4;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_DWMCOMPOSITIONCHANGED = 0x031E;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private IntPtr OverlayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_HOTKEY when (int)wParam == HOTKEY_ID_ESCAPE:
                CloseOverlay();
                return IntPtr.Zero;

            case WM_ERASEBKGND:
                // Tell Windows we erased the background (avoids black fill)
                return (IntPtr)1;

            case WM_DWMCOMPOSITIONCHANGED:
                // Re-apply DWM transparent configuration
                uint cornerPreference = 1; // DWMWCP_DONOTROUND
                uint borderColor = 0xFFFFFFFE; // DWMWA_COLOR_NONE
                DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(uint));
                DwmSetWindowAttribute(hWnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
                return IntPtr.Zero;
        }
        return CallWindowProc(_originalOverlayWndProc, hWnd, msg, wParam, lParam);
    }

    // ---- DWM constants ----
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;

    // ---- SetWindowCompositionAttribute helpers ----
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENTPOLICY
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINCOMPATTRDATA
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    /// <summary>
    /// Override the window's composition with a per-pixel alpha accent,
    /// making the DWM ignore the opaque white from WinUI's composition tree.
    /// Returns 0 on success, non-zero on failure.
    /// </summary>
    private static int ApplyAccent(IntPtr hwnd, int accentState, uint gradientColor)
    {
        var accent = new ACCENTPOLICY
        {
            AccentState = accentState,
            AccentFlags = 0,
            GradientColor = (int)gradientColor,
            AnimationId = 0,
        };
        int size = Marshal.SizeOf<ACCENTPOLICY>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WINCOMPATTRDATA
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size,
            };
            return SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    // ---- P/Invoke declarations ----
    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINCOMPATTRDATA data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// GetWindowLongPtr for GWL_STYLE (32-bit return, not a pointer).
    /// Must use GetWindowLongPtrW instead of GetWindowLong for 64-bit compatibility.
    /// </summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern uint GetWindowLongPtrUint(IntPtr hWnd, int nIndex);

    // ---- Constants ----
    private const uint LWA_ALPHA = 0x00000002;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}
