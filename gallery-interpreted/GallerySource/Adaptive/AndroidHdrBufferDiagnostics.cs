#if ANDROID
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Maui.ApplicationModel;
using Mu3D.Graphics;

namespace Mu3D.Gallery.Pages;

/// <summary>Reads the current Android producer, View, display and window state without changing it.</summary>
internal static partial class AndroidHdrBufferDiagnostics
{
    private const int RgbaFp16 = 22;
    private const int ScRgbLinear = 0x18410000;

    /// <summary>Appends a synchronous snapshot of a live, host-owned native window.</summary>
    /// <remarks>
    /// The caller must obtain the current source on the main thread while its host retains the
    /// window. This method neither retains the borrowed handle nor owns the presentation session.
    /// </remarks>
    internal static void Append(
        StringBuilder text,
        NativeSurfaceSource? source,
        global::Android.Views.View? platformView)
    {
        ArgumentNullException.ThrowIfNull(text);

        string manufacturer = global::Android.OS.Build.Manufacturer ?? "unknown";
        string model = global::Android.OS.Build.Model ?? "unknown";
        string androidVersion = global::Android.OS.Build.VERSION.Release ?? "unknown";
        int apiLevel = (int)global::Android.OS.Build.VERSION.SdkInt;
        text.AppendLine("Android producer window diagnostics:");
        text.AppendLine($"Device: {manufacturer} {model}");
        text.AppendLine($"Android: {androidVersion} (API {apiLevel})");
        text.AppendLine("Scope: producer configuration, display capabilities and requested window settings; queued-buffer, compositor and physical HDR output are not measured.");

        if (!MainThread.IsMainThread)
        {
            text.AppendLine("ANativeWindow: unavailable (requires MainThread; borrowed handle was not queried)");
            return;
        }

        AppendViewAndDisplay(text, platformView);

        if (source is not NativeSurfaceSource current)
        {
            text.AppendLine("ANativeWindow: unavailable (no current native surface)");
            return;
        }

        if (current.Kind != NativeSurfaceKind.AndroidNativeWindow || current.Handle == 0)
        {
            text.AppendLine("ANativeWindow: unavailable (current source is not a live Android native window)");
            return;
        }

        text.AppendLine($"ANativeWindow width: {FormatDimension(GetWidth(current.Handle))}");
        text.AppendLine($"ANativeWindow height: {FormatDimension(GetHeight(current.Handle))}");
        text.AppendLine($"ANativeWindow format: {FormatPixelFormat(GetFormat(current.Handle))}");

        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            text.AppendLine($"Buffers dataspace (producer): {FormatDataSpace(GetBuffersDataSpace(current.Handle))}");
        }
        else
        {
            text.AppendLine("Buffers dataspace (producer): unavailable (requires API 28)");
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            text.AppendLine($"Buffers default dataspace (consumer default): {FormatDataSpace(GetBuffersDefaultDataSpace(current.Handle))}");
        }
        else
        {
            text.AppendLine("Buffers default dataspace (consumer default): unavailable (requires API 34)");
        }
    }

    private static void AppendViewAndDisplay(StringBuilder text, global::Android.Views.View? platformView)
    {
        if (platformView is null || platformView.Handle == 0)
        {
            text.AppendLine("Platform View/display/window: unavailable (no current live platform View)");
            return;
        }

        bool attached = platformView.IsAttachedToWindow;
        bool hardwareAccelerated = platformView.IsHardwareAccelerated;
        text.AppendLine($"Platform View attached to window: {attached}");
        text.AppendLine($"Platform View hardware accelerated: {hardwareAccelerated}");
        bool uiTexture = platformView is global::Android.Views.ViewGroup group &&
            Enumerable.Range(0, group.ChildCount)
                .Any(index => group.GetChildAt(index) is global::Android.Views.TextureView);
        text.AppendLine($"Presentation carrier: {(uiTexture ? "TextureView (normal UI composition)" : "SurfaceView (legacy opaque HDR)")}");
        text.AppendLine("Actual UI-window buffer format/dataspace and compositor output: not measured");
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            text.AppendLine($"UI configuration wide color gamut: {platformView.Resources?.Configuration?.IsScreenWideColorGamut}");

        // The View's attached display is authoritative here; do not substitute the default display.
        global::Android.Views.Display? display = platformView.Display;
        if (display is null)
        {
            text.AppendLine("Display: unavailable (View has no attached display)");
        }
        else
        {
            int displayId = display.DisplayId;
            bool hdrSupported = display.IsHdr;
            var supportedTypes = GetSupportedHdrTypes(display);
            text.AppendLine($"Display from View: {displayId}");
            text.AppendLine($"Display HDR supported: {hdrSupported}");
            text.AppendLine($"Display supported HDR types: {FormatHdrTypes(supportedTypes)}");

            if (OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                bool ratioAvailable = display.IsHdrSdrRatioAvailable;
                text.AppendLine($"Display HDR/SDR ratio available: {ratioAvailable}");
                if (ratioAvailable)
                {
                    float ratio = display.HdrSdrRatio;
                    text.AppendLine($"Display current HDR/SDR ratio: {ratio:G9}");
                }
                else
                {
                    text.AppendLine("Display current HDR/SDR ratio: unknown (reporting unavailable)");
                }

                if (OperatingSystem.IsAndroidVersionAtLeast(36))
                {
                    if (ratioAvailable)
                    {
                        float highestRatio = display.HighestHdrSdrRatio;
                        text.AppendLine($"Display highest HDR/SDR ratio: {highestRatio:G9}");
                    }
                    else
                    {
                        text.AppendLine("Display highest HDR/SDR ratio: unknown (reporting unavailable)");
                    }
                }
                else
                {
                    text.AppendLine("Display highest HDR/SDR ratio: unavailable (requires API 36)");
                }
            }
            else
            {
                text.AppendLine("Display HDR/SDR ratio available: unavailable (requires API 34)");
                text.AppendLine("Display current HDR/SDR ratio: unavailable (requires API 34)");
                text.AppendLine("Display highest HDR/SDR ratio: unavailable (requires API 36)");
            }
        }

        AppendWindow(text, platformView.Context);
    }

    private static void AppendWindow(StringBuilder text, global::Android.Content.Context? context)
    {
        global::Android.App.Activity? activity = null;
        for (int depth = 0; depth < 16 && context is not null; depth++)
        {
            if (context is global::Android.App.Activity currentActivity)
            {
                activity = currentActivity;
                break;
            }

            if (context is not global::Android.Content.ContextWrapper wrapper)
            {
                break;
            }

            global::Android.Content.Context? next = wrapper.BaseContext;
            if (ReferenceEquals(next, context))
            {
                break;
            }

            context = next;
        }

        global::Android.Views.Window? window = activity?.Window;
        if (window is null)
        {
            text.AppendLine("Activity window: unavailable (no window in View context within 16 layers)");
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            text.AppendLine($"Window color mode (requested): {FormatWindowColorMode((int)window.ColorMode)}");
        else
            text.AppendLine("Window color mode (requested): unavailable (requires API 26)");
        text.AppendLine($"Window pixel format (requested): {window.Attributes?.Format}");
        text.AppendLine("TextureView uses the window's HDR composition; legacy SurfaceView has independent HDR settings.");
        if (OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            float desiredHeadroom = window.DesiredHdrHeadroom;
            string headroom = desiredHeadroom == 0 ? "automatic/default (0)" : $"{desiredHeadroom:G9}";
            text.AppendLine($"Window desired HDR headroom (requested): {headroom}");
        }
        else
        {
            text.AppendLine("Window desired HDR headroom (requested): unavailable (requires API 35)");
        }

        text.AppendLine("Requested window settings and FP16 producer storage do not verify physical HDR output.");
    }

    private static int[]? GetSupportedHdrTypes(global::Android.Views.Display display)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            return display.GetMode()?.GetSupportedHdrTypes();
        }
        var types = display.GetHdrCapabilities()?.GetSupportedHdrTypes();
        return types is null ? null : Array.ConvertAll(types, static type => (int)type);
    }

    private static string FormatHdrTypes(int[]? types)
    {
        if (types is null)
        {
            return "unknown (capabilities unavailable)";
        }

        if (types.Length == 0)
        {
            return "none (empty supported type list)";
        }

        var names = new StringBuilder();
        foreach (int type in types)
        {
            if (names.Length > 0)
            {
                names.Append(", ");
            }

            var name = (global::Android.Views.HdrType)type;
            names.Append($"{name} ({type})");
        }

        return names.ToString();
    }

    private static string FormatWindowColorMode(int value) => value switch
    {
        0 => "default (0)",
        1 => "wide color gamut (1)",
        2 => "HDR (2)",
        _ => $"other ({value})",
    };

    private static string FormatDimension(int value) => value switch
    {
        < 0 => $"native error ({value})",
        0 => "unknown (0)",
        _ => $"{value} px",
    };

    private static string FormatPixelFormat(int value) => value switch
    {
        < 0 => $"native error ({value})",
        0 => "unknown (0)",
        RgbaFp16 => "RGBA FP16 (22 / 0x16)",
        _ => $"other ({value} / 0x{value:X})",
    };

    private static string FormatDataSpace(int value) => value switch
    {
        < 0 => $"native error ({value})",
        0 => "unknown (0)",
        ScRgbLinear => "SCRGB_LINEAR (0x18410000)",
        _ => $"other ({value} / 0x{value:X8})",
    };

    [DllImport("android", EntryPoint = "ANativeWindow_getWidth")]
    private static extern int GetWidth(nint window);

    [DllImport("android", EntryPoint = "ANativeWindow_getHeight")]
    private static extern int GetHeight(nint window);

    [DllImport("android", EntryPoint = "ANativeWindow_getFormat")]
    private static extern int GetFormat(nint window);

    [DllImport("android", EntryPoint = "ANativeWindow_getBuffersDataSpace")]
    private static extern int GetBuffersDataSpace(nint window);

    [DllImport("android", EntryPoint = "ANativeWindow_getBuffersDefaultDataSpace")]
    private static extern int GetBuffersDefaultDataSpace(nint window);
}
#endif
