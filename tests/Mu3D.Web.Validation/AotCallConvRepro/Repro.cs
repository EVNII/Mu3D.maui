using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace Mu3D.AotCallConvRepro;

// No renderer, browser, native binary, or application execution is needed to reproduce
// the Mono 10.0.12 macOS ARM64 -> wasm32 custom-attribute allocation assertion.
internal static partial class Repro
{
#if LEGACY_CDECL
    [DllImport("wgpu_native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
#pragma warning disable SYSLIB1054 // This declaration is the explicit-Cdecl control.
    internal static extern void wgpuAdapterAddRef(nint adapter);
#pragma warning restore SYSLIB1054
#elif DEFAULT_CALLCONV
    [LibraryImport("wgpu_native")]
    internal static partial void wgpuAdapterAddRef(nint adapter);
#else
    [LibraryImport("wgpu_native")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void wgpuAdapterAddRef(nint adapter);
#endif
}
