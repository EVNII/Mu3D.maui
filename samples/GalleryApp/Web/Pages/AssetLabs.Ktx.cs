using System.Diagnostics;
using Mu3D.GalleryApp.Pages;
using Mu3D.Native.Ktx;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Web.Pages;

public partial class AssetLabs
{
    private async Task DecodeKtxAsync(int version, CancellationTokenSource operation)
    {
        int opens = 0;
        async ValueTask<Stream> Resolve(CancellationToken token)
        {
            byte[] source = await GalleryAssets.ReadBytesAsync("KtxSamples/color_grid_basis.ktx2", token);
            opens++;
            return new MemoryStream(source, writable: false);
        }
        Stopwatch timer = Stopwatch.StartNew();
        Ktx2TextureAsset result = await Ktx2TextureLoader.LoadAsync("package/KtxSamples/color_grid_basis.ktx2",
            Resolve, ktxCache, new Ktx2TextureLoadOptions
            {
                MaximumSourceByteCount = 128 * 1024,
                MaximumOutputByteCount = 32 * 1024 * 1024,
                RetainEncodedSource = retainSources,
                Name = "Khronos ETC1S color grid",
            }, operation.Token);
        if (!Current(version, operation)) return;
        decodedKtx = result; details = KtxDetails(result, opens, timer.Elapsed.TotalMilliseconds); status = "Decoded";
    }
    private string KtxDetails(Ktx2TextureAsset value, int opens, double milliseconds) =>
        $"{value.Width} × {value.Height} · {value.Representation}\n" +
        $"Output: {value.OutputByteCount:N0} bytes / 32 MiB limit\n" +
        $"Retained encoded source: {value.RetainedEncodedSource.Length:N0} bytes\n" +
        $"Resolver this load: {opens} opens\nSource cache: {ktxCache.Count} entries, {ktxCache.TotalByteCount:N0} bytes / 128 KiB capacity\n" +
        $"Decode time: {milliseconds:F1} ms\nColor space: {value.DecodedColorImage?.ColorSpace.Name}";
    private async Task LoadKtxSamplingAsync(int version, CancellationTokenSource operation)
    {
        byte[] color = await GalleryAssets.ReadBytesAsync("KtxSamples/color_grid_basis.ktx2", operation.Token);
        byte[] normal = await GalleryAssets.ReadBytesAsync("KtxSamples/StainedGlassLamp_grill_normal.ktx2", operation.Token);
        if (!Current(version, operation) || surface is null) return;
        var capabilities = surface.Device.Capabilities;
        Ktx2TextureTranscoder transcoder = new();
        Ktx2ImageDecoder decoder = new();
        KtxTextureLabTextures textures = new(
            transcoder.TryTranscode(color, "image/ktx2", CompressedMaterialTextureContent.Color, capabilities, "Khronos ETC1S color grid"),
            decoder.DecodeColor(color, "image/ktx2", "Khronos ETC1S color grid"),
            transcoder.TryTranscode(normal, "image/ktx2", CompressedMaterialTextureContent.Data, capabilities, "Khronos UASTC normal"),
            decoder.DecodeData(normal, "image/ktx2", "Khronos UASTC normal"));
        if (!Current(version, operation)) return;
        DisposePresenters(); ktxPresenter = new KtxTextureLabPresenter(surface.Device, textures);
        status = "Decoded and GPU-compressed textures ready";
        details = $"GPU compression: ASTC={capabilities.SupportsAstcTextureCompression}, BC={capabilities.SupportsBcTextureCompression}, ETC2={capabilities.SupportsEtc2TextureCompression}\n" +
            $"Color: {Describe(textures.CompressedColor)}\nNormal: {Describe(textures.CompressedNormal)}\n" +
            "Left: decoded RGBA8 · Right: GPU-compressed\nTop: color · Bottom: color + normal";
    }
    private static string Describe(CompressedMaterialTexture? texture) => texture is null
        ? "compressed target unavailable; decoded fallback used"
        : $"{texture.Format}, {texture.Width}x{texture.Height}, {texture.MipLevels.Count} mips";
}
