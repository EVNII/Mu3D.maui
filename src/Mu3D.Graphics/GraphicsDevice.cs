namespace Mu3D.Graphics;

/// <summary>Identifies the lifecycle state of a graphics device.</summary>
public enum GraphicsDeviceState
{
    /// <summary>The device can create resources and submit work.</summary>
    Active,

    /// <summary>The backend reported that the device can no longer execute work.</summary>
    Lost,

    /// <summary>The device has been disposed.</summary>
    Disposed,
}

/// <summary>Reports a graphics device loss without exposing backend-specific error types.</summary>
public sealed class GraphicsDeviceLostEventArgs : EventArgs
{
    /// <summary>Initializes a device-loss report.</summary>
    public GraphicsDeviceLostEventArgs(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Gets the backend-neutral loss reason.</summary>
    public string Reason { get; }
}

/// <summary>
/// Defines the coarse backend boundary used to create resources and command encoders.
/// </summary>
public abstract class GraphicsDevice : IDisposable
{
    private GraphicsDeviceState state = GraphicsDeviceState.Active;

    /// <summary>Initializes a graphics device with its immutable capabilities.</summary>
    protected GraphicsDevice(GraphicsCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        Capabilities = capabilities;
    }

    /// <summary>Occurs once when the backend reports device loss.</summary>
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost;

    /// <summary>Gets the immutable capabilities used by renderer policy.</summary>
    public GraphicsCapabilities Capabilities { get; }

    /// <summary>Gets the current lifecycle state.</summary>
    public GraphicsDeviceState State => state;

    /// <summary>Gets the reason reported for a lost device, if any.</summary>
    public string? LostReason { get; private set; }

    /// <summary>Gets the queue associated with this device.</summary>
    public abstract GraphicsQueue Queue { get; }

    /// <summary>Creates a buffer owned by this device.</summary>
    public GraphicsBuffer CreateBuffer(GraphicsBufferDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (descriptor.Size == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "Buffer size must be non-zero.");
        }
        if (descriptor.Usage == GraphicsBufferUsage.None)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "At least one buffer use is required.");
        }
        const GraphicsBufferUsage knownBufferUsages =
            GraphicsBufferUsage.CopySource | GraphicsBufferUsage.CopyDestination |
            GraphicsBufferUsage.Vertex | GraphicsBufferUsage.Index |
            GraphicsBufferUsage.Uniform | GraphicsBufferUsage.Storage |
            GraphicsBufferUsage.Indirect | GraphicsBufferUsage.MapRead | GraphicsBufferUsage.MapWrite;
        if ((descriptor.Usage & ~knownBufferUsages) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "The buffer contains an unknown usage flag.");
        }
        GraphicsBufferUsage mapping = descriptor.Usage &
            (GraphicsBufferUsage.MapRead | GraphicsBufferUsage.MapWrite);
        if (mapping == (GraphicsBufferUsage.MapRead | GraphicsBufferUsage.MapWrite) ||
            (mapping == GraphicsBufferUsage.MapRead &&
                (descriptor.Usage & ~(GraphicsBufferUsage.MapRead |
                    GraphicsBufferUsage.CopyDestination)) != 0) ||
            (mapping == GraphicsBufferUsage.MapWrite &&
                (descriptor.Usage & ~(GraphicsBufferUsage.MapWrite |
                    GraphicsBufferUsage.CopySource)) != 0))
        {
            throw new ArgumentException(
                "Mapped buffers use either MapRead with CopyDestination or MapWrite with CopySource.",
                nameof(descriptor));
        }
        GraphicsBuffer result = CreateBufferCore(descriptor) ??
            throw new InvalidOperationException("The graphics backend returned a null buffer.");
        ValidateOwner(result, "result");
        return result;
    }

    /// <summary>Creates a texture owned by this device.</summary>
    public GraphicsTexture CreateTexture(GraphicsTextureDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (descriptor.Size.Width == 0 ||
            descriptor.Size.Height == 0 ||
            descriptor.Size.DepthOrArrayLayers == 0 ||
            descriptor.MipLevelCount == 0 ||
            descriptor.SampleCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture dimensions and counts must be non-zero.");
        }
        if (descriptor.Usage == GraphicsTextureUsage.None)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "At least one texture use is required.");
        }
        const GraphicsTextureUsage knownTextureUsages =
            GraphicsTextureUsage.CopySource | GraphicsTextureUsage.CopyDestination |
            GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.StorageBinding |
            GraphicsTextureUsage.RenderAttachment;
        if ((descriptor.Usage & ~knownTextureUsages) != 0 || !Enum.IsDefined(descriptor.Format))
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "The texture contains an unknown format or usage flag.");
        }
        GraphicsTextureBlockLayout block = GetTextureBlockInfo(descriptor.Format);
        if (block.CompressionFamily != GraphicsTextureCompressionFamily.None)
        {
            bool supported = block.CompressionFamily switch
            {
                GraphicsTextureCompressionFamily.Bc => Capabilities.SupportsBcTextureCompression,
                GraphicsTextureCompressionFamily.Etc2 => Capabilities.SupportsEtc2TextureCompression,
                GraphicsTextureCompressionFamily.Astc => Capabilities.SupportsAstcTextureCompression,
                _ => false,
            };
            if (!supported)
            {
                throw new NotSupportedException(
                    $"Texture format '{descriptor.Format}' requires an unavailable compression feature.");
            }
            if (descriptor.SampleCount != 1 ||
                (descriptor.Usage & (GraphicsTextureUsage.StorageBinding |
                    GraphicsTextureUsage.RenderAttachment)) != 0)
            {
                throw new ArgumentException(
                    "Compressed textures are single-sampled and cannot be storage or render attachments.",
                    nameof(descriptor));
            }
        }
        uint maximumMipLevels = GetMaximumMipLevelCount(descriptor.Size.Width, descriptor.Size.Height);
        if (descriptor.MipLevelCount > maximumMipLevels ||
            (descriptor.SampleCount > 1 && descriptor.MipLevelCount != 1))
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "The texture mip-level count is invalid.");
        }
        GraphicsTexture result = CreateTextureCore(descriptor) ??
            throw new InvalidOperationException("The graphics backend returned a null texture.");
        ValidateOwner(result, "result");
        return result;
    }

    /// <summary>Creates an explicitly selected mip/layer view of a texture.</summary>
    public GraphicsTextureView CreateTextureView(GraphicsTextureViewDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (descriptor.Texture is null)
        {
            throw new ArgumentException("A viewed texture is required.", nameof(descriptor));
        }
        ValidateOwner(descriptor.Texture, nameof(descriptor));
        if (!Enum.IsDefined(descriptor.Dimension) ||
            descriptor.MipLevelCount == 0 || descriptor.ArrayLayerCount == 0 ||
            descriptor.BaseMipLevel >= descriptor.Texture.Descriptor.MipLevelCount ||
            descriptor.MipLevelCount > descriptor.Texture.Descriptor.MipLevelCount - descriptor.BaseMipLevel ||
            descriptor.BaseArrayLayer >= descriptor.Texture.Descriptor.Size.DepthOrArrayLayers ||
            descriptor.ArrayLayerCount >
                descriptor.Texture.Descriptor.Size.DepthOrArrayLayers - descriptor.BaseArrayLayer)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "The texture-view range is invalid.");
        }

        bool square = GetMipDimension(
            descriptor.Texture.Descriptor.Size.Width,
            descriptor.BaseMipLevel) == GetMipDimension(
                descriptor.Texture.Descriptor.Size.Height,
                descriptor.BaseMipLevel);
        bool validDimension = descriptor.Dimension switch
        {
            GraphicsTextureViewDimension.TwoD => descriptor.ArrayLayerCount == 1,
            GraphicsTextureViewDimension.TwoDArray => true,
            GraphicsTextureViewDimension.Cube =>
                descriptor.ArrayLayerCount == 6 && square && descriptor.Texture.Descriptor.SampleCount == 1,
            GraphicsTextureViewDimension.CubeArray =>
                descriptor.ArrayLayerCount % 6 == 0 && square && descriptor.Texture.Descriptor.SampleCount == 1,
            _ => false,
        };
        if (!validDimension)
        {
            throw new ArgumentException("The texture-view dimension is incompatible with its layers or extent.", nameof(descriptor));
        }

        GraphicsTextureView result = CreateTextureViewCore(descriptor) ??
            throw new InvalidOperationException("The graphics backend returned a null texture view.");
        ValidateOwner(result, "result");
        return result;
    }

    /// <summary>Creates an immutable texture sampler owned by this device.</summary>
    public GraphicsSampler CreateSampler(GraphicsSamplerDescriptor descriptor)
    {
        ThrowIfUnavailable();
        ValidateSamplerDescriptor(descriptor);
        GraphicsSampler result = CreateSamplerCore(descriptor) ??
            throw new InvalidOperationException("The graphics backend returned a null sampler.");
        ValidateOwner(result, "result");
        return result;
    }

    /// <summary>Creates an immutable bind-group schema owned by this device.</summary>
    public GraphicsBindGroupLayout CreateBindGroupLayout(GraphicsBindGroupLayoutDescriptor descriptor)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(descriptor);
        HashSet<uint> bindings = [];
        const GraphicsShaderStage knownStages = GraphicsShaderStage.Vertex | GraphicsShaderStage.Fragment;
        foreach (GraphicsBindGroupLayoutEntry entry in descriptor.Entries)
        {
            if (entry.Visibility == GraphicsShaderStage.None || (entry.Visibility & ~knownStages) != 0 ||
                !Enum.IsDefined(entry.ResourceType))
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "A binding declaration is invalid.");
            }
            if (!bindings.Add(entry.Binding))
            {
                throw new ArgumentException("Binding numbers must be unique within a layout.", nameof(descriptor));
            }
            bool validResource = entry.ResourceType switch
            {
                GraphicsBindingResourceType.Buffer =>
                    Enum.IsDefined(entry.BufferType) && (entry.MinBindingSize & 3) == 0,
                GraphicsBindingResourceType.Sampler => Enum.IsDefined(entry.SamplerType),
                GraphicsBindingResourceType.SampledTexture =>
                    Enum.IsDefined(entry.TextureSampleType) &&
                    Enum.IsDefined(entry.TextureViewDimension),
                _ => false,
            };
            if (!validResource)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "A binding declaration contains an invalid resource layout.");
            }
        }
        GraphicsBindGroupLayout result = CreateBindGroupLayoutCore(descriptor) ??
            throw new InvalidOperationException("The graphics backend returned a null bind-group layout.");
        ValidateOwner(result, "result");
        return result;
    }

    /// <summary>Creates an ordered pipeline layout owned by this device.</summary>
    public GraphicsPipelineLayout CreatePipelineLayout(GraphicsPipelineLayoutDescriptor descriptor)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(descriptor);
        foreach (GraphicsBindGroupLayout layout in descriptor.BindGroupLayouts)
        {
            ValidateOwner(layout, nameof(descriptor));
        }
        GraphicsPipelineLayout result = CreatePipelineLayoutCore(descriptor) ??
            throw new InvalidOperationException("The graphics backend returned a null pipeline layout.");
        ValidateOwner(result, "result");
        return result;
    }

    /// <summary>Creates immutable buffer bindings against one layout.</summary>
    public GraphicsBindGroup CreateBindGroup(GraphicsBindGroupDescriptor descriptor)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateOwner(descriptor.Layout, nameof(descriptor));
        IReadOnlyList<GraphicsBindGroupLayoutEntry> declarations = descriptor.Layout.Descriptor.Entries;
        if (descriptor.Entries.Count != declarations.Count)
        {
            throw new ArgumentException("A bind group must provide exactly one entry per layout binding.", nameof(descriptor));
        }
        Dictionary<uint, GraphicsBindGroupLayoutEntry> declarationByBinding =
            declarations.ToDictionary(static entry => entry.Binding);
        HashSet<uint> bindings = [];
        foreach (GraphicsBindGroupEntry entry in descriptor.Entries)
        {
            if (!bindings.Add(entry.Binding) || !declarationByBinding.TryGetValue(entry.Binding, out GraphicsBindGroupLayoutEntry declaration))
            {
                throw new ArgumentException("The bind group contains a duplicate or undeclared binding.", nameof(descriptor));
            }
            if (entry.ResourceType != declaration.ResourceType)
            {
                throw new ArgumentException("A bind-group resource does not match its layout category.", nameof(descriptor));
            }
            switch (entry.ResourceType)
            {
                case GraphicsBindingResourceType.Buffer:
                    GraphicsBuffer buffer = entry.Buffer ??
                        throw new ArgumentException("A buffer binding requires a buffer.", nameof(descriptor));
                    ValidateOwner(buffer, nameof(descriptor));
                    GraphicsBufferUsage requiredUsage = declaration.BufferType switch
                    {
                        GraphicsBufferBindingType.Uniform => GraphicsBufferUsage.Uniform,
                        GraphicsBufferBindingType.ReadOnlyStorage or GraphicsBufferBindingType.Storage =>
                            GraphicsBufferUsage.Storage,
                        _ => throw new ArgumentOutOfRangeException(nameof(descriptor)),
                    };
                    if ((buffer.Usage & requiredUsage) == 0)
                    {
                        throw new ArgumentException("A bound buffer does not allow the declared shader use.", nameof(descriptor));
                    }
                    if (entry.Size == 0 || entry.Offset > buffer.Size ||
                        entry.Size > buffer.Size - entry.Offset ||
                        ((entry.Offset | entry.Size) & 3) != 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(descriptor), "A buffer binding range is invalid.");
                    }
                    if (entry.Size < declaration.MinBindingSize)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(descriptor),
                            "A buffer binding is smaller than its layout minimum.");
                    }
                    break;
                case GraphicsBindingResourceType.Sampler:
                    GraphicsSampler sampler = entry.Sampler ??
                        throw new ArgumentException("A sampler binding requires a sampler.", nameof(descriptor));
                    ValidateOwner(sampler, nameof(descriptor));
                    if (declaration.SamplerType == GraphicsSamplerBindingType.NonFiltering &&
                        (sampler.Descriptor.MagFilter != GraphicsFilterMode.Nearest ||
                         sampler.Descriptor.MinFilter != GraphicsFilterMode.Nearest ||
                         sampler.Descriptor.MipmapFilter != GraphicsFilterMode.Nearest))
                    {
                        throw new ArgumentException("A non-filtering binding requires nearest sampler filters.", nameof(descriptor));
                    }
                    if ((declaration.SamplerType == GraphicsSamplerBindingType.Comparison) !=
                        sampler.Descriptor.Compare.HasValue)
                    {
                        throw new ArgumentException(
                            "A comparison sampler binding and sampler comparison must be declared together.",
                            nameof(descriptor));
                    }
                    break;
                case GraphicsBindingResourceType.SampledTexture:
                    GraphicsTextureView textureView = entry.TextureView ??
                        throw new ArgumentException("A texture binding requires a texture view.", nameof(descriptor));
                    ValidateOwner(textureView, nameof(descriptor));
                    GraphicsTexture viewedTexture = textureView.Descriptor.Texture;
                    if ((viewedTexture.Descriptor.Usage & GraphicsTextureUsage.TextureBinding) == 0 ||
                        textureView.Descriptor.Dimension != declaration.TextureViewDimension ||
                        (viewedTexture.Descriptor.SampleCount > 1) != declaration.Multisampled ||
                        !IsCompatibleSampleType(viewedTexture.Descriptor.Format, declaration.TextureSampleType))
                    {
                        throw new ArgumentException("A texture view is incompatible with its sampled layout.", nameof(descriptor));
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(descriptor));
            }
        }
        GraphicsBindGroup result = CreateBindGroupCore(descriptor) ??
            throw new InvalidOperationException("The graphics backend returned a null bind group.");
        ValidateOwner(result, "result");
        return result;
    }

    /// <summary>Creates a WGSL shader module owned by this device.</summary>
    public GraphicsShaderModule CreateShaderModule(GraphicsShaderModuleDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (string.IsNullOrWhiteSpace(descriptor.Code))
        {
            throw new ArgumentException("Shader source must be non-empty.", nameof(descriptor));
        }
        GraphicsShaderModule result = CreateShaderModuleCore(descriptor) ??
            throw new InvalidOperationException("The graphics backend returned a null shader module.");
        ValidateOwner(result, "result");
        return result;
    }

    /// <summary>Creates an immutable render pipeline owned by this device.</summary>
    public GraphicsRenderPipeline CreateRenderPipeline(GraphicsRenderPipelineDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (descriptor.VertexShader is null)
        {
            throw new ArgumentException("A vertex shader module is required.", nameof(descriptor));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.VertexEntryPoint);
        bool hasFragment = descriptor.FragmentShader is not null;
        if (hasFragment != (descriptor.FragmentEntryPoint is not null))
        {
            throw new ArgumentException(
                "The fragment shader and entry point must either both be present or both be absent.",
                nameof(descriptor));
        }
        if (hasFragment)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.FragmentEntryPoint);
        }
        if (!Enum.IsDefined(descriptor.Topology) || !Enum.IsDefined(descriptor.CullMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                "The pipeline contains an unknown topology or cull mode.");
        }
        if (descriptor.ColorFormat is GraphicsTextureFormat colorFormat && !IsColorFormat(colorFormat))
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "The pipeline requires a color-renderable format.");
        }
        if (descriptor.ColorFormat.HasValue && !hasFragment)
        {
            throw new ArgumentException("A color target requires a fragment stage.", nameof(descriptor));
        }
        if (!descriptor.ColorFormat.HasValue && descriptor.DepthStencil is null)
        {
            throw new ArgumentException("A depth-only pipeline requires depth state.", nameof(descriptor));
        }
        if (descriptor.Blend is GraphicsBlendState blend)
        {
            if (!descriptor.ColorFormat.HasValue)
            {
                throw new ArgumentException("A depth-only pipeline cannot enable color blending.", nameof(descriptor));
            }
            ValidateBlendComponent(blend.Color, nameof(descriptor));
            ValidateBlendComponent(blend.Alpha, nameof(descriptor));
        }
        ValidateOwner(descriptor.VertexShader, nameof(descriptor));
        if (descriptor.FragmentShader is not null)
        {
            ValidateOwner(descriptor.FragmentShader, nameof(descriptor));
        }
        if (descriptor.Layout is not null)
        {
            ValidateOwner(descriptor.Layout, nameof(descriptor));
        }
        ValidateVertexLayouts(descriptor.VertexBuffers);
        if (descriptor.DepthStencil is GraphicsDepthStencilState depthStencil)
        {
            if (depthStencil.Format != GraphicsTextureFormat.Depth32Float)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "The initial depth-state contract supports Depth32Float; stencil state is reserved.");
            }
            if (!Enum.IsDefined(depthStencil.DepthCompare))
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "The depth comparison is unknown.");
            }
        }
        GraphicsRenderPipeline result = CreateRenderPipelineCore(descriptor) ??
            throw new InvalidOperationException("The graphics backend returned a null render pipeline.");
        ValidateOwner(result, "result");
        return result;
    }

    private static void ValidateBlendComponent(GraphicsBlendComponent component, string parameterName)
    {
        if (!Enum.IsDefined(component.Operation) ||
            !Enum.IsDefined(component.SourceFactor) ||
            !Enum.IsDefined(component.DestinationFactor))
        {
            throw new ArgumentOutOfRangeException(parameterName, "The pipeline contains an unknown blend value.");
        }
    }

    /// <summary>Creates an encoder for one command-buffer recording.</summary>
    public GraphicsCommandEncoder CreateCommandEncoder(string? label = null)
    {
        ThrowIfUnavailable();
        GraphicsCommandEncoder result = CreateCommandEncoderCore(label) ??
            throw new InvalidOperationException("The graphics backend returned a null command encoder.");
        ValidateOwner(result, "result");
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (state == GraphicsDeviceState.Disposed)
        {
            return;
        }

        state = GraphicsDeviceState.Disposed;
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>Creates the backend buffer after common lifecycle validation.</summary>
    protected abstract GraphicsBuffer CreateBufferCore(GraphicsBufferDescriptor descriptor);

    /// <summary>Creates the backend texture after common lifecycle validation.</summary>
    protected abstract GraphicsTexture CreateTextureCore(GraphicsTextureDescriptor descriptor);

    /// <summary>Creates the backend texture view after common validation.</summary>
    protected abstract GraphicsTextureView CreateTextureViewCore(GraphicsTextureViewDescriptor descriptor);

    /// <summary>Creates the backend sampler after common lifecycle validation.</summary>
    protected abstract GraphicsSampler CreateSamplerCore(GraphicsSamplerDescriptor descriptor);

    /// <summary>Creates a backend bind-group layout after common validation.</summary>
    protected abstract GraphicsBindGroupLayout CreateBindGroupLayoutCore(GraphicsBindGroupLayoutDescriptor descriptor);

    /// <summary>Creates a backend pipeline layout after common validation.</summary>
    protected abstract GraphicsPipelineLayout CreatePipelineLayoutCore(GraphicsPipelineLayoutDescriptor descriptor);

    /// <summary>Creates a backend bind group after common validation.</summary>
    protected abstract GraphicsBindGroup CreateBindGroupCore(GraphicsBindGroupDescriptor descriptor);

    /// <summary>Creates the backend shader module after common lifecycle validation.</summary>
    protected abstract GraphicsShaderModule CreateShaderModuleCore(GraphicsShaderModuleDescriptor descriptor);

    /// <summary>Creates the backend render pipeline after common lifecycle validation.</summary>
    protected abstract GraphicsRenderPipeline CreateRenderPipelineCore(GraphicsRenderPipelineDescriptor descriptor);

    /// <summary>Creates the backend encoder after common lifecycle validation.</summary>
    protected abstract GraphicsCommandEncoder CreateCommandEncoderCore(string? label);

    /// <summary>Releases backend device state. Implementations must tolerate one call.</summary>
    protected abstract void DisposeCore();

    /// <summary>Transitions this device to lost and raises <see cref="DeviceLost"/> once.</summary>
    protected void ReportDeviceLost(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (state != GraphicsDeviceState.Active)
        {
            return;
        }

        LostReason = reason;
        state = GraphicsDeviceState.Lost;
        DeviceLost?.Invoke(this, new GraphicsDeviceLostEventArgs(reason));
    }

    internal void ValidateOwner(GraphicsResource resource, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(resource, parameterName);
        if (!ReferenceEquals(resource.Device, this))
        {
            throw new ArgumentException("The resource belongs to a different graphics device.", parameterName);
        }
        resource.ThrowIfDisposed();
    }

    internal void ThrowIfUnavailable()
    {
        if (state == GraphicsDeviceState.Disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
        if (state == GraphicsDeviceState.Lost)
        {
            throw new InvalidOperationException($"The graphics device is lost: {LostReason}");
        }
    }

    private static void ValidateSamplerDescriptor(GraphicsSamplerDescriptor descriptor)
    {
        if (!Enum.IsDefined(descriptor.AddressModeU) ||
            !Enum.IsDefined(descriptor.AddressModeV) ||
            !Enum.IsDefined(descriptor.AddressModeW) ||
            !Enum.IsDefined(descriptor.MagFilter) ||
            !Enum.IsDefined(descriptor.MinFilter) ||
            !Enum.IsDefined(descriptor.MipmapFilter) ||
            (descriptor.Compare.HasValue && !Enum.IsDefined(descriptor.Compare.Value)) ||
            !float.IsFinite(descriptor.LodMinClamp) ||
            !float.IsFinite(descriptor.LodMaxClamp) ||
            descriptor.LodMinClamp < 0f ||
            descriptor.LodMaxClamp < descriptor.LodMinClamp)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "The sampler contains an unknown enum value.");
        }
    }

    private static void ValidateVertexLayouts(IReadOnlyList<GraphicsVertexBufferLayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        HashSet<uint> shaderLocations = [];
        foreach (GraphicsVertexBufferLayout? layout in layouts)
        {
            if (layout is null || layout.ArrayStride == 0 || (layout.ArrayStride & 3) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(layouts),
                    "Every vertex-buffer stride must be non-zero and four-byte aligned.");
            }
            if (!Enum.IsDefined(layout.StepMode) || layout.Attributes.Count == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(layouts),
                    "Every vertex-buffer layout requires a known step mode and at least one attribute.");
            }

            foreach (GraphicsVertexAttribute attribute in layout.Attributes)
            {
                if (!Enum.IsDefined(attribute.Format) || (attribute.Offset & 3) != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(layouts),
                        "Vertex attributes require a known format and four-byte-aligned offset.");
                }
                ulong size = attribute.Format switch
                {
                    GraphicsVertexFormat.Float32x2 => 8,
                    GraphicsVertexFormat.Float32x3 => 12,
                    GraphicsVertexFormat.Float32x4 => 16,
                    _ => throw new ArgumentOutOfRangeException(nameof(layouts)),
                };
                if (attribute.Offset > layout.ArrayStride || size > layout.ArrayStride - attribute.Offset)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(layouts),
                        "A vertex attribute exceeds its buffer stride.");
                }
                if (!shaderLocations.Add(attribute.ShaderLocation))
                {
                    throw new ArgumentException("Vertex shader locations must be unique.", nameof(layouts));
                }
            }
        }
    }

    private static bool IsColorFormat(GraphicsTextureFormat format) => format is
        GraphicsTextureFormat.Bgra8Unorm or
        GraphicsTextureFormat.Bgra8UnormSrgb or
        GraphicsTextureFormat.Rgba8Unorm or
        GraphicsTextureFormat.Rgba8UnormSrgb or
        GraphicsTextureFormat.Rgba16Float or
        GraphicsTextureFormat.Rgba32Float or
        GraphicsTextureFormat.Rgb10A2Unorm;

    private static bool IsCompatibleSampleType(
        GraphicsTextureFormat format,
        GraphicsTextureSampleType sampleType) => sampleType switch
        {
            GraphicsTextureSampleType.Float => format is
                GraphicsTextureFormat.Bgra8Unorm or
                GraphicsTextureFormat.Bgra8UnormSrgb or
                GraphicsTextureFormat.Rgba8Unorm or
                GraphicsTextureFormat.Rgba8UnormSrgb or
                GraphicsTextureFormat.Rgba16Float or
                GraphicsTextureFormat.Rgb10A2Unorm || IsCompressedColorFormat(format),
            GraphicsTextureSampleType.UnfilterableFloat =>
                IsColorFormat(format) || IsCompressedColorFormat(format),
            GraphicsTextureSampleType.Depth => format is
                GraphicsTextureFormat.Depth32Float or GraphicsTextureFormat.Depth32FloatStencil8,
            _ => false,
        };

    private static bool IsCompressedColorFormat(GraphicsTextureFormat format) =>
        GetTextureBlockInfo(format).CompressionFamily != GraphicsTextureCompressionFamily.None;

    internal static GraphicsTextureBlockLayout GetTextureBlockInfo(GraphicsTextureFormat format)
    {
        if (!Enum.IsDefined(format) || format is
            GraphicsTextureFormat.Depth32Float or
            GraphicsTextureFormat.Depth32FloatStencil8)
        {
            return default;
        }

        return GraphicsTextureFormatInfo.GetBlockLayout(format);
    }

    private static uint GetMaximumMipLevelCount(uint width, uint height)
    {
        uint largest = Math.Max(width, height);
        uint count = 0;
        do
        {
            count++;
            largest >>= 1;
        }
        while (largest != 0);
        return count;
    }

    internal static uint GetMipDimension(uint baseDimension, uint mipLevel) =>
        mipLevel >= 32 ? 1 : Math.Max(1u, baseDimension >> checked((int)mipLevel));
}
