using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Mu3D.Native.OpenColorIO.Interop;

internal static unsafe class OcioNative
{
    private const string Library = "mu3d_opencolorio";
    private static readonly Lazy<bool> Ready = new(Initialize);
    static OcioNative() => NativeLibrary.SetDllImportResolver(typeof(OcioNative).Assembly, Resolve);
    internal static void EnsureAvailable() => _ = Ready.Value;
    private static bool Initialize()
    {
        if ((!OperatingSystem.IsMacOS() && !OperatingSystem.IsMacCatalyst()) || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            throw new PlatformNotSupportedException("This OpenColorIO adapter provides native recipes for macOS arm64 and Mac Catalyst arm64. iOS, Android, Windows and other architectures remain unsupported until their pinned native builds are verified.");
        if (AbiVersion() != 1 || Marshal.PtrToStringUTF8(Version()) != "2.5.2")
            throw new NotSupportedException("Mu3D requires OpenColorIO 2.5.2 with Mu3D C ABI version 1.");
        return true;
    }
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return 0;
        if (OperatingSystem.IsMacCatalyst())
        {
            nint mainProgram = NativeLibrary.GetMainProgramHandle();
            if (!NativeLibrary.TryGetExport(mainProgram, "mu3d_ocio_abi_version", out _))
                throw new DllNotFoundException("The pinned Mac Catalyst OpenColorIO static runtime is missing. Link the maccatalyst-arm64 libmu3d_opencolorio.a using the package's NativeReference target or Mu3DOpenColorIONativeArchive. A macOS dylib cannot substitute for the Mac Catalyst archive.");
            return mainProgram;
        }
        string file = "libmu3d_opencolorio.dylib";
        foreach (string path in new[] { Path.Combine(AppContext.BaseDirectory, file),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "osx-arm64", "native", file) })
            if (File.Exists(path)) return NativeLibrary.Load(path);
        throw new DllNotFoundException("The pinned Mu3D OpenColorIO native runtime is missing. Install libmu3d_opencolorio.dylib beside the application; maintainer instructions are in eng/opencolorio. No runtime is downloaded or compiled during consumer builds.");
    }
    internal static void Check(int result)
    {
        if (result != 0) throw new InvalidOperationException("OpenColorIO: " + Marshal.PtrToStringUTF8(LastError()));
    }
    internal static string ConfigText(OcioConfigHandle handle, int kind, string? display = null, string? view = null, int index = 0)
    {
        Check(ConfigString(handle, kind, display, view, index, null, 0, out int length));
        byte[] text = new byte[length];
        fixed (byte* data = text) Check(ConfigString(handle, kind, display, view, index, data, text.Length, out _));
        return Encoding.UTF8.GetString(text.AsSpan(0, length - 1));
    }
    internal static string ProcessorCacheId(OcioProcessorHandle handle)
    {
        Check(ProcessorCache(handle, null, 0, out int length));
        byte[] text = new byte[length];
        fixed (byte* data = text) Check(ProcessorCache(handle, data, text.Length, out _));
        return Encoding.UTF8.GetString(text.AsSpan(0, length - 1));
    }
    internal static void ValidateText(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Contains('\0')) throw new ArgumentException("Embedded NUL is not a valid OCIO identifier or configuration string.", name);
    }

    [DllImport(Library, EntryPoint = "mu3d_ocio_abi_version", CallingConvention = CallingConvention.Cdecl)] private static extern uint AbiVersion();
    [DllImport(Library, EntryPoint = "mu3d_ocio_version", CallingConvention = CallingConvention.Cdecl)] internal static extern nint Version();
    [DllImport(Library, EntryPoint = "mu3d_ocio_last_error", CallingConvention = CallingConvention.Cdecl)] private static extern nint LastError();
    [DllImport(Library, EntryPoint = "mu3d_ocio_config_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ConfigCreate(int kind, [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? directory, out nint handle);
    [DllImport(Library, EntryPoint = "mu3d_ocio_config_delete", CallingConvention = CallingConvention.Cdecl)] internal static extern void ConfigDelete(nint handle);
    [DllImport(Library, EntryPoint = "mu3d_ocio_config_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ConfigCount(OcioConfigHandle handle, int kind, [MarshalAs(UnmanagedType.LPUTF8Str)] string? display, out int count);
    [DllImport(Library, EntryPoint = "mu3d_ocio_config_string", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ConfigString(OcioConfigHandle handle, int kind, [MarshalAs(UnmanagedType.LPUTF8Str)] string? display,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? view, int index, byte* output, int capacity, out int length);
    [DllImport(Library, EntryPoint = "mu3d_ocio_processor_colorspace", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ColorProcessor(OcioConfigHandle handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, out nint processor);
    [DllImport(Library, EntryPoint = "mu3d_ocio_processor_display", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DisplayProcessor(OcioConfigHandle handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string display, [MarshalAs(UnmanagedType.LPUTF8Str)] string view,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? looks, [MarshalAs(UnmanagedType.LPUTF8Str)] string? decodeSource,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? linearDestination, out nint processor);
    [DllImport(Library, EntryPoint = "mu3d_ocio_processor_delete", CallingConvention = CallingConvention.Cdecl)] internal static extern void ProcessorDelete(nint handle);
    [DllImport(Library, EntryPoint = "mu3d_ocio_processor_cache_id", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ProcessorCache(OcioProcessorHandle handle, byte* output, int capacity, out int length);
    [DllImport(Library, EntryPoint = "mu3d_ocio_apply", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Apply(OcioProcessorHandle handle, float* pixels, int count, int stride);
}

internal sealed class OcioConfigHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal OcioConfigHandle(nint pointer) : base(true) => SetHandle(pointer);
    protected override bool ReleaseHandle() { OcioNative.ConfigDelete(handle); return true; }
}
internal sealed class OcioProcessorHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal OcioProcessorHandle(nint pointer) : base(true) => SetHandle(pointer);
    protected override bool ReleaseHandle() { OcioNative.ProcessorDelete(handle); return true; }
}
