using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Overlay;

/// <summary>
/// WPF-based danmaku renderer using DrawingVisual + VisualCollection.
/// Each danmaku is a pre-rendered DrawingVisual positioned via Visual.Offset (GPU transform).
/// Object pooling recycles visuals to reduce GC pressure.
/// </summary>
public class DanmakuRenderer : FrameworkElement
{
    private readonly VisualCollection _visuals;
    private readonly Dictionary<DanmakuItem, DrawingVisual> _itemMap = new();
    private readonly Queue<DrawingVisual> _pool = new();
    private readonly Config _config;

    private static readonly System.Windows.Media.Color ShadowColor = System.Windows.Media.Color.FromArgb(255, 0, 0, 0);
    private const double ShadowOffset = 2.0;
    private const double ScBackgroundAlpha = 0.3;

    public DanmakuRenderer(Config config)
    {
        _config = config;
        _visuals = new VisualCollection(this);
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    /// <summary>
    /// Sync visuals with engine state. Called each frame.
    /// </summary>
    public void Sync(IReadOnlyList<DanmakuItem> items)
    {
        // Fast path: track which items remain
        var remaining = new HashSet<DanmakuItem>(_itemMap.Keys);

        foreach (var item in items)
        {
            remaining.Remove(item);

            if (_itemMap.TryGetValue(item, out var visual))
            {
                // Update position only
                visual.Offset = new Vector(item.X, item.Y);
            }
            else
            {
                // New item — render and add
                visual = GetOrCreateVisual(item);
                visual.Offset = new Vector(item.X, item.Y);
                _itemMap[item] = visual;
                _visuals.Add(visual);
            }
        }

        // Remove stale items
        foreach (var stale in remaining)
        {
            if (_itemMap.Remove(stale, out var visual))
            {
                _visuals.Remove(visual);
                _pool.Enqueue(visual);
            }
        }
    }

    /// <summary>
    /// Remove all visuals (called on overlay close / clear).
    /// </summary>
    public void Clear()
    {
        foreach (var visual in _itemMap.Values)
            _pool.Enqueue(visual);
        _itemMap.Clear();
        _visuals.Clear();
    }

    /// <summary>
    /// Measure text width using WPF's FormattedText.
    /// </summary>
    public float MeasureText(string text, double fontSize)
    {
        var ft = BuildFormattedText(text, fontSize, System.Windows.Media.Brushes.White);
        return (float)ft.Width;
    }

    // ---- Private helpers ----

    private DrawingVisual GetOrCreateVisual(DanmakuItem item)
    {
        if (_pool.TryDequeue(out var visual))
        {
            RenderVisual(visual, item);
            return visual;
        }

        visual = new DrawingVisual();
        RenderVisual(visual, item);
        return visual;
    }

    private void RenderVisual(DrawingVisual visual, DanmakuItem item)
    {
        using var dc = visual.RenderOpen();

        var fontSize = item.FontSize;
        var opacity = item.Opacity;
        var textColor = ColorFromInt(item.Color, opacity);

        var foregroundBrush = new System.Windows.Media.SolidColorBrush(textColor);
        foregroundBrush.Freeze();

        var ft = BuildFormattedText(item.Text, fontSize, foregroundBrush);

        // Shadow
        var shadowAlpha = (byte)(opacity * 0.6 * 255);
        var shadowBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(shadowAlpha, 0, 0, 0));
        shadowBrush.Freeze();

        var ftShadow = BuildFormattedText(item.Text, fontSize, shadowBrush);
        dc.DrawText(ftShadow, new System.Windows.Point(ShadowOffset, ShadowOffset));

        // Main text
        dc.DrawText(ft, new System.Windows.Point(0, 0));

        // SC background
        if (item.IsSC)
        {
            var bgAlpha = (byte)(opacity * ScBackgroundAlpha * 255);
            var bgBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(bgAlpha, 0, 0, 0));
            bgBrush.Freeze();
            double w = item.TextWidth + 12;
            double h = fontSize + 12;
            dc.DrawRectangle(bgBrush, null, new Rect(-4, -2, w, h));
        }
    }

    private static FormattedText BuildFormattedText(string text, double fontSize, System.Windows.Media.Brush foreground)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            fontSize,
            foreground,
            VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).PixelsPerDip);
    }

    private static System.Windows.Media.Color ColorFromInt(int color, float opacity = 1f)
    {
        byte r = (byte)((color >> 16) & 0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte b = (byte)(color & 0xFF);

        // Minimum brightness clamp
        float lum = 0.299f * r / 255f + 0.587f * g / 255f + 0.114f * b / 255f;
        if (lum < 0.3f)
        {
            r = Math.Max(r, (byte)77);
            g = Math.Max(g, (byte)77);
            b = Math.Max(b, (byte)77);
        }

        byte a = (byte)Math.Clamp(opacity * 255, 0, 255);
        return System.Windows.Media.Color.FromArgb(a, r, g, b);
    }
}
