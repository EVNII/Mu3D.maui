using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mu3D.Color;

/// <summary>Converts one explicitly represented color coordinate to another.</summary>
/// <typeparam name="TSource">The source coordinate value type.</typeparam>
/// <typeparam name="TDestination">The destination coordinate value type.</typeparam>
/// <remarks>
/// Applications may supply value-type or caller-owned reference-type implementations. Coordinate
/// types and transform parameters carry color identities, units and any selected conversion policy;
/// the interface does not infer RGB semantics, alpha handling, clipping or display mapping.
/// Implementing this CPU contract does not generate a GPU shader or lookup table.
/// </remarks>
public interface IColorTransform<TSource, TDestination>
    where TSource : struct
    where TDestination : struct
{
    /// <summary>Converts one coordinate according to this transform's explicit contract.</summary>
    /// <param name="source">The source coordinate and its required metadata.</param>
    /// <returns>The converted coordinate with the destination's required metadata.</returns>
    TDestination Transform(TSource source);
}

/// <summary>Evaluates typed CPU color transforms into caller-owned storage.</summary>
public static class ColorBatch
{
    /// <summary>Converts each source coordinate and writes the corresponding destination prefix.</summary>
    /// <typeparam name="TSource">The source coordinate value type.</typeparam>
    /// <typeparam name="TDestination">The destination coordinate value type.</typeparam>
    /// <typeparam name="TTransform">The statically selected CPU transform implementation.</typeparam>
    /// <param name="source">Source coordinates; an empty span is accepted.</param>
    /// <param name="destination">Storage for at least <paramref name="source"/>.Length coordinates.</param>
    /// <param name="transform">The conversion implementation; reference-type implementations cannot be null.</param>
    /// <remarks>
    /// The method allocates no output storage and uses constrained generic calls, without a
    /// per-coordinate delegate or boxing when <typeparamref name="TTransform"/> is a value type. The transform is passed by value;
    /// a value-type implementation is copied once for this call. Any allocations made by a custom
    /// transform remain its own responsibility. Only the first <paramref name="source"/>.Length
    /// destination elements are written, leaving any destination tail unchanged.
    /// Source and the written destination prefix may overlap only when their element types and
    /// starting locations are identical. That exact in-place case reads each element before writing
    /// its result. Other overlap is rejected before conversion begins. Conversion is not
    /// transactional: if the transform throws, earlier results remain written. Metadata and
    /// conversion policies are supplied by coordinate values and the transform, not this loop.
    /// </remarks>
    /// <exception cref="ArgumentException">Destination storage is too short or overlaps source in an unsupported way.</exception>
    /// <exception cref="ArgumentNullException">A reference-type transform is null.</exception>
    public static void Transform<TSource, TDestination, TTransform>(
        ReadOnlySpan<TSource> source, Span<TDestination> destination, TTransform transform)
        where TSource : struct
        where TDestination : struct
        where TTransform : IColorTransform<TSource, TDestination>
    {
        if (destination.Length < source.Length)
            throw new ArgumentException("Destination storage must hold every source coordinate.", nameof(destination));
        // The static type guard avoids executing a generic boxing null test for value types.
        if (!typeof(TTransform).IsValueType)
            ArgumentNullException.ThrowIfNull(transform);
        if (source.IsEmpty) return;

        ValidateOverlap(source, destination[..source.Length]);
        for (int i = 0; i < source.Length; i++)
            destination[i] = transform.Transform(source[i]);
    }

    private static void ValidateOverlap<TSource, TDestination>(
        ReadOnlySpan<TSource> source, Span<TDestination> destination)
        where TSource : struct
        where TDestination : struct
    {
        // Compare managed byrefs only: no pinning, pointer dereference or raw byte access. Structs
        // may contain metadata references, so MemoryMarshal.AsBytes' unmanaged-only view is unsuitable.
        ref byte firstSource = ref Unsafe.As<TSource, byte>(ref MemoryMarshal.GetReference(source));
        ref byte firstDestination = ref Unsafe.As<TDestination, byte>(ref MemoryMarshal.GetReference(destination));
        nint offset = Unsafe.ByteOffset(ref firstSource, ref firstDestination);
        if (offset == 0 && typeof(TSource) == typeof(TDestination)) return;

        nuint sourceBytes = checked((nuint)source.Length * (nuint)Unsafe.SizeOf<TSource>());
        nuint destinationBytes = checked((nuint)destination.Length * (nuint)Unsafe.SizeOf<TDestination>());
        if (unchecked((nuint)offset) < sourceBytes || unchecked((nuint)(-offset)) < destinationBytes)
            throw new ArgumentException("Only exact in-place conversion of the same coordinate type may overlap.", nameof(destination));
    }
}
