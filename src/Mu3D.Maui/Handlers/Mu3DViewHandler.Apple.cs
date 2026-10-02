#if IOS || MACCATALYST
using CoreAnimation;
using Foundation;
using Microsoft.Maui.Handlers;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using ObjCRuntime;
using UIKit;

namespace Mu3D.Maui.Handlers;

/// <summary>Hosts Mu3D in a UIView whose backing layer is a CAMetalLayer.</summary>
public sealed partial class Mu3DViewHandler : ViewHandler<Mu3DView, Mu3DMetalView>
{
    private static readonly IPropertyMapper<Mu3DView, Mu3DViewHandler> Mapper =
        new PropertyMapper<Mu3DView, Mu3DViewHandler>(ViewMapper)
        {
            [nameof(Mu3DView.OutputSettings)] = MapOutputSettings,
        };

    /// <summary>Initializes an Apple Mu3D view handler.</summary>
    public Mu3DViewHandler()
        : base(Mapper)
    {
    }

    /// <inheritdoc />
    protected override Mu3DMetalView CreatePlatformView()
    {
        Mu3DMetalView view = new();
        ApplyOutputSettings(view);
        return view;
    }

    /// <inheritdoc />
    protected override void ConnectHandler(Mu3DMetalView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.WindowAttachmentChanged = OnWindowAttachmentChanged;
        if (platformView.Window is not null)
        {
            ConnectNativeSurface(platformView);
        }
    }

    /// <inheritdoc />
    protected override void DisconnectHandler(Mu3DMetalView platformView)
    {
        platformView.WindowAttachmentChanged = null;
        VirtualView.SetNativeSurfaceSource(null);
        base.DisconnectHandler(platformView);
    }

    private void OnWindowAttachmentChanged(Mu3DMetalView platformView, bool attached)
    {
        if (attached)
        {
            ConnectNativeSurface(platformView);
        }
        else
        {
            VirtualView.SetNativeSurfaceSource(null);
        }
    }

    private void ConnectNativeSurface(Mu3DMetalView platformView) =>
        VirtualView.SetNativeSurfaceSource(
            NativeSurfaceSource.FromMetalLayer(platformView.MetalLayer.Handle));

    private static void MapOutputSettings(Mu3DViewHandler handler, Mu3DView view)
    {
        _ = view;
        handler.ApplyOutputSettings(handler.PlatformView);
    }

    private void ApplyOutputSettings(Mu3DMetalView? platformView)
    {
        if (platformView is null)
        {
            return;
        }

        bool opaque = VirtualView.OutputSettings.AlphaMode is
            SurfaceAlphaMode.Automatic or SurfaceAlphaMode.Opaque;
        platformView.Opaque = opaque;
        platformView.MetalLayer.Opaque = opaque;
        platformView.BackgroundColor = UIColor.Clear;
    }
}

/// <summary>A UIView with a CAMetalLayer backing store.</summary>
public sealed class Mu3DMetalView : UIView
{
    internal Action<Mu3DMetalView, bool>? WindowAttachmentChanged { get; set; }

    /// <summary>Gets the Objective-C layer class used by this view.</summary>
    [Export("layerClass")]
    public static Class LayerClass => new(typeof(CAMetalLayer));

    /// <summary>Gets the strongly typed backing layer.</summary>
    public CAMetalLayer MetalLayer => (CAMetalLayer)Layer;

    /// <inheritdoc />
    public override void MovedToWindow()
    {
        base.MovedToWindow();
        WindowAttachmentChanged?.Invoke(this, Window is not null);
    }
}
#endif
