using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.SceneGraph;

namespace Mu3D.Gallery.Pages;

/// <summary>
/// Demonstrates a two-column product feed where each card hosts a live 3D preview through
/// <see cref="SceneViewProxy"/> instances sharing one <see cref="SceneViewProxyHost"/> device.
/// </summary>
/// <remarks>
/// Reduced port of the GalleryApp product feed: procedural sphere/cone scenes replace the glTF
/// assets, and the scroll lifecycle collapses to three tiers — live rows render, nearby rows keep
/// preloaded content suspended, and distant rows release their scene and surface.
/// </remarks>
public partial class FeedPage : ContentPage
{
    private const int ProductCount = 24;
    private const int GridSpan = 2;
    private const int LiveMarginRows = 1;
    private const int WarmMarginRows = 3;

    /// <summary>Initializes the feed with procedurally generated product cards.</summary>
    public FeedPage()
    {
        InitializeComponent();
        Products = [];
        for (int index = 0; index < ProductCount; index++)
        {
            Products.Add(new FeedProduct(index));
        }
        BindingContext = this;
        // AdaptiveShell.Maui 0.1.x hosts content pages without raising Appearing/Disappearing;
        // Loaded/Unloaded fire as the hosted page enters and leaves the window's visual tree.
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    /// <summary>Gets the product cards shown by the two-column list.</summary>
    public ObservableCollection<FeedProduct> Products { get; }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        ProxyHost.IsRenderingEnabled = true;
        ApplyLifecycle(0, Math.Min(ProductCount, 3 * GridSpan) - 1);
        // Proxy registration, the bootstrap device and frame pumping are asynchronous; refresh the
        // diagnostics line once the initial pipeline has settled so a stuck state is visible.
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1), () =>
        {
            if (IsLoaded)
            {
                RefreshStatus();
            }
        });
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        ProxyHost.IsRenderingEnabled = false;
        foreach (FeedProduct product in Products)
        {
            product.ReleaseContent();
        }
    }

    private void OnProductsScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        _ = sender;
        if (e.FirstVisibleItemIndex < 0 || e.LastVisibleItemIndex < e.FirstVisibleItemIndex)
        {
            return;
        }
        ApplyLifecycle(e.FirstVisibleItemIndex, e.LastVisibleItemIndex);
    }

    private void ApplyLifecycle(int firstVisible, int lastVisible)
    {
        int liveFirst = Math.Max(0, ((firstVisible / GridSpan) - LiveMarginRows) * GridSpan);
        int liveLast = Math.Min(
            Products.Count - 1,
            (((lastVisible / GridSpan) + 1 + LiveMarginRows) * GridSpan) - 1);
        int warmFirst = Math.Max(0, ((firstVisible / GridSpan) - WarmMarginRows) * GridSpan);
        int warmLast = Math.Min(
            Products.Count - 1,
            (((lastVisible / GridSpan) + 1 + WarmMarginRows) * GridSpan) - 1);

        int live = 0;
        int warm = 0;
        for (int index = 0; index < Products.Count; index++)
        {
            FeedProduct product = Products[index];
            if (index >= liveFirst && index <= liveLast)
            {
                product.EnsureContent();
                product.IsLive = true;
                live++;
            }
            else if (index >= warmFirst && index <= warmLast)
            {
                // Preload: content is ready to present, but its surface stays suspended.
                product.EnsureContent();
                product.IsLive = false;
                warm++;
            }
            else
            {
                // Far outside the viewport: release the scene so the proxy drops its surface.
                product.ReleaseContent();
            }
        }

        lastLive = live;
        lastWarm = warm;
        RefreshStatus();
    }

    private int lastLive;
    private int lastWarm;

    private void RefreshStatus()
    {
        StatusLabel.Text = $"{ProxyHost.ProxyCount} loaded cells · " +
            $"{ProxyHost.ActiveSurfaceCount} active surfaces · {lastLive} live · {lastWarm} preloaded · " +
            $"shared device {(ProxyHost.HasSharedDevice ? "ready" : "PENDING")} · {ProxyHost.PendingFrameCount} queued";
    }

    private void OnProxyFramePresented(object? sender, SceneViewProxyFramePresentedEventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshStatus();
    }

    private void OnProxySurfaceError(object? sender, SceneViewProxySurfaceErrorEventArgs e)
    {
        _ = sender;
        string message = $"Proxy surface failed: {e.Exception.Message}";
        System.Diagnostics.Debug.WriteLine(message);
        Console.WriteLine(message);
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    private void OnProxyHostSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        string message = $"Presentation {e.Operation} failed: {e.Exception.Message}";
        System.Diagnostics.Debug.WriteLine(message);
        Console.WriteLine(message);
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}

/// <summary>One lightweight product record; no native view or presentation surface is stored here.</summary>
public sealed class FeedProduct : INotifyPropertyChanged
{
    private static readonly (bool Sphere, float Red, float Green, float Blue, string Price)[] Variants =
    [
        (true, 1.6f, 0.5f, 0.3f, "$24.99"),
        (false, 0.35f, 1.1f, 2.2f, "$31.50"),
        (true, 0.9f, 0.8f, 0.2f, "$18.00"),
        (false, 1.8f, 0.4f, 1.2f, "$42.75"),
    ];

    private readonly bool usesSphere;
    private readonly LinearRgba color;
    private Scene? scene;
    private Camera? camera;
    private bool isLive;

    internal FeedProduct(int index)
    {
        (bool sphere, float red, float green, float blue, string price) =
            Variants[index % Variants.Length];
        usesSphere = sphere;
        color = new LinearRgba(red, green, blue, 1f, StandardColorSpaces.LinearSrgb);
        Name = $"Mu3D {(sphere ? "Sphere" : "Cone")} {index + 1:00}";
        Price = price;
    }

    /// <summary>Gets the product display name.</summary>
    public string Name { get; }

    /// <summary>Gets the demonstration price.</summary>
    public string Price { get; }

    /// <summary>Gets the borrowed procedural scene, or null while the card is cold.</summary>
    public Scene? Scene => scene;

    /// <summary>Gets the borrowed product camera, or null while the card is cold.</summary>
    public Camera? Camera => camera;

    /// <summary>Gets or sets whether this card may present shared-device surface frames.</summary>
    public bool IsLive
    {
        get => isLive;
        set
        {
            if (isLive == value)
            {
                return;
            }
            isLive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLive)));
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void EnsureContent()
    {
        if (scene is not null)
        {
            return;
        }

        Scene created = new(Name);
        MeshGeometry geometry = usesSphere
            ? MeshPrimitives.CreateUvSphere(radius: 0.85f, longitudeSegments: 40, latitudeSegments: 24)
            : MeshPrimitives.CreateCone(radius: 0.75f, height: 1.7f, radialSegments: 40);
        Mesh mesh = new(geometry, new UnlitMaterial(color, $"{Name} material"), $"{Name} mesh");
        created.Add(mesh);
        scene = created;
        camera = CreateCamera();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Scene)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Camera)));
    }

    internal void ReleaseContent()
    {
        if (scene is null && camera is null)
        {
            IsLive = false;
            return;
        }
        scene = null;
        camera = null;
        IsLive = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Scene)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Camera)));
    }

    private static PerspectiveCamera CreateCamera()
    {
        PerspectiveCamera camera = new(fieldOfViewRadians: MathF.PI / 3f, name: "Feed camera");
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            new Vector3(2.2f, 1.6f, 2.6f),
            Vector3.Zero,
            Vector3.UnitY);
        if (Matrix4x4.Invert(view, out Matrix4x4 world) &&
            Matrix4x4.Decompose(world, out Vector3 scale, out Quaternion rotation, out Vector3 position))
        {
            camera.Transform.Position = position;
            camera.Transform.Rotation = rotation;
            camera.Transform.Scale = scale;
        }
        return camera;
    }
}
