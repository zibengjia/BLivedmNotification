using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;
using Application = System.Windows.Application;

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
    private readonly FontWeight _fontWeight;

    private static readonly Color DefaultShadowColor = Color.FromArgb(255, 0, 0, 0);
    private const double ScBackgroundAlpha = 0.3;

    public DanmakuRenderer(Config config)
    {
        _config = config;
        _visuals = new VisualCollection(this);
        _fontWeight = ParseFontWeight(config.Danmaku.FontWeight);
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
                // Update position and opacity
                visual.Offset = new Vector(item.X, item.Y);
                visual.Opacity = item.IsHovered ? 0.0 : item.Opacity;
            }
            else
            {
                // New item — render and add
                visual = GetOrCreateVisual(item);
                visual.Offset = new Vector(item.X, item.Y);
                visual.Opacity = item.IsHovered ? 0.0 : item.Opacity;
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
        var ft = BuildFormattedText(text, fontSize, Brushes.White);
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
        // Use 1.0 opacity at render time — visual.Opacity (set per-frame in Sync)
        // handles hover, fade-at-edge, and SC fade effects at GPU level.
        const float opacity = 1.0f;
        var textColor = ColorFromInt(item.Color, opacity);

        var foregroundBrush = new SolidColorBrush(textColor);
        foregroundBrush.Freeze();

        var ft = BuildFormattedText(item.Text, fontSize, foregroundBrush);

        // Shadow rendering (configurable)
        var danmakuConfig = _config.Danmaku;
        if (danmakuConfig.ShadowEnabled)
        {
            var shadowAlpha = (byte)(opacity * danmakuConfig.ShadowOpacity * 255);
            var shadowBrush = new SolidColorBrush(Color.FromArgb(shadowAlpha, 0, 0, 0));
            shadowBrush.Freeze();

            var offset = danmakuConfig.ShadowOffset;
            var ftShadow = BuildFormattedText(item.Text, fontSize, shadowBrush);
            dc.DrawText(ftShadow, new Point(offset, offset));
        }

        // Main text
        dc.DrawText(ft, new Point(0, 0));

        // SC background
        if (item.IsSC)
        {
            var bgAlpha = (byte)(opacity * ScBackgroundAlpha * 255);
            var bgBrush = new SolidColorBrush(Color.FromArgb(bgAlpha, 0, 0, 0));
            bgBrush.Freeze();
            double w = item.TextWidth + 12;
            double h = fontSize + 12;
            dc.DrawRectangle(bgBrush, null, new Rect(-4, -2, w, h));
        }
    }

    private FormattedText BuildFormattedText(string text, double fontSize, Brush foreground)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, _fontWeight, FontStretches.Normal),
            fontSize,
            foreground,
            VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
    }

    private static FontWeight ParseFontWeight(string weight)
    {
        return weight switch
        {
            "Bold" => FontWeights.Bold,
            "SemiBold" => FontWeights.SemiBold,
            "Light" => FontWeights.Light,
            "Medium" => FontWeights.Medium,
            _ => FontWeights.Normal,
        };
    }

    private static Color ColorFromInt(int color, float opacity = 1f)
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
        return Color.FromArgb(a, r, g, b);
    }
}
