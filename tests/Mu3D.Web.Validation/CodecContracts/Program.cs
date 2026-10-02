using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.Graphics;
using Mu3D.GalleryApp.Pages;
using Mu3D.Native.Ktx;
using Mu3D.Native.Ktx.Interop;
using Mu3D.Native.UltraHdr;
using Mu3D.Native.UltraHdr.Interop;
using Mu3D.SceneGraph;
using Mu3D.Web.Validation;

[assembly: SupportedOSPlatform("browser")]

if (RuntimeInformation.ProcessArchitecture != Architecture.Wasm || IntPtr.Size != 4)
    throw new InvalidOperationException("Codec contracts require actual wasm32 execution.");

// These values were measured by compiling against the pinned C headers in wasm32,
// rather than copied from desktop ABI layouts.
Check(Marshal.SizeOf<UltraHdrErrorInfo>() == 264, "UltraHDR error-result ABI");
Check(Marshal.SizeOf<UltraHdrRawImage>() == 48, "UltraHDR raw-image ABI");
Check(Marshal.SizeOf<UltraHdrCompressedImage>() == 24, "UltraHDR compressed-image ABI");
Check((int)Marshal.OffsetOf<UltraHdrRawImage>("Plane0") == 24, "UltraHDR plane offset");
Check((int)Marshal.OffsetOf<UltraHdrRawImage>(nameof(UltraHdrRawImage.Stride0)) == 36, "UltraHDR stride offset");
Check(Marshal.SizeOf<KtxTexture2>() == 112, "KTX2 ABI");
Check((int)Marshal.OffsetOf<KtxTexture2>(nameof(KtxTexture2.Data)) == 76, "KTX2 data offset");
Check((int)Marshal.OffsetOf<KtxTexture2>(nameof(KtxTexture2.VulkanFormat)) == 80, "KTX2 Vulkan-format offset");
Console.WriteLine("wasm32 C/managed codec layouts passed.");

IEncodedImageDecoder jpeg = CodecMemory.Track(new JpegImageDecoder());
HdrJpegRoundTripResult roundTrip = HdrJpegRoundTrip.Run();
byte[] hdrJpeg = roundTrip.Encoded;
LinearRgbaImage hdrDecoded = roundTrip.Decoded;
CodecMemory.Record(hdrDecoded.PixelStorage, hdrDecoded, "HDR JPEG round trip");
Check(hdrDecoded.Width == 128 && hdrDecoded.Height == 96, "actual Gallery HDR JPEG dimensions");
Check(roundTrip.Comparison.ActualPeakMagnitude > 1f, "actual linear HDR JPEG headroom");
bool rejected = false;
try { _ = jpeg.DecodeColor(new byte[] { 0xff, 0xd8, 0xff, 0xd9 }, "image/jpeg"); }
catch (InvalidDataException) { rejected = true; }
Check(rejected && jpeg.DecodeColor(hdrJpeg, "image/jpeg").Width == 128, "malformed JPEG recovery");
Console.WriteLine($"Actual UltraHDR encode/decode passed: {hdrJpeg.Length} bytes, linear peak {hdrDecoded.Pixels.Max(static p => p.X):F3}.");

byte[] colorBytes = ReadFixture("Color.ktx2");
byte[] normalBytes = ReadFixture("Normal.ktx2");
IEncodedImageDecoder ktx = CodecMemory.Track(new Ktx2ImageDecoder());
long beforeColorDecode = GC.GetAllocatedBytesForCurrentThread();
LinearRgbaImage color = ktx.DecodeColor(colorBytes, "image/ktx2");
long colorDecodeAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeColorDecode;
long beforeDataDecode = GC.GetAllocatedBytesForCurrentThread();
NormalizedRgbaDataImage normal = ktx.DecodeData(normalBytes, "image/ktx2");
long dataDecodeAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeDataDecode;
Check(color.Width == 1024 && color.Height == 1024, "actual ETC1S color fixture");
Check(normal.Width == 1024 && normal.Height == 1024, "actual UASTC normal fixture");
// One RGBA8 bridge and one FP32 output fit below 24 bytes/pixel; a second FP32 copy does not.
Check(colorDecodeAllocation < (long)color.Width * color.Height * 24 + 65536 &&
    dataDecodeAllocation < (long)normal.Width * normal.Height * 24 + 65536,
    "actual KTX decoding excludes a second FP32 image");
Console.WriteLine($"Actual KTX decode allocations: color {colorDecodeAllocation:N0}, data {dataDecodeAllocation:N0} bytes.");
Ktx2TextureTranscoder transcoder = new();
foreach (string family in new[] { "BC", "ETC2", "ASTC" })
{
    // Codec targets are explicit test inputs; these do not claim browser GPU support.
    GraphicsCapabilities capabilities = new()
    {
        SupportsFloat16Textures = true,
        SupportsShaderFloat16 = false,
        SupportsHdrSurface = false,
        SurfaceFormats = Array.Empty<PresentationFormat>(),
        SupportsBcTextureCompression = family == "BC",
        SupportsEtc2TextureCompression = family == "ETC2",
        SupportsAstcTextureCompression = family == "ASTC",
    };
    CompressedMaterialTexture compressedColor = transcoder.TryTranscode(colorBytes, "image/ktx2",
        CompressedMaterialTextureContent.Color, capabilities) ?? throw new InvalidOperationException(family);
    CompressedMaterialTexture? compressedNormal = transcoder.TryTranscode(normalBytes, "image/ktx2",
        CompressedMaterialTextureContent.Data, capabilities);
    Check(compressedColor.Width == 1024 && compressedColor.MipLevels.Count > 0, $"actual color Basis transcoding {family}");
    if (family == "ETC2") Check(compressedNormal is null, "native ETC2 non-color decoded-fallback policy");
    else Check(compressedNormal is { Width: 1024 } && compressedNormal.MipLevels.Count > 1, $"actual normal Basis transcoding {family}");
    Console.WriteLine($"Basis {family}: {compressedColor.Format}, normal {compressedNormal?.Format.ToString() ?? "explicit decoded fallback"}, {compressedNormal?.MipLevels.Count ?? 0} compressed normal mips.");
}
Ktx2SourceCache cache = new(128 * 1024);
int resolverCalls = 0;
ValueTask<Stream> Resolve(CancellationToken token)
{
    token.ThrowIfCancellationRequested();
    resolverCalls++;
    return ValueTask.FromResult<Stream>(new MemoryStream(colorBytes, writable: false));
}
Ktx2TextureLoadOptions loadOptions = new()
{
    MaximumSourceByteCount = 128 * 1024,
    MaximumOutputByteCount = 32 * 1024 * 1024,
    RetainEncodedSource = true,
};
Ktx2TextureAsset loaded = await Ktx2TextureLoader.LoadAsync("actual/color", Resolve, cache, loadOptions);
_ = await Ktx2TextureLoader.LoadAsync("actual/color", Resolve, cache, loadOptions);
Check(resolverCalls == 1 && cache.Count == 1, "KTX source cache hit");
Check(loaded.RetainedEncodedSource.Span.SequenceEqual(colorBytes), "exact KTX retained bytes");
cache.Clear();
Check(loaded.RetainedEncodedSource.Span.SequenceEqual(colorBytes), "cache clear preserves loaded asset");
using CancellationTokenSource cancelled = new();
cancelled.Cancel();
bool cancellationObserved = false;
try { _ = await Ktx2TextureLoader.LoadAsync("actual/color", Resolve, cache, loadOptions, cancelled.Token); }
catch (OperationCanceledException) { cancellationObserved = true; }
Check(cancellationObserved && resolverCalls == 1, "KTX cancellation before resolver");
Console.WriteLine("Actual KTX decode/transcode/cache/retention/cancellation passed.");

WeakReference[] retiredExamples = VerifyGalleryInstancesAndAnimation(jpeg);
Console.WriteLine($"Retired examples managed heap: {await CollectRetiredAsync(retiredExamples):N0} bytes.");
Check(ModelLabCatalog.Models.Length == 24, "full native Advanced catalog");
GraphicsCapabilities matrixCapabilities = new()
{
    SupportsFloat16Textures = true, SupportsShaderFloat16 = false, SupportsHdrSurface = false,
    SurfaceFormats = Array.Empty<PresentationFormat>(), SupportsBcTextureCompression = true,
};
foreach (ModelSpec spec in ModelLabCatalog.Models)
{
    // A gallery selection owns one model, not all24 graphs in an AOT main frame.
    // Return only its summary before collecting retired fixture CPU images/geometry.
    var retired = VerifyModel(spec, jpeg, matrixCapabilities);
    Console.WriteLine(retired.Summary);
    Console.WriteLine($"Retired model managed heap: {await CollectRetiredAsync([retired.Asset]):N0} bytes.");
}

foreach (string name in new[] { "Hdri/studio_small_02_1k.hdr", "Hdri/artist_workshop_1k.hdr" })
{
    using MemoryStream stream = new(ReadFixture(name), writable: false);
    EquirectangularHdrEnvironment environment = RadianceHdrReader.Read(stream, StandardColorSpaces.LinearSrgb);
    Check(environment.Width == 1024 && environment.Height == 512, $"actual Gallery HDRI {name}");
}
Console.WriteLine("All24 actual native Model Lab imports/animation/variants and both HDRI sources passed; GPU rendering is a separate gate.");
Console.WriteLine("Mu3D WASM codec contracts passed.");
return 0;

static byte[] ReadFixture(string name)
{
    using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ??
        throw new InvalidOperationException($"Missing actual Gallery fixture {name}.");
    using MemoryStream copy = new();
    stream.CopyTo(copy);
    return copy.ToArray();
}
static void Check(bool condition, string contract)
{
    if (!condition) throw new InvalidOperationException($"WASM codec contract failed: {contract}.");
}

[MethodImpl(MethodImplOptions.NoInlining)]
static async Task<long> CollectRetiredAsync(WeakReference[] references)
{
    // Collect outside the large import/Main native frame: conservative AOT stack roots
    // can otherwise retain retired images even when their owning asset is unreachable.
    await Task.Delay(1);
    GC.Collect();
    Check(references.All(static reference => !reference.IsAlive), "retired Gallery assets released");
    return GC.GetTotalMemory(false);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference[] VerifyGalleryInstancesAndAnimation(IEncodedImageDecoder jpeg)
{
    GltfAsset shoe = GltfImporter.ImportAsset(ReadFixture("GltfSamples/MaterialsVariantsShoe.glb"), imageDecoder: jpeg);
    Check(shoe.MaterialVariants.Count == 3, "actual authored shoe variants");
    var (_, left, right) = GltfInstancesExample.Create(shoe);
    left.ApplyMaterialVariant(0);
    right.ApplyMaterialVariant(0);
    Mesh leftMesh = left.Root.EnumerateDepthFirst().OfType<Mesh>().First();
    Mesh rightMesh = right.Root.EnumerateDepthFirst().OfType<Mesh>().First();
    Check(ReferenceEquals(leftMesh.Geometry, rightMesh.Geometry), "glTF shared geometry");
    Check(!ReferenceEquals(leftMesh.Material, rightMesh.Material), "glTF isolated instance materials");
    right.ApplyMaterialVariant(1);
    Check(left.ActiveMaterialVariantIndex == 0 && right.ActiveMaterialVariantIndex == 1, "glTF independent variant state");
    GltfAsset fox = GltfImporter.ImportAsset(ReadFixture("GltfSamples/Fox.glb"));
    Check(fox.Animations.Count == 3 && fox.Scene.Root.EnumerateDepthFirst().OfType<Mesh>().Any(static mesh => mesh.Skin is not null),
        "actual Fox clips and skin");
    foreach (AnimationClip clip in fox.Animations) clip.Apply(clip.Duration / 2f, AnimationWrapMode.Loop);
    Console.WriteLine("Actual Gallery JPEG shoe variants/instances and Fox skin/animation imports passed.");
    return [new(shoe), new(fox)];
}

[MethodImpl(MethodImplOptions.NoInlining)]
static (string Summary, WeakReference Asset) VerifyModel(ModelSpec spec, IEncodedImageDecoder jpeg, GraphicsCapabilities matrixCapabilities)
{

    Dictionary<string, ReadOnlyMemory<byte>> external = new(StringComparer.Ordinal);
    string directory = spec.PackagePath[..(spec.PackagePath.LastIndexOf('/') + 1)];
    foreach (string uri in spec.ExternalResources ?? []) external.Add(uri, ReadFixture(directory + uri));
    ReadOnlyMemory<byte> ResolveModelResource(string uri) => external.TryGetValue(uri, out var bytes) ? bytes :
        throw new InvalidDataException($"Undeclared actual Model Lab resource: {uri}.");
    GltfAsset imported = GltfImporter.ImportAssetWithOptions(ReadFixture(spec.PackagePath), new GltfImportOptions
    {
        Name = spec.SceneName, ExternalBufferResolver = ResolveModelResource, ExternalImageResolver = ResolveModelResource,
        ImageDecoder = spec.UseKtx ? CodecMemory.Track(new Ktx2ImageDecoder(jpeg, allowBt709PrimariesForLinearData: true)) : jpeg,
        TextureTranscoder = spec.UseKtx ? new Ktx2TextureTranscoder(allowBt709PrimariesForLinearData: true) : null,
        GraphicsCapabilities = spec.UseKtx ? matrixCapabilities : null,
    });
    if (spec.AddAmbientOcclusionProbe) ModelLabAssets.AddAmbientOcclusionProbe(imported, spec.CameraTarget);
    if (spec.AddDiagnosticGround) ModelLabAssets.AddDiagnosticGround(imported);
    ModelResourceCounts counts = ModelLabAssets.CountResources(imported);
    Check(counts.Meshes > 0, $"actual Model Lab {spec.DisplayName} scene");
    foreach (AnimationClip clip in imported.Animations) clip.Apply(clip.Duration / 2f, AnimationWrapMode.Loop);
    foreach (GltfMaterialVariant variant in imported.MaterialVariants) imported.ApplyMaterialVariant(variant.Index);
    imported.ApplyMaterialVariant((int?)null);
    return ($"Model {spec.DisplayName}: {counts.Meshes} meshes, {imported.Animations.Count} clips, {counts.CompressedFormats}.", new(imported));
}

namespace Mu3D.Web.Validation
{
    internal static partial class CodecMemory
    {
        private static readonly List<(WeakReference Storage, WeakReference Image, long Bytes, string Label)> decodedStorage = [];

        internal static IEncodedImageDecoder Track(IEncodedImageDecoder decoder) => new TrackingDecoder(decoder);
        internal static void Record(Array storage, object image, string? name) =>
            decodedStorage.Add((new WeakReference(storage), new WeakReference(image), storage.Length * 16L, name ?? "decoded image"));

        // The JS host calls this only after Main returns, with no importer native frame on the stack.
        [JSExport]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string CollectAndReport()
        {
            // Keep GC outside formatting/iterator frames whose unused AOT pin slots can
            // conservatively retain an old image. Enter the reporting frame only afterwards.
            GC.Collect();
            return ReportAndAssert();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ReportAndAssert()
        {
            if (decodedStorage.Count == 0) throw new InvalidOperationException("No actual decoded arrays were tracked.");
            if (decodedStorage.Any(static item => item.Image.IsAlive))
                throw new InvalidOperationException("Retired codec image owners remain alive.");
            long retained = decodedStorage.Sum(static item => item.Storage.IsAlive ? item.Bytes : 0);
            long bytes = GC.GetTotalMemory(false);
            Console.WriteLine($"Retired decoder images: {decodedStorage.Count}, all released; managed heap {bytes:N0} bytes; " +
                $"conservatively retained array bytes {retained:N0} ({string.Join(", ", decodedStorage.Where(static item => item.Storage.IsAlive).Select(static item => item.Label))}).");
            // A conservative stack may pin a raw large array even after its image owner dies.
            // Keep weak storage records across both cycles so the runner checks growth explicitly.
            return FormattableString.Invariant($"{{\"managedBytes\":{bytes},\"retainedArrayBytes\":{retained},\"trackedImages\":{decodedStorage.Count}}}");
        }

        private sealed class TrackingDecoder(IEncodedImageDecoder decoder) : IEncodedImageDecoder
        {
            public LinearRgbaImage DecodeColor(ReadOnlyMemory<byte> source, string mimeType, string? name = null)
            {
                LinearRgbaImage image = decoder.DecodeColor(source, mimeType, name);
                Record(image.PixelStorage, image, name);
                return image;
            }

            public NormalizedRgbaDataImage DecodeData(ReadOnlyMemory<byte> source, string mimeType, string? name = null)
            {
                NormalizedRgbaDataImage image = decoder.DecodeData(source, mimeType, name);
                Record(image.TexelStorage, image, name);
                return image;
            }
        }
    }
}
