using Mu3D.Graphics;

namespace Mu3D.Native.Wgpu;

internal interface IWgpuTexturePresentationSink
{
    SurfaceCapabilities Capabilities { get; }

    void Attach(
        nint d3d12Device,
        nint d3d12CommandQueue,
        nint d3d12Resource,
        uint width,
        uint height,
        PresentationFormat format,
        SurfaceAlphaMode alphaMode);

    void Present();

    void Detach();
}

internal interface IWgpuPresentationSessionLifecycle
{
    void DrainAndReleaseDependentResources(Action release);
}
