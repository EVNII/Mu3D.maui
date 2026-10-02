using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mu3D.Native.Wgpu.Interop;
using Mu3D.Web.Validation;

try
{
    await WgpuAbiProbe.VerifyAsync(args.Contains("--flat-callbacks", StringComparer.Ordinal));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

namespace Mu3D.Web.Validation
{
    internal static partial class WgpuAbiProbe
    {
        [LibraryImport("mu3d_wgpu_abi", EntryPoint = "mu3d_wgpu_abi_value")]
        private static partial int NativeValue(int index);

        [LibraryImport("mu3d_wgpu_abi", EntryPoint = "mu3d_wgpu_callback_shape")]
        private static partial WGPUFuture Schedule(WGPURequestAdapterCallbackInfo info);

        [LibraryImport("mu3d_wgpu_abi", EntryPoint = "mu3d_wgpu_callback_flat")]
        private static unsafe partial WGPUFuture ScheduleFlat(nint callback, void* state, void* sentinel);

        internal static async Task VerifyAsync(bool flattenCallbacks)
        {
            Task<string> completion = VerifyLayoutAndSchedule(flattenCallbacks);
            string message = await completion.WaitAsync(TimeSpan.FromSeconds(10));
            Console.WriteLine($"WGPU-shaped callback payload: '{message}'");
            if (message != "Mu3D ABI") throw new InvalidOperationException(message);
            Console.WriteLine($"Mu3D WGPU ABI subset passed: 20 wasm32 layout/constants, uint64 future, {(flattenCallbacks ? "C-flattened" : "raw by-value")} callback; no GPU execution.");
        }

        private static unsafe Task<string> VerifyLayoutAndSchedule(bool flattenCallbacks)
        {
            if (IntPtr.Size != 4 || RuntimeInformation.ProcessArchitecture != Architecture.Wasm)
                throw new InvalidOperationException("Expected wasm32.");
            int[] managed = [
                sizeof(WGPUStringView), sizeof(WGPUFuture), sizeof(WGPURequestAdapterCallbackInfo),
                sizeof(WGPUInstanceDescriptor), sizeof(WGPURequestAdapterOptions),
                sizeof(WGPUDeviceDescriptor), sizeof(WGPULimits), sizeof(WGPUTextureDescriptor),
                sizeof(WGPUBufferDescriptor), sizeof(WGPURenderPassColorAttachment),
                sizeof(WGPURenderPassDescriptor), sizeof(WGPUSurfaceConfiguration),
                (int)Marshal.OffsetOf<WGPURequestAdapterCallbackInfo>("callback"),
                (int)Marshal.OffsetOf<WGPUDeviceDescriptor>("deviceLostCallbackInfo"),
                (int)WGPUCallbackMode.AllowSpontaneous,
                (int)WGPUTextureFormat.RGBA16Float,
                (int)WgpuNative.WGPUTextureUsage_RenderAttachment,
                (int)WgpuNative.WGPUBufferUsage_MapRead,
                (int)WGPUSType.ShaderSourceWGSL,
                (int)WGPURequestAdapterStatus.Success
            ];
            List<string> differences = [];
            for (int i = 0; i < managed.Length; i++)
                if (managed[i] != NativeValue(i)) differences.Add($"ABI item {i}: managed={managed[i]}, C={NativeValue(i)}");
            if (differences.Count != 0)
            {
                Console.Error.WriteLine(string.Join("; ", differences));
                throw new InvalidOperationException("WGPU header ABI differs.");
            }
            Console.WriteLine("WGPU wasm32 layout/constants: 20/20 checks passed.");

            TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            GCHandle handle = GCHandle.Alloc(completion);
            WGPURequestAdapterCallbackInfo info = new()
            {
                mode = WGPUCallbackMode.AllowSpontaneous,
                callback = &Complete,
                userdata1 = (void*)GCHandle.ToIntPtr(handle),
                userdata2 = (void*)1234,
            };
            WGPUFuture future;
            try
            {
                future = flattenCallbacks
                    ? ScheduleFlat((nint)(delegate* unmanaged[Cdecl]<WGPURequestAdapterStatus, WGPUAdapterImpl*, sbyte*, nuint, void*, void*, void>)&CompleteFlat,
                        info.userdata1, info.userdata2)
                    : Schedule(info);
            }
            catch { handle.Free(); throw; }
            if (future.id == 0) { handle.Free(); throw new OutOfMemoryException(); }
            if (future.id != 0x123456789abcdef0UL)
            {
                Console.Error.WriteLine($"WGPUFuture.id: 0x{future.id:x16}");
                throw new InvalidOperationException("By-value uint64 future return changed.");
            }
            return completion.Task;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe void Complete(WGPURequestAdapterStatus status, WGPUAdapterImpl* adapter,
            WGPUStringView message, void* state, void* sentinel)
            => Finish(status, adapter, message.data, message.length, state, sentinel);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe void CompleteFlat(WGPURequestAdapterStatus status, WGPUAdapterImpl* adapter,
            sbyte* data, nuint length, void* state, void* sentinel)
            => Finish(status, adapter, data, length, state, sentinel);

        private static unsafe void Finish(WGPURequestAdapterStatus status, WGPUAdapterImpl* adapter,
            sbyte* data, nuint length, void* state, void* sentinel)
        {
            GCHandle handle = GCHandle.FromIntPtr((nint)state);
            TaskCompletionSource<string> completion = (TaskCompletionSource<string>)handle.Target!;
            handle.Free();
            string text = System.Text.Encoding.UTF8.GetString((byte*)data, (int)length);
            Console.WriteLine($"Callback status={(uint)status}, adapter={(nint)adapter}, length={length}, sentinel={(nint)sentinel}");
            completion.TrySetResult(status == WGPURequestAdapterStatus.Success &&
                adapter == null && (nint)sentinel == 1234 ? text : "Callback ABI differs.");
        }
    }
}
