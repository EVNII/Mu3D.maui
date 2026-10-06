using System.Text;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.Native.Wgpu;

// Native bootstrap and blocking progress are separate from shared C-API resource creation.
public sealed partial class WgpuGraphicsDevice
{
    /// <summary>Creates a real backend-independent device using the preferred native adapter.</summary>
    /// <param name="timeout">Maximum time allowed for each asynchronous native request.</param>
    /// <param name="cancellationToken">Cancels the managed wait.</param>
    /// <returns>A device that owns its native instance, adapter, device and queue.</returns>
    public static async Task<WgpuGraphicsDevice> CreateAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Device creation timeout must be positive.");
        }

        return await CreateCoreAsync(
            timeout,
            forceFallbackAdapter: false,
            WGPUBackendType.Undefined,
            cancellationToken,
            cooperativeCancellation: false).ConfigureAwait(false) ??
            throw new InvalidOperationException("The throwing device request unexpectedly returned no device.");
    }

    internal static async Task<WgpuGraphicsDevice> CreateForTestingAsync(
        TimeSpan timeout,
        bool forceFallbackAdapter,
        WGPUBackendType backendType,
        CancellationToken cancellationToken = default,
        bool disableShaderF16 = false) =>
        await CreateCoreAsync(
            timeout,
            forceFallbackAdapter,
            backendType,
            cancellationToken,
            cooperativeCancellation: false,
            disableShaderF16: disableShaderF16).ConfigureAwait(false) ??
        throw new InvalidOperationException("The throwing device request unexpectedly returned no device.");

    internal static Task<WgpuGraphicsDevice?> TryCreateForLifecycleAsync(
        TimeSpan timeout,
        bool forceFallbackAdapter,
        WGPUBackendType backendType,
        CancellationToken cancellationToken) =>
        CreateCoreAsync(
            timeout,
            forceFallbackAdapter,
            backendType,
            cancellationToken,
            cooperativeCancellation: true);

    internal unsafe nint NativeD3D12Device =>
        (nint)WgpuNative.wgpuDeviceGetNativeD3D12Device(NativeDevice);

    internal unsafe nint NativeD3D12CommandQueue =>
        (nint)WgpuNative.wgpuDeviceGetNativeD3D12CommandQueue(NativeDevice);

    internal static unsafe nint GetNativeD3D12Resource(GraphicsTexture texture)
    {
        WgpuGraphicsTexture nativeTexture = RequireResource<WgpuGraphicsTexture>(texture);
        return (nint)WgpuNative.wgpuTextureGetNativeD3D12Resource(nativeTexture.Pointer);
    }

    internal unsafe void ThrowIfNativeErrors(string operation)
        => ThrowIfNativeErrors(NativeDevice, operation);

    private unsafe void ThrowIfNativeErrors(WGPUDeviceImpl* retainedDevice, string operation)
    {
        _ = WgpuNative.wgpuDevicePoll(retainedDevice, 0, null);
        IReadOnlyList<WgpuDeviceError> errors = device.DrainErrors();
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                $"wgpu {operation} failed: {string.Join("; ", errors.Select(static error => $"{error.Type}: {error.Message}"))}");
        }
    }

    internal unsafe void WaitForSubmittedWork(string operation)
        => WaitForSubmittedWork(NativeDevice, operation);

    private unsafe void WaitForSubmittedWork(WGPUDeviceImpl* retainedDevice, string operation)
    {
        _ = WgpuNative.wgpuDevicePoll(retainedDevice, 1, null);
        ThrowIfNativeErrors(retainedDevice, operation);
        if (State == GraphicsDeviceState.Lost)
        {
            throw new InvalidOperationException($"The graphics device was lost: {LostReason}");
        }
    }

    internal NativeDeviceReadLease RetainNativeDeviceForRead() => new(this, device);

    // Retain the SafeHandle itself: a native AddRef alone would not keep its callback GCHandles alive.
    internal sealed unsafe class NativeDeviceReadLease : IDisposable
    {
        private readonly WgpuGraphicsDevice owner;
        private readonly WgpuDeviceHandle handle;
        private readonly WGPUDeviceImpl* pointer;
        private int retained;

        internal NativeDeviceReadLease(WgpuGraphicsDevice owner, WgpuDeviceHandle handle)
        {
            this.owner = owner;
            this.handle = handle;
            bool addedReference = false;
            try
            {
                handle.DangerousAddRef(ref addedReference);
                pointer = handle.DangerousGetPointer();
                if (pointer is null)
                {
                    throw new ObjectDisposedException(nameof(WgpuGraphicsDevice));
                }
                retained = 1;
            }
            catch
            {
                if (addedReference) handle.DangerousRelease();
                throw;
            }
        }

        internal void WaitForSubmittedWork(string operation) =>
            owner.WaitForSubmittedWork(pointer, operation);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref retained, 0) != 0) handle.DangerousRelease();
        }
    }

    internal unsafe void DrainSubmittedWorkForDisposal()
    {
        _ = WgpuNative.wgpuDevicePoll(NativeDevice, 1, null);
        _ = device.DrainErrors();
    }

    private static async Task<WgpuGraphicsDevice?> CreateCoreAsync(
        TimeSpan timeout,
        bool forceFallbackAdapter,
        WGPUBackendType backendType,
        CancellationToken cancellationToken,
        bool cooperativeCancellation,
        bool disableShaderF16 = false)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Device creation timeout must be positive.");
        }
        if (cooperativeCancellation && cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        WgpuInstanceHandle? instance = null;
        WgpuAdapterHandle? adapter = null;
        WgpuDeviceHandle? device = null;
        WgpuDeviceLostSink deviceLostSink = new();
        try
        {
            instance = CreateInstance();
            Task<WgpuAdapterHandle> adapterRequest =
                RequestAdapterAsync(instance, forceFallbackAdapter, backendType);
            adapter = cooperativeCancellation
                ? await WgpuSurfaceProbe.TryWaitForOwnedHandleAsync(
                    adapterRequest,
                    timeout,
                    cancellationToken).ConfigureAwait(false)
                : await WaitForOwnedHandleAsync(
                    adapterRequest,
                    timeout,
                    cancellationToken).ConfigureAwait(false);
            if (adapter is null)
            {
                return null;
            }

            WgpuEnabledFeatures enabledFeatures = ReadEnabledFeatures(adapter);
            if (disableShaderF16) enabledFeatures = enabledFeatures with { ShaderF16 = false };
            Task<WgpuDeviceHandle> deviceRequest =
                RequestDeviceAsync(adapter, enabledFeatures, deviceLostSink);
            device = cooperativeCancellation
                ? await WgpuSurfaceProbe.TryWaitForOwnedHandleAsync(
                    deviceRequest,
                    timeout,
                    cancellationToken).ConfigureAwait(false)
                : await WaitForOwnedHandleAsync(
                    deviceRequest,
                    timeout,
                    cancellationToken).ConfigureAwait(false);
            if (device is null)
            {
                return null;
            }

            WgpuGraphicsDevice result = new(
                instance,
                adapter,
                device,
                deviceLostSink,
                enabledFeatures);
            instance = null;
            adapter = null;
            device = null;
            return result;
        }
        finally
        {
            device?.Dispose();
            adapter?.Dispose();
            instance?.Dispose();
        }
    }

    private static async Task<THandle> WaitForOwnedHandleAsync<THandle>(
        Task<THandle> request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        where THandle : IDisposable
    {
        try
        {
            return await request.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _ = request.ContinueWith(
                static completedRequest =>
                {
                    if (completedRequest.Status == TaskStatus.RanToCompletion)
                    {
                        completedRequest.Result.Dispose();
                    }
                    else
                    {
                        _ = completedRequest.Exception;
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private static unsafe Task<WgpuAdapterHandle> RequestAdapterAsync(
        WgpuInstanceHandle instance,
        bool forceFallbackAdapter,
        WGPUBackendType backendType)
    {
        WGPURequestAdapterOptions options = new()
        {
            featureLevel = WGPUFeatureLevel.Core,
            powerPreference = forceFallbackAdapter
                ? WGPUPowerPreference.LowPower
                : WGPUPowerPreference.HighPerformance,
            forceFallbackAdapter = forceFallbackAdapter ? 1u : 0u,
            backendType = backendType,
        };
        return WgpuBootstrap.RequestAdapterAsync(instance, &options);
    }

    private static unsafe WgpuInstanceHandle CreateInstance() => WgpuBootstrap.CreateInstance();

    internal static unsafe bool HasShaderF16(WgpuAdapterHandle adapter) =>
        WgpuNative.wgpuAdapterHasFeature(
            adapter.DangerousGetPointer(),
            WGPUFeatureName.ShaderF16) != 0;

    internal static unsafe WgpuEnabledFeatures ReadEnabledFeatures(WgpuAdapterHandle adapter) => new(
        HasShaderF16(adapter),
        HasFeature(adapter, WGPUFeatureName.TextureCompressionBC),
        HasFeature(adapter, WGPUFeatureName.TextureCompressionETC2),
        HasFeature(adapter, WGPUFeatureName.TextureCompressionASTC));

    // wgpu gates cube-array sampling behind a downlevel flag with no public feature query, and
    // its Metal backend derives that flag from the deprecated MTLFeatureSet query, which new
    // Apple devices no longer answer. Probe the real device with a minimal cube-array module.
    internal static unsafe bool ProbeCubeArrayTextureSupport(WgpuDeviceHandle device)
    {
        const string probeCode = """
            @group(0) @binding(0) var probe_texture: texture_cube_array<f32>;

            @fragment
            fn fs_probe() -> @location(0) vec4f {
                let size = textureDimensions(probe_texture);
                return vec4f(f32(size.x));
            }
            """;
        byte[] code = Encoding.UTF8.GetBytes(probeCode);
        byte[] label = Encode("cube-array capability probe");
        fixed (byte* codePointer = code)
        fixed (byte* labelPointer = label)
        {
            WGPUShaderSourceWGSL wgsl = new()
            {
                chain = new WGPUChainedStruct { sType = WGPUSType.ShaderSourceWGSL },
                code = CreateStringView(codePointer, code.Length),
            };
            WGPUShaderModuleDescriptor descriptor = new()
            {
                nextInChain = &wgsl.chain,
                label = CreateStringView(labelPointer, label.Length),
            };
            WGPUShaderModuleImpl* module = WgpuNative.wgpuDeviceCreateShaderModule(
                device.DangerousGetPointer(),
                &descriptor);
            if (module is not null)
            {
                WgpuNative.wgpuShaderModuleRelease(module);
            }
            _ = WgpuNative.wgpuDevicePoll(device.DangerousGetPointer(), 0, null);
            IReadOnlyList<WgpuDeviceError> errors = device.DrainErrors();
            return module is not null && !errors.Any(static error =>
                error.Message.Contains("CUBE_ARRAY_TEXTURES", StringComparison.Ordinal));
        }
    }

    private static unsafe bool HasFeature(WgpuAdapterHandle adapter, WGPUFeatureName feature) =>
        WgpuNative.wgpuAdapterHasFeature(adapter.DangerousGetPointer(), feature) != 0;

    internal static unsafe Task<WgpuDeviceHandle> RequestDeviceAsync(
        WgpuAdapterHandle adapter,
        WgpuEnabledFeatures enabledFeatures,
        WgpuDeviceLostSink deviceLostSink)
    {
        WGPUFeatureName* requiredFeatures = stackalloc WGPUFeatureName[4];
        nuint requiredFeatureCount = 0;
        if (enabledFeatures.ShaderF16)
        {
            requiredFeatures[requiredFeatureCount++] = WGPUFeatureName.ShaderF16;
        }
        if (enabledFeatures.TextureCompressionBc)
        {
            requiredFeatures[requiredFeatureCount++] = WGPUFeatureName.TextureCompressionBC;
        }
        if (enabledFeatures.TextureCompressionEtc2)
        {
            requiredFeatures[requiredFeatureCount++] = WGPUFeatureName.TextureCompressionETC2;
        }
        if (enabledFeatures.TextureCompressionAstc)
        {
            requiredFeatures[requiredFeatureCount++] = WGPUFeatureName.TextureCompressionASTC;
        }
        WGPULimits requiredLimits = CreateRendererRequiredLimits();
        WGPUDeviceDescriptor descriptor = new()
        {
            requiredLimits = &requiredLimits,
        };
        if (requiredFeatureCount != 0)
        {
            descriptor.requiredFeatureCount = requiredFeatureCount;
            descriptor.requiredFeatures = requiredFeatures;
        }
        return WgpuBootstrap.RequestDeviceAsync(
            adapter,
            &descriptor,
            new WgpuDeviceErrorSink(),
            deviceLostSink);
    }

    private static WGPULimits CreateRendererRequiredLimits() => new()
    {
        // WebGPU requires every limit not explicitly requested to use its undefined sentinel.
        // A zero-initialized WGPULimits instead requests invalid/minimal values for several fields.
        maxTextureDimension1D = uint.MaxValue,
        maxTextureDimension2D = uint.MaxValue,
        maxTextureDimension3D = uint.MaxValue,
        maxTextureArrayLayers = uint.MaxValue,
        maxBindGroups = uint.MaxValue,
        maxBindGroupsPlusVertexBuffers = uint.MaxValue,
        maxBindingsPerBindGroup = uint.MaxValue,
        maxDynamicUniformBuffersPerPipelineLayout = uint.MaxValue,
        maxDynamicStorageBuffersPerPipelineLayout = uint.MaxValue,
        // Mu3D's portable material variants stay within WebGPU's guaranteed baseline.
        maxSampledTexturesPerShaderStage = 16,
        maxSamplersPerShaderStage = uint.MaxValue,
        // Explicitly request WebGPU's guaranteed baseline of 8: OpenPBR's hybrid/path-tracing
        // pipelines bind 7 storage buffers in the fragment stage. Leaving this undefined would
        // resolve to the downlevel tier (4) on every iOS device — wgpu's Metal backend reports
        // only 15 inter-stage shader variables there (its varying count check uses a macOS-only
        // MTLFeatureSet), so wgpu-native's base-limit selection falls back from core defaults
        // (8) to downlevel defaults (4). The Metal adapter's actual ceiling is 31.
        maxStorageBuffersPerShaderStage = 8,
        maxStorageTexturesPerShaderStage = uint.MaxValue,
        maxUniformBuffersPerShaderStage = uint.MaxValue,
        maxUniformBufferBindingSize = ulong.MaxValue,
        maxStorageBufferBindingSize = ulong.MaxValue,
        minUniformBufferOffsetAlignment = uint.MaxValue,
        minStorageBufferOffsetAlignment = uint.MaxValue,
        maxVertexBuffers = uint.MaxValue,
        maxBufferSize = ulong.MaxValue,
        maxVertexAttributes = uint.MaxValue,
        maxVertexBufferArrayStride = uint.MaxValue,
        maxInterStageShaderVariables = uint.MaxValue,
        maxColorAttachments = uint.MaxValue,
        maxColorAttachmentBytesPerSample = uint.MaxValue,
        maxComputeWorkgroupStorageSize = uint.MaxValue,
        maxComputeInvocationsPerWorkgroup = uint.MaxValue,
        maxComputeWorkgroupSizeX = uint.MaxValue,
        maxComputeWorkgroupSizeY = uint.MaxValue,
        maxComputeWorkgroupSizeZ = uint.MaxValue,
        maxComputeWorkgroupsPerDimension = uint.MaxValue,
        maxImmediateSize = uint.MaxValue,
    };

}
