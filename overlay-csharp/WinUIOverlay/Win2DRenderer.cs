using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;
using Windows.Foundation;

namespace Overlay;

/// <summary>
/// Win2D-based danmaku renderer.
/// Designed for use with CanvasControl (WinUI 3).
/// All drawing is done via CanvasDrawingSession in the Draw event.
/// </summary>
public class Win2DRenderer : IDisposable
{
    private readonly Config _config;
    private readonly Dictionary<float, CanvasTextFormat> _formatCache = new();
    private bool _disposed;

    private static readonly Color ShadowColor = Color.FromArgb(255, 0, 0, 0);
    private const float ShadowOffsetX = 2f;
    private const float ShadowOffsetY = 2f;
    private const float ScZoneHeight = 50f;

    public Win2DRenderer(Config config)
    {
        _config = config;
    }

    /// <summary>
    /// Measure text width using Win2D's CanvasTextLayout for accurate DirectWrite measurement.
    /// </summary>
    public float MeasureText(ICanvasResourceCreator creator, string text, float fontSize)
    {
        var format = GetOrCreateFormat(fontSize);
        using var layout = new CanvasTextLayout(creator, text, format, 9999f, 100f);
        return (float)layout.LayoutBounds.Width;
    }

    /// <summary>
    /// Render all danmaku items to a drawing session.
    /// Must be called from within CanvasControl.Draw event.
    /// </summary>
    public void Render(CanvasDrawingSession ds, IReadOnlyList<DanmakuItem> items)
    {
        // Normal danmaku first, then SC on top
        foreach (var item in items)
            if (!item.IsSC) RenderItem(ds, item);
        foreach (var item in items)
            if (item.IsSC) RenderItem(ds, item);
    }

    private void RenderItem(CanvasDrawingSession ds, DanmakuItem item)
    {
        var format = GetOrCreateFormat(item.FontSize);
        var color = ColorFromInt(item.Color, item.Opacity);
        var shadowAlpha = item.Opacity * 0.6f;

        // Text position
        float x = item.X;
        float y = item.Y;

        // 1. Shadow (offset right+down)
        ds.DrawText(item.Text, x + ShadowOffsetX, y + ShadowOffsetY,
            Color.FromArgb((byte)(shadowAlpha * 255), ShadowColor.R, ShadowColor.G, ShadowColor.B), format);

        // 2. Main text
        ds.DrawText(item.Text, x, y, color, format);

        // 3. Background for SC
        if (item.IsSC)
        {
            var alpha = (byte)(item.Opacity * 0.3 * 255);
            var bgColor = Color.FromArgb(alpha, 0, 0, 0);
            var w = item.TextWidth + 12;
            var h = item.FontSize + 12;
            ds.FillRectangle(x - 4, y - 2, w, h, bgColor);
        }
    }

    private CanvasTextFormat GetOrCreateFormat(float fontSize)
    {
        if (_formatCache.TryGetValue(fontSize, out var format))
            return format;

        format = new CanvasTextFormat
        {
            FontSize = fontSize,
            FontFamily = "Microsoft YaHei UI",
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Center,
        };
        _formatCache[fontSize] = format;
        return format;
    }

    private static Color ColorFromInt(int color, float opacity = 1f)
    {
        byte r = (byte)((color >> 16) & 0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte b = (byte)(color & 0xFF);

        // Ensure minimum brightness for readability
        float lum = 0.299f * r / 255f + 0.587f * g / 255f + 0.114f * b / 255f;
        if (lum < 0.3f)
        {
            r = Math.Max(r, (byte)77);  // 0.3 * 255
            g = Math.Max(g, (byte)77);
            b = Math.Max(b, (byte)77);
        }

        byte a = (byte)Math.Clamp(opacity * 255, 0, 255);
        return Color.FromArgb(a, r, g, b);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var fmt in _formatCache.Values)
            fmt.Dispose();
        _formatCache.Clear();
        _disposed = true;
    }
}
