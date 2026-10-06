using Mu3D.Graphics;
using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.Native.Wgpu;

/// <summary>Creates a temporary wgpu surface and reports its adapter-compatible capabilities.</summary>
public static class WgpuSurfaceProbe
{
    private static readonly Task NeverCompletes =
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;

    /// <summary>Probes a concrete platform surface without configuring or presenting it.</summary>
    public static async Task<WgpuSurfaceProbeResult> ProbeAsync(
        NativeSurfaceSource source,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (source.Handle == 0)
        {
            throw new ArgumentException("The native surface handle must not be zero.", nameof(source));
        }

        if (source.Kind == NativeSurfaceKind.WindowsHwnd && source.AuxiliaryHandle == 0)
        {
            throw new ArgumentException("A Windows surface requires a non-zero HINSTANCE.", nameof(source));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Probe timeout must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        // The asynchronous adapter request must not outlive a host-owned native consumer.
        // Using-declaration order releases the surface before returning this producer lease.
        using IDisposable? sourceLifetimeLease = source.AcquireLifetime();
        using WgpuInstanceHandle instance = CreateInstance(source.Kind);
        using WgpuSurfaceHandle surface = CreateSurface(instance, source);
        using WgpuAdapterHandle adapter = await WaitForOwnedHandleAsync(
            RequestCompatibleAdapterAsync(instance, surface),
            timeout,
            cancellationToken).ConfigureAwait(false);

        return new WgpuSurfaceProbeResult(ReadBackend(adapter),
            source.ConstrainCapabilities(ReadCapabilities(surface, adapter)));
    }

    internal static async Task<THandle> WaitForOwnedHandleAsync<THandle>(
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

    // Automatic MAUI teardown is routine. Race the uncancellable native callback without using
    // WaitAsync(token), then release any owned handle that arrives after its view has left.
    internal static async Task<THandle?> TryWaitForOwnedHandleAsync<THandle>(
        Task<THandle> request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        where THandle : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Native request timeout must be positive.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            ObserveLateOwnedHandle(request);
            return null;
        }

        TaskCompletionSource? cancellationCompletion = cancellationToken.CanBeCanceled
            ? new(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        using CancellationTokenRegistration registration = cancellationCompletion is null
            ? default
            : cancellationToken.UnsafeRegister(
                static state => ((TaskCompletionSource)state!).TrySetResult(),
                cancellationCompletion);
        Task cancellation = cancellationCompletion?.Task ?? NeverCompletes;
        Task delay = Task.Delay(timeout);
        Task completed = await Task.WhenAny(request, cancellation, delay).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            ObserveLateOwnedHandle(request);
            return null;
        }
        if (request.IsCompleted)
        {
            return await request.ConfigureAwait(false);
        }

        ObserveLateOwnedHandle(request);
        if (ReferenceEquals(completed, cancellation) || cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        throw new TimeoutException($"The native wgpu request exceeded {timeout}.");
    }

    private static void ObserveLateOwnedHandle<THandle>(Task<THandle> request)
        where THandle : class, IDisposable
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
    }

    internal static unsafe WgpuInstanceHandle CreateInstance(NativeSurfaceKind surfaceKind)
    {
        ulong backends = surfaceKind switch
        {
            NativeSurfaceKind.MetalLayer => WgpuNative.WGPUInstanceBackend_Metal,
            NativeSurfaceKind.AndroidNativeWindow => WgpuNative.WGPUInstanceBackend_Vulkan,
            NativeSurfaceKind.WindowsHwnd or NativeSurfaceKind.WindowsSwapChainPanel =>
                WgpuNative.WGPUInstanceBackend_DX12,
            _ => throw new ArgumentOutOfRangeException(
                nameof(surfaceKind), surfaceKind, "Unknown surface kind."),
        };
        WGPUInstanceExtras extras = new()
        {
            chain = new WGPUChainedStruct
            {
                sType = (WGPUSType)WGPUNativeSType.WGPUSType_InstanceExtras,
            },
            backends = backends,
            flags = WgpuNative.WGPUInstanceFlag_Default,
        };
        WGPUInstanceDescriptor descriptor = new() { nextInChain = &extras.chain };
        return WgpuBootstrap.CreateInstance(&descriptor);
    }

    internal static unsafe WgpuSurfaceHandle CreateSurface(
        WgpuInstanceHandle instance,
        NativeSurfaceSource source)
    {
        WGPUSurfaceDescriptor descriptor = default;
        WGPUSurfaceImpl* surface;

        switch (source.Kind)
        {
            case NativeSurfaceKind.MetalLayer:
            {
                WGPUSurfaceSourceMetalLayer metal = new()
                {
                    chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceMetalLayer },
                    layer = (void*)source.Handle,
                };
                descriptor.nextInChain = &metal.chain;
                surface = WgpuNative.wgpuInstanceCreateSurface(instance.DangerousGetPointer(), &descriptor);
                break;
            }
            case NativeSurfaceKind.AndroidNativeWindow:
            {
                WGPUSurfaceSourceAndroidNativeWindow android = new()
                {
                    chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceAndroidNativeWindow },
                    window = (void*)source.Handle,
                };
                descriptor.nextInChain = &android.chain;
                surface = WgpuNative.wgpuInstanceCreateSurface(instance.DangerousGetPointer(), &descriptor);
                break;
            }
            case NativeSurfaceKind.WindowsHwnd:
            {
                WGPUSurfaceSourceWindowsHWND windows = new()
                {
                    chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceWindowsHWND },
                    hwnd = (void*)source.Handle,
                    hinstance = (void*)source.AuxiliaryHandle,
                };
                descriptor.nextInChain = &windows.chain;
                surface = WgpuNative.wgpuInstanceCreateSurface(instance.DangerousGetPointer(), &descriptor);
                break;
            }
            case NativeSurfaceKind.WindowsSwapChainPanel:
            {
                WGPUSurfaceSourceSwapChainPanel windows = new()
                {
                    chain = new WGPUChainedStruct
                    {
                        sType = (WGPUSType)WGPUNativeSType.WGPUSType_SurfaceSourceSwapChainPanel,
                    },
                    panelNative = (void*)source.Handle,
                };
                descriptor.nextInChain = &windows.chain;
                surface = WgpuNative.wgpuInstanceCreateSurface(instance.DangerousGetPointer(), &descriptor);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(source), source.Kind, "Unknown surface kind.");
        }

        return surface is null
            ? throw new InvalidOperationException("wgpuInstanceCreateSurface returned a null surface.")
            : new WgpuSurfaceHandle(surface);
    }

    internal static unsafe Task<WgpuAdapterHandle> RequestCompatibleAdapterAsync(
        WgpuInstanceHandle instance,
        WgpuSurfaceHandle surface)
    {
        WGPURequestAdapterOptions options = new()
        {
            featureLevel = WGPUFeatureLevel.Core,
            powerPreference = WGPUPowerPreference.HighPerformance,
            compatibleSurface = surface.DangerousGetPointer(),
        };
        return WgpuBootstrap.RequestAdapterAsync(instance, &options);
    }

    private static unsafe string ReadBackend(WgpuAdapterHandle adapter)
    {
        WGPUAdapterInfo info = default;
        WGPUStatus status = WgpuNative.wgpuAdapterGetInfo(adapter.DangerousGetPointer(), &info);
        if (status != WGPUStatus.Success)
        {
            throw new InvalidOperationException($"wgpuAdapterGetInfo failed with status {status}.");
        }

        try
        {
            return info.backendType.ToString();
        }
        finally
        {
            WgpuNative.wgpuAdapterInfoFreeMembers(info);
        }
    }

    internal static unsafe SurfaceCapabilities ReadCapabilities(
        WgpuSurfaceHandle surface,
        WgpuAdapterHandle adapter) => ReadCapabilities(surface, adapter, out _);

    internal static unsafe SurfaceCapabilities ReadCapabilities(
        WgpuSurfaceHandle surface,
        WgpuAdapterHandle adapter,
        out GraphicsTextureUsage supportedUsages)
    {
        WGPUSurfaceCapabilities capabilities = default;
        WGPUStatus status = WgpuNative.wgpuSurfaceGetCapabilities(
            surface.DangerousGetPointer(),
            adapter.DangerousGetPointer(),
            &capabilities);
        if (status != WGPUStatus.Success)
        {
            throw new InvalidOperationException($"wgpuSurfaceGetCapabilities failed with status {status}.");
        }

        try
        {
            supportedUsages = MapTextureUsages(capabilities.usages);
            List<PresentationFormat> formats = [];
            List<string> unmappedFormats = [];
            for (nuint index = 0; index < capabilities.formatCount; index++)
            {
                WGPUTextureFormat native = capabilities.formats[index];
                PresentationFormat mapped = MapFormat(native);
                if (mapped == PresentationFormat.Unknown)
                {
                    unmappedFormats.Add(native.ToString());
                }
                else
                {
                    formats.Add(mapped);
                }
            }

            List<SurfacePresentMode> presentModes = [];
            for (nuint index = 0; index < capabilities.presentModeCount; index++)
            {
                presentModes.Add(MapPresentMode(capabilities.presentModes[index]));
            }

            List<SurfaceAlphaMode> alphaModes = [];
            for (nuint index = 0; index < capabilities.alphaModeCount; index++)
            {
                alphaModes.Add(MapAlphaMode(capabilities.alphaModes[index]));
            }

            return new SurfaceCapabilities(
                formats,
                presentModes,
                alphaModes,
                formats.Contains(PresentationFormat.Rgba16Float),
                unmappedFormats);
        }
        finally
        {
            WgpuNative.wgpuSurfaceCapabilitiesFreeMembers(capabilities);
        }
    }

    internal static GraphicsTextureUsage MapTextureUsages(ulong usages)
    {
        GraphicsTextureUsage result = GraphicsTextureUsage.None;
        AddUsage(usages, WgpuNative.WGPUTextureUsage_CopySrc, GraphicsTextureUsage.CopySource, ref result);
        AddUsage(usages, WgpuNative.WGPUTextureUsage_CopyDst, GraphicsTextureUsage.CopyDestination, ref result);
        AddUsage(usages, WgpuNative.WGPUTextureUsage_TextureBinding, GraphicsTextureUsage.TextureBinding, ref result);
        AddUsage(usages, WgpuNative.WGPUTextureUsage_StorageBinding, GraphicsTextureUsage.StorageBinding, ref result);
        AddUsage(usages, WgpuNative.WGPUTextureUsage_RenderAttachment, GraphicsTextureUsage.RenderAttachment, ref result);
        return result;
    }

    private static void AddUsage(
        ulong nativeUsages,
        ulong nativeUsage,
        GraphicsTextureUsage usage,
        ref GraphicsTextureUsage result)
    {
        if ((nativeUsages & nativeUsage) != 0)
        {
            result |= usage;
        }
    }

    private static PresentationFormat MapFormat(WGPUTextureFormat format) => format switch
    {
        WGPUTextureFormat.BGRA8Unorm => PresentationFormat.Bgra8Unorm,
        WGPUTextureFormat.BGRA8UnormSrgb => PresentationFormat.Bgra8UnormSrgb,
        WGPUTextureFormat.RGBA8Unorm => PresentationFormat.Rgba8Unorm,
        WGPUTextureFormat.RGBA8UnormSrgb => PresentationFormat.Rgba8UnormSrgb,
        WGPUTextureFormat.RGBA16Float => PresentationFormat.Rgba16Float,
        WGPUTextureFormat.RGB10A2Unorm => PresentationFormat.Rgb10A2Unorm,
        _ => PresentationFormat.Unknown,
    };

    private static SurfacePresentMode MapPresentMode(WGPUPresentMode mode) => mode switch
    {
        WGPUPresentMode.Fifo => SurfacePresentMode.Fifo,
        WGPUPresentMode.FifoRelaxed => SurfacePresentMode.FifoRelaxed,
        WGPUPresentMode.Immediate => SurfacePresentMode.Immediate,
        WGPUPresentMode.Mailbox => SurfacePresentMode.Mailbox,
        _ => SurfacePresentMode.Unknown,
    };

    private static SurfaceAlphaMode MapAlphaMode(WGPUCompositeAlphaMode mode) => mode switch
    {
        WGPUCompositeAlphaMode.Auto => SurfaceAlphaMode.Automatic,
        WGPUCompositeAlphaMode.Opaque => SurfaceAlphaMode.Opaque,
        WGPUCompositeAlphaMode.Premultiplied => SurfaceAlphaMode.Premultiplied,
        WGPUCompositeAlphaMode.Unpremultiplied => SurfaceAlphaMode.Unpremultiplied,
        WGPUCompositeAlphaMode.Inherit => SurfaceAlphaMode.Inherit,
        _ => SurfaceAlphaMode.Unknown,
    };
}

/// <summary>Reports the backend and capabilities selected for a concrete platform surface.</summary>
/// <param name="Backend">The native graphics API selected by wgpu-native.</param>
/// <param name="Capabilities">Formats and presentation modes advertised by the surface.</param>
public sealed record WgpuSurfaceProbeResult(string Backend, SurfaceCapabilities Capabilities);
