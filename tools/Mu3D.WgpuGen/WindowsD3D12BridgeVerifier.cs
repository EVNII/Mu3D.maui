namespace Mu3D.WgpuGen;

internal static class WindowsD3D12BridgeVerifier
{
    internal static readonly string[] RequiredExports =
    [
        "wgpuCreateInstance",
        "wgpuDeviceGetNativeD3D12Device",
        "wgpuDeviceGetNativeD3D12CommandQueue",
        "wgpuTextureGetNativeD3D12Resource",
    ];

    public static IReadOnlyList<string> Verify(string nativeLibraryPath)
    {
        string path = Path.GetFullPath(nativeLibraryPath);
        if (!File.Exists(path))
        {
            return [$"Native library does not exist: {path}"];
        }
        if (!Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return [$"The Windows D3D12 bridge must be verified from a PE DLL: {path}"];
        }

        try
        {
            return VerifyExports(PeExportReader.Read(path).ToHashSet(StringComparer.Ordinal));
        }
        catch (BadImageFormatException exception)
        {
            return [$"Could not read PE exports from {path}: {exception.Message}"];
        }
    }

    internal static IReadOnlyList<string> VerifyExports(IReadOnlySet<string> exports) =>
        RequiredExports
            .Where(required => !exports.Contains(required))
            .Select(required => $"Windows wgpu-native runtime is missing required export '{required}'.")
            .ToArray();
}
