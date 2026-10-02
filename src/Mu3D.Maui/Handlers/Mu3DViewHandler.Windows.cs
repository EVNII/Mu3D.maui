#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using WinUIGrid = Microsoft.UI.Xaml.Controls.Grid;

namespace Mu3D.Maui.Handlers;

/// <summary>Hosts Mu3D in a WinUI SwapChainPanel.</summary>
public sealed partial class Mu3DViewHandler : ViewHandler<Mu3DView, WinUIGrid>
{
    private const string ExperimentalSynchronizedHdrMaskSwitchName =
        "Mu3D.Maui.Windows.ExperimentalSynchronizedTransparentHdrMask";
    private static readonly Guid SwapChainPanelNativeIid =
        new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    private static readonly IPropertyMapper<Mu3DView, Mu3DViewHandler> Mapper =
        new PropertyMapper<Mu3DView, Mu3DViewHandler>(ViewMapper)
        {
            [nameof(Mu3DView.OutputSettings)] = MapOutputSettings,
            [nameof(Mu3DView.WindowsTransparentUnderlay)] = MapOutputSettings,
        };

    /// <summary>Initializes a Windows Mu3D view handler.</summary>
    public Mu3DViewHandler()
        : base(Mapper)
    {
    }

    /// <inheritdoc />
    protected override WinUIGrid CreatePlatformView()
    {
        WinUIGrid host = new();
        EnsurePresentationCarrier(host);
        return host;
    }

    /// <inheritdoc />
    protected override void ConnectHandler(WinUIGrid platformView)
    {
        base.ConnectHandler(platformView);
        connectedHost = platformView;
        platformView.Loaded += OnPlatformViewLoaded;
        platformView.Unloaded += OnPlatformViewUnloaded;
        platformView.SizeChanged += OnPlatformViewSizeChanged;
        if (platformView.IsLoaded)
        {
            QueueNativeSurfaceConnection(platformView);
        }
    }

    /// <inheritdoc />
    protected override void DisconnectHandler(WinUIGrid platformView)
    {
        platformView.Loaded -= OnPlatformViewLoaded;
        platformView.Unloaded -= OnPlatformViewUnloaded;
        platformView.SizeChanged -= OnPlatformViewSizeChanged;
        DisconnectDisplayInformation();
        DisconnectXamlRoot();
        DisconnectNativeSurface();
        DetachPresentationView();
        VirtualView.SetPlatformSurfaceScale(1.0);
        connectedHost = null;
        presentationView = null;
        compositionSink = null;
        base.DisconnectHandler(platformView);
    }

    private void OnPlatformViewLoaded(object sender, RoutedEventArgs e)
    {
        _ = e;
        WinUIGrid platformView = (WinUIGrid)sender;
        EnsurePresentationCarrier(platformView);
        ConnectXamlRoot(platformView.XamlRoot);
        SynchronizePresentationScale(platformView);
        QueueNativeSurfaceConnection(platformView);
    }

    private void OnPlatformViewUnloaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DisconnectDisplayInformation();
        DisconnectXamlRoot();
        DisconnectNativeSurface();
        DetachPresentationView();
        presentationView = null;
    }

    private void OnPlatformViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = e;
        SynchronizePresentationScale((WinUIGrid)sender);
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (connectedHost is not null)
        {
            SynchronizePresentationScale(connectedHost);
        }
    }

    private void QueueNativeSurfaceConnection(WinUIGrid platformView)
    {
        // Let WinUI finish committing the Shell page before querying COM or notifying application
        // code. Raising NativeSurfaceChanged from inside Loaded can re-enter page construction and
        // leave the Windows Shell frame with a selected title but no committed content.
        _ = platformView.DispatcherQueue.TryEnqueue(
            () => ConnectNativeSurfaceAfterLoad(platformView));
    }

    private void ConnectNativeSurfaceAfterLoad(WinUIGrid platformView)
    {
        if (!ReferenceEquals(connectedHost, platformView) ||
            !platformView.IsLoaded ||
            !HasPresentationCarrier)
        {
            return;
        }

        try
        {
            ConnectDisplayInformation();
            ConnectXamlRoot(platformView.XamlRoot);
            SynchronizePresentationScale(platformView);
            ConnectNativeSurface();
        }
        catch (Exception exception)
        {
            // A platform-surface failure must not abort the Shell navigation that hosts the view.
            // Surface pages remain usable and report an unavailable source while Debug output keeps
            // the complete interop failure for diagnosis.
            DisconnectNativeSurface();
            Debug.WriteLine($"[Mu3D] Windows SwapChainPanel connection failed: {exception}");
        }
    }

    private void ConnectXamlRoot(XamlRoot? xamlRoot)
    {
        if (ReferenceEquals(connectedXamlRoot, xamlRoot))
        {
            return;
        }

        DisconnectXamlRoot();
        connectedXamlRoot = xamlRoot;
        if (connectedXamlRoot is not null)
        {
            connectedXamlRoot.Changed += OnXamlRootChanged;
        }
    }

    private void DisconnectXamlRoot()
    {
        if (connectedXamlRoot is not null)
        {
            connectedXamlRoot.Changed -= OnXamlRootChanged;
            connectedXamlRoot = null;
        }
    }

    private Microsoft.Graphics.Display.DisplayInformation? displayInformation;

    private void ConnectDisplayInformation()
    {
        if (displayInformation is not null) return;
        // The WinAppSDK window-scoped query follows monitor moves and system white changes.
        VirtualView.SetPlatformWhite(80, null);
        if (VirtualView.Window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window) return;
        try
        {
            displayInformation = Microsoft.Graphics.Display.DisplayInformation.CreateForWindowId(window.AppWindow.Id);
            displayInformation.AdvancedColorInfoChanged += OnAdvancedColorInfoChanged;
            UpdateDisplayWhite();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Mu3D] System white query unavailable: {exception}");
            DisconnectDisplayInformation();
        }
    }

    private void OnAdvancedColorInfoChanged(Microsoft.Graphics.Display.DisplayInformation sender, object args)
    {
        _ = args;
        // Do not let a queued notification from an unloaded window touch a new session.
        connectedHost?.DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(displayInformation, sender)) UpdateDisplayWhite();
        });
    }

    private void UpdateDisplayWhite()
    {
        try
        {
            double? value = displayInformation?.GetAdvancedColorInfo().SdrWhiteLevelInNits;
            float? white = value is > 0 && double.IsFinite(value.Value) && value <= 10000
                ? (float)value.Value : null;
            VirtualView.SetPlatformWhite(80, white);
        }
        catch (Exception exception)
        {
            VirtualView.SetPlatformWhite(80, null);
            Debug.WriteLine($"[Mu3D] System white query unavailable: {exception}");
        }
    }

    private void DisconnectDisplayInformation()
    {
        if (displayInformation is not null)
        {
            displayInformation.AdvancedColorInfoChanged -= OnAdvancedColorInfoChanged;
            displayInformation.Dispose();
            displayInformation = null;
        }
        VirtualView.SetPlatformWhite(80, null);
    }

    private void DetachPresentationView()
    {
        if (connectedHost is not null && presentationView is not null)
        {
            _ = connectedHost.Children.Remove(presentationView);
        }
        compositionSink?.Dispose();
        compositionSink = null;
    }

    private static SwapChainPanel CreatePresentationView() => new()
    {
        HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
        VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top,
        CompositeMode = ElementCompositeMode.SourceOver,
        RenderTransformOrigin = new Windows.Foundation.Point(0, 0),
        RenderTransform = new ScaleTransform(),
    };

    private void EnsurePresentationCarrier(WinUIGrid platformView)
    {
        if (UsesSynchronizedHdrMaskCarrier)
        {
            if (presentationView is null)
            {
                presentationView = CreatePresentationView();
                platformView.Children.Add(presentationView);
            }
            compositionSink ??= new WindowsCompositionDrawingSurfaceSink(
                platformView,
                VirtualView.ReportPlatformSurfaceError,
                ResolveTransparentUnderlay);
            return;
        }

        if (UsesCompositionCarrier)
        {
            compositionSink ??= new WindowsCompositionDrawingSurfaceSink(
                platformView,
                VirtualView.ReportPlatformSurfaceError);
            return;
        }

        if (presentationView is null)
        {
            presentationView = CreatePresentationView();
            platformView.Children.Add(presentationView);
        }
    }

    private void SynchronizePresentationScale(WinUIGrid platformView)
    {
        double rasterizationScale = platformView.XamlRoot?.RasterizationScale ?? 1.0;
        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
        {
            rasterizationScale = 1.0;
        }

        VirtualView.SetPlatformSurfaceScale(rasterizationScale);
        VirtualView.SetPlatformSurfaceSize(
            platformView.ActualWidth * rasterizationScale,
            platformView.ActualHeight * rasterizationScale);

        compositionSink?.SynchronizeLayout(
            platformView.ActualWidth,
            platformView.ActualHeight,
            rasterizationScale);

        if (presentationView is null)
        {
            return;
        }

        // SwapChainPanel includes render transforms in its CompositionScale. Give the panel a
        // physical-pixel layout size, then apply the inverse root scale so its final footprint still
        // matches the device-independent MAUI host. The effective panel scale becomes 1:1, allowing
        // the wgpu swap chain to remain crisp without exposing its private IDXGISwapChain.
        presentationView.Width = Math.Max(0, platformView.ActualWidth * rasterizationScale);
        presentationView.Height = Math.Max(0, platformView.ActualHeight * rasterizationScale);

        if (presentationView.RenderTransform is not ScaleTransform inverseScale)
        {
            inverseScale = new ScaleTransform();
            presentationView.RenderTransform = inverseScale;
        }

        inverseScale.ScaleX = 1.0 / rasterizationScale;
        inverseScale.ScaleY = 1.0 / rasterizationScale;
    }

    private void ConnectNativeSurface()
    {
        if (panelNative != 0)
        {
            return;
        }

        if (UsesCompositionCarrier)
        {
            WindowsCompositionDrawingSurfaceSink sink = compositionSink ??
                throw new InvalidOperationException("The Windows composition carrier is unavailable.");
            VirtualView.SetNativeSurfaceSource(null);
            VirtualView.SetTexturePresentationSink(sink);
            return;
        }

        VirtualView.SetTexturePresentationSink(
            UsesSynchronizedHdrMaskCarrier
                ? compositionSink ?? throw new InvalidOperationException(
                    "The Windows synchronized HDR mask carrier is unavailable.")
                : null);

        SwapChainPanel platformView = presentationView ??
            throw new InvalidOperationException("The Windows SwapChainPanel carrier is unavailable.");

        nint inspectable = WinRT.MarshalInspectable<SwapChainPanel>.FromManaged(platformView);
        try
        {
            int result = Marshal.QueryInterface(
                inspectable,
                in SwapChainPanelNativeIid,
                out nint queriedPanelNative);
            Marshal.ThrowExceptionForHR(result);
            panelNative = queriedPanelNative;
            VirtualView.SetNativeSurfaceSource(
                NativeSurfaceSource.FromWindowsSwapChainPanel(panelNative));
        }
        finally
        {
            _ = Marshal.Release(inspectable);
        }
    }

    private void DisconnectNativeSurface()
    {
        nint nativePanel = Interlocked.Exchange(ref panelNative, 0);
        try
        {
            VirtualView.SetTexturePresentationSink(null);
            VirtualView.SetNativeSurfaceSource(null);
        }
        finally
        {
            if (nativePanel != 0)
            {
                _ = Marshal.Release(nativePanel);
            }
        }
    }

    private static void MapOutputSettings(Mu3DViewHandler handler, Mu3DView view)
    {
        _ = view;
        WinUIGrid? host = handler.connectedHost ?? handler.PlatformView;
        if (host is null)
        {
            return;
        }

        handler.DisconnectNativeSurface();
        handler.DetachPresentationView();
        handler.presentationView = null;
        handler.EnsurePresentationCarrier(host);
        handler.SynchronizePresentationScale(host);
        if (host.IsLoaded)
        {
            handler.QueueNativeSurfaceConnection(host);
        }
    }

    private bool RequestsTransparentCarrier => VirtualView.OutputSettings.AlphaMode is
        SurfaceAlphaMode.Premultiplied or
        SurfaceAlphaMode.Unpremultiplied or
        SurfaceAlphaMode.Inherit;

    private bool UsesSynchronizedHdrMaskCarrier =>
        RequestsTransparentCarrier &&
        VirtualView.WindowsTransparentUnderlay is not null &&
        AppContext.TryGetSwitch(
            ExperimentalSynchronizedHdrMaskSwitchName,
            out bool enabled) && enabled;

    private bool UsesCompositionCarrier =>
        RequestsTransparentCarrier && !UsesSynchronizedHdrMaskCarrier;

    private bool HasPresentationCarrier => UsesSynchronizedHdrMaskCarrier
        ? presentationView is not null && compositionSink is not null
        : UsesCompositionCarrier
            ? compositionSink is not null
            : presentationView is not null;

    private FrameworkElement? ResolveTransparentUnderlay() =>
        VirtualView.WindowsTransparentUnderlay?.Handler?.PlatformView as FrameworkElement;

    private nint panelNative;
    private WindowsCompositionDrawingSurfaceSink? compositionSink;
    private WinUIGrid? connectedHost;
    private XamlRoot? connectedXamlRoot;
    private SwapChainPanel? presentationView;
}
#endif
