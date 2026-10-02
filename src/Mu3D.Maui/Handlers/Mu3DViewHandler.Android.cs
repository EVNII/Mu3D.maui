#if ANDROID
using System.Runtime.InteropServices;
using Android.Graphics;
using Android.Widget;
using Android.Views;
using Java.Interop;
using Microsoft.Maui.Handlers;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;

namespace Mu3D.Maui.Handlers;

/// <summary>Hosts Mu3D in an Android SurfaceView and owns its ANativeWindow reference.</summary>
public sealed partial class Mu3DViewHandler : ViewHandler<Mu3DView, FrameLayout>
{
    private const float DefaultHdrHeadroom = 4.0f;

    private static readonly IPropertyMapper<Mu3DView, Mu3DViewHandler> Mapper =
        new PropertyMapper<Mu3DView, Mu3DViewHandler>(ViewMapper)
        {
            [nameof(Mu3DView.OutputSettings)] = MapOutputSettings,
        };

    private nint nativeWindow;
    private SurfaceCallback? callback;
    private SurfaceView? presentationView;

    /// <summary>Initializes an Android Mu3D view handler.</summary>
    public Mu3DViewHandler()
        : base(Mapper)
    {
    }

    /// <inheritdoc />
    protected override FrameLayout CreatePlatformView()
    {
        FrameLayout host = new(Context);
        presentationView = new SurfaceView(Context);
        presentationView.SetWillNotDraw(true);
        ApplyOutputSettings();
        host.AddView(
            presentationView,
            new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
        return host;
    }

    /// <inheritdoc />
    protected override void ConnectHandler(FrameLayout platformView)
    {
        base.ConnectHandler(platformView);
        callback = new SurfaceCallback(this);
        presentationView?.Holder?.AddCallback(callback);
    }

    /// <inheritdoc />
    protected override void DisconnectHandler(FrameLayout platformView)
    {
        if (callback is not null)
        {
            presentationView?.Holder?.RemoveCallback(callback);
            callback.Dispose();
            callback = null;
        }
        ReleaseNativeWindow();
        presentationView = null;
        base.DisconnectHandler(platformView);
    }

    private static void MapOutputSettings(Mu3DViewHandler handler, Mu3DView view)
    {
        _ = view;
        handler.ApplyOutputSettings();
    }

    private void ApplyOutputSettings()
    {
        if (presentationView is null)
        {
            return;
        }

        bool transparent = VirtualView.OutputSettings.AlphaMode is
            SurfaceAlphaMode.Premultiplied or
            SurfaceAlphaMode.Unpremultiplied or
            SurfaceAlphaMode.Inherit;
        presentationView.SetZOrderOnTop(transparent);
        presentationView.Holder?.SetFormat(
            transparent ? Format.Translucent : Format.Opaque);

        if (OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            float desiredHeadroom = VirtualView.OutputSettings.DynamicRange == OutputDynamicRange.Sdr
                ? 1.0f
                : DefaultHdrHeadroom;
            presentationView.SetDesiredHdrHeadroom(desiredHeadroom);
        }
    }

    private void OnSurfaceCreated(ISurfaceHolder holder)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ReleaseNativeWindow();
        Surface? surface = holder.Surface;
        if (surface is null || !surface.IsValid)
        {
            return;
        }

        nativeWindow = ANativeWindowFromSurface(
            JniEnvironment.EnvironmentPointer,
            surface.PeerReference.Handle);
        if (nativeWindow != 0)
        {
            VirtualView.SetNativeSurfaceSource(
                NativeSurfaceSource.FromAndroidNativeWindow(nativeWindow));
        }
    }

    private void ReleaseNativeWindow()
    {
        VirtualView?.SetNativeSurfaceSource(null);
        if (nativeWindow == 0)
        {
            return;
        }

        ANativeWindowRelease(nativeWindow);
        nativeWindow = 0;
    }

    [LibraryImport("android", EntryPoint = "ANativeWindow_fromSurface")]
    private static partial nint ANativeWindowFromSurface(nint environment, nint surface);

    [LibraryImport("android", EntryPoint = "ANativeWindow_release")]
    private static partial void ANativeWindowRelease(nint window);

    private sealed class SurfaceCallback(Mu3DViewHandler owner) : Java.Lang.Object, ISurfaceHolderCallback
    {
        public void SurfaceCreated(ISurfaceHolder holder) => owner.OnSurfaceCreated(holder);

        public void SurfaceChanged(
            ISurfaceHolder holder,
            Android.Graphics.Format format,
            int width,
            int height)
        {
            _ = holder;
            _ = format;
            owner.VirtualView.SetPlatformSurfaceSize(width, height);
        }

        public void SurfaceDestroyed(ISurfaceHolder holder)
        {
            _ = holder;
            owner.ReleaseNativeWindow();
        }
    }
}
#endif
