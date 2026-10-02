namespace Mu3D.Native.Wgpu;

/// <summary>
/// Exposes the exact native ABI version paired with this managed backend.
/// </summary>
public static class WgpuBackendInfo
{
    /// <summary>Gets the upstream wgpu version used by the pinned wgpu-native release.</summary>
    public const string WgpuVersion = "29.0.1";

    /// <summary>Gets the pinned wgpu-native release tag.</summary>
    public const string NativeVersion = Interop.WgpuNativeBuildInfo.Version;

    /// <summary>Gets the pinned upstream commit.</summary>
    public const string NativeCommit = Interop.WgpuNativeBuildInfo.Commit;

    /// <summary>Gets the hash of the asset manifest used to generate this backend.</summary>
    public const string NativeManifestSha256 = Interop.WgpuNativeBuildInfo.ManifestSha256;

    /// <summary>Reads the version encoded by the loaded native runtime.</summary>
    public static uint GetRuntimeVersionValue() => Interop.WgpuNative.wgpuGetVersion();

    /// <summary>
    /// Reads and formats the four-component native runtime version, or reports that the runtime
    /// did not provide a version value.
    /// </summary>
    public static string GetRuntimeVersion()
    {
        uint version = GetRuntimeVersionValue();
        return FormatRuntimeVersion(version);
    }

    internal static string FormatRuntimeVersion(uint version)
    {
        if (version == 0)
        {
            return "unavailable (native returned 0)";
        }
        return $"{version >> 24}.{(version >> 16) & 0xff}.{(version >> 8) & 0xff}.{version & 0xff}";
    }
}
