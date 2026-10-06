#if ANDROID
using Android.Content.PM;
using Android.Graphics;
using System.Runtime.Versioning;
using AWindow = global::Android.Views.Window;

namespace Mu3D.Maui.Handlers;

/// <summary>Shares the UI compositor HDR request between controls attached to one window.</summary>
[SupportedOSPlatform("android34.0")]
internal sealed class AndroidHdrWindowLease : IDisposable
{
    // Handler attachment, property mapping and detachment run on the UI thread. Keeping the
    // window alive until the final lease also prevents a recycled JNI handle matching this key.
    private static readonly Dictionary<nint, WindowRequest> Requests = [];
    private WindowRequest? request;

    private AndroidHdrWindowLease(WindowRequest request) => this.request = request;

    internal static AndroidHdrWindowLease Acquire(AWindow window)
    {
        nint key = window.PeerReference.Handle;
        if (!Requests.TryGetValue(key, out WindowRequest? request))
        {
            request = new WindowRequest(window);
            Requests.Add(key, request);
        }
        request.Count++;
        return new AndroidHdrWindowLease(request);
    }

    public void Dispose()
    {
        WindowRequest? current = request;
        request = null;
        if (current is null || --current.Count != 0) return;
        Requests.Remove(current.Window.PeerReference.Handle);
        current.Restore();
    }

    private sealed class WindowRequest
    {
        private const float DesiredHeadroom = 4f;
        private readonly ActivityColorMode originalColorMode;
        private readonly Format originalFormat;
        private readonly float originalHeadroom;
        private readonly float requestedHeadroom;

        internal WindowRequest(AWindow window)
        {
            Window = window;
            originalColorMode = window.ColorMode;
            originalFormat = window.Attributes?.Format ?? Format.Opaque;
            originalHeadroom = OperatingSystem.IsAndroidVersionAtLeast(35)
                ? window.DesiredHdrHeadroom : 0f;
            // Respect an application's existing automatic HDR headroom policy. Otherwise
            // retain a larger explicit request instead of reducing it to the Mu3D default.
            requestedHeadroom = originalColorMode == ActivityColorMode.Hdr && originalHeadroom == 0
                ? 0f : Math.Max(DesiredHeadroom, originalHeadroom);
            try
            {
                // TextureView's alpha alone does not clear the window's SurfaceControl opaque
                // flag. An alpha-capable root avoids Android 16's saturating opaque-FP16 branch.
                SetFormatAttribute(Format.Translucent);
                window.ColorMode = ActivityColorMode.Hdr;
                if (OperatingSystem.IsAndroidVersionAtLeast(35))
                    window.DesiredHdrHeadroom = requestedHeadroom;
            }
            catch
            {
                Restore();
                throw;
            }
        }

        internal AWindow Window { get; }
        internal int Count { get; set; }

        internal void Restore()
        {
            // Do not overwrite window settings an application changed while Mu3D was attached.
            if (OperatingSystem.IsAndroidVersionAtLeast(35) &&
                Window.DesiredHdrHeadroom == requestedHeadroom)
                Window.DesiredHdrHeadroom = originalHeadroom;
            if (Window.ColorMode == ActivityColorMode.Hdr)
                Window.ColorMode = originalColorMode;
            if (Window.Attributes?.Format == Format.Translucent)
                SetFormatAttribute(originalFormat);
        }

        private void SetFormatAttribute(Format format)
        {
            // Window.setAttributes copies and dispatches the parameters without changing
            // setFormat's private explicit-format flag. Preserve automatic format selection.
            using global::Android.Views.WindowManagerLayoutParams attributes = new();
            attributes.CopyFrom(Window.Attributes!);
            attributes.Format = format;
            Window.Attributes = attributes;
        }
    }
}
#endif
