using System.Numerics;
using Mu3D.Color;
using Mu3D.Native.OpenColorIO.Interop;

namespace Mu3D.Native.OpenColorIO;

/// <summary>An immutable OCIO CPU processor whose identifiers explicitly describe its untagged numeric RGB input/output.</summary>
/// <remarks>Output may be nonlinear/encoded. Applying the processor never silently declares it to be linear display RGB.</remarks>
public sealed class OcioProcessor : IDisposable
{
    private readonly OcioProcessorHandle handle;
    internal OcioProcessor(OcioProcessorHandle handle, string source, string output, string configurationCacheId)
    {
        this.handle = handle; SourceIdentifier = source; OutputIdentifier = output; ConfigurationCacheId = configurationCacheId;
        try { CacheId = OcioNative.ProcessorCacheId(handle); }
        catch { handle.Dispose(); throw; }
    }
    /// <summary>Gets the explicit input color-space identifier within the originating configuration.</summary>
    public string SourceIdentifier { get; }
    /// <summary>Gets the explicit numeric output color-space identifier, which may describe encoded RGB.</summary>
    public string OutputIdentifier { get; }
    /// <summary>Gets the originating immutable configuration/context cache identity.</summary>
    public string ConfigurationCacheId { get; }
    /// <summary>Gets the compiled CPU processor's cache identity, including its actual operations.</summary>
    public string CacheId { get; }

    /// <summary>Transforms one finite RGB tuple and rejects nonfinite output without clipping HDR or negative values.</summary>
    public unsafe Vector3 ApplyRgb(Vector3 value)
    {
        ThrowIfDisposed(); Validate(value);
        OcioNative.Check(OcioNative.Apply(handle, (float*)&value, 1, 3));
        ValidateResult(value); return value;
    }
    /// <summary>Transforms straight RGBA pixels in place, preserving every alpha bit exactly.</summary>
    /// <remarks>Input is validated and output staged before mutation. Premultiplied pixels must be unpremultiplied by the caller.</remarks>
    public unsafe void ApplyRgba(Span<Vector4> pixels)
    {
        ThrowIfDisposed();
        foreach (Vector4 value in pixels)
        {
            Validate(new(value.X, value.Y, value.Z));
            if (!float.IsFinite(value.W)) throw new ArgumentOutOfRangeException(nameof(pixels), "Alpha must be finite.");
        }
        if (pixels.IsEmpty) return;
        Vector4[] result = pixels.ToArray();
        fixed (Vector4* data = result) OcioNative.Check(OcioNative.Apply(handle, (float*)data, result.Length, 4));
        foreach (Vector4 value in result) ValidateResult(new(value.X, value.Y, value.Z));
        result.CopyTo(pixels);
    }
    /// <summary>Adapts a processor already known to have linear endpoints to the portable LUT-baking contract.</summary>
    /// <param name="sourceSpace">Caller-verified Mu3D identity corresponding exactly to <see cref="SourceIdentifier"/>.</param>
    /// <param name="destinationSpace">Caller-verified linear identity corresponding exactly to <see cref="OutputIdentifier"/>.</param>
    /// <remarks>
    /// This is an explicit metadata assertion, not transfer decoding. Never use it for encoded display/view
    /// output. Use an explicit decode first, then bake with LinearRgbLut3D.Bake using a chosen domain/range policy.
    /// The adapter borrows this processor, which must remain alive while evaluating or baking it.
    /// </remarks>
    public ILinearRgbTransform AsLinearTransform(ColorSpaceReference sourceSpace, ColorSpaceReference destinationSpace)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(sourceSpace); ArgumentNullException.ThrowIfNull(destinationSpace);
        if (!sourceSpace.IsLinear || !destinationSpace.IsLinear) throw new ArgumentException("Both declared endpoints must be linear.");
        return new LinearAdapter(this, sourceSpace, destinationSpace);
    }
    /// <summary>Releases native processing resources; subsequent evaluation fails explicitly.</summary>
    public void Dispose() => handle.Dispose();
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(handle.IsClosed, this);
    private static void Validate(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(nameof(value), "RGB must be finite.");
    }
    private static void ValidateResult(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new InvalidOperationException("The OCIO processor produced nonfinite FP32 RGB.");
    }
    private sealed class LinearAdapter(OcioProcessor processor, ColorSpaceReference source, ColorSpaceReference destination) : ILinearRgbTransform
    {
        public ColorSpaceReference SourceSpace { get; } = source;
        public ColorSpaceReference DestinationSpace { get; } = destination;
        public LinearRgba Transform(LinearRgba value)
        {
            if (value.ColorSpace != SourceSpace) throw new ArgumentException("Color does not match the declared OCIO input identity.", nameof(value));
            Vector3 rgb = processor.ApplyRgb(new(value.Red, value.Green, value.Blue));
            return new(rgb.X, rgb.Y, rgb.Z, value.Alpha, DestinationSpace);
        }
    }
}
