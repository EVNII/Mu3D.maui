using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Mu3D.Native.Wgpu.Interop;

internal static partial class WgpuDxcLibrary
{
    // Retain the module for the process lifetime; wgpu instances load the same module
    // independently. Resolve through .NET so RID-specific NuGet and extracted native
    // assets work without depending on PATH, a developer SDK or the working directory.
    private static readonly Lazy<string> ResolvedPath = new(Resolve);

    internal static string Path => ResolvedPath.Value;

    private static unsafe string Resolve()
    {
        if (!NativeLibrary.TryLoad("dxcompiler", typeof(WgpuDxcLibrary).Assembly, null, out nint module))
            throw new DllNotFoundException("Mu3D's Windows backend requires its pinned dxcompiler.dll and dxil.dll runtime assets. Prepare the Windows runtimes or restore Mu3D.Native.Wgpu.Runtime.");
        char[] path = new char[32768];
        fixed (char* buffer = path)
        {
            uint length = GetModuleFileName(module, buffer, (uint)path.Length);
            if (length == 0 || length >= path.Length)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot resolve the loaded DXC module path.");
            return new string(buffer, 0, (int)length);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", SetLastError = true)]
    private static unsafe partial uint GetModuleFileName(nint module, char* filename, uint size);
}
