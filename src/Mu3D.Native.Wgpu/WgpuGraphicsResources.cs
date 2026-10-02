using Mu3D.Graphics;
using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.Native.Wgpu;

internal sealed unsafe partial class WgpuGraphicsBuffer : GraphicsBuffer
{
    private WGPUBufferImpl* pointer;

    internal WgpuGraphicsBuffer(
        WgpuGraphicsDevice device,
        GraphicsBufferDescriptor descriptor,
        WGPUBufferImpl* pointer)
        : base(device, descriptor) => this.pointer = pointer;

    internal WGPUBufferImpl* Pointer => pointer;

    protected override void DisposeCore()
    {
        WgpuNative.wgpuBufferRelease(pointer);
        pointer = null;
    }
}

internal sealed unsafe class WgpuGraphicsTexture : GraphicsTexture
{
    private WGPUTextureImpl* pointer;

    internal WgpuGraphicsTexture(
        WgpuGraphicsDevice device,
        GraphicsTextureDescriptor descriptor,
        WGPUTextureImpl* pointer)
        : base(device, descriptor) => this.pointer = pointer;

    internal WGPUTextureImpl* Pointer => pointer;

    internal WGPUTextureViewImpl* CreateView()
    {
        WGPUTextureViewImpl* result = WgpuNative.wgpuTextureCreateView(pointer, null);
        return result is null
            ? throw new InvalidOperationException("wgpuTextureCreateView returned a null texture view.")
            : result;
    }

    protected override void DisposeCore()
    {
        WgpuNative.wgpuTextureRelease(pointer);
        pointer = null;
    }
}

internal sealed unsafe class WgpuGraphicsTextureView : GraphicsTextureView
{
    private WGPUTextureViewImpl* pointer;

    internal WgpuGraphicsTextureView(
        WgpuGraphicsDevice device,
        GraphicsTextureViewDescriptor descriptor,
        WGPUTextureViewImpl* pointer)
        : base(device, descriptor) => this.pointer = pointer;

    internal WGPUTextureViewImpl* Pointer => pointer;

    protected override void DisposeCore()
    {
        WgpuNative.wgpuTextureViewRelease(pointer);
        pointer = null;
    }
}

internal sealed unsafe class WgpuGraphicsSampler : GraphicsSampler
{
    private WGPUSamplerImpl* pointer;

    internal WgpuGraphicsSampler(
        WgpuGraphicsDevice device,
        GraphicsSamplerDescriptor descriptor,
        WGPUSamplerImpl* pointer)
        : base(device, descriptor) => this.pointer = pointer;

    internal WGPUSamplerImpl* Pointer => pointer;

    protected override void DisposeCore()
    {
        WgpuNative.wgpuSamplerRelease(pointer);
        pointer = null;
    }
}

internal sealed unsafe class WgpuGraphicsBindGroupLayout : GraphicsBindGroupLayout
{
    private WGPUBindGroupLayoutImpl* pointer;

    internal WgpuGraphicsBindGroupLayout(
        WgpuGraphicsDevice device,
        GraphicsBindGroupLayoutDescriptor descriptor,
        WGPUBindGroupLayoutImpl* pointer)
        : base(device, descriptor) => this.pointer = pointer;

    internal WGPUBindGroupLayoutImpl* Pointer => pointer;

    protected override void DisposeCore()
    {
        WgpuNative.wgpuBindGroupLayoutRelease(pointer);
        pointer = null;
    }
}

internal sealed unsafe class WgpuGraphicsPipelineLayout : GraphicsPipelineLayout
{
    private WGPUPipelineLayoutImpl* pointer;

    internal WgpuGraphicsPipelineLayout(
        WgpuGraphicsDevice device,
        GraphicsPipelineLayoutDescriptor descriptor,
        WGPUPipelineLayoutImpl* pointer)
        : base(device, descriptor) => this.pointer = pointer;

    internal WGPUPipelineLayoutImpl* Pointer => pointer;

    protected override void DisposeCore()
    {
        WgpuNative.wgpuPipelineLayoutRelease(pointer);
        pointer = null;
    }
}

internal sealed unsafe class WgpuGraphicsBindGroup : GraphicsBindGroup
{
    private WGPUBindGroupImpl* pointer;

    internal WgpuGraphicsBindGroup(
        WgpuGraphicsDevice device,
        GraphicsBindGroupDescriptor descriptor,
        WGPUBindGroupImpl* pointer)
        : base(device, descriptor) => this.pointer = pointer;

    internal WGPUBindGroupImpl* Pointer => pointer;

    protected override void DisposeCore()
    {
        WgpuNative.wgpuBindGroupRelease(pointer);
        pointer = null;
    }
}

internal sealed unsafe class WgpuGraphicsShaderModule : GraphicsShaderModule
{
    private WGPUShaderModuleImpl* pointer;

    internal WgpuGraphicsShaderModule(
        WgpuGraphicsDevice device,
        GraphicsShaderModuleDescriptor descriptor,
        WGPUShaderModuleImpl* pointer)
        : base(device, descriptor) => this.pointer = pointer;

    internal WGPUShaderModuleImpl* Pointer => pointer;

    protected override void DisposeCore()
    {
        WgpuNative.wgpuShaderModuleRelease(pointer);
        pointer = null;
    }
}

internal sealed unsafe class WgpuGraphicsRenderPipeline : GraphicsRenderPipeline
{
    private WGPURenderPipelineImpl* pointer;

    internal WgpuGraphicsRenderPipeline(
        WgpuGraphicsDevice device,
        GraphicsRenderPipelineDescriptor descriptor,
        WGPURenderPipelineImpl* pointer)
        : base(device, descriptor) => this.pointer = pointer;

    internal WGPURenderPipelineImpl* Pointer => pointer;

    protected override void DisposeCore()
    {
        WgpuNative.wgpuRenderPipelineRelease(pointer);
        pointer = null;
    }
}
