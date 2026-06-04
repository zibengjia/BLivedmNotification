using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Overlay;

public class DanmakuRenderer : IDisposable
{
    private ID2D1Factory? _d2dFactory;
    private ID2D1HwndRenderTarget? _renderTarget;
    private IDWriteFactory? _dwFactory;
    private IDWriteTextFormat? _danmakuFormat;
    private IDWriteTextFormat? _scFormat;
    private ID2D1SolidColorBrush? _textBrush;
    private ID2D1SolidColorBrush? _shadowBrush;
    private ID2D1SolidColorBrush? _strokeBrush;

    private readonly Config _config;

    public DanmakuRenderer(Config config)
    {
        _config = config;
    }

    public void Initialize(IntPtr hwnd, int width, int height)
    {
        // D2D factory -- FactoryType is in Vortice.Direct2D1
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory>(
            Vortice.Direct2D1.FactoryType.SingleThreaded);

        // DWrite factory -- FactoryType is in Vortice.DirectWrite
        _dwFactory = DWrite.DWriteCreateFactory<IDWriteFactory>(
            Vortice.DirectWrite.FactoryType.Shared);

        // Render target properties
        var rtProps = new RenderTargetProperties
        {
            PixelFormat = new Vortice.DCommon.PixelFormat
            {
                Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                AlphaMode = Vortice.DCommon.AlphaMode.Premultiplied
            }
        };

        var hwndProps = new HwndRenderTargetProperties
        {
            Hwnd = hwnd,
            PixelSize = new Size(width, height),
            PresentOptions = PresentOptions.None
        };

        _renderTarget = _d2dFactory.CreateHwndRenderTarget(rtProps, hwndProps);

        // Danmaku text format
        _danmakuFormat = _dwFactory.CreateTextFormat(
            "Microsoft YaHei UI", _config.Danmaku.FontSize);
        _danmakuFormat.TextAlignment = TextAlignment.Leading;
        _danmakuFormat.ParagraphAlignment = ParagraphAlignment.Center;

        // SC text format (larger)
        _scFormat = _dwFactory.CreateTextFormat(
            "Microsoft YaHei UI", _config.SuperChat.FontSize);
        _scFormat.TextAlignment = TextAlignment.Leading;
        _scFormat.ParagraphAlignment = ParagraphAlignment.Center;

        // Brushes
        _textBrush = _renderTarget.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
        _shadowBrush = _renderTarget.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 0.5f));
        _strokeBrush = _renderTarget.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 1f));
    }

    public void Resize(int width, int height)
    {
        _renderTarget?.Resize(new Size(width, height));
    }

    public void Render(IReadOnlyList<DanmakuItem> items)
    {
        if (_renderTarget == null) return;

        _renderTarget.BeginDraw();
        _renderTarget.Clear(new Color4(0f, 0f, 0f, 0f)); // fully transparent

        // Normal danmaku first, SC on top
        foreach (var item in items)
            if (!item.IsSC) RenderDanmaku(item);
        foreach (var item in items)
            if (item.IsSC) RenderDanmaku(item);

        _renderTarget.EndDraw();
    }

    private void RenderDanmaku(DanmakuItem item)
    {
        if (_renderTarget == null || _textBrush == null || _shadowBrush == null) return;

        var format = item.IsSC ? _scFormat : _danmakuFormat;
        if (format == null) return;

        var color = ColorFromInt(item.Color, item.Opacity);
        var shadowColor = new Color4(0f, 0f, 0f, item.Opacity * 0.6f);

        var textRect = new RectangleF(
            item.X, item.Y,
            item.TextWidth + 4, item.FontSize + 8
        );

        // 1. Shadow (offset right+down 2px)
        _shadowBrush.Color = shadowColor;
        _renderTarget!.DrawText(item.Text, format,
            new RectangleF(item.X + 2, item.Y + 2, item.TextWidth + 6, item.FontSize + 10),
            _shadowBrush);

        // 2. Main text
        _textBrush.Color = color;
        _renderTarget.DrawText(item.Text, format, textRect, _textBrush);

        // 3. Semi-transparent dark background for SC
        if (item.IsSC && _strokeBrush != null)
        {
            _strokeBrush.Color = new Color4(0f, 0f, 0f, item.Opacity * 0.3f);
            var bgRect = new Vortice.RawRectF(
                item.X - 4, item.Y - 2,
                item.X + item.TextWidth + 8,
                item.Y + item.FontSize + 12
            );
            _renderTarget.FillRectangle(bgRect, _strokeBrush);
        }
    }

    private static Color4 ColorFromInt(int color, float opacity = 1f)
    {
        float r = ((color >> 16) & 0xFF) / 255f;
        float g = ((color >> 8) & 0xFF) / 255f;
        float b = (color & 0xFF) / 255f;

        // Ensure minimum brightness for readability
        var lum = 0.299f * r + 0.587f * g + 0.114f * b;
        if (lum < 0.3f)
        {
            r = Math.Max(r, 0.3f);
            g = Math.Max(g, 0.3f);
            b = Math.Max(b, 0.3f);
        }

        return new Color4(r, g, b, opacity);
    }

    public void Dispose()
    {
        _textBrush?.Dispose();
        _shadowBrush?.Dispose();
        _strokeBrush?.Dispose();
        _danmakuFormat?.Dispose();
        _scFormat?.Dispose();
        _renderTarget?.Dispose();
        _dwFactory?.Dispose();
        _d2dFactory?.Dispose();
    }

    /// <summary>
    /// Measure text width accurately using DirectWrite. Called from engine thread.
    /// </summary>
    public float MeasureText(string text, float fontSize)
    {
        if (_dwFactory == null)
            return text.Length * fontSize * 0.55f;

        using var format = _dwFactory.CreateTextFormat("Microsoft YaHei UI", fontSize);
        using var layout = _dwFactory.CreateTextLayout(text, format, 9999, 100);
        return layout.Metrics.Width;
    }
}
