using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.Native.Wgpu;

// Native map callbacks need a device-poll worker; browsers use event-loop completion instead.
internal sealed unsafe partial class WgpuGraphicsBuffer
{
    protected override Task<byte[]> ReadAsyncCore(
        ulong offset,
        int byteCount,
        CancellationToken cancellationToken)
    {
        WGPUBufferImpl* retained = pointer;
        if (retained is null)
        {
            throw new ObjectDisposedException(nameof(WgpuGraphicsBuffer));
        }
        WgpuNative.wgpuBufferAddRef(retained);
        BufferReadState state = new(
            (WgpuGraphicsDevice)Device,
            retained,
            offset,
            byteCount,
            cancellationToken);
        GCHandle stateHandle = GCHandle.Alloc(state);
        WGPUBufferMapCallbackInfo callbackInfo = new()
        {
            mode = WGPUCallbackMode.AllowSpontaneous,
            callback = &OnReadMapped,
            userdata1 = (void*)GCHandle.ToIntPtr(stateHandle),
        };
        try
        {
            _ = WgpuNative.wgpuBufferMapAsync(
                retained,
                WgpuNative.WGPUMapMode_Read,
                checked((nuint)offset),
                checked((nuint)byteCount),
                callbackInfo);
            // wgpu-native does not guarantee that submitted Metal work and mapping callbacks make
            // progress merely because AllowSpontaneous was requested. Drive its device poll on a
            // worker so the public async API never blocks the UI/calling thread.
            ThreadPool.UnsafeQueueUserWorkItem(
                static readState => readState.PumpDevice(),
                state,
                preferLocal: false);
            return state.Completion.Task;
        }
        catch
        {
            state.CancellationRegistration.Dispose();
            stateHandle.Free();
            WgpuNative.wgpuBufferRelease(retained);
            throw;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnReadMapped(
        WGPUMapAsyncStatus status,
        WGPUStringView message,
        void* userdata1,
        void* userdata2)
    {
        _ = userdata2;
        GCHandle stateHandle = GCHandle.FromIntPtr((nint)userdata1);
        BufferReadState state = (BufferReadState)stateHandle.Target!;
        bool mapped = status == WGPUMapAsyncStatus.Success;
        try
        {
            if (!mapped)
            {
                string nativeMessage = WgpuBootstrap.Decode(message);
                state.Completion.TrySetException(new InvalidOperationException(
                    string.IsNullOrWhiteSpace(nativeMessage)
                        ? $"wgpu buffer mapping failed: {status}."
                        : $"wgpu buffer mapping failed: {status}: {nativeMessage}"));
                return;
            }

            void* range = WgpuNative.wgpuBufferGetConstMappedRange(
                state.Buffer,
                checked((nuint)state.Offset),
                checked((nuint)state.ByteCount));
            if (range is null)
            {
                state.Completion.TrySetException(new InvalidOperationException(
                    "wgpuBufferGetConstMappedRange returned null after a successful map."));
                return;
            }
            byte[] result = new byte[state.ByteCount];
            new ReadOnlySpan<byte>(range, state.ByteCount).CopyTo(result);
            state.Completion.TrySetResult(result);
        }
        catch (Exception exception)
        {
            state.Completion.TrySetException(exception);
        }
        finally
        {
            if (mapped)
            {
                WgpuNative.wgpuBufferUnmap(state.Buffer);
            }
            state.CancellationRegistration.Dispose();
            WgpuNative.wgpuBufferRelease(state.Buffer);
            stateHandle.Free();
        }
    }

    private sealed unsafe class BufferReadState
    {
        internal BufferReadState(
            WgpuGraphicsDevice device,
            WGPUBufferImpl* buffer,
            ulong offset,
            int byteCount,
            CancellationToken cancellationToken)
        {
            Device = device;
            Buffer = buffer;
            Offset = offset;
            ByteCount = byteCount;
            Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationRegistration = cancellationToken.Register(
                static value =>
                {
                    (TaskCompletionSource<byte[]> Completion, CancellationToken Token) state =
                        ((TaskCompletionSource<byte[]>, CancellationToken))value!;
                    state.Completion.TrySetCanceled(state.Token);
                },
                (Completion, cancellationToken));
        }

        internal WgpuGraphicsDevice Device { get; }

        internal WGPUBufferImpl* Buffer { get; }

        internal ulong Offset { get; }

        internal int ByteCount { get; }

        internal TaskCompletionSource<byte[]> Completion { get; }

        internal CancellationTokenRegistration CancellationRegistration { get; }

        internal void PumpDevice()
        {
            try
            {
                Device.WaitForSubmittedWork("mapped buffer read");
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
            }
        }
    }
}
