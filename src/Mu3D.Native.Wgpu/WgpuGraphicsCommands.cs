using System.Text;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.Native.Wgpu;

internal sealed unsafe class WgpuGraphicsQueue : GraphicsQueue, IDisposable
{
    private WGPUQueueImpl* pointer;

    internal WgpuGraphicsQueue(WgpuGraphicsDevice device, WGPUQueueImpl* pointer)
        : base(device) => this.pointer = pointer;

    private WgpuGraphicsDevice WgpuDevice => (WgpuGraphicsDevice)Device;

    protected override void WriteBufferCore(
        GraphicsBuffer destination,
        ulong destinationOffset,
        ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        WgpuGraphicsBuffer buffer = WgpuGraphicsDevice.RequireResource<WgpuGraphicsBuffer>(destination);
        fixed (byte* dataPointer = data)
        {
            WgpuNative.wgpuQueueWriteBuffer(
                pointer,
                buffer.Pointer,
                destinationOffset,
                dataPointer,
                checked((nuint)data.Length));
        }
        WgpuDevice.ThrowIfNativeErrors("queue buffer write");
    }

    protected override void WriteTextureCore(
        GraphicsTexture destination,
        uint mipLevel,
        GraphicsOrigin3D origin,
        GraphicsExtent3D writeSize,
        ReadOnlySpan<byte> data,
        uint bytesPerRow,
        uint rowsPerImage)
    {
        WgpuGraphicsTexture texture = WgpuGraphicsDevice.RequireResource<WgpuGraphicsTexture>(destination);
        WGPUTexelCopyTextureInfo nativeDestination = new()
        {
            texture = texture.Pointer,
            mipLevel = mipLevel,
            origin = new WGPUOrigin3D { x = origin.X, y = origin.Y, z = origin.Z },
            aspect = WGPUTextureAspect.All,
        };
        WGPUTexelCopyBufferLayout nativeLayout = new()
        {
            bytesPerRow = bytesPerRow,
            rowsPerImage = rowsPerImage,
        };
        WGPUExtent3D nativeSize = new()
        {
            width = writeSize.Width,
            height = writeSize.Height,
            depthOrArrayLayers = writeSize.DepthOrArrayLayers,
        };
        fixed (byte* dataPointer = data)
        {
            WgpuNative.wgpuQueueWriteTexture(
                pointer,
                &nativeDestination,
                dataPointer,
                checked((nuint)data.Length),
                &nativeLayout,
                &nativeSize);
        }
        WgpuDevice.ThrowIfNativeErrors("queue texture write");
    }

    protected override void SubmitCore(GraphicsCommandBuffer commandBuffer)
    {
        WgpuGraphicsCommandBuffer commands =
            WgpuGraphicsDevice.RequireResource<WgpuGraphicsCommandBuffer>(commandBuffer);
        WGPUCommandBufferImpl* submittedCommand = commands.Pointer;
        WgpuNative.wgpuQueueSubmit(pointer, 1, &submittedCommand);
        WgpuDevice.ThrowIfNativeErrors("queue submission");
    }

    public void Dispose()
    {
        if (pointer is null)
        {
            return;
        }

        WgpuNative.wgpuQueueRelease(pointer);
        pointer = null;
    }
}

internal sealed unsafe class WgpuGraphicsCommandEncoder : GraphicsCommandEncoder
{
    private WGPUCommandEncoderImpl* pointer;

    internal WgpuGraphicsCommandEncoder(
        WgpuGraphicsDevice device,
        string? label,
        WGPUCommandEncoderImpl* pointer)
        : base(device, label) => this.pointer = pointer;

    private WgpuGraphicsDevice WgpuDevice => (WgpuGraphicsDevice)Device;

    protected override void CopyBufferToBufferCore(
        GraphicsBuffer source,
        ulong sourceOffset,
        GraphicsBuffer destination,
        ulong destinationOffset,
        ulong size)
    {
        WgpuGraphicsBuffer sourceBuffer = WgpuGraphicsDevice.RequireResource<WgpuGraphicsBuffer>(source);
        WgpuGraphicsBuffer destinationBuffer = WgpuGraphicsDevice.RequireResource<WgpuGraphicsBuffer>(destination);
        WgpuNative.wgpuCommandEncoderCopyBufferToBuffer(
            pointer,
            sourceBuffer.Pointer,
            sourceOffset,
            destinationBuffer.Pointer,
            destinationOffset,
            size);
    }

    protected override void CopyTextureToBufferCore(
        GraphicsTexture source,
        uint mipLevel,
        GraphicsOrigin3D origin,
        GraphicsExtent3D copySize,
        GraphicsBuffer destination,
        ulong destinationOffset,
        uint bytesPerRow,
        uint rowsPerImage)
    {
        WgpuGraphicsTexture sourceTexture =
            WgpuGraphicsDevice.RequireResource<WgpuGraphicsTexture>(source);
        WgpuGraphicsBuffer destinationBuffer =
            WgpuGraphicsDevice.RequireResource<WgpuGraphicsBuffer>(destination);
        WGPUTexelCopyTextureInfo nativeSource = new()
        {
            texture = sourceTexture.Pointer,
            mipLevel = mipLevel,
            origin = new WGPUOrigin3D { x = origin.X, y = origin.Y, z = origin.Z },
            aspect = WGPUTextureAspect.All,
        };
        WGPUTexelCopyBufferInfo nativeDestination = new()
        {
            buffer = destinationBuffer.Pointer,
            layout = new WGPUTexelCopyBufferLayout
            {
                offset = destinationOffset,
                bytesPerRow = bytesPerRow,
                rowsPerImage = rowsPerImage,
            },
        };
        WGPUExtent3D nativeSize = new()
        {
            width = copySize.Width,
            height = copySize.Height,
            depthOrArrayLayers = copySize.DepthOrArrayLayers,
        };
        WgpuNative.wgpuCommandEncoderCopyTextureToBuffer(
            pointer,
            &nativeSource,
            &nativeDestination,
            &nativeSize);
    }

    protected override void CopyTextureToTextureCore(
        GraphicsTexture source,
        uint sourceMipLevel,
        GraphicsOrigin3D sourceOrigin,
        GraphicsTexture destination,
        uint destinationMipLevel,
        GraphicsOrigin3D destinationOrigin,
        GraphicsExtent3D copySize)
    {
        WgpuGraphicsTexture sourceTexture =
            WgpuGraphicsDevice.RequireResource<WgpuGraphicsTexture>(source);
        WgpuGraphicsTexture destinationTexture =
            WgpuGraphicsDevice.RequireResource<WgpuGraphicsTexture>(destination);
        WGPUTexelCopyTextureInfo nativeSource = new()
        {
            texture = sourceTexture.Pointer,
            mipLevel = sourceMipLevel,
            origin = new WGPUOrigin3D
            {
                x = sourceOrigin.X,
                y = sourceOrigin.Y,
                z = sourceOrigin.Z,
            },
            aspect = WGPUTextureAspect.All,
        };
        WGPUTexelCopyTextureInfo nativeDestination = new()
        {
            texture = destinationTexture.Pointer,
            mipLevel = destinationMipLevel,
            origin = new WGPUOrigin3D
            {
                x = destinationOrigin.X,
                y = destinationOrigin.Y,
                z = destinationOrigin.Z,
            },
            aspect = WGPUTextureAspect.All,
        };
        WGPUExtent3D nativeSize = new()
        {
            width = copySize.Width,
            height = copySize.Height,
            depthOrArrayLayers = copySize.DepthOrArrayLayers,
        };
        WgpuNative.wgpuCommandEncoderCopyTextureToTexture(
            pointer,
            &nativeSource,
            &nativeDestination,
            &nativeSize);
    }

    protected override GraphicsRenderPassEncoder BeginRenderPassCore(GraphicsRenderPassDescriptor descriptor)
    {
        WGPUTextureViewImpl* textureView = null;
        bool ownsTextureView = false;
        if (descriptor.ColorAttachment is GraphicsRenderPassColorAttachment colorAttachment)
        {
            if (colorAttachment.View is not null)
            {
                textureView = WgpuGraphicsDevice.RequireResource<WgpuGraphicsTextureView>(
                    colorAttachment.View).Pointer;
            }
            else
            {
                textureView = WgpuGraphicsDevice.RequireResource<WgpuGraphicsTexture>(
                    colorAttachment.Texture).CreateView();
                ownsTextureView = true;
            }
        }
        WGPUTextureViewImpl* depthTextureView = null;
        if (descriptor.DepthAttachment is GraphicsRenderPassDepthAttachment depthAttachment)
        {
            depthTextureView = WgpuGraphicsDevice.RequireResource<WgpuGraphicsTexture>(
                depthAttachment.Texture).CreateView();
        }
        byte[] label = Encode(descriptor.Label);
        fixed (byte* labelPointer = label)
        {
            WGPURenderPassColorAttachment nativeAttachment = default;
            WGPURenderPassDescriptor nativeDescriptor = new()
            {
                label = CreateStringView(labelPointer, label.Length),
            };
            if (descriptor.ColorAttachment is GraphicsRenderPassColorAttachment attachment)
            {
                nativeAttachment = new WGPURenderPassColorAttachment
                {
                    view = textureView,
                    depthSlice = uint.MaxValue,
                    loadOp = WgpuGraphicsMapper.MapLoadOperation(attachment.LoadOperation),
                    storeOp = WgpuGraphicsMapper.MapStoreOperation(attachment.StoreOperation),
                    clearValue = new WGPUColor
                    {
                        r = attachment.ClearColor.Red,
                        g = attachment.ClearColor.Green,
                        b = attachment.ClearColor.Blue,
                        a = attachment.ClearColor.Alpha,
                    },
                };
                nativeDescriptor.colorAttachmentCount = 1;
                nativeDescriptor.colorAttachments = &nativeAttachment;
            }
            WGPURenderPassDepthStencilAttachment nativeDepthAttachment = default;
            if (descriptor.DepthAttachment is GraphicsRenderPassDepthAttachment depth)
            {
                nativeDepthAttachment = new WGPURenderPassDepthStencilAttachment
                {
                    view = depthTextureView,
                    depthLoadOp = WgpuGraphicsMapper.MapLoadOperation(depth.LoadOperation),
                    depthStoreOp = WgpuGraphicsMapper.MapStoreOperation(depth.StoreOperation),
                    depthClearValue = depth.ClearValue,
                    stencilLoadOp = WGPULoadOp.Undefined,
                    stencilStoreOp = WGPUStoreOp.Undefined,
                };
                nativeDescriptor.depthStencilAttachment = &nativeDepthAttachment;
            }
            WGPURenderPassEncoderImpl* pass = WgpuNative.wgpuCommandEncoderBeginRenderPass(
                pointer,
                &nativeDescriptor);
            try
            {
                WgpuDevice.ThrowIfNativeErrors("render-pass creation");
                return pass is null
                    ? throw new InvalidOperationException(
                        "wgpuCommandEncoderBeginRenderPass returned a null render-pass encoder.")
                    : new WgpuGraphicsRenderPassEncoder(
                        this,
                        descriptor,
                        pass,
                        textureView,
                        depthTextureView,
                        ownsTextureView);
            }
            catch
            {
                if (pass is not null)
                {
                    WgpuNative.wgpuRenderPassEncoderRelease(pass);
                }
                if (ownsTextureView && textureView is not null)
                {
                    WgpuNative.wgpuTextureViewRelease(textureView);
                }
                if (depthTextureView is not null)
                {
                    WgpuNative.wgpuTextureViewRelease(depthTextureView);
                }
                throw;
            }
        }
    }

    protected override GraphicsCommandBuffer FinishCore(string? label)
    {
        byte[] encodedLabel = Encode(label);
        fixed (byte* labelPointer = encodedLabel)
        {
            WGPUCommandBufferDescriptor descriptor = new()
            {
                label = CreateStringView(labelPointer, encodedLabel.Length),
            };
            WGPUCommandBufferImpl* result = WgpuNative.wgpuCommandEncoderFinish(pointer, &descriptor);
            try
            {
                WgpuDevice.ThrowIfNativeErrors("command-encoder finish");
                return result is null
                    ? throw new InvalidOperationException("wgpuCommandEncoderFinish returned a null command buffer.")
                    : new WgpuGraphicsCommandBuffer(WgpuDevice, label, result);
            }
            catch
            {
                if (result is not null)
                {
                    WgpuNative.wgpuCommandBufferRelease(result);
                }
                throw;
            }
        }
    }

    protected override void DisposeCore()
    {
        WgpuNative.wgpuCommandEncoderRelease(pointer);
        pointer = null;
    }

    private static byte[] Encode(string? value) =>
        string.IsNullOrEmpty(value) ? [] : Encoding.UTF8.GetBytes(value);

    private static WGPUStringView CreateStringView(byte* data, int length) => new()
    {
        data = (sbyte*)data,
        length = checked((nuint)length),
    };
}

internal sealed unsafe class WgpuGraphicsRenderPassEncoder : GraphicsRenderPassEncoder
{
    private WGPURenderPassEncoderImpl* pointer;
    private WGPUTextureViewImpl* textureView;
    private WGPUTextureViewImpl* depthTextureView;
    private readonly bool ownsTextureView;

    internal WgpuGraphicsRenderPassEncoder(
        WgpuGraphicsCommandEncoder commandEncoder,
        GraphicsRenderPassDescriptor descriptor,
        WGPURenderPassEncoderImpl* pointer,
        WGPUTextureViewImpl* textureView,
        WGPUTextureViewImpl* depthTextureView,
        bool ownsTextureView)
        : base(commandEncoder, descriptor)
    {
        this.pointer = pointer;
        this.textureView = textureView;
        this.depthTextureView = depthTextureView;
        this.ownsTextureView = ownsTextureView;
    }

    protected override void SetPipelineCore(GraphicsRenderPipeline value)
    {
        WgpuGraphicsRenderPipeline pipeline =
            WgpuGraphicsDevice.RequireResource<WgpuGraphicsRenderPipeline>(value);
        WgpuNative.wgpuRenderPassEncoderSetPipeline(pointer, pipeline.Pointer);
    }

    protected override void SetVertexBufferCore(uint slot, GraphicsBuffer buffer, ulong offset, ulong size)
    {
        WgpuGraphicsBuffer nativeBuffer = WgpuGraphicsDevice.RequireResource<WgpuGraphicsBuffer>(buffer);
        WgpuNative.wgpuRenderPassEncoderSetVertexBuffer(pointer, slot, nativeBuffer.Pointer, offset, size);
    }

    protected override void SetIndexBufferCore(
        GraphicsBuffer buffer,
        GraphicsIndexFormat format,
        ulong offset,
        ulong size)
    {
        WgpuGraphicsBuffer nativeBuffer = WgpuGraphicsDevice.RequireResource<WgpuGraphicsBuffer>(buffer);
        WgpuNative.wgpuRenderPassEncoderSetIndexBuffer(
            pointer,
            nativeBuffer.Pointer,
            WgpuGraphicsMapper.MapIndexFormat(format),
            offset,
            size);
    }

    protected override void SetBindGroupCore(uint groupIndex, GraphicsBindGroup bindGroup)
    {
        WgpuGraphicsBindGroup nativeBindGroup =
            WgpuGraphicsDevice.RequireResource<WgpuGraphicsBindGroup>(bindGroup);
        WgpuNative.wgpuRenderPassEncoderSetBindGroup(
            pointer,
            groupIndex,
            nativeBindGroup.Pointer,
            0,
            null);
    }

    protected override void SetViewportCore(
        float x,
        float y,
        float width,
        float height,
        float minimumDepth,
        float maximumDepth) =>
        WgpuNative.wgpuRenderPassEncoderSetViewport(
            pointer,
            x,
            y,
            width,
            height,
            minimumDepth,
            maximumDepth);

    protected override void SetScissorRectCore(uint x, uint y, uint width, uint height) =>
        WgpuNative.wgpuRenderPassEncoderSetScissorRect(pointer, x, y, width, height);

    protected override void DrawCore(
        uint vertexCount,
        uint instanceCount,
        uint firstVertex,
        uint firstInstance) =>
        WgpuNative.wgpuRenderPassEncoderDraw(
            pointer,
            vertexCount,
            instanceCount,
            firstVertex,
            firstInstance);

    protected override void DrawIndexedCore(
        uint indexCount,
        uint instanceCount,
        uint firstIndex,
        int baseVertex,
        uint firstInstance) =>
        WgpuNative.wgpuRenderPassEncoderDrawIndexed(
            pointer,
            indexCount,
            instanceCount,
            firstIndex,
            baseVertex,
            firstInstance);

    protected override void EndCore()
    {
        WgpuNative.wgpuRenderPassEncoderEnd(pointer);
        ((WgpuGraphicsDevice)Device).ThrowIfNativeErrors("render-pass recording");
    }

    protected override void DisposeCore()
    {
        WgpuNative.wgpuRenderPassEncoderRelease(pointer);
        if (ownsTextureView && textureView is not null)
        {
            WgpuNative.wgpuTextureViewRelease(textureView);
        }
        if (depthTextureView is not null)
        {
            WgpuNative.wgpuTextureViewRelease(depthTextureView);
        }
        pointer = null;
        textureView = null;
        depthTextureView = null;
    }
}

internal sealed unsafe class WgpuGraphicsCommandBuffer : GraphicsCommandBuffer
{
    private WGPUCommandBufferImpl* pointer;

    internal WgpuGraphicsCommandBuffer(
        WgpuGraphicsDevice device,
        string? label,
        WGPUCommandBufferImpl* pointer)
        : base(device, label) => this.pointer = pointer;

    internal WGPUCommandBufferImpl* Pointer => pointer;

    protected override void DisposeCore()
    {
        WgpuNative.wgpuCommandBufferRelease(pointer);
        pointer = null;
    }
}
