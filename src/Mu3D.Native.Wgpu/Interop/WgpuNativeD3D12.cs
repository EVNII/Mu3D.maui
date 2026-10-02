using System.Runtime.InteropServices;

namespace Mu3D.Native.Wgpu.Interop;

internal static unsafe partial class WgpuNative
{
    private static readonly object D3D12ExportGate = new();
    // Keep the module loaded for the lifetime of the cached function pointers.
    private static nint retainedD3D12Library;
    private static nint getNativeD3D12Device;
    private static nint getNativeD3D12CommandQueue;
    private static nint getNativeD3D12Resource;

    internal static void* wgpuDeviceGetNativeD3D12Device(WGPUDeviceImpl* device)
    {
        EnsureD3D12Exports();
        return ((delegate* unmanaged[Cdecl]<WGPUDeviceImpl*, void*>)getNativeD3D12Device)(device);
    }

    internal static void* wgpuDeviceGetNativeD3D12CommandQueue(WGPUDeviceImpl* device)
    {
        EnsureD3D12Exports();
        return ((delegate* unmanaged[Cdecl]<WGPUDeviceImpl*, void*>)getNativeD3D12CommandQueue)(device);
    }

    internal static void* wgpuTextureGetNativeD3D12Resource(WGPUTextureImpl* texture)
    {
        EnsureD3D12Exports();
        return ((delegate* unmanaged[Cdecl]<WGPUTextureImpl*, void*>)getNativeD3D12Resource)(texture);
    }

    private static void EnsureD3D12Exports()
    {
        if (Volatile.Read(ref getNativeD3D12Resource) != 0)
        {
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The native D3D12 composition bridge is available only on Windows.");
        }

        lock (D3D12ExportGate)
        {
            if (getNativeD3D12Resource != 0)
            {
                return;
            }
            if (!NativeLibrary.TryLoad(
                    "wgpu_native",
                    typeof(WgpuNative).Assembly,
                    searchPath: null,
                    out nint library))
            {
                throw new DllNotFoundException(
                    "The ABI-locked wgpu_native runtime could not be loaded.");
            }

            try
            {
                nint device = NativeLibrary.GetExport(
                    library,
                    "wgpuDeviceGetNativeD3D12Device");
                nint queue = NativeLibrary.GetExport(
                    library,
                    "wgpuDeviceGetNativeD3D12CommandQueue");
                nint resource = NativeLibrary.GetExport(
                    library,
                    "wgpuTextureGetNativeD3D12Resource");
                retainedD3D12Library = library;
                getNativeD3D12Device = device;
                getNativeD3D12CommandQueue = queue;
                Volatile.Write(ref getNativeD3D12Resource, resource);
            }
            catch
            {
                NativeLibrary.Free(library);
                throw;
            }
        }
    }
}
