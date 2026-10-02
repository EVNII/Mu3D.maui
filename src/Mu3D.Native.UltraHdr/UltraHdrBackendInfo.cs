namespace Mu3D.Native.UltraHdr;

/// <summary>Reports immutable capabilities of the packaged libultrahdr native feature set.</summary>
public static class UltraHdrBackendInfo
{
    /// <summary>Gets the pinned libultrahdr source version.</summary>
    public const string NativeVersion = "v2.0.1";

    /// <summary>Gets the pinned libjpeg-turbo source version.</summary>
    public const string JpegTurboVersion = "3.1.0";

    /// <summary>
    /// Gets whether this managed assembly must be paired with the separately built HEIF-enabled
    /// native feature set. The normal package is JPEG-only and returns <see langword="false"/>.
    /// </summary>
#if MU3D_ULTRAHDR_HEIF
    public static bool IsHeifEnabled => true;
#else
    public static bool IsHeifEnabled => false;
#endif
}
