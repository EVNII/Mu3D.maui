using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Mu3D.Native.Wgpu.Interop;

internal abstract class WgpuOwnedHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    protected WgpuOwnedHandle(nint value)
        : base(ownsHandle: true)
    {
        SetHandle(value);
    }
}

internal sealed unsafe class WgpuInstanceHandle : WgpuOwnedHandle
{
    internal WgpuInstanceHandle(WGPUInstanceImpl* value)
        : base((nint)value)
    {
    }

    internal WGPUInstanceImpl* DangerousGetPointer() => (WGPUInstanceImpl*)DangerousGetHandle();

    protected override bool ReleaseHandle()
    {
        WgpuNative.wgpuInstanceRelease((WGPUInstanceImpl*)handle);
        return true;
    }
}

internal sealed unsafe class WgpuAdapterHandle : WgpuOwnedHandle
{
    internal WgpuAdapterHandle(WGPUAdapterImpl* value)
        : base((nint)value)
    {
    }

    internal WGPUAdapterImpl* DangerousGetPointer() => (WGPUAdapterImpl*)DangerousGetHandle();

    protected override bool ReleaseHandle()
    {
        WgpuNative.wgpuAdapterRelease((WGPUAdapterImpl*)handle);
        return true;
    }
}

internal sealed unsafe class WgpuDeviceHandle : WgpuOwnedHandle
{
    private readonly nint errorSinkHandle;
    private readonly nint deviceLostSinkHandle;

    internal WgpuDeviceHandle(
        WGPUDeviceImpl* value,
        nint errorSinkHandle = 0,
        nint deviceLostSinkHandle = 0)
        : base((nint)value)
    {
        this.errorSinkHandle = errorSinkHandle;
        this.deviceLostSinkHandle = deviceLostSinkHandle;
    }

    internal WGPUDeviceImpl* DangerousGetPointer() => (WGPUDeviceImpl*)DangerousGetHandle();

    internal IReadOnlyList<WgpuDeviceError> DrainErrors()
    {
        if (errorSinkHandle == 0)
        {
            return [];
        }

        WgpuDeviceErrorSink sink =
            (WgpuDeviceErrorSink)GCHandle.FromIntPtr(errorSinkHandle).Target!;
        return sink.Drain();
    }

    protected override bool ReleaseHandle()
    {
        WgpuNative.wgpuDeviceRelease((WGPUDeviceImpl*)handle);
        if (errorSinkHandle != 0)
        {
            GCHandle.FromIntPtr(errorSinkHandle).Free();
        }
        if (deviceLostSinkHandle != 0)
        {
            GCHandle.FromIntPtr(deviceLostSinkHandle).Free();
        }
        return true;
    }
}

internal sealed class WgpuDeviceLostSink
{
    private readonly Lock gate = new();
    private Action<string>? handler;
    private string? pendingReason;

    internal Action<string>? Handler
    {
        set
        {
            string? pending;
            lock (gate)
            {
                handler = value;
                pending = pendingReason;
                pendingReason = null;
            }
            if (pending is not null)
            {
                value?.Invoke(pending);
            }
        }
    }

    internal void Report(string reason)
    {
        Action<string>? current;
        lock (gate)
        {
            current = handler;
            if (current is null)
            {
                pendingReason ??= reason;
                return;
            }
        }
        current(reason);
    }
}

internal readonly record struct WgpuDeviceError(WGPUErrorType Type, string Message);

internal sealed class WgpuDeviceErrorSink
{
    private readonly Lock gate = new();
    private readonly List<WgpuDeviceError> errors = [];

    internal void Add(WgpuDeviceError error)
    {
        lock (gate)
        {
            errors.Add(error);
        }
    }

    internal IReadOnlyList<WgpuDeviceError> Drain()
    {
        lock (gate)
        {
            WgpuDeviceError[] result = [.. errors];
            errors.Clear();
            return result;
        }
    }
}

internal sealed unsafe class WgpuSurfaceHandle : WgpuOwnedHandle
{
    internal WgpuSurfaceHandle(WGPUSurfaceImpl* value)
        : base((nint)value)
    {
    }

    internal WGPUSurfaceImpl* DangerousGetPointer() => (WGPUSurfaceImpl*)DangerousGetHandle();

    protected override bool ReleaseHandle()
    {
        WgpuNative.wgpuSurfaceRelease((WGPUSurfaceImpl*)handle);
        return true;
    }
}
