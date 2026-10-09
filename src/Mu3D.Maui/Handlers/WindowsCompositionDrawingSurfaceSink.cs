#if WINDOWS
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D11on12;
using Vortice.Direct3D12;
using Vortice.Mathematics;
using MicrosoftDirectXAlphaMode = Microsoft.Graphics.DirectX.DirectXAlphaMode;
using MicrosoftDirectXPixelFormat = Microsoft.Graphics.DirectX.DirectXPixelFormat;
using WinUIGrid = Microsoft.UI.Xaml.Controls.Grid;

namespace Mu3D.Maui.Handlers;

/// <summary>
/// Copies a wgpu-owned D3D12 render target into a WinUI compositor-owned drawing surface.
/// The normal path presents sRGB color. The synchronized-HDR path uses an FP16 render only as a
/// same-tree alpha mask above a direct HDR Surface. Neither path uses CPU readback, an image object
/// or a codec.
/// </summary>
internal sealed class WindowsCompositionDrawingSurfaceSink : IWgpuTexturePresentationSink, IDisposable
{
    private static readonly SurfaceCapabilities SdrDrawingSurfaceCapabilities = new(
        [PresentationFormat.Bgra8UnormSrgb],
        [SurfacePresentMode.Fifo],
        [SurfaceAlphaMode.Premultiplied],
        SupportsRgba16Float: false,
        UnmappedBackendFormats: []);
    private static readonly SurfaceCapabilities SynchronizedHdrMaskCapabilities = new(
        [PresentationFormat.Rgba16Float, PresentationFormat.Bgra8UnormSrgb],
        [SurfacePresentMode.Fifo],
        [SurfaceAlphaMode.Premultiplied],
        SupportsRgba16Float: true,
        UnmappedBackendFormats: []);
    private static readonly Guid CompositorInteropIid =
        new("FAB19398-6D19-4D8A-B752-8F096C396069");
    private static readonly Guid DrawingSurfaceInteropIid =
        new("2D6355C2-AD57-4EAE-92E4-4C3EFF65D578");
    private static readonly Guid D3D11Texture2DIid =
        new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    private readonly WinUIGrid host;
    private readonly object sinkGate = new();
    private readonly Action<Exception> reportError;
    private readonly Func<Microsoft.UI.Xaml.FrameworkElement?>? resolveUnderlay;
    private readonly bool useSynchronizedHdrMask;
    private ID3D12Resource? d3d12Resource;
    private WindowsD3D11On12Bridge.Lease? bridge;
    private ID3D11Resource? wrappedSource;
    private CompositionGraphicsDevice? compositionDevice;
    private CompositionDrawingSurface? drawingSurface;
    private CompositionSurfaceBrush? colorBrush;
    private CompositionVisualSurface? underlaySurface;
    private CompositionSurfaceBrush? underlayBrush;
    private CompositionEffectFactory? underlayMaskFactory;
    private CompositionEffectBrush? underlayMaskBrush;
    private SpriteVisual? visual;
    private nint drawingSurfaceInterop;
    private uint textureWidth;
    private uint textureHeight;
    private double logicalWidth;
    private double logicalHeight;
    private double rasterizationScale = 1.0;
    private bool disposed;

    internal WindowsCompositionDrawingSurfaceSink(
        WinUIGrid host,
        Action<Exception> reportError,
        Func<Microsoft.UI.Xaml.FrameworkElement?>? resolveUnderlay = null)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        this.resolveUnderlay = resolveUnderlay;
        useSynchronizedHdrMask = resolveUnderlay is not null;
    }

    public SurfaceCapabilities Capabilities =>
        useSynchronizedHdrMask
        ? SynchronizedHdrMaskCapabilities
        : SdrDrawingSurfaceCapabilities;

    internal void SynchronizeLayout(
        double logicalWidth,
        double logicalHeight,
        double rasterizationScale)
    {
        if (!double.IsFinite(logicalWidth) || logicalWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        }
        if (!double.IsFinite(logicalHeight) || logicalHeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalHeight));
        }
        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rasterizationScale));
        }

        this.logicalWidth = logicalWidth;
        this.logicalHeight = logicalHeight;
        this.rasterizationScale = rasterizationScale;
        ApplyVisualLayout();
    }

    public void Attach(
        nint nativeD3D12Device,
        nint nativeD3D12CommandQueue,
        nint nativeD3D12Resource,
        uint width,
        uint height,
        PresentationFormat format,
        SurfaceAlphaMode alphaMode)
    {
        ArgumentOutOfRangeException.ThrowIfZero(nativeD3D12Device);
        ArgumentOutOfRangeException.ThrowIfZero(nativeD3D12CommandQueue);
        ArgumentOutOfRangeException.ThrowIfZero(nativeD3D12Resource);
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        InvokeOnUiThread(() =>
        {
            lock (sinkGate)
            {
                AttachCore(
                    nativeD3D12Device,
                    nativeD3D12CommandQueue,
                    nativeD3D12Resource,
                    width,
                    height,
                    format,
                    alphaMode);
            }
        });
    }

    // CompositionDrawingSurface supports worker-thread interop. Serialize the shared immediate
    // context across sinks as well as each session's Present/Detach boundary.
    public void Present()
    {
        lock (sinkGate)
        {
            if (bridge is not { } activeBridge)
                throw new InvalidOperationException("The Windows composition source is not attached.");
            lock (activeBridge.Gate) PresentCore();
        }
    }

    public void Detach()
    {
        InvokeOnUiThread(() =>
        {
            lock (sinkGate)
            {
                if (!disposed)
                {
                    DetachCore();
                }
            }
        });
    }

    public void Dispose()
    {
        try
        {
            InvokeOnUiThread(() =>
            {
                lock (sinkGate)
                {
                    if (!disposed)
                    {
                        DetachCore();
                        disposed = true;
                    }
                }
            });
        }
        catch (Exception exception)
        {
            lock (sinkGate)
            {
                disposed = true;
            }
            ReportError(exception);
        }
    }

    private void AttachCore(
        nint nativeD3D12Device,
        nint nativeD3D12CommandQueue,
        nint nativeD3D12Resource,
        uint width,
        uint height,
        PresentationFormat format,
        SurfaceAlphaMode alphaMode)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (width > int.MaxValue || height > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "The Windows composition drawing-surface extent exceeds Int32 pixel dimensions.");
        }

        DetachCore();
        try
        {
            // The bridge exports borrowed pointers. Give each Vortice wrapper its own COM reference.
            bridge = WindowsD3D11On12Bridge.Acquire(nativeD3D12Device, nativeD3D12CommandQueue);
            _ = Marshal.AddRef(nativeD3D12Resource);
            d3d12Resource = new ID3D12Resource(nativeD3D12Resource);

            Vortice.Direct3D11on12.ResourceFlags resourceFlags = new()
            {
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            };
            lock (bridge.Gate)
            {
                wrappedSource = bridge.On12.CreateWrappedResource<ID3D11Resource>(
                    d3d12Resource,
                    resourceFlags,
                    ResourceStates.RenderTarget,
                    ResourceStates.RenderTarget);
            }

            if (useSynchronizedHdrMask)
            {
                AttachSynchronizedHdrMaskCarrier(
                    bridge.Device.NativePointer,
                    width,
                    height,
                    format,
                    alphaMode);
            }
            else
            {
                Compositor compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
                compositionDevice = CreateCompositionGraphicsDevice(
                    compositor,
                    bridge.Device.NativePointer);
                drawingSurface = compositionDevice.CreateDrawingSurface(
                    new Windows.Foundation.Size(width, height),
                    MapPixelFormat(format),
                    MapAlphaMode(alphaMode));
                drawingSurfaceInterop = QueryDrawingSurfaceInterop(drawingSurface);
                colorBrush = compositor.CreateSurfaceBrush(drawingSurface);
                colorBrush.Stretch = CompositionStretch.Fill;
                visual = compositor.CreateSpriteVisual();
                visual.Brush = colorBrush;
                ElementCompositionPreview.SetElementChildVisual(host, visual);
            }
            textureWidth = width;
            textureHeight = height;
            ApplyVisualLayout();
        }
        catch
        {
            DetachCore();
            throw;
        }
    }

    private void AttachSynchronizedHdrMaskCarrier(
        nint d3d11DevicePointer,
        uint width,
        uint height,
        PresentationFormat format,
        SurfaceAlphaMode alphaMode)
    {
        Microsoft.UI.Xaml.FrameworkElement underlay = resolveUnderlay?.Invoke() ??
            throw new InvalidOperationException(
                "The synchronized HDR mask carrier requires a loaded MAUI underlay visual.");
        Compositor compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        Visual underlayVisual = ElementCompositionPreview.GetElementVisual(underlay);

        compositionDevice = CreateCompositionGraphicsDevice(compositor, d3d11DevicePointer);
        drawingSurface = compositionDevice.CreateDrawingSurface(
            new Windows.Foundation.Size(width, height),
            MapPixelFormat(format),
            MapAlphaMode(alphaMode));
        drawingSurfaceInterop = QueryDrawingSurfaceInterop(drawingSurface);
        colorBrush = compositor.CreateSurfaceBrush(drawingSurface);
        colorBrush.Stretch = CompositionStretch.Fill;

        underlaySurface = compositor.CreateVisualSurface();
        underlaySurface.SourceVisual = underlayVisual;
        underlaySurface.SourceOffset = Vector2.Zero;
        underlaySurface.SourceSize = new Vector2(
            (float)Math.Max(0d, underlay.ActualWidth),
            (float)Math.Max(0d, underlay.ActualHeight));
        underlayBrush = compositor.CreateSurfaceBrush(underlaySurface);
        underlayBrush.Stretch = CompositionStretch.Fill;

        ColorMatrixEffect inverseAlpha = new()
        {
            Name = "InverseModelAlpha",
            Source = new CompositionEffectSourceParameter("ModelMask"),
            ColorMatrix = new Microsoft.Graphics.Canvas.Effects.Matrix5x4
            {
                M44 = -1f,
                M54 = 1f,
            },
        };
        AlphaMaskEffect revealUnderlay = new()
        {
            Name = "RevealUnderlay",
            Source = new CompositionEffectSourceParameter("Underlay"),
            AlphaMask = inverseAlpha,
        };
        underlayMaskFactory = compositor.CreateEffectFactory(revealUnderlay);
        underlayMaskBrush = underlayMaskFactory.CreateBrush();
        underlayMaskBrush.SetSourceParameter("Underlay", underlayBrush);
        underlayMaskBrush.SetSourceParameter("ModelMask", colorBrush);

        visual = compositor.CreateSpriteVisual();
        visual.Brush = underlayMaskBrush;
        ElementCompositionPreview.SetElementChildVisual(host, visual);
    }

    private unsafe void PresentCore()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ID3D11On12Device on12 = bridge?.On12 ??
            throw new InvalidOperationException("The Windows composition source is not attached.");
        ID3D11DeviceContext context = bridge?.Context ??
            throw new InvalidOperationException("The Windows composition device is unavailable.");
        ID3D11Resource source = wrappedSource ??
            throw new InvalidOperationException("The Windows composition source resource is unavailable.");
        if (drawingSurfaceInterop == 0)
        {
            throw new InvalidOperationException("The Windows composition drawing surface is unavailable.");
        }

        NativeRect updateRect = new(0, 0, checked((int)textureWidth), checked((int)textureHeight));
        NativePoint updateOffset = default;
        nint destinationPointer = 0;
        bool drawStarted = false;
        bool sourceAcquired = false;
        try
        {
            nint* vtable = *(nint**)drawingSurfaceInterop;
            delegate* unmanaged[Stdcall]<nint, NativeRect*, Guid*, nint*, NativePoint*, int>
                beginDraw = (delegate* unmanaged[Stdcall]<
                    nint,
                    NativeRect*,
                    Guid*,
                    nint*,
                    NativePoint*,
                    int>)vtable[3];
            Guid textureIidValue = D3D11Texture2DIid;
            int result = beginDraw(
                drawingSurfaceInterop,
                &updateRect,
                &textureIidValue,
                &destinationPointer,
                &updateOffset);
            Marshal.ThrowExceptionForHR(result);
            drawStarted = true;
            if (destinationPointer == 0)
            {
                throw new InvalidOperationException(
                    "CompositionDrawingSurface.BeginDraw returned a null D3D11 texture.");
            }

            using ID3D11Resource destination = new(destinationPointer);
            destinationPointer = 0;
            on12.AcquireWrappedResources([source]);
            sourceAcquired = true;
            context.CopySubresourceRegion(
                destination,
                0,
                checked((uint)updateOffset.X),
                checked((uint)updateOffset.Y),
                0,
                source,
                0,
                new Box(
                    0,
                    0,
                    0,
                    checked((int)textureWidth),
                    checked((int)textureHeight),
                    1));
            on12.ReleaseWrappedResources([source]);
            sourceAcquired = false;
            context.Flush();
        }
        finally
        {
            if (sourceAcquired)
            {
                on12.ReleaseWrappedResources([source]);
                context.Flush();
            }
            if (destinationPointer != 0)
            {
                _ = Marshal.Release(destinationPointer);
            }
            if (drawStarted)
            {
                nint* vtable = *(nint**)drawingSurfaceInterop;
                delegate* unmanaged[Stdcall]<nint, int> endDraw =
                    (delegate* unmanaged[Stdcall]<nint, int>)vtable[4];
                Marshal.ThrowExceptionForHR(endDraw(drawingSurfaceInterop));
            }
        }
    }

    private void DetachCore()
    {
        ElementCompositionPreview.SetElementChildVisual(host, null);
        textureWidth = 0;
        textureHeight = 0;

        if (visual is not null)
        {
            visual.Brush = null;
            visual.Dispose();
            visual = null;
        }
        underlayMaskBrush?.Dispose();
        underlayMaskBrush = null;
        underlayMaskFactory?.Dispose();
        underlayMaskFactory = null;
        underlayBrush?.Dispose();
        underlayBrush = null;
        if (underlaySurface is not null)
        {
            underlaySurface.SourceVisual = null;
            underlaySurface.Dispose();
            underlaySurface = null;
        }
        colorBrush?.Dispose();
        colorBrush = null;
        if (drawingSurfaceInterop != 0)
        {
            _ = Marshal.Release(drawingSurfaceInterop);
            drawingSurfaceInterop = 0;
        }
        drawingSurface?.Dispose();
        drawingSurface = null;
        compositionDevice?.Dispose();
        compositionDevice = null;
        if (bridge is { } activeBridge)
        {
            lock (activeBridge.Gate) wrappedSource?.Dispose();
        }
        wrappedSource = null;
        d3d12Resource?.Dispose();
        d3d12Resource = null;
        bridge?.Dispose();
        bridge = null;
    }

    private void ApplyVisualLayout()
    {
        if (visual is null)
        {
            return;
        }

        double physicalWidth = logicalWidth * rasterizationScale;
        double physicalHeight = logicalHeight * rasterizationScale;
        if (physicalWidth > float.MaxValue || physicalHeight > float.MaxValue)
        {
            throw new InvalidOperationException(
                "The Windows composition surface extent exceeds the supported visual size.");
        }

        float inverseScale = (float)(1.0 / rasterizationScale);
        if (underlaySurface is not null && resolveUnderlay?.Invoke() is { } underlay)
        {
            underlaySurface.SourceSize = new Vector2(
                (float)Math.Max(0d, underlay.ActualWidth),
                (float)Math.Max(0d, underlay.ActualHeight));
        }
        visual.RelativeSizeAdjustment = Vector2.Zero;
        visual.Size = new Vector2((float)physicalWidth, (float)physicalHeight);
        visual.CenterPoint = Vector3.Zero;
        visual.Scale = new Vector3(inverseScale, inverseScale, 1f);
    }

    private void InvokeOnUiThread(Action action)
    {
        if (host.DispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!host.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completed.TrySetResult();
                }
                catch (Exception exception)
                {
                    completed.TrySetException(exception);
                }
            }))
        {
            throw new InvalidOperationException(
                "The Windows UI dispatcher rejected compositor drawing-surface work.");
        }

        if (!completed.Task.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("Windows compositor drawing-surface work timed out.");
        }
        completed.Task.GetAwaiter().GetResult();
    }

    private static MicrosoftDirectXPixelFormat MapPixelFormat(PresentationFormat format) =>
        format switch
    {
        PresentationFormat.Rgba16Float => MicrosoftDirectXPixelFormat.R16G16B16A16Float,
        PresentationFormat.Bgra8UnormSrgb => MicrosoftDirectXPixelFormat.B8G8R8A8UIntNormalized,
        _ => throw new NotSupportedException(
            $"Windows composition drawing-surface presentation does not support {format}."),
    };

    private static MicrosoftDirectXAlphaMode MapAlphaMode(SurfaceAlphaMode mode) => mode switch
    {
        SurfaceAlphaMode.Opaque => MicrosoftDirectXAlphaMode.Ignore,
        SurfaceAlphaMode.Unpremultiplied => MicrosoftDirectXAlphaMode.Straight,
        _ => MicrosoftDirectXAlphaMode.Premultiplied,
    };

    private static unsafe CompositionGraphicsDevice CreateCompositionGraphicsDevice(
        Compositor compositor,
        nint d3d11Device)
    {
        nint inspectable = WinRT.MarshalInspectable<Compositor>.FromManaged(compositor);
        nint compositorInterop = 0;
        nint graphicsDeviceAbi = 0;
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(
                inspectable,
                in CompositorInteropIid,
                out compositorInterop));
            nint* vtable = *(nint**)compositorInterop;
            delegate* unmanaged[Stdcall]<nint, nint, nint*, int> createGraphicsDevice =
                (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)vtable[3];
            Marshal.ThrowExceptionForHR(createGraphicsDevice(
                compositorInterop,
                d3d11Device,
                &graphicsDeviceAbi));
            if (graphicsDeviceAbi == 0)
            {
                throw new InvalidOperationException(
                    "ICompositorInterop.CreateGraphicsDevice returned a null device.");
            }
            return WinRT.MarshalInterface<CompositionGraphicsDevice>.FromAbi(graphicsDeviceAbi);
        }
        finally
        {
            if (graphicsDeviceAbi != 0)
            {
                WinRT.MarshalInterface<CompositionGraphicsDevice>.DisposeAbi(graphicsDeviceAbi);
            }
            if (compositorInterop != 0)
            {
                _ = Marshal.Release(compositorInterop);
            }
            _ = Marshal.Release(inspectable);
        }
    }

    private static nint QueryDrawingSurfaceInterop(CompositionDrawingSurface surface)
    {
        nint inspectable = WinRT.MarshalInspectable<CompositionDrawingSurface>.FromManaged(surface);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(
                inspectable,
                in DrawingSurfaceInteropIid,
                out nint interop));
            return interop;
        }
        finally
        {
            _ = Marshal.Release(inspectable);
        }
    }

    private void ReportError(Exception exception)
    {
        Debug.WriteLine($"[Mu3D] Windows composition drawing-surface error: {exception}");
        try
        {
            reportError(exception);
        }
        catch (Exception reportException)
        {
            Debug.WriteLine(
                $"[Mu3D] Windows composition error reporting failed: {reportException}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }
}
#endif
