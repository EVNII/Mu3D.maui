using System.Buffers;
using System.Buffers.Binary;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.SceneGraph;

namespace Mu3D.Native.Ktx;

/// <summary>Provides bounded asynchronous loading for standalone KTX2 material textures.</summary>
public static class Ktx2TextureLoader
{
    /// <summary>Gets the default maximum encoded source size (256 MiB).</summary>
    public const int DefaultMaximumSourceByteCount = 256 * 1024 * 1024;

    /// <summary>Gets the default maximum retained decoded/compressed output size (256 MiB).</summary>
    public const long DefaultMaximumOutputByteCount = 256L * 1024 * 1024;

    private const int ReadBufferSize = 80 * 1024;
    private const int Ktx2HeaderByteCount = 80;
    private static ReadOnlySpan<byte> Ktx2Identifier =>
        [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Reads and loads a caller-owned KTX2 stream. The stream remains open after success,
    /// cancellation or failure.
    /// </summary>
    public static async Task<Ktx2TextureAsset> LoadAsync(
        Stream source,
        Ktx2TextureLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The KTX2 source stream must be readable.", nameof(source));
        }
        options ??= new Ktx2TextureLoadOptions();
        ValidateOptions(options);
        cancellationToken.ThrowIfCancellationRequested();
        using MemoryStream bufferedSource = await ReadBoundedAsync(
            source,
            options.MaximumSourceByteCount,
            cancellationToken).ConfigureAwait(false);
        return await LoadBufferedAsync(
            AsMemory(bufferedSource),
            options,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves and loads one application-keyed KTX2 source, optionally using an application-owned
    /// encoded-source cache. A cache hit does not invoke <paramref name="sourceResolver"/>.
    /// Ownership of a resolved stream transfers to the loader.
    /// </summary>
    public static async Task<Ktx2TextureAsset> LoadAsync(
        string sourceKey,
        Ktx2SourceStreamResolver sourceResolver,
        Ktx2SourceCache? sourceCache = null,
        Ktx2TextureLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceKey);
        ArgumentNullException.ThrowIfNull(sourceResolver);
        options ??= new Ktx2TextureLoadOptions();
        ValidateOptions(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (sourceCache?.TryGet(sourceKey, out ReadOnlyMemory<byte> cachedSource) == true)
        {
            ValidateSourceSize(cachedSource, options.MaximumSourceByteCount);
            return await LoadBufferedAsync(
                cachedSource,
                options,
                cancellationToken).ConfigureAwait(false);
        }

        Stream resolved = await sourceResolver(cancellationToken).ConfigureAwait(false) ??
            throw new InvalidDataException(
                $"The KTX2 source resolver returned no stream for '{sourceKey}'.");
        await using (resolved.ConfigureAwait(false))
        {
            if (!resolved.CanRead)
            {
                throw new InvalidDataException(
                    $"The KTX2 source resolver returned an unreadable stream for '{sourceKey}'.");
            }
            using MemoryStream bufferedSource = await ReadBoundedAsync(
                resolved,
                options.MaximumSourceByteCount,
                cancellationToken).ConfigureAwait(false);
            ReadOnlyMemory<byte> source = AsMemory(bufferedSource);
            _ = sourceCache?.TryAdd(sourceKey, source);
            return await LoadBufferedAsync(source, options, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<Ktx2TextureAsset> LoadBufferedAsync(
        ReadOnlyMemory<byte> source,
        Ktx2TextureLoadOptions options,
        CancellationToken cancellationToken)
    {
        ValidateSourceSize(source, options.MaximumSourceByteCount);
        Ktx2Header header = ReadHeader(source.Span);
        long expectedOutputByteCount = CalculateExpectedOutputByteCount(header, options);
        if (expectedOutputByteCount > options.MaximumOutputByteCount)
        {
            throw new InvalidDataException(
                "The KTX2 decoded or compressed output exceeds the configured " +
                $"{options.MaximumOutputByteCount} byte limit.");
        }

        Ktx2TextureAsset asset = await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Ktx2TextureAsset loaded = CreateAsset(source, options);
                cancellationToken.ThrowIfCancellationRequested();
                return loaded;
            },
            cancellationToken).ConfigureAwait(false);
        if (asset.OutputByteCount > options.MaximumOutputByteCount)
        {
            throw new InvalidDataException(
                "The KTX2 output exceeds the configured byte limit after processing.");
        }
        return asset;
    }

    private static Ktx2TextureAsset CreateAsset(
        ReadOnlyMemory<byte> source,
        Ktx2TextureLoadOptions options)
    {
        if (options.Preference == Ktx2TextureLoadPreference.PreferGpuCompressed)
        {
            CompressedMaterialTexture? compressed = new Ktx2TextureTranscoder(
                options.AllowBt709PrimariesForLinearData).TryTranscode(
                    source,
                    "image/ktx2",
                    options.Content,
                    options.GraphicsCapabilities!,
                    options.Name);
            if (compressed is not null)
            {
                return new Ktx2TextureAsset(
                    options.Content,
                    decodedColorImage: null,
                    decodedDataImage: null,
                    compressed,
                    source,
                    options.RetainEncodedSource);
            }
        }

        Ktx2ImageDecoder decoder = new(
            fallback: null,
            options.AllowBt709PrimariesForLinearData);
        if (options.Content == CompressedMaterialTextureContent.Color)
        {
            LinearRgbaImage image = decoder.DecodeColor(
                source,
                "image/ktx2",
                options.Name);
            return new Ktx2TextureAsset(
                options.Content,
                image,
                decodedDataImage: null,
                compressedTexture: null,
                source,
                options.RetainEncodedSource);
        }
        NormalizedRgbaDataImage dataImage = decoder.DecodeData(
            source,
            "image/ktx2",
            options.Name);
        return new Ktx2TextureAsset(
            options.Content,
            decodedColorImage: null,
            dataImage,
            compressedTexture: null,
            source,
            options.RetainEncodedSource);
    }

    private static long CalculateExpectedOutputByteCount(
        Ktx2Header header,
        Ktx2TextureLoadOptions options)
    {
        KtxTranscodeTarget? target = options.Preference ==
                Ktx2TextureLoadPreference.PreferGpuCompressed
            ? Ktx2TextureTranscoder.SelectTarget(
                options.Content,
                options.GraphicsCapabilities!)
            : null;
        if (target is null)
        {
            return checked((long)header.Width * header.Height * 16);
        }

        GraphicsTextureBlockLayout block = GraphicsTextureFormatInfo.GetBlockLayout(
            target.Value.GraphicsFormat);
        long byteCount = 0;
        for (uint level = 0; level < header.LevelCount; level++)
        {
            uint width = Math.Max(1u, header.Width >> checked((int)level));
            uint height = Math.Max(1u, header.Height >> checked((int)level));
            ulong blocksWide = ((ulong)width + block.BlockWidth - 1) / block.BlockWidth;
            ulong blockRows = ((ulong)height + block.BlockHeight - 1) / block.BlockHeight;
            byteCount = checked(byteCount + (long)(blocksWide * blockRows * block.BytesPerBlock));
        }
        return byteCount;
    }

    private static Ktx2Header ReadHeader(ReadOnlySpan<byte> source)
    {
        if (source.Length < Ktx2HeaderByteCount || !source[..12].SequenceEqual(Ktx2Identifier))
        {
            throw new InvalidDataException("The source is not a complete KTX2 header.");
        }
        uint width = BinaryPrimitives.ReadUInt32LittleEndian(source[20..]);
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(source[24..]);
        uint depth = BinaryPrimitives.ReadUInt32LittleEndian(source[28..]);
        uint layerCount = BinaryPrimitives.ReadUInt32LittleEndian(source[32..]);
        uint faceCount = BinaryPrimitives.ReadUInt32LittleEndian(source[36..]);
        uint levelCount = BinaryPrimitives.ReadUInt32LittleEndian(source[40..]);
        if (width == 0 || height == 0 || depth != 0 || layerCount != 0 || faceCount != 1)
        {
            throw new InvalidDataException(
                "Standalone material KTX2 loading requires one non-array, non-cubemap 2D texture.");
        }
        uint normalizedLevelCount = Math.Max(1u, levelCount);
        uint maximumLevelCount = checked((uint)System.Numerics.BitOperations.Log2(
            Math.Max(width, height)) + 1);
        if (normalizedLevelCount > maximumLevelCount)
        {
            throw new InvalidDataException("The KTX2 mip level count is invalid for its dimensions.");
        }
        return new Ktx2Header(width, height, normalizedLevelCount);
    }

    private static void ValidateOptions(Ktx2TextureLoadOptions options)
    {
        if (options.MaximumSourceByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaximumSourceByteCount),
                options.MaximumSourceByteCount,
                "The maximum KTX2 source byte count must be positive.");
        }
        if (options.MaximumOutputByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaximumOutputByteCount),
                options.MaximumOutputByteCount,
                "The maximum KTX2 output byte count must be positive.");
        }
        if (!Enum.IsDefined(options.Content))
        {
            throw new ArgumentOutOfRangeException(nameof(options.Content));
        }
        if (!Enum.IsDefined(options.Preference))
        {
            throw new ArgumentOutOfRangeException(nameof(options.Preference));
        }
        if (options.Preference == Ktx2TextureLoadPreference.PreferGpuCompressed &&
            options.GraphicsCapabilities is null)
        {
            throw new ArgumentException(
                "GPU-compressed KTX2 loading requires explicit graphics capabilities.",
                nameof(options));
        }
    }

    private static void ValidateSourceSize(ReadOnlyMemory<byte> source, int maximumSourceByteCount)
    {
        if (source.IsEmpty)
        {
            throw new ArgumentException("KTX2 source cannot be empty.", nameof(source));
        }
        if (source.Length > maximumSourceByteCount)
        {
            throw new InvalidDataException(
                $"The KTX2 source exceeds the configured {maximumSourceByteCount} byte limit.");
        }
    }

    private static async Task<MemoryStream> ReadBoundedAsync(
        Stream source,
        int maximumByteCount,
        CancellationToken cancellationToken)
    {
        int initialCapacity = GetInitialCapacity(source, maximumByteCount);
        MemoryStream buffered = new(initialCapacity);
        byte[] readBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        try
        {
            while (true)
            {
                int read = await source.ReadAsync(
                    readBuffer.AsMemory(0, ReadBufferSize),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (read == 0)
                {
                    break;
                }
                if (buffered.Length > maximumByteCount - read)
                {
                    throw new InvalidDataException(
                        $"The KTX2 source exceeds the configured {maximumByteCount} byte limit.");
                }
                buffered.Write(readBuffer, 0, read);
            }
            return buffered;
        }
        catch
        {
            buffered.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
        }
    }

    private static int GetInitialCapacity(Stream source, int maximumByteCount)
    {
        if (source.CanSeek)
        {
            try
            {
                long remaining = source.Length - source.Position;
                if (remaining > maximumByteCount)
                {
                    throw new InvalidDataException(
                        $"The KTX2 source exceeds the configured {maximumByteCount} byte limit.");
                }
                if (remaining > 0)
                {
                    return checked((int)remaining);
                }
            }
            catch (NotSupportedException)
            {
            }
        }
        return Math.Min(ReadBufferSize, maximumByteCount);
    }

    private static ReadOnlyMemory<byte> AsMemory(MemoryStream stream) =>
        stream.GetBuffer().AsMemory(0, checked((int)stream.Length));

    private readonly record struct Ktx2Header(uint Width, uint Height, uint LevelCount);
}
