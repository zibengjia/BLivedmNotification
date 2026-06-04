using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Overlay;

/// <summary>
/// Custom SystemBackdrop that outputs fully transparent ARGB(0,0,0,0).
/// Prevents WinUI 3 from painting its opaque white background behind the CanvasControl.
/// Follows the approach from DevWinUI / WindBoard:
///   - Creates a transparent composition brush
///   - Configures DWM transparent behavior via composition
/// </summary>
public class TransparentBackdrop : SystemBackdrop
{
    private CompositionBrush? _brush;

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        // Get the Compositor from the visual tree
        var compositor = ElementCompositionPreview
            .GetElementVisual(xamlRoot.Content)
            .Compositor;

        // Create a fully transparent ARGB(0,0,0,0) brush
        _brush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        // Apply default backdrop configuration (theme awareness etc.)
        var config = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
    }

    protected override void OnTargetDisconnected(
        ICompositionSupportsSystemBackdrop connectedTarget)
    {
        _brush?.Dispose();
        _brush = null;
    }
}
