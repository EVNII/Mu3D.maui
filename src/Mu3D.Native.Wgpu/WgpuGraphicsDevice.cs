using System.Text;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.Native.Wgpu;

/// <summary>Provides the backend-independent Mu3D graphics ABI through pinned wgpu-native.</summary>
public sealed partial class WgpuGraphicsDevice : GraphicsDevice
{
    private readonly WgpuInstanceHandle instance;
    private readonly WgpuAdapterHandle adapter;
    private readonly WgpuDeviceHandle device;
    private readonly WgpuGraphicsQueue queue;

    internal unsafe WgpuGraphicsDevice(
        WgpuInstanceHandle instance,
        WgpuAdapterHandle adapter,
        WgpuDeviceHandle device,
        WgpuDeviceLostSink deviceLostSink,
        WgpuEnabledFeatures enabledFeatures)
        : base(new GraphicsCapabilities
        {
            SupportsFloat16Textures = true,
            SupportsShaderFloat16 = enabledFeatures.ShaderF16,
            SupportsHdrSurface = false,
            SupportsBcTextureCompression = enabledFeatures.TextureCompressionBc,
            SupportsEtc2TextureCompression = enabledFeatures.TextureCompressionEtc2,
            SupportsAstcTextureCompression = enabledFeatures.TextureCompressionAstc,
            SurfaceFormats = [],
        })
    {
        this.instance = instance;
        this.adapter = adapter;
        this.device = device;
        deviceLostSink.Handler = ReportDeviceLost;
        WGPUQueueImpl* nativeQueue = WgpuNative.wgpuDeviceGetQueue(device.DangerousGetPointer());
        if (nativeQueue is null)
        {
            throw new InvalidOperationException("wgpuDeviceGetQueue returned a null queue.");
        }
        queue = new WgpuGraphicsQueue(this, nativeQueue);
    }

    /// <inheritdoc />
    public override GraphicsQueue Queue => queue;

    internal unsafe WGPUDeviceImpl* NativeDevice => device.DangerousGetPointer();

    internal WgpuInstanceHandle NativeInstance => instance;

    internal WgpuAdapterHandle NativeAdapter => adapter;

    internal static TResource RequireResource<TResource>(GraphicsResource resource)
        where TResource : GraphicsResource =>
        resource as TResource ?? throw new InvalidOperationException(
            $"Resource type {resource.GetType().Name} is not implemented by the wgpu backend.");

    /// <inheritdoc />
    protected override unsafe GraphicsBuffer CreateBufferCore(GraphicsBufferDescriptor descriptor)
    {
        byte[] label = Encode(descriptor.Label);
        fixed (byte* labelPointer = label)
        {
            WGPUBufferDescriptor nativeDescriptor = new()
            {
                label = CreateStringView(labelPointer, label.Length),
                usage = WgpuGraphicsMapper.MapBufferUsage(descriptor.Usage),
                size = descriptor.Size,
            };
            WGPUBufferImpl* value = WgpuNative.wgpuDeviceCreateBuffer(NativeDevice, &nativeDescriptor);
            try
            {
                ThrowIfNativeErrors("buffer creation");
                return value is null
                    ? throw new InvalidOperationException("wgpuDeviceCreateBuffer returned a null buffer.")
                    : new WgpuGraphicsBuffer(this, descriptor, value);
            }
            catch
            {
                if (value is not null)
                {
                    WgpuNative.wgpuBufferRelease(value);
                }
                throw;
            }
        }
    }

    /// <inheritdoc />
    protected override unsafe GraphicsTexture CreateTextureCore(GraphicsTextureDescriptor descriptor)
    {
        byte[] label = Encode(descriptor.Label);
        fixed (byte* labelPointer = label)
        {
            WGPUTextureDescriptor nativeDescriptor = new()
            {
                label = CreateStringView(labelPointer, label.Length),
                usage = WgpuGraphicsMapper.MapTextureUsage(descriptor.Usage),
                dimension = WGPUTextureDimension._2D,
                size = new WGPUExtent3D
                {
                    width = descriptor.Size.Width,
                    height = descriptor.Size.Height,
                    depthOrArrayLayers = descriptor.Size.DepthOrArrayLayers,
                },
                format = WgpuGraphicsMapper.MapTextureFormat(descriptor.Format),
                mipLevelCount = descriptor.MipLevelCount,
                sampleCount = descriptor.SampleCount,
            };
            WGPUTextureImpl* value = WgpuNative.wgpuDeviceCreateTexture(NativeDevice, &nativeDescriptor);
            try
            {
                ThrowIfNativeErrors("texture creation");
                return value is null
                    ? throw new InvalidOperationException("wgpuDeviceCreateTexture returned a null texture.")
                    : new WgpuGraphicsTexture(this, descriptor, value);
            }
            catch
            {
                if (value is not null)
                {
                    WgpuNative.wgpuTextureRelease(value);
                }
                throw;
            }
        }
    }

    /// <inheritdoc />
    protected override unsafe GraphicsTextureView CreateTextureViewCore(GraphicsTextureViewDescriptor descriptor)
    {
        byte[] label = Encode(descriptor.Label);
        fixed (byte* labelPointer = label)
        {
            WGPUTextureViewDescriptor nativeDescriptor = new()
            {
                label = CreateStringView(labelPointer, label.Length),
                format = WgpuGraphicsMapper.MapTextureFormat(descriptor.Texture.Descriptor.Format),
                dimension = WgpuGraphicsMapper.MapTextureViewDimension(descriptor.Dimension),
                baseMipLevel = descriptor.BaseMipLevel,
                mipLevelCount = descriptor.MipLevelCount,
                baseArrayLayer = descriptor.BaseArrayLayer,
                arrayLayerCount = descriptor.ArrayLayerCount,
                aspect = WGPUTextureAspect.All,
            };
            WGPUTextureViewImpl* value = WgpuNative.wgpuTextureCreateView(
                RequireResource<WgpuGraphicsTexture>(descriptor.Texture).Pointer,
                &nativeDescriptor);
            try
            {
                ThrowIfNativeErrors("texture-view creation");
                return value is null
                    ? throw new InvalidOperationException("wgpuTextureCreateView returned a null texture view.")
                    : new WgpuGraphicsTextureView(this, descriptor, value);
            }
            catch
            {
                if (value is not null)
                {
                    WgpuNative.wgpuTextureViewRelease(value);
                }
                throw;
            }
        }
    }

    /// <inheritdoc />
    protected override unsafe GraphicsSampler CreateSamplerCore(GraphicsSamplerDescriptor descriptor)
    {
        byte[] label = Encode(descriptor.Label);
        fixed (byte* labelPointer = label)
        {
            WGPUSamplerDescriptor nativeDescriptor = new()
            {
                label = CreateStringView(labelPointer, label.Length),
                addressModeU = WgpuGraphicsMapper.MapAddressMode(descriptor.AddressModeU),
                addressModeV = WgpuGraphicsMapper.MapAddressMode(descriptor.AddressModeV),
                addressModeW = WgpuGraphicsMapper.MapAddressMode(descriptor.AddressModeW),
                magFilter = WgpuGraphicsMapper.MapFilterMode(descriptor.MagFilter),
                minFilter = WgpuGraphicsMapper.MapFilterMode(descriptor.MinFilter),
                mipmapFilter = WgpuGraphicsMapper.MapMipmapFilterMode(descriptor.MipmapFilter),
                compare = descriptor.Compare.HasValue
                    ? WgpuGraphicsMapper.MapCompareFunction(descriptor.Compare.Value)
                    : WGPUCompareFunction.Undefined,
                lodMinClamp = descriptor.LodMinClamp,
                lodMaxClamp = descriptor.LodMaxClamp,
                maxAnisotropy = 1,
            };
            WGPUSamplerImpl* value = WgpuNative.wgpuDeviceCreateSampler(NativeDevice, &nativeDescriptor);
            try
            {
                ThrowIfNativeErrors("sampler creation");
                return value is null
                    ? throw new InvalidOperationException("wgpuDeviceCreateSampler returned a null sampler.")
                    : new WgpuGraphicsSampler(this, descriptor, value);
            }
            catch
            {
                if (value is not null)
                {
                    WgpuNative.wgpuSamplerRelease(value);
                }
                throw;
            }
        }
    }

    /// <inheritdoc />
    protected override unsafe GraphicsBindGroupLayout CreateBindGroupLayoutCore(
        GraphicsBindGroupLayoutDescriptor descriptor)
    {
        byte[] label = Encode(descriptor.Label);
        int entryCount = descriptor.Entries.Count;
        WGPUBindGroupLayoutEntry* entries = stackalloc WGPUBindGroupLayoutEntry[entryCount];
        for (int index = 0; index < entryCount; index++)
        {
            GraphicsBindGroupLayoutEntry entry = descriptor.Entries[index];
            WGPUBindGroupLayoutEntry nativeEntry = new()
            {
                binding = entry.Binding,
                visibility = WgpuGraphicsMapper.MapShaderStages(entry.Visibility),
            };
            switch (entry.ResourceType)
            {
                case GraphicsBindingResourceType.Buffer:
                    nativeEntry.buffer = new WGPUBufferBindingLayout
                    {
                        type = WgpuGraphicsMapper.MapBufferBindingType(entry.BufferType),
                        minBindingSize = entry.MinBindingSize,
                    };
                    break;
                case GraphicsBindingResourceType.Sampler:
                    nativeEntry.sampler = new WGPUSamplerBindingLayout
                    {
                        type = WgpuGraphicsMapper.MapSamplerBindingType(entry.SamplerType),
                    };
                    break;
                case GraphicsBindingResourceType.SampledTexture:
                    nativeEntry.texture = new WGPUTextureBindingLayout
                    {
                        sampleType = WgpuGraphicsMapper.MapTextureSampleType(entry.TextureSampleType),
                        viewDimension = WgpuGraphicsMapper.MapTextureViewDimension(entry.TextureViewDimension),
                        multisampled = entry.Multisampled ? 1u : 0u,
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(descriptor));
            }
            entries[index] = nativeEntry;
        }
        fixed (byte* labelPointer = label)
        {
            WGPUBindGroupLayoutDescriptor nativeDescriptor = new()
            {
                label = CreateStringView(labelPointer, label.Length),
                entryCount = checked((nuint)entryCount),
                entries = entryCount == 0 ? null : entries,
            };
            WGPUBindGroupLayoutImpl* value = WgpuNative.wgpuDeviceCreateBindGroupLayout(
                NativeDevice,
                &nativeDescriptor);
            try
            {
                ThrowIfNativeErrors("bind-group-layout creation");
                return value is null
                    ? throw new InvalidOperationException("wgpuDeviceCreateBindGroupLayout returned null.")
                    : new WgpuGraphicsBindGroupLayout(this, descriptor, value);
            }
            catch
            {
                if (value is not null)
                {
                    WgpuNative.wgpuBindGroupLayoutRelease(value);
                }
                throw;
            }
        }
    }

    /// <inheritdoc />
    protected override unsafe GraphicsPipelineLayout CreatePipelineLayoutCore(
        GraphicsPipelineLayoutDescriptor descriptor)
    {
        byte[] label = Encode(descriptor.Label);
        int layoutCount = descriptor.BindGroupLayouts.Count;
        WGPUBindGroupLayoutImpl** layouts = stackalloc WGPUBindGroupLayoutImpl*[layoutCount];
        for (int index = 0; index < layoutCount; index++)
        {
            layouts[index] = RequireResource<WgpuGraphicsBindGroupLayout>(
                descriptor.BindGroupLayouts[index]).Pointer;
        }
        fixed (byte* labelPointer = label)
        {
            WGPUPipelineLayoutDescriptor nativeDescriptor = new()
            {
                label = CreateStringView(labelPointer, label.Length),
                bindGroupLayoutCount = checked((nuint)layoutCount),
                bindGroupLayouts = layoutCount == 0 ? null : layouts,
            };
            WGPUPipelineLayoutImpl* value = WgpuNative.wgpuDeviceCreatePipelineLayout(
                NativeDevice,
                &nativeDescriptor);
            try
            {
                ThrowIfNativeErrors("pipeline-layout creation");
                return value is null
                    ? throw new InvalidOperationException("wgpuDeviceCreatePipelineLayout returned null.")
                    : new WgpuGraphicsPipelineLayout(this, descriptor, value);
            }
            catch
            {
                if (value is not null)
                {
                    WgpuNative.wgpuPipelineLayoutRelease(value);
                }
                throw;
            }
        }
    }

    /// <inheritdoc />
    protected override unsafe GraphicsBindGroup CreateBindGroupCore(GraphicsBindGroupDescriptor descriptor)
    {
        byte[] label = Encode(descriptor.Label);
        int entryCount = descriptor.Entries.Count;
        WGPUBindGroupEntry* entries = stackalloc WGPUBindGroupEntry[entryCount];
        for (int index = 0; index < entryCount; index++)
        {
            GraphicsBindGroupEntry entry = descriptor.Entries[index];
            WGPUBindGroupEntry nativeEntry = new()
            {
                binding = entry.Binding,
            };
            switch (entry.ResourceType)
            {
                case GraphicsBindingResourceType.Buffer:
                    nativeEntry.buffer = RequireResource<WgpuGraphicsBuffer>(entry.Buffer!).Pointer;
                    nativeEntry.offset = entry.Offset;
                    nativeEntry.size = entry.Size;
                    break;
                case GraphicsBindingResourceType.Sampler:
                    nativeEntry.sampler = RequireResource<WgpuGraphicsSampler>(entry.Sampler!).Pointer;
                    break;
                case GraphicsBindingResourceType.SampledTexture:
                    nativeEntry.textureView = RequireResource<WgpuGraphicsTextureView>(entry.TextureView!).Pointer;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(descriptor));
            }
            entries[index] = nativeEntry;
        }
        fixed (byte* labelPointer = label)
        {
            WGPUBindGroupDescriptor nativeDescriptor = new()
            {
                label = CreateStringView(labelPointer, label.Length),
                layout = RequireResource<WgpuGraphicsBindGroupLayout>(descriptor.Layout).Pointer,
                entryCount = checked((nuint)entryCount),
                entries = entryCount == 0 ? null : entries,
            };
            WGPUBindGroupImpl* value = WgpuNative.wgpuDeviceCreateBindGroup(NativeDevice, &nativeDescriptor);
            try
            {
                ThrowIfNativeErrors("bind-group creation");
                return value is null
                    ? throw new InvalidOperationException("wgpuDeviceCreateBindGroup returned null.")
                    : new WgpuGraphicsBindGroup(this, descriptor, value);
            }
            catch
            {
                if (value is not null)
                {
                    WgpuNative.wgpuBindGroupRelease(value);
                }
                throw;
            }
        }
    }

    /// <inheritdoc />
    protected override unsafe GraphicsShaderModule CreateShaderModuleCore(GraphicsShaderModuleDescriptor descriptor)
    {
        byte[] code = Encoding.UTF8.GetBytes(descriptor.Code);
        byte[] label = Encode(descriptor.Label);
        fixed (byte* codePointer = code)
        fixed (byte* labelPointer = label)
        {
            WGPUShaderSourceWGSL wgsl = new()
            {
                chain = new WGPUChainedStruct { sType = WGPUSType.ShaderSourceWGSL },
                code = CreateStringView(codePointer, code.Length),
            };
            WGPUShaderModuleDescriptor nativeDescriptor = new()
            {
                nextInChain = &wgsl.chain,
                label = CreateStringView(labelPointer, label.Length),
            };
            WGPUShaderModuleImpl* value = WgpuNative.wgpuDeviceCreateShaderModule(NativeDevice, &nativeDescriptor);
            try
            {
                ThrowIfNativeErrors("shader-module creation");
                return value is null
                    ? throw new InvalidOperationException("wgpuDeviceCreateShaderModule returned a null shader module.")
                    : new WgpuGraphicsShaderModule(this, descriptor, value);
            }
            catch
            {
                if (value is not null)
                {
                    WgpuNative.wgpuShaderModuleRelease(value);
                }
                throw;
            }
        }
    }

    /// <inheritdoc />
    protected override unsafe GraphicsRenderPipeline CreateRenderPipelineCore(GraphicsRenderPipelineDescriptor descriptor)
    {
        WgpuGraphicsShaderModule vertexShader = RequireResource<WgpuGraphicsShaderModule>(descriptor.VertexShader);
        WgpuGraphicsShaderModule? fragmentShader = descriptor.FragmentShader is null
            ? null
            : RequireResource<WgpuGraphicsShaderModule>(descriptor.FragmentShader);
        byte[] vertexEntryPoint = Encoding.UTF8.GetBytes(descriptor.VertexEntryPoint);
        byte[] fragmentEntryPoint = descriptor.FragmentEntryPoint is null
            ? []
            : Encoding.UTF8.GetBytes(descriptor.FragmentEntryPoint);
        byte[] label = Encode(descriptor.Label);
        fixed (byte* vertexEntryPointPointer = vertexEntryPoint)
        fixed (byte* fragmentEntryPointPointer = fragmentEntryPoint)
        fixed (byte* labelPointer = label)
        {
            int vertexBufferCount = descriptor.VertexBuffers.Count;
            int vertexAttributeCount = 0;
            foreach (GraphicsVertexBufferLayout layout in descriptor.VertexBuffers)
            {
                vertexAttributeCount = checked(vertexAttributeCount + layout.Attributes.Count);
            }
            WGPUVertexBufferLayout* vertexBuffers = stackalloc WGPUVertexBufferLayout[vertexBufferCount];
            WGPUVertexAttribute* vertexAttributes = stackalloc WGPUVertexAttribute[vertexAttributeCount];
            int attributeOffset = 0;
            for (int bufferIndex = 0; bufferIndex < vertexBufferCount; bufferIndex++)
            {
                GraphicsVertexBufferLayout layout = descriptor.VertexBuffers[bufferIndex];
                int layoutAttributeCount = layout.Attributes.Count;
                for (int attributeIndex = 0; attributeIndex < layoutAttributeCount; attributeIndex++)
                {
                    GraphicsVertexAttribute attribute = layout.Attributes[attributeIndex];
                    vertexAttributes[attributeOffset + attributeIndex] = new WGPUVertexAttribute
                    {
                        format = WgpuGraphicsMapper.MapVertexFormat(attribute.Format),
                        offset = attribute.Offset,
                        shaderLocation = attribute.ShaderLocation,
                    };
                }
                vertexBuffers[bufferIndex] = new WGPUVertexBufferLayout
                {
                    stepMode = WgpuGraphicsMapper.MapVertexStepMode(layout.StepMode),
                    arrayStride = layout.ArrayStride,
                    attributeCount = checked((nuint)layoutAttributeCount),
                    attributes = vertexAttributes + attributeOffset,
                };
                attributeOffset += layoutAttributeCount;
            }
            WGPURenderPipelineDescriptor nativeDescriptor = new()
            {
                label = CreateStringView(labelPointer, label.Length),
                layout = descriptor.Layout is null
                    ? null
                    : RequireResource<WgpuGraphicsPipelineLayout>(descriptor.Layout).Pointer,
                vertex = new WGPUVertexState
                {
                    module = vertexShader.Pointer,
                    entryPoint = CreateStringView(vertexEntryPointPointer, vertexEntryPoint.Length),
                    bufferCount = checked((nuint)vertexBufferCount),
                    buffers = vertexBufferCount == 0 ? null : vertexBuffers,
                },
                primitive = new WGPUPrimitiveState
                {
                    topology = WgpuGraphicsMapper.MapPrimitiveTopology(descriptor.Topology),
                    frontFace = WGPUFrontFace.CCW,
                    cullMode = WgpuGraphicsMapper.MapCullMode(descriptor.CullMode),
                },
                multisample = new WGPUMultisampleState
                {
                    count = 1,
                    mask = uint.MaxValue,
                },
            };
            WGPUBlendState nativeBlend = default;
            WGPUColorTargetState colorTarget = default;
            WGPUFragmentState fragment = default;
            if (fragmentShader is not null)
            {
                if (descriptor.ColorFormat is GraphicsTextureFormat colorFormat)
                {
                    colorTarget = new WGPUColorTargetState
                    {
                        format = WgpuGraphicsMapper.MapTextureFormat(colorFormat),
                        writeMask = WgpuNative.WGPUColorWriteMask_All,
                    };
                    if (descriptor.Blend is GraphicsBlendState blend)
                    {
                        nativeBlend = new WGPUBlendState
                        {
                            color = new WGPUBlendComponent
                            {
                                operation = WgpuGraphicsMapper.MapBlendOperation(blend.Color.Operation),
                                srcFactor = WgpuGraphicsMapper.MapBlendFactor(blend.Color.SourceFactor),
                                dstFactor = WgpuGraphicsMapper.MapBlendFactor(blend.Color.DestinationFactor),
                            },
                            alpha = new WGPUBlendComponent
                            {
                                operation = WgpuGraphicsMapper.MapBlendOperation(blend.Alpha.Operation),
                                srcFactor = WgpuGraphicsMapper.MapBlendFactor(blend.Alpha.SourceFactor),
                                dstFactor = WgpuGraphicsMapper.MapBlendFactor(blend.Alpha.DestinationFactor),
                            },
                        };
                        colorTarget.blend = &nativeBlend;
                    }
                }
                fragment = new WGPUFragmentState
                {
                    module = fragmentShader.Pointer,
                    entryPoint = CreateStringView(fragmentEntryPointPointer, fragmentEntryPoint.Length),
                    targetCount = descriptor.ColorFormat.HasValue ? 1u : 0u,
                    targets = descriptor.ColorFormat.HasValue ? &colorTarget : null,
                };
                nativeDescriptor.fragment = &fragment;
            }
            WGPUDepthStencilState nativeDepthStencil = default;
            if (descriptor.DepthStencil is GraphicsDepthStencilState depthStencil)
            {
                WGPUStencilFaceState stencilFace = new()
                {
                    compare = WGPUCompareFunction.Always,
                    failOp = WGPUStencilOperation.Keep,
                    depthFailOp = WGPUStencilOperation.Keep,
                    passOp = WGPUStencilOperation.Keep,
                };
                nativeDepthStencil = new WGPUDepthStencilState
                {
                    format = WgpuGraphicsMapper.MapTextureFormat(depthStencil.Format),
                    depthWriteEnabled = depthStencil.DepthWriteEnabled
                        ? WGPUOptionalBool.True
                        : WGPUOptionalBool.False,
                    depthCompare = WgpuGraphicsMapper.MapCompareFunction(depthStencil.DepthCompare),
                    stencilFront = stencilFace,
                    stencilBack = stencilFace,
                    stencilReadMask = uint.MaxValue,
                    stencilWriteMask = uint.MaxValue,
                };
                nativeDescriptor.depthStencil = &nativeDepthStencil;
            }
            WGPURenderPipelineImpl* value = WgpuNative.wgpuDeviceCreateRenderPipeline(NativeDevice, &nativeDescriptor);
            try
            {
                ThrowIfNativeErrors("render-pipeline creation");
                return value is null
                    ? throw new InvalidOperationException("wgpuDeviceCreateRenderPipeline returned a null pipeline.")
                    : new WgpuGraphicsRenderPipeline(this, descriptor, value);
            }
            catch
            {
                if (value is not null)
                {
                    WgpuNative.wgpuRenderPipelineRelease(value);
                }
                throw;
            }
        }
    }

    /// <inheritdoc />
    protected override unsafe GraphicsCommandEncoder CreateCommandEncoderCore(string? label)
    {
        byte[] encodedLabel = Encode(label);
        fixed (byte* labelPointer = encodedLabel)
        {
            WGPUCommandEncoderDescriptor descriptor = new()
            {
                label = CreateStringView(labelPointer, encodedLabel.Length),
            };
            WGPUCommandEncoderImpl* value = WgpuNative.wgpuDeviceCreateCommandEncoder(NativeDevice, &descriptor);
            try
            {
                ThrowIfNativeErrors("command-encoder creation");
                return value is null
                    ? throw new InvalidOperationException("wgpuDeviceCreateCommandEncoder returned a null encoder.")
                    : new WgpuGraphicsCommandEncoder(this, label, value);
            }
            catch
            {
                if (value is not null)
                {
                    WgpuNative.wgpuCommandEncoderRelease(value);
                }
                throw;
            }
        }
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        queue.Dispose();
        device.Dispose();
        adapter.Dispose();
        instance.Dispose();
    }

    private static byte[] Encode(string? value) =>
        string.IsNullOrEmpty(value) ? [] : Encoding.UTF8.GetBytes(value);

    private static unsafe WGPUStringView CreateStringView(byte* data, int length) => new()
    {
        data = (sbyte*)data,
        length = checked((nuint)length),
    };
}

internal readonly record struct WgpuEnabledFeatures(
    bool ShaderF16,
    bool TextureCompressionBc,
    bool TextureCompressionEtc2,
    bool TextureCompressionAstc);
