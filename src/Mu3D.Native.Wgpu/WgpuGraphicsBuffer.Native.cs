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
        nuint nativeOffset = checked((nuint)offset);
        nuint nativeByteCount = checked((nuint)byteCount);
        WGPUBufferImpl* retained = pointer;
        if (retained is null)
        {
            throw new ObjectDisposedException(nameof(WgpuGraphicsBuffer));
        }
        WgpuGraphicsDevice.NativeDeviceReadLease? deviceLease = null;
        BufferReadState? state = null;
        bool bufferRetained = false;
        bool workerQueued = false;
        bool mappingSubmitted = false;
        try
        {
            deviceLease = ((WgpuGraphicsDevice)Device).RetainNativeDeviceForRead();
            WgpuNative.wgpuBufferAddRef(retained);
            bufferRetained = true;
            state = new(deviceLease, retained, offset, byteCount, cancellationToken);
            state.CallbackHandle = GCHandle.Alloc(state);
            WGPUBufferMapCallbackInfo callbackInfo = new()
            {
                mode = WGPUCallbackMode.AllowSpontaneous,
                callback = &OnReadMapped,
                userdata1 = (void*)GCHandle.ToIntPtr(state.CallbackHandle),
            };

            // Reserve progress before native code owns the callback. The worker waits for setup,
            // so a queue/setup failure cannot abandon an accepted mapping or its retained handles.
            workerQueued = ThreadPool.UnsafeQueueUserWorkItem<BufferReadState>(
                static readState => readState.PumpDevice(),
                state,
                preferLocal: false);
            if (!workerQueued)
            {
                throw new InvalidOperationException("Unable to queue the mapped-buffer device-poll worker.");
            }
            _ = WgpuNative.wgpuBufferMapAsync(
                retained,
                WgpuNative.WGPUMapMode_Read,
                nativeOffset,
                nativeByteCount,
                callbackInfo);
            mappingSubmitted = true;
            return state.Completion.Task;
        }
        finally
        {
            if (state is not null)
            {
                state.FinishSetup(mappingSubmitted, workerQueued);
            }
            else
            {
                try
                {
                    if (bufferRetained) WgpuNative.wgpuBufferRelease(retained);
                }
                finally
                {
                    deviceLease?.Dispose();
                }
            }
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
            try
            {
                state.CleanupCallback(mapped);
            }
            catch (Exception exception)
            {
                state.Completion.TrySetException(exception);
            }
        }
    }

    private sealed unsafe class BufferReadState
    {
        private readonly WgpuGraphicsDevice.NativeDeviceReadLease deviceLease;
        private readonly object setupGate = new();
        private int remainingParticipants = 3; // Setup, callback cleanup and poll worker.
        private int callbackCleanupStarted;
        private bool setupComplete;
        private bool mappingSubmitted;

        internal BufferReadState(
            WgpuGraphicsDevice.NativeDeviceReadLease deviceLease,
            WGPUBufferImpl* buffer,
            ulong offset,
            int byteCount,
            CancellationToken cancellationToken)
        {
            this.deviceLease = deviceLease;
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

        internal WGPUBufferImpl* Buffer { get; }

        internal GCHandle CallbackHandle { get; set; }

        internal ulong Offset { get; }

        internal int ByteCount { get; }

        internal TaskCompletionSource<byte[]> Completion { get; }

        internal CancellationTokenRegistration CancellationRegistration { get; }

        internal void FinishSetup(bool submitted, bool workerQueued)
        {
            try
            {
                if (!submitted) CleanupCallback(mapped: false);
            }
            finally
            {
                lock (setupGate)
                {
                    mappingSubmitted = submitted;
                    setupComplete = true;
                    Monitor.PulseAll(setupGate);
                }
                if (!workerQueued) CompleteParticipant();
                CompleteParticipant();
            }
        }

        internal void CleanupCallback(bool mapped)
        {
            if (Interlocked.Exchange(ref callbackCleanupStarted, 1) != 0) return;
            try
            {
                if (mapped) WgpuNative.wgpuBufferUnmap(Buffer);
            }
            finally
            {
                try
                {
                    CancellationRegistration.Dispose();
                }
                finally
                {
                    try
                    {
                        WgpuNative.wgpuBufferRelease(Buffer);
                    }
                    finally
                    {
                        try
                        {
                            GCHandle callbackHandle = CallbackHandle;
                            CallbackHandle = default;
                            if (callbackHandle.IsAllocated) callbackHandle.Free();
                        }
                        finally
                        {
                            CompleteParticipant();
                        }
                    }
                }
            }
        }

        internal void PumpDevice()
        {
            try
            {
                lock (setupGate)
                {
                    while (!setupComplete) Monitor.Wait(setupGate);
                    if (!mappingSubmitted) return;
                }
                // Keep both the stable native pointer and its callback sinks alive even if the
                // public task is cancelled/completed and the owning session is disposed meanwhile.
                deviceLease.WaitForSubmittedWork("mapped buffer read");
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
            }
            finally
            {
                CompleteParticipant();
            }
        }

        private void CompleteParticipant()
        {
            if (Interlocked.Decrement(ref remainingParticipants) == 0) deviceLease.Dispose();
        }
    }
}
