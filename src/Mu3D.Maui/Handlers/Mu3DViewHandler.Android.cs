#if ANDROID
using System.Runtime.InteropServices;
using Android.Graphics;
using Android.Widget;
using Android.Views;
using Java.Interop;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;

namespace Mu3D.Maui.Handlers;

/// <summary>Hosts a GPU surface in the Android UI hierarchy and owns its native window lifetime.</summary>
public sealed partial class Mu3DViewHandler : ViewHandler<Mu3DView, FrameLayout>
{
    private static readonly IPropertyMapper<Mu3DView, Mu3DViewHandler> Mapper =
        new PropertyMapper<Mu3DView, Mu3DViewHandler>(ViewMapper)
        {
            [nameof(Mu3DView.OutputSettings)] = MapOutputSettings,
        };

    private nint nativeWindow;
    private SurfaceCallback? callback;
    private SurfaceView? presentationView;
    private TextureView? textureView;
    private TextureCallback? textureCallback;
    private SurfaceTexture? ownedTexture;
    private NativeSurfaceLifetime? textureLifetime;
    private AndroidHdrWindowLease? windowLease;
    private bool connected;

    /// <summary>Initializes an Android Mu3D view handler.</summary>
    public Mu3DViewHandler()
        : base(Mapper)
    {
    }

    /// <inheritdoc />
    protected override FrameLayout CreatePlatformView()
    {
        FrameLayout host = new SurfaceHost(this);
        CreateCarrier(host);
        ApplyOutputSettings();
        return host;
    }

    private bool NeedsLegacyHdr => !OperatingSystem.IsAndroidVersionAtLeast(34) &&
        VirtualView.OutputSettings.DynamicRange != OutputDynamicRange.Sdr &&
        VirtualView.OutputSettings.AlphaMode is SurfaceAlphaMode.Automatic or SurfaceAlphaMode.Opaque;

    private void CreateCarrier(FrameLayout host)
    {
        // HDR in the normal UI toolkit is supported from API 34. Older opaque HDR views
        // retain their direct SurfaceView path; transparent views use honest SDR UI output.
        Android.Views.View child;
        if (NeedsLegacyHdr)
        {
            presentationView = new SurfaceView(Context);
            presentationView.SetWillNotDraw(true);
            child = presentationView;
        }
        else
        {
            textureView = new TextureView(Context)
            {
                Clickable = false,
                Focusable = false,
                FocusableInTouchMode = false,
            };
            // HWUI imports the image as premultiplied; preserve transparent clear pixels.
            textureView.SetOpaque(false);
            child = textureView;
        }
        host.AddView(
            child,
            new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
    }

    /// <inheritdoc />
    protected override void ConnectHandler(FrameLayout platformView)
    {
        base.ConnectHandler(platformView);
        connected = true;
        AttachCarrier();
    }

    private void AttachCarrier()
    {
        if (presentationView is not null)
        {
            callback = new SurfaceCallback(this);
            presentationView.Holder?.AddCallback(callback);
        }
        if (textureView is not null)
        {
            textureCallback = new TextureCallback(this);
            textureView.SurfaceTextureListener = textureCallback;
            if (textureView.IsAvailable && textureView.SurfaceTexture is { } texture)
                OnTextureAvailable(texture, textureView.Width, textureView.Height);
        }
    }

    /// <inheritdoc />
    protected override void DisconnectHandler(FrameLayout platformView)
    {
        connected = false;
        RemoveCarrier(platformView);
        base.DisconnectHandler(platformView);
    }

    private void RemoveCarrier(FrameLayout platformView)
    {
        // Detach with the listener still installed. Destroyed transfers SurfaceTexture
        // ownership to our lifetime, allowing an in-flight worker frame to finish safely.
        if (textureView is not null)
        {
            platformView.RemoveView(textureView);
            textureView.SurfaceTextureListener = null;
            textureCallback?.Dispose();
            textureCallback = null;
            textureView = null;
        }
        ReleaseTexture();
        ReleaseWindowLease();
        if (callback is not null)
        {
            if (presentationView is not null) platformView.RemoveView(presentationView);
            presentationView?.Holder?.RemoveCallback(callback);
            callback.Dispose();
            callback = null;
        }
        ReleaseNativeWindow();
        presentationView = null;
    }

    private static void MapOutputSettings(Mu3DViewHandler handler, Mu3DView view)
    {
        _ = view;
        handler.ApplyOutputSettings();
    }

    private void ApplyOutputSettings()
    {
        if (connected && NeedsLegacyHdr != (presentationView is not null))
        {
            RemoveCarrier(PlatformView);
            CreateCarrier(PlatformView);
            AttachCarrier();
        }
        if (textureView is not null)
        {
            UpdateWindowLease();
            PublishTextureSource();
            return;
        }
        if (presentationView is null)
        {
            return;
        }

        // The legacy carrier is exclusively opaque HDR on older Android. It never
        // escapes above the app window; explicit alpha selects the normal UI carrier.
        presentationView.SetZOrderOnTop(false);
        presentationView.Holder?.SetFormat(Format.Opaque);
    }

    private bool SupportsHdrUi => OperatingSystem.IsAndroidVersionAtLeast(34) &&
        textureView?.Resources?.Configuration?.IsScreenWideColorGamut == true &&
        textureView.Display?.IsHdrSdrRatioAvailable == true;

    private void UpdateWindowLease()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(34)) return;
        Android.Views.Window? window = Context.GetActivity()?.Window;
        nint root = textureView?.RootView?.PeerReference.Handle ?? 0;
        bool ownsWindowRoot = root != 0 && root == window?.DecorView?.RootView?.PeerReference.Handle;
        bool needsHdr = connected && textureView?.IsShown == true &&
            textureView.WindowVisibility == ViewStates.Visible && SupportsHdrUi &&
            ownsWindowRoot && VirtualView.OutputSettings.DynamicRange != OutputDynamicRange.Sdr;
        if (!needsHdr)
        {
            windowLease?.Dispose();
            windowLease = null;
        }
        else if (windowLease is null && window is not null)
        {
            windowLease = AndroidHdrWindowLease.Acquire(window);
        }
    }

    private void OnTextureAvailable(SurfaceTexture texture, int width, int height)
    {
        if (!connected || textureView is null) return;
        if (object.Equals(ownedTexture, texture))
        {
            VirtualView.SetPlatformSurfaceSize(width, height);
            UpdateWindowLease();
            PublishTextureSource();
            return;
        }
        ReleaseTexture();
        UpdateWindowLease();
        Surface surface = new(texture);
        nint window = ANativeWindowFromSurface(
            JniEnvironment.EnvironmentPointer, surface.PeerReference.Handle);
        if (window == 0)
        {
            surface.Release();
            surface.Dispose();
            VirtualView.ReportPlatformSurfaceError(new InvalidOperationException(
                "Android could not create the UI presentation native window."));
            return;
        }
        nativeWindow = window;
        ownedTexture = texture;
        textureLifetime = new NativeSurfaceLifetime(() =>
        {
            ANativeWindowRelease(window);
            surface.Release();
            surface.Dispose();
            texture.Release();
            texture.Dispose();
        });
        VirtualView.SetPlatformSurfaceSize(width, height);
        PublishTextureSource();
    }

    private void PublishTextureSource()
    {
        if (textureLifetime is null || nativeWindow == 0) return;
        if (!connected || textureView?.IsShown != true || textureView.WindowVisibility != ViewStates.Visible)
        {
            VirtualView.SetNativeSurfaceSource(null);
            return;
        }
        VirtualView.SetNativeSurfaceSource(NativeSurfaceSource.FromAndroidTextureView(
            nativeWindow, textureLifetime, SupportsHdrUi && windowLease is not null));
    }

    private void ReleaseTexture()
    {
        if (textureLifetime is null) return;
        VirtualView.SetNativeSurfaceSource(null);
        NativeSurfaceLifetime previous = textureLifetime;
        textureLifetime = null;
        ownedTexture = null;
        nativeWindow = 0;
        previous.Dispose();
    }

    private void OnHostAttached()
    {
        UpdateWindowLease();
        PublishTextureSource();
    }

    private void OnHostDetached()
    {
        ReleaseWindowLease();
    }

    private void ReleaseWindowLease()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(34)) return;
        windowLease?.Dispose();
        windowLease = null;
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
                NativeSurfaceSource.FromAndroidSurfaceView(nativeWindow));
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

    private sealed class TextureCallback(Mu3DViewHandler owner) : Java.Lang.Object,
        TextureView.ISurfaceTextureListener
    {
        public void OnSurfaceTextureAvailable(SurfaceTexture texture, int width, int height) =>
            owner.OnTextureAvailable(texture, width, height);

        public void OnSurfaceTextureSizeChanged(SurfaceTexture texture, int width, int height)
        {
            if (object.Equals(owner.ownedTexture, texture))
                owner.VirtualView.SetPlatformSurfaceSize(width, height);
        }

        public bool OnSurfaceTextureDestroyed(SurfaceTexture texture)
        {
            if (!object.Equals(owner.ownedTexture, texture)) return true;
            owner.ReleaseTexture();
            // The last native session lease releases the texture after its frame and
            // Vulkan Surface retire. Returning false prevents premature abandonment.
            return false;
        }

        public void OnSurfaceTextureUpdated(SurfaceTexture texture)
        {
            // Android owns consumer invalidation. Do not turn presentation into a render loop.
            _ = texture;
        }
    }

    private sealed class SurfaceHost(Mu3DViewHandler owner) : FrameLayout(owner.Context)
    {
        protected override void OnAttachedToWindow()
        {
            base.OnAttachedToWindow();
            owner.OnHostAttached();
        }

        protected override void OnDetachedFromWindow()
        {
            base.OnDetachedFromWindow();
            owner.OnHostDetached();
        }

        protected override void OnConfigurationChanged(Android.Content.Res.Configuration? configuration)
        {
            base.OnConfigurationChanged(configuration);
            owner.OnHostAttached();
        }

        protected override void OnVisibilityChanged(Android.Views.View changedView, ViewStates visibility)
        {
            base.OnVisibilityChanged(changedView, visibility);
            owner.OnHostAttached();
        }

        protected override void OnWindowVisibilityChanged(ViewStates visibility)
        {
            base.OnWindowVisibilityChanged(visibility);
            owner.OnHostAttached();
        }
    }
}
#endif
