using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using WinUIBrush = Microsoft.UI.Composition.CompositionBrush;
using WinRTBrush = Windows.UI.Composition.CompositionBrush;

namespace Overlay;

/// <summary>
/// Custom SystemBackdrop that outputs fully transparent ARGB(0,0,0,0).
///
/// WinAppSDK 2.1.3's C# projection doesn't expose SetSystemBackdropBrush(),
/// and ICompositionSupportsSystemBackdrop.SystemBackdrop expects
/// Windows.UI.Composition.CompositionBrush (not Microsoft.UI.Composition.CompositionBrush).
///
/// Workaround: Create the transparent brush via Microsoft.UI.Composition,
/// then get its underlying IUnknown and re-wrap as Windows.UI.Composition.CompositionBrush.
/// Both are CCWs for the same WinRT object, so the COM cast is valid.
/// </summary>
public class TransparentBackdrop : SystemBackdrop
{
    private WinUIBrush? _managedBrush;
    private WinRTBrush? _winrtBrush;

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        // Get compositor from the WinUI visual tree
        var compositor = ElementCompositionPreview
            .GetElementVisual(xamlRoot.Content)
            .Compositor;

        // Create transparent ARGB(0,0,0,0) brush (Microsoft.UI.Composition type)
        _managedBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        // Get the underlying IUnknown for the Microsoft brush
        IntPtr pUnk = Marshal.GetIUnknownForObject(_managedBrush);

        try
        {
            // Re-wrap the same WinRT object as Windows.UI.Composition brush type
            _winrtBrush = Marshal.GetObjectForIUnknown(pUnk) as WinRTBrush;
            if (_winrtBrush != null)
            {
                // Assign to the composition target's backdrop
                connectedTarget.SystemBackdrop = _winrtBrush;
            }
        }
        finally
        {
            // Release the ref we got from GetIUnknownForObject
            Marshal.Release(pUnk);
        }

        // Get default backdrop configuration
        _ = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
    }

    protected override void OnTargetDisconnected(
        ICompositionSupportsSystemBackdrop connectedTarget)
    {
        _managedBrush?.Dispose();
        _managedBrush = null;
        _winrtBrush = null;
    }
}
