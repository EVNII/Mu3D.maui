using System.Reflection;
using System.Runtime.InteropServices;

namespace Mu3D.Native.Ktx.Interop;

internal static class KtxNativeLibraryResolver
{
    private const string LibraryName = "ktx";
    private static readonly object RegistrationLock = new();
    private static bool isRegistered;

    internal static void Register()
    {
        lock (RegistrationLock)
        {
            if (isRegistered)
            {
                return;
            }
            NativeLibrary.SetDllImportResolver(
                typeof(KtxNativeLibraryResolver).Assembly,
                Resolve);
            isRegistered = true;
        }
    }

    private static nint Resolve(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;
        if (libraryName == LibraryName &&
            (OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()))
        {
            return NativeLibrary.GetMainProgramHandle();
        }
        return 0;
    }
}
