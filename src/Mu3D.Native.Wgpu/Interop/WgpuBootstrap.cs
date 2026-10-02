using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Mu3D.Native.Wgpu.Interop;

internal static unsafe class WgpuBootstrap
{
    internal static WgpuInstanceHandle CreateInstance(WGPUInstanceDescriptor* descriptor = null)
    {
        if (OperatingSystem.IsWindows())
        {
            // Surface probes carry backend restrictions in InstanceExtras. Copy them
            // before selecting DXC so windowed and headless rendering use one compiler.
            WGPUInstanceDescriptor configured = descriptor is null ? default : *descriptor;
            WGPUInstanceExtras extras = new()
            {
                chain = new WGPUChainedStruct
                {
                    sType = (WGPUSType)WGPUNativeSType.WGPUSType_InstanceExtras,
                    next = configured.nextInChain,
                },
            };
            if (configured.nextInChain is not null &&
                configured.nextInChain->sType == (WGPUSType)WGPUNativeSType.WGPUSType_InstanceExtras)
                extras = *(WGPUInstanceExtras*)configured.nextInChain;
            byte[] compilerPath = Encoding.UTF8.GetBytes(WgpuDxcLibrary.Path);
            fixed (byte* path = compilerPath)
            {
                extras.dx12ShaderCompiler = WGPUDx12Compiler.Dxc;
                extras.dxcPath = new WGPUStringView { data = (sbyte*)path, length = (nuint)compilerPath.Length };
                configured.nextInChain = &extras.chain;
                return CreateConfiguredInstance(&configured);
            }
        }
        return CreateConfiguredInstance(descriptor);
    }

    private static WgpuInstanceHandle CreateConfiguredInstance(WGPUInstanceDescriptor* descriptor)
    {
        WGPUInstanceImpl* instance = WgpuNative.wgpuCreateInstance(descriptor);
        return instance is null
            ? throw new InvalidOperationException("wgpuCreateInstance returned a null instance.")
            : new WgpuInstanceHandle(instance);
    }

    internal static Task<WgpuAdapterHandle> RequestAdapterAsync(
        WgpuInstanceHandle instance,
        WGPURequestAdapterOptions* options = null)
    {
        ObjectDisposedException.ThrowIf(instance.IsClosed, instance);
        AdapterRequestState state = new();
        GCHandle stateHandle = GCHandle.Alloc(state);
        WGPURequestAdapterCallbackInfo callbackInfo = new()
        {
            mode = WGPUCallbackMode.AllowSpontaneous,
            callback = &OnAdapterRequestCompleted,
            userdata1 = (void*)GCHandle.ToIntPtr(stateHandle),
        };

        bool instanceRefAdded = false;
        try
        {
            instance.DangerousAddRef(ref instanceRefAdded);
            _ = WgpuNative.wgpuInstanceRequestAdapter(
                instance.DangerousGetPointer(),
                options,
                callbackInfo);
        }
        catch
        {
            stateHandle.Free();
            throw;
        }
        finally
        {
            if (instanceRefAdded)
            {
                instance.DangerousRelease();
            }
        }

        return state.Completion.Task;
    }

    internal static Task<WgpuDeviceHandle> RequestDeviceAsync(
        WgpuAdapterHandle adapter,
        WGPUDeviceDescriptor* descriptor = null,
        WgpuDeviceErrorSink? errorSink = null,
        WgpuDeviceLostSink? deviceLostSink = null)
    {
        ObjectDisposedException.ThrowIf(adapter.IsClosed, adapter);
        nint errorSinkHandle = errorSink is null
            ? 0
            : GCHandle.ToIntPtr(GCHandle.Alloc(errorSink));
        nint deviceLostSinkHandle = deviceLostSink is null
            ? 0
            : GCHandle.ToIntPtr(GCHandle.Alloc(deviceLostSink));
        DeviceRequestState state = new(errorSinkHandle, deviceLostSinkHandle);
        GCHandle stateHandle = GCHandle.Alloc(state);
        if (descriptor is not null && errorSinkHandle != 0)
        {
            descriptor->uncapturedErrorCallbackInfo.callback = &OnUncapturedError;
            descriptor->uncapturedErrorCallbackInfo.userdata1 = (void*)errorSinkHandle;
        }
        if (descriptor is not null && deviceLostSinkHandle != 0)
        {
            descriptor->deviceLostCallbackInfo.mode = WGPUCallbackMode.AllowSpontaneous;
            descriptor->deviceLostCallbackInfo.callback = &OnDeviceLost;
            descriptor->deviceLostCallbackInfo.userdata1 = (void*)deviceLostSinkHandle;
        }
        WGPURequestDeviceCallbackInfo callbackInfo = new()
        {
            mode = WGPUCallbackMode.AllowSpontaneous,
            callback = &OnDeviceRequestCompleted,
            userdata1 = (void*)GCHandle.ToIntPtr(stateHandle),
        };

        bool adapterRefAdded = false;
        try
        {
            adapter.DangerousAddRef(ref adapterRefAdded);
            _ = WgpuNative.wgpuAdapterRequestDevice(
                adapter.DangerousGetPointer(),
                descriptor,
                callbackInfo);
        }
        catch
        {
            stateHandle.Free();
            state.ReleaseSinks();
            throw;
        }
        finally
        {
            if (adapterRefAdded)
            {
                adapter.DangerousRelease();
            }
        }

        return state.Completion.Task;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnAdapterRequestCompleted(
        WGPURequestAdapterStatus status,
        WGPUAdapterImpl* adapter,
        WGPUStringView message,
        void* userdata1,
        void* userdata2)
    {
        _ = userdata2;
        GCHandle stateHandle = GCHandle.FromIntPtr((nint)userdata1);
        try
        {
            AdapterRequestState state = (AdapterRequestState)stateHandle.Target!;
            if (status == WGPURequestAdapterStatus.Success && adapter is not null)
            {
                state.Completion.TrySetResult(new WgpuAdapterHandle(adapter));
            }
            else
            {
                state.Completion.TrySetException(CreateRequestException(status, message));
            }
        }
        catch
        {
            // Exceptions cannot cross a reverse P/Invoke boundary.
        }
        finally
        {
            stateHandle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDeviceRequestCompleted(
        WGPURequestDeviceStatus status,
        WGPUDeviceImpl* device,
        WGPUStringView message,
        void* userdata1,
        void* userdata2)
    {
        _ = userdata2;
        GCHandle stateHandle = GCHandle.FromIntPtr((nint)userdata1);
        DeviceRequestState state = (DeviceRequestState)stateHandle.Target!;
        try
        {
            if (status == WGPURequestDeviceStatus.Success && device is not null)
            {
                state.Completion.TrySetResult(
                    new WgpuDeviceHandle(
                        device,
                        state.TakeErrorSinkHandle(),
                        state.TakeDeviceLostSinkHandle()));
            }
            else
            {
                state.Completion.TrySetException(CreateRequestException(status, message));
            }
        }
        catch
        {
            // Exceptions cannot cross a reverse P/Invoke boundary.
        }
        finally
        {
            state.ReleaseSinks();
            stateHandle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnUncapturedError(
        WGPUDeviceImpl** device,
        WGPUErrorType type,
        WGPUStringView message,
        void* userdata1,
        void* userdata2)
    {
        _ = device;
        _ = userdata2;
        try
        {
            WgpuDeviceErrorSink sink =
                (WgpuDeviceErrorSink)GCHandle.FromIntPtr((nint)userdata1).Target!;
            string nativeMessage = Decode(message);
            sink.Add(new WgpuDeviceError(type, nativeMessage));
        }
        catch
        {
            // Exceptions cannot cross a reverse P/Invoke boundary.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDeviceLost(
        WGPUDeviceImpl** device,
        WGPUDeviceLostReason reason,
        WGPUStringView message,
        void* userdata1,
        void* userdata2)
    {
        _ = device;
        _ = userdata2;
        try
        {
            WgpuDeviceLostSink sink =
                (WgpuDeviceLostSink)GCHandle.FromIntPtr((nint)userdata1).Target!;
            string nativeMessage = Decode(message);
            sink.Report(string.IsNullOrWhiteSpace(nativeMessage)
                ? reason.ToString()
                : $"{reason}: {nativeMessage}");
        }
        catch
        {
            // Exceptions cannot cross a reverse P/Invoke boundary.
        }
    }

    private static WgpuNativeRequestException CreateRequestException<TStatus>(
        TStatus status,
        WGPUStringView message)
        where TStatus : struct, Enum
    {
        return new WgpuNativeRequestException(status.ToString(), Decode(message));
    }

    internal static string Decode(WGPUStringView value)
    {
        if (value.data is null || value.length == 0)
        {
            return string.Empty;
        }

        if (value.length > int.MaxValue)
        {
            return "Native error message exceeded the managed string limit.";
        }

        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(value.data, checked((int)value.length)));
    }

    private sealed class AdapterRequestState
    {
        internal TaskCompletionSource<WgpuAdapterHandle> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class DeviceRequestState(nint errorSinkHandle, nint deviceLostSinkHandle)
    {
        private nint errorSinkHandle = errorSinkHandle;
        private nint deviceLostSinkHandle = deviceLostSinkHandle;

        internal TaskCompletionSource<WgpuDeviceHandle> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal nint TakeErrorSinkHandle()
        {
            nint result = errorSinkHandle;
            errorSinkHandle = 0;
            return result;
        }

        internal nint TakeDeviceLostSinkHandle()
        {
            nint result = deviceLostSinkHandle;
            deviceLostSinkHandle = 0;
            return result;
        }

        internal void ReleaseSinks()
        {
            if (errorSinkHandle != 0)
            {
                GCHandle.FromIntPtr(errorSinkHandle).Free();
                errorSinkHandle = 0;
            }
            if (deviceLostSinkHandle != 0)
            {
                GCHandle.FromIntPtr(deviceLostSinkHandle).Free();
                deviceLostSinkHandle = 0;
            }
        }
    }
}

internal sealed class WgpuNativeRequestException : Exception
{
    internal WgpuNativeRequestException(string status, string nativeMessage)
        : base(string.IsNullOrEmpty(nativeMessage)
            ? $"wgpu-native request failed with status {status}."
            : $"wgpu-native request failed with status {status}: {nativeMessage}")
    {
        Status = status;
        NativeMessage = nativeMessage;
    }

    internal string Status { get; }

    internal string NativeMessage { get; }
}
