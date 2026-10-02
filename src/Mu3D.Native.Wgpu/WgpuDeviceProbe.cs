using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.Native.Wgpu;

/// <summary>
/// Creates a short-lived native WebGPU device and reports capabilities useful for device validation.
/// </summary>
public static class WgpuDeviceProbe
{
    /// <summary>Creates and releases a native adapter/device pair within the supplied timeout.</summary>
    /// <param name="timeout">Maximum time allowed for each asynchronous native request.</param>
    /// <param name="cancellationToken">Cancels the managed wait.</param>
    /// <returns>A snapshot of the adapter and device capabilities.</returns>
    public static async Task<WgpuDeviceProbeResult> ProbeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Probe timeout must be positive.");
        }

        return await ProbeCoreAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WgpuDeviceProbeResult> ProbeCoreAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using WgpuInstanceHandle instance = CreateInstance();
        using WgpuAdapterHandle adapter = await WaitForOwnedHandleAsync(
            RequestAdapterAsync(instance),
            timeout,
            cancellationToken).ConfigureAwait(false);

        AdapterSnapshot adapterSnapshot = ReadAdapterSnapshot(adapter);
        bool shaderF16Available = adapterSnapshot.Features.Contains(
            WGPUFeatureName.ShaderF16.ToString(),
            StringComparer.Ordinal);
        using WgpuDeviceHandle device = await WaitForOwnedHandleAsync(
            RequestDeviceAsync(adapter, shaderF16Available),
            timeout,
            cancellationToken).ConfigureAwait(false);
        bool shaderF16Enabled = HasFeature(device, WGPUFeatureName.ShaderF16);
        bool rgba16FloatTextureCreated = TryCreateRgba16FloatTexture(device);
        bool cubeArrayTextureShaderCompiled = WgpuGraphicsDevice.ProbeCubeArrayTextureSupport(device);
        uint maxSampledTexturesPerShaderStage = ReadMaxSampledTexturesPerShaderStage(device);

        return new WgpuDeviceProbeResult(
            adapterSnapshot.Backend,
            adapterSnapshot.AdapterType,
            adapterSnapshot.Vendor,
            adapterSnapshot.Architecture,
            adapterSnapshot.Device,
            adapterSnapshot.Description,
            adapterSnapshot.Features,
            shaderF16Available,
            shaderF16Enabled,
            rgba16FloatTextureCreated,
            cubeArrayTextureShaderCompiled,
            maxSampledTexturesPerShaderStage,
            SurfaceProbed: false);
    }

    private static unsafe uint ReadMaxSampledTexturesPerShaderStage(WgpuDeviceHandle device)
    {
        WGPULimits limits = default;
        WGPUStatus status = WgpuNative.wgpuDeviceGetLimits(device.DangerousGetPointer(), &limits);
        return status == WGPUStatus.Success ? limits.maxSampledTexturesPerShaderStage : 0;
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

    private static unsafe WgpuInstanceHandle CreateInstance() => WgpuBootstrap.CreateInstance();

    private static unsafe bool HasFeature(WgpuDeviceHandle device, WGPUFeatureName feature) =>
        WgpuNative.wgpuDeviceHasFeature(device.DangerousGetPointer(), feature) != 0;

    private static unsafe Task<WgpuAdapterHandle> RequestAdapterAsync(WgpuInstanceHandle instance)
    {
        WGPURequestAdapterOptions options = new()
        {
            featureLevel = WGPUFeatureLevel.Core,
            powerPreference = WGPUPowerPreference.HighPerformance,
            backendType = WGPUBackendType.Undefined,
        };
        return WgpuBootstrap.RequestAdapterAsync(instance, &options);
    }

    private static unsafe Task<WgpuDeviceHandle> RequestDeviceAsync(
        WgpuAdapterHandle adapter,
        bool enableShaderF16)
    {
        WGPUFeatureName requiredFeature = WGPUFeatureName.ShaderF16;
        WGPUDeviceDescriptor descriptor = default;
        if (enableShaderF16)
        {
            descriptor.requiredFeatureCount = 1;
            descriptor.requiredFeatures = &requiredFeature;
        }

        return WgpuBootstrap.RequestDeviceAsync(adapter, &descriptor);
    }

    private static unsafe AdapterSnapshot ReadAdapterSnapshot(WgpuAdapterHandle adapter)
    {
        WGPUAdapterInfo info = default;
        WGPUStatus status = WgpuNative.wgpuAdapterGetInfo(adapter.DangerousGetPointer(), &info);
        if (status != WGPUStatus.Success)
        {
            throw new InvalidOperationException($"wgpuAdapterGetInfo failed with status {status}.");
        }

        try
        {
            return new AdapterSnapshot(
                info.backendType.ToString(),
                info.adapterType.ToString(),
                WgpuBootstrap.Decode(info.vendor),
                WgpuBootstrap.Decode(info.architecture),
                WgpuBootstrap.Decode(info.device),
                WgpuBootstrap.Decode(info.description),
                ReadFeatures(adapter));
        }
        finally
        {
            WgpuNative.wgpuAdapterInfoFreeMembers(info);
        }
    }

    private static unsafe IReadOnlyList<string> ReadFeatures(WgpuAdapterHandle adapter)
    {
        WGPUSupportedFeatures supported = default;
        WgpuNative.wgpuAdapterGetFeatures(adapter.DangerousGetPointer(), &supported);
        try
        {
            if (supported.features is null || supported.featureCount == 0)
            {
                return [];
            }

            string[] features = new string[checked((int)supported.featureCount)];
            for (int index = 0; index < features.Length; index++)
            {
                features[index] = supported.features[index].ToString();
            }

            Array.Sort(features, StringComparer.Ordinal);
            return features;
        }
        finally
        {
            WgpuNative.wgpuSupportedFeaturesFreeMembers(supported);
        }
    }

    private static unsafe bool TryCreateRgba16FloatTexture(WgpuDeviceHandle device)
    {
        WGPUTextureDescriptor descriptor = new()
        {
            usage = WgpuNative.WGPUTextureUsage_TextureBinding |
                WgpuNative.WGPUTextureUsage_RenderAttachment,
            dimension = WGPUTextureDimension._2D,
            size = new WGPUExtent3D
            {
                width = 1,
                height = 1,
                depthOrArrayLayers = 1,
            },
            format = WGPUTextureFormat.RGBA16Float,
            mipLevelCount = 1,
            sampleCount = 1,
        };
        WGPUTextureImpl* texture = WgpuNative.wgpuDeviceCreateTexture(
            device.DangerousGetPointer(),
            &descriptor);
        if (texture is null)
        {
            return false;
        }

        WgpuNative.wgpuTextureRelease(texture);
        return true;
    }

    private sealed record AdapterSnapshot(
        string Backend,
        string AdapterType,
        string Vendor,
        string Architecture,
        string Device,
        string Description,
        IReadOnlyList<string> Features);
}

/// <summary>Reports a completed native device probe without claiming surface or HDR output support.</summary>
/// <param name="Backend">Native graphics API selected by wgpu-native.</param>
/// <param name="AdapterType">Adapter classification reported by wgpu-native.</param>
/// <param name="Vendor">Adapter vendor text.</param>
/// <param name="Architecture">Adapter architecture text.</param>
/// <param name="Device">Adapter device text.</param>
/// <param name="Description">Adapter description.</param>
/// <param name="AvailableFeatures">Features advertised by the adapter.</param>
/// <param name="ShaderF16Available">Whether the adapter advertises ShaderF16.</param>
/// <param name="ShaderF16Enabled">Whether ShaderF16 was enabled on the created device.</param>
/// <param name="Rgba16FloatTextureCreated">Whether a real RGBA16Float texture was created.</param>
/// <param name="CubeArrayTextureShaderCompiled">
/// Whether a minimal cube-array sampling shader validated on the created device. False on devices
/// whose backend wrongly withholds the cube-array downlevel flag (for example Apple devices whose
/// Metal driver no longer answers the deprecated MTLFeatureSet query wgpu relies on).
/// </param>
/// <param name="MaxSampledTexturesPerShaderStage">
/// The device limit for sampled textures per shader stage, or zero when the limit query failed.
/// </param>
/// <param name="SurfaceProbed">Whether this result includes surface negotiation.</param>
public sealed record WgpuDeviceProbeResult(
    string Backend,
    string AdapterType,
    string Vendor,
    string Architecture,
    string Device,
    string Description,
    IReadOnlyList<string> AvailableFeatures,
    bool ShaderF16Available,
    bool ShaderF16Enabled,
    bool Rgba16FloatTextureCreated,
    bool CubeArrayTextureShaderCompiled,
    uint MaxSampledTexturesPerShaderStage,
    bool SurfaceProbed);
