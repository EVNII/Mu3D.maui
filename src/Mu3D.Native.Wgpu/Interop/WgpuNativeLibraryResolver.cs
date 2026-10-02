using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mu3D.Native.Wgpu.Interop;

/// <summary>Resolves the logical wgpu-native library name across dynamic and static platforms.</summary>
internal static class WgpuNativeLibraryResolver
{
    private const string LogicalLibraryName = "wgpu_native";

    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(WgpuNativeLibraryResolver).Assembly,
            Resolve);
    }

    private static nint Resolve(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;
        if (libraryName == LogicalLibraryName &&
            (OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()))
        {
            return NativeLibrary.GetMainProgramHandle();
        }

        return 0;
    }
}
