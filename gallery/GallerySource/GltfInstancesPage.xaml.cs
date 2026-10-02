using System.Numerics;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.Maui.Controls;
using Mu3D.Native.UltraHdr;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates one glTF definition supplying independent mutable instances.</summary>
public partial class GltfInstancesPage : ContentPage
{
    private readonly Scene scene = new("Reusable glTF instances");
    private readonly PerspectiveCamera camera = GltfInstancesExample.CreateCamera();
    private readonly SemaphoreSlim loadingGate = new(1, 1);
    private CancellationTokenSource? loadingCancellation;
    private Task? loadingTask;
    private int loadingVersion;
    private bool isPageVisible;
    private GltfSceneInstance? leftInstance;
    private GltfSceneInstance? rightInstance;

    /// <summary>Initializes the reusable glTF-instances example.</summary>
    public GltfInstancesPage()
    {
        InitializeComponent();
        SceneView.ClearColor = new LinearRgba(
            0.012f,
            0.018f,
            0.035f,
            1f,
            StandardColorSpaces.LinearSrgb);
        SceneView.Scene = scene;
        SceneView.Camera = camera;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        isPageVisible = true;
        if (leftInstance is not null)
        {
            SceneView.InvalidateScene();
            return;
        }
        if (loadingTask is null || loadingTask.IsCompleted)
        {
            StartLoading();
        }
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        isPageVisible = false;
        InvalidateCurrentLoad();
        base.OnDisappearing();
    }

    private void StartLoading()
    {
        CancelCurrentLoad();
        int version = ++loadingVersion;
        CancellationTokenSource operation = new();
        loadingCancellation = operation;
        loadingTask = LoadAsync(version, operation);
    }

    private async Task LoadAsync(int version, CancellationTokenSource operation)
    {
        bool gateEntered = false;
        try
        {
            await loadingGate.WaitAsync();
            gateEntered = true;
            if (!IsLoadCurrent(version, operation))
            {
                return;
            }

            // Page disappearance only invalidates this finite operation. Package copy, import and
            // IBL preparation complete without throwing at the MAUI navigation boundary.
            StatusLabel.Text = "Loading MaterialsVariantsShoe.glb once…";
            byte[] bytes = await GalleryAssets.ReadBytesAsync(
                "GltfSamples/MaterialsVariantsShoe.glb",
                CancellationToken.None);
            if (!IsLoadCurrent(version, operation))
            {
                return;
            }

            GltfAsset imported = await Task.Run(
                () => GltfImporter.ImportAsset(
                    bytes,
                    name: "Materials Variants Shoe",
                    imageDecoder: new JpegImageDecoder()));
            if (!IsLoadCurrent(version, operation))
            {
                return;
            }

            var (reusable, left, right) = GltfInstancesExample.Create(imported);

            var environment = await GalleryEnvironment.LoadStudioAsync(CancellationToken.None);
            if (!IsLoadCurrent(version, operation))
            {
                return;
            }

            GltfInstancesExample.AddLighting(scene, environment);
            left.AttachTo(scene);
            right.AttachTo(scene);
            leftInstance = left;
            rightInstance = right;

            string[] choices = new[] { "Default material" }
                .Concat(reusable.MaterialVariants.Select(static variant => variant.Name))
                .ToArray();
            LeftVariantPicker.ItemsSource = choices;
            RightVariantPicker.ItemsSource = choices;
            LeftVariantPicker.SelectedIndex = choices.Length > 1 ? 1 : 0;
            RightVariantPicker.SelectedIndex = choices.Length > 2 ? 2 : 0;
            UpdateStatus(bytes.Length);
            SceneView.InvalidateScene();
        }
        catch (Exception exception)
        {
            if (IsLoadCurrent(version, operation))
            {
                StatusLabel.Text = $"glTF instances failed: {exception.Message}";
            }
        }
        finally
        {
            if (gateEntered)
            {
                loadingGate.Release();
            }
            if (version == loadingVersion && ReferenceEquals(loadingCancellation, operation))
            {
                loadingCancellation = null;
            }
            operation.Dispose();
        }
    }

    private bool IsLoadCurrent(int version, CancellationTokenSource operation) =>
        isPageVisible &&
        !operation.IsCancellationRequested &&
        version == loadingVersion &&
        ReferenceEquals(loadingCancellation, operation);

    private void InvalidateCurrentLoad()
    {
        loadingVersion++;
        CancelCurrentLoad();
    }

    private void CancelCurrentLoad()
    {
        CancellationTokenSource? operation = loadingCancellation;
        loadingCancellation = null;
        loadingTask = null;
        operation?.Cancel();
    }

    private void OnLeftVariantChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyPickerSelection(leftInstance, LeftVariantPicker);
    }

    private void OnRightVariantChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyPickerSelection(rightInstance, RightVariantPicker);
    }

    private void ApplyPickerSelection(GltfSceneInstance? instance, Picker picker)
    {
        if (instance is null || picker.SelectedIndex < 0)
        {
            return;
        }
        instance.ApplyMaterialVariant(
            picker.SelectedIndex == 0 ? null : picker.SelectedIndex - 1);
        UpdateStatus();
        SceneView.InvalidateScene();
    }

    private void UpdateStatus(int? loadedByteCount = null)
    {
        if (leftInstance is null || rightInstance is null)
        {
            return;
        }
        Mesh leftMesh = leftInstance.Root.EnumerateDepthFirst().OfType<Mesh>().First();
        Mesh rightMesh = rightInstance.Root.EnumerateDepthFirst().OfType<Mesh>().First();
        string prefix = loadedByteCount.HasValue
            ? $"Loaded {loadedByteCount.Value / 1024f:0.0} KiB once. "
            : string.Empty;
        StatusLabel.Text = prefix +
            $"Geometry shared: {ReferenceEquals(leftMesh.Geometry, rightMesh.Geometry)}; " +
            $"materials isolated: {!ReferenceEquals(leftMesh.Material, rightMesh.Material)}.";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }


}
