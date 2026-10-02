using System.Reflection;
using System.Runtime.InteropServices;

namespace Mu3D.Native.UltraHdr.Interop;

internal static class UltraHdrNativeLibraryResolver
{
    private const string UltraHdrLibraryName = "uhdr";
    private const string TurboJpegLibraryName = "turbojpeg";
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
                typeof(UltraHdrNativeLibraryResolver).Assembly,
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
        if ((libraryName == UltraHdrLibraryName || libraryName == TurboJpegLibraryName) &&
            (OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()))
        {
            return NativeLibrary.GetMainProgramHandle();
        }
        return 0;
    }
}
