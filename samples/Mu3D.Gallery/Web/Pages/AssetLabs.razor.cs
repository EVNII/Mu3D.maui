using System.Numerics;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.GalleryApp.Pages;
using Mu3D.GalleryApp.Web.Infrastructure;
using Mu3D.GalleryApp.Web.Shared;
using Mu3D.Graphics;
using Mu3D.Native.Ktx;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Web.Pages;

/// <summary>Hosts the actual Gallery glTF and optional native-codec examples in the browser.</summary>
public partial class AssetLabs
{
    private string feature = "", status = "Ready", details = "", performance = "";
    private bool disposed, busy, retainSources, playing = true, loop = true;
    private int generation, variantIndex, clipIndex, leftVariant = 1, rightVariant = 2, modelIndex;
    private float animationTime;
    private CancellationTokenSource? loading;
    private Task? loadingTask;
    private IPresentationSurfaceSession? surface;
    private GalleryCanvas? viewport;
    private GltfAsset? asset;
    private GltfScenePresenter? gltfPresenter;
    private KtxTextureLabPresenter? ktxPresenter;
    private ModelLabPresenter? modelPresenter;
    private SceneRenderer? instanceRenderer;
    private Scene? instanceScene;
    private PerspectiveCamera? instanceCamera;
    private GltfSceneInstance? leftInstance, rightInstance;
    private Ktx2TextureAsset? decodedKtx;
    private readonly GltfExternalResourceCache externalCache = new(64 * 1024);
    private readonly Ktx2SourceCache ktxCache = new(128 * 1024);
    private bool HasCanvas => feature is not ("hdr-jpeg" or "ktx2-decode");
    private string Title => feature switch
    {
        "gltf-loading" => "glTF Loading", "gltf-instances" => "glTF Instances",
        "gltf-animation" => "glTF Animation", "gltf-material-variants" => "glTF Variants",
        "hdr-jpeg" => "HDR JPEG Lab", "ktx2-decode" => "KTX Decode",
        "ktx2-gpu-sampling" => "KTX Texture Lab", _ => "Material Conformance",
    };
    private float Duration => asset is null || asset.Animations.Count == 0 ? 1f :
        Math.Max(.001f, feature == "material-conformance" && loadedModel?.ApplyAllAnimations == true
            ? asset.Animations.Max(static animation => animation.Duration) : asset.Animations[clipIndex].Duration);

    protected override async Task OnParametersSetAsync()
    {
        GalleryAssets.Configure(new Uri(Navigation.BaseUri));
        string next = new Uri(Navigation.Uri).AbsolutePath.TrimEnd('/').Split('/')[^1];
        if (next == feature) return;
        generation++; loading?.Cancel(); DisposePresenters(); surface = null;
        feature = next; status = "Ready"; details = ""; animationTime = 0; busy = false;
        await Task.CompletedTask;
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!disposed && viewport is not null) await viewport.InvalidateAsync();
    }
    private async Task ConnectAsync(IPresentationSurfaceSession session)
    { surface = session; await ReloadAsync(); }
    private Task ReloadAsync() => loadingTask = LoadAsync();
    private async Task LoadAsync()
    {
        loading?.Cancel();
        CancellationTokenSource operation = new();
        loading = operation;
        int version = ++generation;
        string currentFeature = feature;
        busy = true; status = "Loading…";
        await InvokeAsync(StateHasChanged);
        try
        {
            if (currentFeature == "hdr-jpeg")
            {
                // Yield once so the busy state paints before the finite synchronous codec stage.
                await Task.Yield();
                operation.Token.ThrowIfCancellationRequested();
                HdrJpegRoundTripResult result = HdrJpegRoundTrip.Run();
                if (!Current(version, operation)) return;
                details = result.Report; status = "HDR JPEG round trip complete";
            }
            else if (currentFeature == "ktx2-decode") await DecodeKtxAsync(version, operation);
            else if (surface is not null)
            {
                if (currentFeature == "ktx2-gpu-sampling") await LoadKtxSamplingAsync(version, operation);
                else if (currentFeature == "material-conformance") await LoadModelAsync(version, operation);
                else await LoadGltfAsync(version, operation);
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        { if (version == generation && !disposed) status = "Loading cancelled"; }
        catch (Exception error)
        { if (Current(version, operation)) { status = "Loading failed"; details = error.ToString(); } }
        finally
        {
            if (version == generation && ReferenceEquals(loading, operation)) { loading = null; busy = false; }
            operation.Dispose();
            if (!disposed) await InvokeAsync(StateHasChanged);
        }
    }
    private bool Current(int version, CancellationTokenSource operation) =>
        !disposed && version == generation && ReferenceEquals(loading, operation) && !operation.IsCancellationRequested;
    private void CancelLoad() => loading?.Cancel();
    private void ClearCache()
    {
        externalCache.Clear(); ktxCache.Clear();
        status = "Source cache cleared; the loaded scene/image remains available";
        if (feature == "ktx2-decode" && decodedKtx is not null) details = KtxDetails(decodedKtx, 0, 0);
    }
    private void Draw(GraphicsDevice device, GraphicsTexture target, GraphicsTexture? depth, double delta)
    {
        if (disposed) return;
        uint width = target.Descriptor.Size.Width, height = target.Descriptor.Size.Height;
        if (playing && asset?.Animations.Count > 0)
        {
            animationTime += (float)delta;
            if (feature == "material-conformance" && loop) animationTime %= Duration;
            if (!loop && animationTime >= Duration) { animationTime = Duration; playing = false; }
        }
        if (modelPresenter is not null)
        {
            modelPresenter.Render(target, width, height, animationTime, loop ? AnimationWrapMode.Loop : AnimationWrapMode.Clamp);
            UpdateModelPerformance(delta, width, height);
        }
        else if (gltfPresenter is not null) gltfPresenter.Render(target, width, height,
            feature == "gltf-animation" ? clipIndex : null, animationTime);
        else if (ktxPresenter is not null) ktxPresenter.Render(target, width, height);
        else if (instanceScene is not null && instanceCamera is not null)
        {
            instanceCamera.AspectRatio = (float)width / height;
            instanceRenderer ??= new SceneRenderer(device, target.Descriptor.Format);
            instanceRenderer.Render(instanceScene, instanceCamera, target,
                depth ?? throw new InvalidOperationException("The Gallery instance target requires depth."),
                new LinearRgba(.012f, .018f, .035f, 1f, StandardColorSpaces.LinearSrgb));
        }
        else
        {
            using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("Gallery asset loading");
            using (encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(target, clearColor: new(0, 0, 0, 1))))) { }
            using GraphicsCommandBuffer commands = encoder.Finish(); device.Queue.Submit(commands);
        }
    }
    private void ChangeClip()
    {
        animationTime = 0;
        if (modelPresenter is not null) { modelPresenter.AnimationIndex = clipIndex; playing = true; }
    }
    private void ChangePlayback()
    { if (feature == "material-conformance" && playing && animationTime >= Duration) animationTime = 0; }
    private void ChangeLoop()
    { if (loop && animationTime >= Duration) animationTime = 0; }
    private void Replay() { animationTime = 0; playing = true; }
    private void ApplyVariants()
    {
        if (modelPresenter is not null) modelPresenter.ApplyMaterialVariant(variantIndex == 0 ? null : variantIndex - 1);
        else if (leftInstance is not null && rightInstance is not null)
        {
            leftInstance.ApplyMaterialVariant(leftVariant == 0 ? null : leftVariant - 1);
            rightInstance.ApplyMaterialVariant(rightVariant == 0 ? null : rightVariant - 1);
        }
        else asset?.ApplyMaterialVariant(variantIndex == 0 ? null : variantIndex - 1);
    }
    private void DisposePresenters()
    {
        gltfPresenter?.Dispose(); ktxPresenter?.Dispose(); modelPresenter?.Dispose(); instanceRenderer?.Dispose();
        gltfPresenter = null; ktxPresenter = null; modelPresenter = null; instanceRenderer = null;
        asset = null; instanceScene = null; instanceCamera = null; leftInstance = null; rightInstance = null; loadedModel = null;
    }
    private void Disconnect()
    { generation++; loading?.Cancel(); DisposePresenters(); surface = null; }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        disposed = true; generation++; loading?.Cancel();
        if (loadingTask is not null) await loadingTask;
        if (viewport is not null) await viewport.DisposeAsync();
        DisposePresenters();
    }
}
