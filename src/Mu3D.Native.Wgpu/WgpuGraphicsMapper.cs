using Mu3D.Graphics;
using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.Native.Wgpu;

internal static class WgpuGraphicsMapper
{
    internal static ulong MapBufferUsage(GraphicsBufferUsage usage)
    {
        ulong result = 0;
        AddFlag(usage, GraphicsBufferUsage.MapRead, WgpuNative.WGPUBufferUsage_MapRead, ref result);
        AddFlag(usage, GraphicsBufferUsage.MapWrite, WgpuNative.WGPUBufferUsage_MapWrite, ref result);
        AddFlag(usage, GraphicsBufferUsage.CopySource, WgpuNative.WGPUBufferUsage_CopySrc, ref result);
        AddFlag(usage, GraphicsBufferUsage.CopyDestination, WgpuNative.WGPUBufferUsage_CopyDst, ref result);
        AddFlag(usage, GraphicsBufferUsage.Index, WgpuNative.WGPUBufferUsage_Index, ref result);
        AddFlag(usage, GraphicsBufferUsage.Vertex, WgpuNative.WGPUBufferUsage_Vertex, ref result);
        AddFlag(usage, GraphicsBufferUsage.Uniform, WgpuNative.WGPUBufferUsage_Uniform, ref result);
        AddFlag(usage, GraphicsBufferUsage.Storage, WgpuNative.WGPUBufferUsage_Storage, ref result);
        AddFlag(usage, GraphicsBufferUsage.Indirect, WgpuNative.WGPUBufferUsage_Indirect, ref result);
        return result;
    }

    internal static ulong MapTextureUsage(GraphicsTextureUsage usage)
    {
        ulong result = 0;
        AddFlag(usage, GraphicsTextureUsage.CopySource, WgpuNative.WGPUTextureUsage_CopySrc, ref result);
        AddFlag(usage, GraphicsTextureUsage.CopyDestination, WgpuNative.WGPUTextureUsage_CopyDst, ref result);
        AddFlag(usage, GraphicsTextureUsage.TextureBinding, WgpuNative.WGPUTextureUsage_TextureBinding, ref result);
        AddFlag(usage, GraphicsTextureUsage.StorageBinding, WgpuNative.WGPUTextureUsage_StorageBinding, ref result);
        AddFlag(usage, GraphicsTextureUsage.RenderAttachment, WgpuNative.WGPUTextureUsage_RenderAttachment, ref result);
        return result;
    }

    internal static WGPUTextureFormat MapTextureFormat(GraphicsTextureFormat format) => format switch
    {
        GraphicsTextureFormat.Bgra8Unorm => WGPUTextureFormat.BGRA8Unorm,
        GraphicsTextureFormat.Bgra8UnormSrgb => WGPUTextureFormat.BGRA8UnormSrgb,
        GraphicsTextureFormat.Rgba8Unorm => WGPUTextureFormat.RGBA8Unorm,
        GraphicsTextureFormat.Rgba8UnormSrgb => WGPUTextureFormat.RGBA8UnormSrgb,
        GraphicsTextureFormat.Rgba16Float => WGPUTextureFormat.RGBA16Float,
        GraphicsTextureFormat.Rgba32Float => WGPUTextureFormat.RGBA32Float,
        GraphicsTextureFormat.Rgb10A2Unorm => WGPUTextureFormat.RGB10A2Unorm,
        GraphicsTextureFormat.Bc1RgbaUnorm => WGPUTextureFormat.BC1RGBAUnorm,
        GraphicsTextureFormat.Bc1RgbaUnormSrgb => WGPUTextureFormat.BC1RGBAUnormSrgb,
        GraphicsTextureFormat.Bc3RgbaUnorm => WGPUTextureFormat.BC3RGBAUnorm,
        GraphicsTextureFormat.Bc3RgbaUnormSrgb => WGPUTextureFormat.BC3RGBAUnormSrgb,
        GraphicsTextureFormat.Bc7RgbaUnorm => WGPUTextureFormat.BC7RGBAUnorm,
        GraphicsTextureFormat.Bc7RgbaUnormSrgb => WGPUTextureFormat.BC7RGBAUnormSrgb,
        GraphicsTextureFormat.Etc2Rgb8Unorm => WGPUTextureFormat.ETC2RGB8Unorm,
        GraphicsTextureFormat.Etc2Rgb8UnormSrgb => WGPUTextureFormat.ETC2RGB8UnormSrgb,
        GraphicsTextureFormat.Etc2Rgba8Unorm => WGPUTextureFormat.ETC2RGBA8Unorm,
        GraphicsTextureFormat.Etc2Rgba8UnormSrgb => WGPUTextureFormat.ETC2RGBA8UnormSrgb,
        GraphicsTextureFormat.Astc4x4Unorm => WGPUTextureFormat.ASTC4x4Unorm,
        GraphicsTextureFormat.Astc4x4UnormSrgb => WGPUTextureFormat.ASTC4x4UnormSrgb,
        GraphicsTextureFormat.Astc6x6Unorm => WGPUTextureFormat.ASTC6x6Unorm,
        GraphicsTextureFormat.Astc6x6UnormSrgb => WGPUTextureFormat.ASTC6x6UnormSrgb,
        GraphicsTextureFormat.Astc8x8Unorm => WGPUTextureFormat.ASTC8x8Unorm,
        GraphicsTextureFormat.Astc8x8UnormSrgb => WGPUTextureFormat.ASTC8x8UnormSrgb,
        GraphicsTextureFormat.Depth32Float => WGPUTextureFormat.Depth32Float,
        GraphicsTextureFormat.Depth32FloatStencil8 => WGPUTextureFormat.Depth32FloatStencil8,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported texture format."),
    };

    internal static WGPUAddressMode MapAddressMode(GraphicsAddressMode mode) => mode switch
    {
        GraphicsAddressMode.ClampToEdge => WGPUAddressMode.ClampToEdge,
        GraphicsAddressMode.Repeat => WGPUAddressMode.Repeat,
        GraphicsAddressMode.MirrorRepeat => WGPUAddressMode.MirrorRepeat,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported address mode."),
    };

    internal static WGPUFilterMode MapFilterMode(GraphicsFilterMode mode) => mode switch
    {
        GraphicsFilterMode.Nearest => WGPUFilterMode.Nearest,
        GraphicsFilterMode.Linear => WGPUFilterMode.Linear,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported filter mode."),
    };

    internal static WGPUMipmapFilterMode MapMipmapFilterMode(GraphicsFilterMode mode) => mode switch
    {
        GraphicsFilterMode.Nearest => WGPUMipmapFilterMode.Nearest,
        GraphicsFilterMode.Linear => WGPUMipmapFilterMode.Linear,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported mipmap filter mode."),
    };

    internal static WGPUPrimitiveTopology MapPrimitiveTopology(GraphicsPrimitiveTopology topology) => topology switch
    {
        GraphicsPrimitiveTopology.TriangleList => WGPUPrimitiveTopology.TriangleList,
        GraphicsPrimitiveTopology.TriangleStrip => WGPUPrimitiveTopology.TriangleStrip,
        _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, "Unsupported primitive topology."),
    };

    internal static WGPUCullMode MapCullMode(GraphicsCullMode mode) => mode switch
    {
        GraphicsCullMode.None => WGPUCullMode.None,
        GraphicsCullMode.Front => WGPUCullMode.Front,
        GraphicsCullMode.Back => WGPUCullMode.Back,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported cull mode."),
    };

    internal static WGPUBlendFactor MapBlendFactor(GraphicsBlendFactor factor) => factor switch
    {
        GraphicsBlendFactor.Zero => WGPUBlendFactor.Zero,
        GraphicsBlendFactor.One => WGPUBlendFactor.One,
        GraphicsBlendFactor.SourceAlpha => WGPUBlendFactor.SrcAlpha,
        GraphicsBlendFactor.OneMinusSourceAlpha => WGPUBlendFactor.OneMinusSrcAlpha,
        GraphicsBlendFactor.DestinationAlpha => WGPUBlendFactor.DstAlpha,
        GraphicsBlendFactor.OneMinusDestinationAlpha => WGPUBlendFactor.OneMinusDstAlpha,
        _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, "Unsupported blend factor."),
    };

    internal static WGPUBlendOperation MapBlendOperation(GraphicsBlendOperation operation) => operation switch
    {
        GraphicsBlendOperation.Add => WGPUBlendOperation.Add,
        GraphicsBlendOperation.Subtract => WGPUBlendOperation.Subtract,
        GraphicsBlendOperation.ReverseSubtract => WGPUBlendOperation.ReverseSubtract,
        GraphicsBlendOperation.Minimum => WGPUBlendOperation.Min,
        GraphicsBlendOperation.Maximum => WGPUBlendOperation.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported blend operation."),
    };

    internal static WGPUVertexFormat MapVertexFormat(GraphicsVertexFormat format) => format switch
    {
        GraphicsVertexFormat.Float32x2 => WGPUVertexFormat.Float32x2,
        GraphicsVertexFormat.Float32x3 => WGPUVertexFormat.Float32x3,
        GraphicsVertexFormat.Float32x4 => WGPUVertexFormat.Float32x4,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported vertex format."),
    };

    internal static WGPUVertexStepMode MapVertexStepMode(GraphicsVertexStepMode mode) => mode switch
    {
        GraphicsVertexStepMode.Vertex => WGPUVertexStepMode.Vertex,
        GraphicsVertexStepMode.Instance => WGPUVertexStepMode.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported vertex step mode."),
    };

    internal static WGPUIndexFormat MapIndexFormat(GraphicsIndexFormat format) => format switch
    {
        GraphicsIndexFormat.Uint16 => WGPUIndexFormat.Uint16,
        GraphicsIndexFormat.Uint32 => WGPUIndexFormat.Uint32,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported index format."),
    };

    internal static WGPUCompareFunction MapCompareFunction(GraphicsCompareFunction function) => function switch
    {
        GraphicsCompareFunction.Never => WGPUCompareFunction.Never,
        GraphicsCompareFunction.Less => WGPUCompareFunction.Less,
        GraphicsCompareFunction.Equal => WGPUCompareFunction.Equal,
        GraphicsCompareFunction.LessEqual => WGPUCompareFunction.LessEqual,
        GraphicsCompareFunction.Greater => WGPUCompareFunction.Greater,
        GraphicsCompareFunction.NotEqual => WGPUCompareFunction.NotEqual,
        GraphicsCompareFunction.GreaterEqual => WGPUCompareFunction.GreaterEqual,
        GraphicsCompareFunction.Always => WGPUCompareFunction.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(function), function, "Unsupported compare function."),
    };

    internal static ulong MapShaderStages(GraphicsShaderStage stages)
    {
        ulong result = 0;
        AddFlag(stages, GraphicsShaderStage.Vertex, WgpuNative.WGPUShaderStage_Vertex, ref result);
        AddFlag(stages, GraphicsShaderStage.Fragment, WgpuNative.WGPUShaderStage_Fragment, ref result);
        return result;
    }

    internal static WGPUBufferBindingType MapBufferBindingType(GraphicsBufferBindingType type) => type switch
    {
        GraphicsBufferBindingType.Uniform => WGPUBufferBindingType.Uniform,
        GraphicsBufferBindingType.ReadOnlyStorage => WGPUBufferBindingType.ReadOnlyStorage,
        GraphicsBufferBindingType.Storage => WGPUBufferBindingType.Storage,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported buffer binding type."),
    };

    internal static WGPUSamplerBindingType MapSamplerBindingType(GraphicsSamplerBindingType type) => type switch
    {
        GraphicsSamplerBindingType.Filtering => WGPUSamplerBindingType.Filtering,
        GraphicsSamplerBindingType.NonFiltering => WGPUSamplerBindingType.NonFiltering,
        GraphicsSamplerBindingType.Comparison => WGPUSamplerBindingType.Comparison,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported sampler binding type."),
    };

    internal static WGPUTextureSampleType MapTextureSampleType(GraphicsTextureSampleType type) => type switch
    {
        GraphicsTextureSampleType.Float => WGPUTextureSampleType.Float,
        GraphicsTextureSampleType.UnfilterableFloat => WGPUTextureSampleType.UnfilterableFloat,
        GraphicsTextureSampleType.Depth => WGPUTextureSampleType.Depth,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported texture sample type."),
    };

    internal static WGPUTextureViewDimension MapTextureViewDimension(
        GraphicsTextureViewDimension dimension) => dimension switch
        {
            GraphicsTextureViewDimension.TwoD => WGPUTextureViewDimension._2D,
            GraphicsTextureViewDimension.TwoDArray => WGPUTextureViewDimension._2DArray,
            GraphicsTextureViewDimension.Cube => WGPUTextureViewDimension.Cube,
            GraphicsTextureViewDimension.CubeArray => WGPUTextureViewDimension.CubeArray,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Unsupported texture-view dimension."),
        };

    internal static WGPULoadOp MapLoadOperation(GraphicsLoadOperation operation) => operation switch
    {
        GraphicsLoadOperation.Load => WGPULoadOp.Load,
        GraphicsLoadOperation.Clear => WGPULoadOp.Clear,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported load operation."),
    };

    internal static WGPUStoreOp MapStoreOperation(GraphicsStoreOperation operation) => operation switch
    {
        GraphicsStoreOperation.Store => WGPUStoreOp.Store,
        GraphicsStoreOperation.Discard => WGPUStoreOp.Discard,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported store operation."),
    };

    private static void AddFlag<T>(T value, T flag, ulong nativeFlag, ref ulong result)
        where T : struct, Enum
    {
        ulong bits = Convert.ToUInt64(value);
        ulong flagBits = Convert.ToUInt64(flag);
        if ((bits & flagBits) != 0)
        {
            result |= nativeFlag;
        }
    }
}
