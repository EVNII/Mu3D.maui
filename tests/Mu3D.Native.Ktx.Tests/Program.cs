using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.Graphics;
using Mu3D.Native.Ktx;
using Mu3D.Native.Ktx.Interop;
using Mu3D.SceneGraph;

List<string> failures = [];

GraphicsCapabilities capabilities = CreateCapabilities(astc: true, bc: true, etc2: true);
KtxTranscodeTarget? astcColor = Ktx2TextureTranscoder.SelectTarget(
    CompressedMaterialTextureContent.Color,
    capabilities);
KtxTranscodeTarget? astcData = Ktx2TextureTranscoder.SelectTarget(
    CompressedMaterialTextureContent.Data,
    capabilities);
Expect(
    astcColor is
    {
        NativeFormat: KtxTranscodeFormat.Astc4x4Rgba,
        GraphicsFormat: GraphicsTextureFormat.Astc4x4UnormSrgb
    } &&
    astcData is
    {
        NativeFormat: KtxTranscodeFormat.Astc4x4Rgba,
        GraphicsFormat: GraphicsTextureFormat.Astc4x4Unorm
    },
    "ASTC target selection and color transfer identity",
    failures);

KtxTranscodeTarget? bcColor = Ktx2TextureTranscoder.SelectTarget(
    CompressedMaterialTextureContent.Color,
    CreateCapabilities(astc: false, bc: true, etc2: true));
KtxTranscodeTarget? bcData = Ktx2TextureTranscoder.SelectTarget(
    CompressedMaterialTextureContent.Data,
    CreateCapabilities(astc: false, bc: true, etc2: true));
Expect(
    bcColor is
    {
        NativeFormat: KtxTranscodeFormat.Bc7Rgba,
        GraphicsFormat: GraphicsTextureFormat.Bc7RgbaUnormSrgb
    } &&
    bcData is
    {
        NativeFormat: KtxTranscodeFormat.Bc7Rgba,
        GraphicsFormat: GraphicsTextureFormat.Bc7RgbaUnorm
    },
    "BC7 target selection and color transfer identity",
    failures);

KtxTranscodeTarget? etcColor = Ktx2TextureTranscoder.SelectTarget(
    CompressedMaterialTextureContent.Color,
    CreateCapabilities(astc: false, bc: false, etc2: true));
KtxTranscodeTarget? etcData = Ktx2TextureTranscoder.SelectTarget(
    CompressedMaterialTextureContent.Data,
    CreateCapabilities(astc: false, bc: false, etc2: true));
Expect(
    etcColor is
    {
        NativeFormat: KtxTranscodeFormat.Etc2Rgba,
        GraphicsFormat: GraphicsTextureFormat.Etc2Rgba8UnormSrgb
    } &&
    etcData is null,
    "ETC2 color target and high-quality data fallback policy",
    failures);

Expect(
    Ktx2TextureTranscoder.SelectTarget(
        CompressedMaterialTextureContent.Color,
        CreateCapabilities(astc: false, bc: false, etc2: false)) is null,
    "no compressed target returns decoded fallback",
    failures);

Expect(
    (int)KtxTranscodeFormat.Etc2Rgba == 1 &&
    (int)KtxTranscodeFormat.Bc7Rgba == 6 &&
    (int)KtxTranscodeFormat.Astc4x4Rgba == 10,
    "libktx v4.4.2 transcode enum ABI",
    failures);
if (Environment.Is64BitProcess)
{
    Expect(
        Marshal.SizeOf<KtxTexture2>() == 168 &&
        Marshal.OffsetOf<KtxTexture2>(nameof(KtxTexture2.BaseWidth)).ToInt32() == 36 &&
        Marshal.OffsetOf<KtxTexture2>(nameof(KtxTexture2.NumLevels)).ToInt32() == 52 &&
        Marshal.OffsetOf<KtxTexture2>(nameof(KtxTexture2.KvDataHead)).ToInt32() == 80 &&
        Marshal.OffsetOf<KtxTexture2>(nameof(KtxTexture2.Data)).ToInt32() == 112,
        "libktx v4.4.2 64-bit texture ABI layout",
        failures);
}

Ktx2TextureTranscoder transcoder = new();
ExpectThrows<ArgumentException>(
    () => transcoder.TryTranscode(
        ReadOnlyMemory<byte>.Empty,
        "image/ktx2",
        CompressedMaterialTextureContent.Color,
        capabilities),
    "empty KTX2 rejection",
    failures);
ExpectThrows<NotSupportedException>(
    () => transcoder.TryTranscode(
        new byte[] { 0 },
        "image/png",
        CompressedMaterialTextureContent.Color,
        capabilities),
    "non-KTX MIME rejection",
    failures);
TestFallbackDecoder fallbackDecoder = new();
Ktx2ImageDecoder routingDecoder = new(fallbackDecoder);
_ = routingDecoder.DecodeColor(new byte[] { 1 }, "image/jpeg", "delegated color");
_ = routingDecoder.DecodeData(new byte[] { 2 }, "image/jpeg", "delegated data");
Expect(
    fallbackDecoder.ColorCount == 1 && fallbackDecoder.DataCount == 1,
    "non-KTX decoder routing",
    failures);

Ktx2SourceCache ownershipCache = new(4);
byte[] mutableCacheSource = [1, 2, 3];
bool ownershipAdded = ownershipCache.TryAdd("texture", mutableCacheSource);
mutableCacheSource[0] = 9;
bool ownershipHit = ownershipCache.TryGet("texture", out ReadOnlyMemory<byte> ownedSource);
bool ownershipOverflowRejected = !ownershipCache.TryAdd("other", new byte[] { 4, 5 });
ownershipCache.Clear();
Expect(
    ownershipAdded && ownershipHit && ownedSource.Span.SequenceEqual(new byte[] { 1, 2, 3 }) &&
    ownershipOverflowRejected && ownershipCache.Count == 0 &&
    ownershipCache.TotalByteCount == 0 &&
    ownedSource.Span.SequenceEqual(new byte[] { 1, 2, 3 }),
    "KTX2 source cache owns bounded copies that survive explicit clearing",
    failures);

byte[] standaloneFixture = File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "color_grid_basis.ktx2"));
using MemoryStream outputLimitedStandaloneStream = new(standaloneFixture);
await ExpectThrowsAsync<InvalidDataException>(
    () => Ktx2TextureLoader.LoadAsync(
        outputLimitedStandaloneStream,
        new Ktx2TextureLoadOptions
        {
            MaximumSourceByteCount = 128 * 1024,
            MaximumOutputByteCount = 1024,
        }),
    "standalone KTX2 decoded-output byte limit before native processing",
    failures);
bool nativeKtxAvailable = NativeLibrary.TryLoad("ktx", out nint nativeKtxHandle);
if (nativeKtxHandle != 0)
{
    NativeLibrary.Free(nativeKtxHandle);
}
if (nativeKtxAvailable)
{
    Ktx2SourceCache standaloneCache = new(128 * 1024);
    int standaloneResolverOpenCount = 0;
    TrackingKtxStream? openedStandaloneStream = null;
    Ktx2TextureAsset decodedStandalone = await Ktx2TextureLoader.LoadAsync(
        "gallery/color-grid",
        cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            standaloneResolverOpenCount++;
            openedStandaloneStream = new TrackingKtxStream(standaloneFixture);
            return ValueTask.FromResult<Stream>(openedStandaloneStream);
        },
        standaloneCache,
        new Ktx2TextureLoadOptions
        {
            MaximumSourceByteCount = 128 * 1024,
            MaximumOutputByteCount = 32 * 1024 * 1024,
            Name = "standalone decoded color",
        });
    Expect(
        decodedStandalone.Representation == Ktx2TextureRepresentation.DecodedColor &&
        decodedStandalone.Content == CompressedMaterialTextureContent.Color &&
        decodedStandalone.DecodedColorImage is not null &&
        decodedStandalone.DecodedDataImage is null &&
        decodedStandalone.CompressedTexture is null &&
        decodedStandalone.Width == 1024 && decodedStandalone.Height == 1024 &&
        decodedStandalone.OutputByteCount == 1024L * 1024 * 16 &&
        !decodedStandalone.HasRetainedEncodedSource &&
        standaloneResolverOpenCount == 1 && openedStandaloneStream?.IsDisposed == true &&
        standaloneCache.Count == 1 && standaloneCache.TotalByteCount == standaloneFixture.Length,
        "bounded standalone KTX2 decode, cache fill, resolver ownership and default source release",
        failures);

    Ktx2TextureAsset retainedStandalone = await Ktx2TextureLoader.LoadAsync(
        "gallery/color-grid",
        _ => throw new InvalidOperationException("A warm KTX2 cache must not invoke its resolver."),
        standaloneCache,
        new Ktx2TextureLoadOptions
        {
            MaximumSourceByteCount = 128 * 1024,
            MaximumOutputByteCount = 32 * 1024 * 1024,
            RetainEncodedSource = true,
            Name = "retained standalone color",
        });
    Expect(
        retainedStandalone.Representation == Ktx2TextureRepresentation.DecodedColor &&
        retainedStandalone.HasRetainedEncodedSource &&
        retainedStandalone.RetainedEncodedSource.Span.SequenceEqual(standaloneFixture) &&
        standaloneResolverOpenCount == 1,
        "warm KTX2 source cache avoids resolution and supports independent asset retention",
        failures);

    await ExpectThrowsAsync<InvalidDataException>(
        () => Ktx2TextureLoader.LoadAsync(
            "gallery/color-grid",
            _ => throw new InvalidOperationException("A warm KTX2 cache must not invoke its resolver."),
            standaloneCache,
            new Ktx2TextureLoadOptions
            {
                MaximumSourceByteCount = 128 * 1024,
                MaximumOutputByteCount = 1024,
            }),
        "standalone KTX2 decoded-output byte limit",
        failures);

    using MemoryStream compressedStandaloneStream = new(standaloneFixture);
    Ktx2TextureAsset compressedStandalone = await Ktx2TextureLoader.LoadAsync(
        compressedStandaloneStream,
        new Ktx2TextureLoadOptions
        {
            MaximumSourceByteCount = 128 * 1024,
            MaximumOutputByteCount = 8 * 1024 * 1024,
            Preference = Ktx2TextureLoadPreference.PreferGpuCompressed,
            GraphicsCapabilities = CreateCapabilities(astc: false, bc: true, etc2: false),
            Name = "standalone BC7 color",
        });
    Expect(
        compressedStandaloneStream.CanRead &&
        compressedStandalone.Representation == Ktx2TextureRepresentation.GpuCompressed &&
        compressedStandalone.CompressedTexture?.Format == GraphicsTextureFormat.Bc7RgbaUnormSrgb &&
        compressedStandalone.DecodedColorImage is null &&
        compressedStandalone.OutputByteCount > 0 &&
        compressedStandalone.OutputByteCount <= 8 * 1024 * 1024,
        "standalone KTX2 caller-stream ownership and GPU-compressed output",
        failures);

    standaloneCache.Clear();
    Expect(
        standaloneCache.Count == 0 && standaloneCache.TotalByteCount == 0 &&
        retainedStandalone.RetainedEncodedSource.Span.SequenceEqual(standaloneFixture),
        "clearing application KTX2 cache does not change retained asset source",
        failures);
}

using CancellationTokenSource standaloneCancellation = new();
using CancellingKtxStream cancellingStandaloneStream = new(
    standaloneFixture,
    standaloneCancellation);
await ExpectThrowsAsync<OperationCanceledException>(
    () => Ktx2TextureLoader.LoadAsync(
        cancellingStandaloneStream,
        cancellationToken: standaloneCancellation.Token),
    "standalone KTX2 asynchronous read cancellation",
    failures);
Expect(
    cancellingStandaloneStream.CanRead,
    "cancelled standalone KTX2 caller stream remains open",
    failures);

string? fixtureDirectory = Environment.GetEnvironmentVariable("MU3D_KTX_FIXTURE_DIRECTORY");
if (!string.IsNullOrWhiteSpace(fixtureDirectory))
{
    byte[] colorFixture = File.ReadAllBytes(Path.Combine(
        fixtureDirectory,
        "color_grid_basis.ktx2"));
    byte[] dataFixture = File.ReadAllBytes(Path.Combine(
        fixtureDirectory,
        "StainedGlassLamp_grill_normal.ktx2"));
    ValidateRealColorTarget(
        transcoder,
        colorFixture,
        CreateCapabilities(astc: true, bc: false, etc2: false),
        GraphicsTextureFormat.Astc4x4UnormSrgb,
        "real ETC1S to ASTC",
        failures);
    ValidateRealColorTarget(
        transcoder,
        colorFixture,
        CreateCapabilities(astc: false, bc: true, etc2: false),
        GraphicsTextureFormat.Bc7RgbaUnormSrgb,
        "real ETC1S to BC7",
        failures);
    ValidateRealColorTarget(
        transcoder,
        colorFixture,
        CreateCapabilities(astc: false, bc: false, etc2: true),
        GraphicsTextureFormat.Etc2Rgba8UnormSrgb,
        "real ETC1S to ETC2",
        failures);
    ValidateRealDataTarget(
        transcoder,
        dataFixture,
        CreateCapabilities(astc: true, bc: false, etc2: true),
        GraphicsTextureFormat.Astc4x4Unorm,
        "real UASTC data to ASTC",
        failures);
    ValidateRealDataTarget(
        transcoder,
        dataFixture,
        CreateCapabilities(astc: false, bc: true, etc2: true),
        GraphicsTextureFormat.Bc7RgbaUnorm,
        "real UASTC data to BC7",
        failures);
    Expect(
        transcoder.TryTranscode(
            dataFixture,
            "image/ktx2",
            CompressedMaterialTextureContent.Data,
            CreateCapabilities(astc: false, bc: false, etc2: true),
            "official UASTC normal") is null,
        "real UASTC ETC2-only decoded fallback",
        failures);
    Ktx2ImageDecoder ktxDecoder = new();
    long beforeColorDecode = GC.GetAllocatedBytesForCurrentThread();
    LinearRgbaImage decodedColor = ktxDecoder.DecodeColor(
        colorFixture,
        "image/ktx2",
        "official ETC1S color");
    long colorDecodeAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeColorDecode;
    long beforeDataDecode = GC.GetAllocatedBytesForCurrentThread();
    NormalizedRgbaDataImage decodedData = ktxDecoder.DecodeData(
        dataFixture,
        "image/ktx2",
        "official UASTC normal");
    long dataDecodeAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeDataDecode;
    // One RGBA8 bridge plus one FP32 output is 20 bytes/pixel. Allow metadata overhead,
    // while rejecting another 16-byte/pixel copy of either actual decoded fixture.
    Expect(colorDecodeAllocation < (long)decodedColor.Width * decodedColor.Height * 24 + 65536 &&
        dataDecodeAllocation < (long)decodedData.Width * decodedData.Height * 24 + 65536,
        "real KTX decoder allocation excludes a second FP32 image", failures);
    Expect(
        decodedColor.Width > 0 && decodedColor.Height > 0 &&
        decodedColor.ColorSpace == StandardColorSpaces.LinearSrgb &&
        decodedColor.Pixels.All(IsNormalized) &&
        decodedData.Width > 0 && decodedData.Height > 0 &&
        decodedData.Texels.All(IsNormalized),
        "real ETC1S/UASTC RGBA8 decoded fallback",
        failures);

    byte[] basisGltf = CreateBasisGltf(colorFixture, dataFixture);
    PbrMaterial compressedMaterial = (PbrMaterial)
        GltfImporter.ImportWithOptions(
            basisGltf,
            new GltfImportOptions
            {
                ImageDecoder = ktxDecoder,
                TextureTranscoder = transcoder,
                GraphicsCapabilities = CreateCapabilities(astc: true, bc: false, etc2: false),
            }).Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material;
    Expect(
        compressedMaterial.BaseColorTexture is null &&
        compressedMaterial.NormalTexture is null &&
        compressedMaterial.CompressedBaseColorTexture?.Format ==
            GraphicsTextureFormat.Astc4x4UnormSrgb &&
        compressedMaterial.CompressedNormalTexture?.Format ==
            GraphicsTextureFormat.Astc4x4Unorm,
        "real glTF KHR_texture_basisu to ASTC material chain",
        failures);

    PbrMaterial mixedFallbackMaterial = (PbrMaterial)
        GltfImporter.ImportWithOptions(
            basisGltf,
            new GltfImportOptions
            {
                ImageDecoder = ktxDecoder,
                TextureTranscoder = transcoder,
                GraphicsCapabilities = CreateCapabilities(astc: false, bc: false, etc2: true),
            }).Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material;
    Expect(
        mixedFallbackMaterial.BaseColorTexture is null &&
        mixedFallbackMaterial.CompressedBaseColorTexture?.Format ==
            GraphicsTextureFormat.Etc2Rgba8UnormSrgb &&
        mixedFallbackMaterial.NormalTexture is not null &&
        mixedFallbackMaterial.CompressedNormalTexture is null,
        "real glTF ETC2 color plus decoded UASTC data fallback chain",
        failures);

    byte[] bt709LinearData = File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "AnisotropyBarnLamp_normalbump.ktx2"));
    ExpectThrows<InvalidDataException>(
        () => transcoder.TryTranscode(
            bt709LinearData,
            "image/ktx2",
            CompressedMaterialTextureContent.Data,
            CreateCapabilities(astc: true, bc: false, etc2: false),
            "strict BT709+linear data rejection"),
        "strict glTF data DFD rejects BT709 primaries",
        failures);
    Ktx2TextureTranscoder compatibleTranscoder = new(
        allowBt709PrimariesForLinearData: true);
    ValidateRealDataTarget(
        compatibleTranscoder,
        bt709LinearData,
        CreateCapabilities(astc: true, bc: false, etc2: false),
        GraphicsTextureFormat.Astc4x4Unorm,
        "explicit BT709+linear legacy-data compatibility",
        failures);
    NormalizedRgbaDataImage compatibleDecodedData = new Ktx2ImageDecoder(
        fallback: null,
        allowBt709PrimariesForLinearData: true).DecodeData(
            bt709LinearData,
            "image/ktx2",
            "compatible decoded normal");
    Expect(
        compatibleDecodedData.Width > 0 && compatibleDecodedData.Height > 0 &&
        compatibleDecodedData.Texels.All(IsNormalized),
        "explicit BT709+linear decoded fallback compatibility",
        failures);
}

if (failures.Count != 0)
{
    foreach (string failure in failures)
    {
        Console.Error.WriteLine($"FAILED: {failure}");
    }
    return 1;
}

Console.WriteLine(
    "Validated pinned libktx ABI, glTF KTX2 composition, and BC7/ETC2/ASTC policy.");
return 0;

static GraphicsCapabilities CreateCapabilities(bool astc, bool bc, bool etc2) => new()
{
    SupportsFloat16Textures = true,
    SupportsShaderFloat16 = true,
    SupportsHdrSurface = true,
    SupportsAstcTextureCompression = astc,
    SupportsBcTextureCompression = bc,
    SupportsEtc2TextureCompression = etc2,
    SurfaceFormats = [PresentationFormat.Rgba16Float],
};

static byte[] CreateBasisGltf(byte[] colorFixture, byte[] dataFixture)
{
    float[] positions = [-1f, -1f, 0f, 1f, -1f, 0f, 0f, 1f, 0f];
    byte[] meshBuffer = new byte[42];
    MemoryMarshal.AsBytes(positions.AsSpan()).CopyTo(meshBuffer);
    BinaryPrimitives.WriteUInt16LittleEndian(meshBuffer.AsSpan(36), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(meshBuffer.AsSpan(38), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(meshBuffer.AsSpan(40), 2);
    string json = $$$"""
        {
          "asset":{"version":"2.0"},
          "extensionsUsed":["KHR_texture_basisu"],
          "extensionsRequired":["KHR_texture_basisu"],
          "buffers":[{"byteLength":42,"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String(meshBuffer)}}}"}],
          "bufferViews":[
            {"buffer":0,"byteOffset":0,"byteLength":36},
            {"buffer":0,"byteOffset":36,"byteLength":6}
          ],
          "accessors":[
            {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
            {"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}
          ],
          "images":[
            {"uri":"data:image/ktx2;base64,{{{Convert.ToBase64String(colorFixture)}}}"},
            {"uri":"data:image/ktx2;base64,{{{Convert.ToBase64String(dataFixture)}}}"}
          ],
          "textures":[
            {"extensions":{"KHR_texture_basisu":{"source":0} } },
            {"extensions":{"KHR_texture_basisu":{"source":1} } }
          ],
          "materials":[{
            "pbrMetallicRoughness":{"baseColorTexture":{"index":0}},
            "normalTexture":{"index":1}
          }],
          "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1,"material":0}]}],
          "nodes":[{"mesh":0}],
          "scenes":[{"nodes":[0]}],
          "scene":0
        }
        """;
    return Encoding.UTF8.GetBytes(json);
}

static void ValidateRealColorTarget(
    Ktx2TextureTranscoder transcoder,
    byte[] fixture,
    GraphicsCapabilities capabilities,
    GraphicsTextureFormat expectedFormat,
    string name,
    List<string> failures)
{
    CompressedMaterialTexture? result = transcoder.TryTranscode(
        fixture,
        "image/ktx2",
        CompressedMaterialTextureContent.Color,
        capabilities,
        name);
    Expect(
        result is not null && result.Format == expectedFormat &&
        result.Content == CompressedMaterialTextureContent.Color &&
        result.ColorSpace is not null && result.MipLevels.Count > 0 &&
        result.MipLevels.All(static mip => !mip.Data.IsEmpty),
        name,
        failures);
}

static void ValidateRealDataTarget(
    Ktx2TextureTranscoder transcoder,
    byte[] fixture,
    GraphicsCapabilities capabilities,
    GraphicsTextureFormat expectedFormat,
    string name,
    List<string> failures)
{
    CompressedMaterialTexture? result = transcoder.TryTranscode(
        fixture,
        "image/ktx2",
        CompressedMaterialTextureContent.Data,
        capabilities,
        name);
    Expect(
        result is not null && result.Format == expectedFormat &&
        result.Content == CompressedMaterialTextureContent.Data &&
        result.ColorSpace is null && result.MipLevels.Count > 0 &&
        result.MipLevels.All(static mip => !mip.Data.IsEmpty),
        name,
        failures);
}

static bool IsNormalized(Vector4 value) =>
    value.X is >= 0 and <= 1 && value.Y is >= 0 and <= 1 &&
    value.Z is >= 0 and <= 1 && value.W is >= 0 and <= 1;

static void Expect(bool condition, string name, List<string> failures)
{
    if (!condition)
    {
        failures.Add(name);
    }
}

static void ExpectThrows<TException>(
    Action action,
    string name,
    List<string> failures)
    where TException : Exception
{
    try
    {
        action();
        failures.Add(name);
    }
    catch (TException)
    {
    }
}

static async Task ExpectThrowsAsync<TException>(
    Func<Task> action,
    string name,
    List<string> failures)
    where TException : Exception
{
    try
    {
        await action();
        failures.Add(name);
    }
    catch (TException)
    {
    }
}

sealed class TrackingKtxStream(byte[] source) : MemoryStream(source)
{
    internal bool IsDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}

sealed class CancellingKtxStream(
    byte[] source,
    CancellationTokenSource cancellation) : MemoryStream(source)
{
    private bool cancelled;

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int read = await base.ReadAsync(buffer, cancellationToken);
        if (!cancelled)
        {
            cancelled = true;
            cancellation.Cancel();
        }
        return read;
    }
}

sealed class TestFallbackDecoder : IEncodedImageDecoder
{
    internal int ColorCount { get; private set; }

    internal int DataCount { get; private set; }

    public LinearRgbaImage DecodeColor(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        _ = source;
        _ = mimeType;
        ColorCount++;
        return new LinearRgbaImage(1, 1, [Vector4.One], StandardColorSpaces.LinearSrgb, name);
    }

    public NormalizedRgbaDataImage DecodeData(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        _ = source;
        _ = mimeType;
        DataCount++;
        return new NormalizedRgbaDataImage(1, 1, [Vector4.One], name);
    }
}
