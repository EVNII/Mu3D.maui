using MauiColor = Microsoft.Maui.Graphics.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Native.Wgpu;
using Mu3D.SceneGraph;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>
/// Hosts one lightweight HDR or binary-mask presentation Surface as an ordinary MAUI visual element.
/// </summary>
/// <remarks>
/// The proxy owns its native Surface through the normal <see cref="Mu3DView"/> Handler lifecycle,
/// while <see cref="SceneViewProxyHost"/> supplies a shared device, renderer and GPU resource cache.
/// The Surface therefore moves, clips and animates with this MAUI element without GPU readback or
/// image encoding. An opaque scene background selects the direct HDR carrier; a transparent
/// background selects the platform compositor's alpha carrier. Windows uses premultiplied alpha,
/// Apple uses a GPU conversion to its straight-alpha Metal carrier, and Android inherits the
/// native-window compositor mode. Assigned scene and camera objects remain application-owned.
/// </remarks>
[ContentProperty(nameof(Placeholder))]
public sealed class SceneViewProxy : Grid
{
    private static readonly OutputSettings DefaultProxyOutputSettings = OutputSettings.Default with
    {
        AlphaMode = PlatformTransparentAlphaMode,
    };
    private static readonly BindablePropertyKey HasPresentedFramePropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(HasPresentedFrame),
            typeof(bool),
            typeof(SceneViewProxy),
            false);
    private readonly Mu3DView surfaceView;
    private OutputSettings hostOutputSettings = DefaultProxyOutputSettings;
    private SceneViewProxyHost? registeredHost;
    private long renderVersion;

    /// <summary>Identifies the <see cref="Host"/> bindable property.</summary>
    public static readonly BindableProperty HostProperty = BindableProperty.Create(
        nameof(Host),
        typeof(SceneViewProxyHost),
        typeof(SceneViewProxy),
        default(SceneViewProxyHost),
        propertyChanged: static (bindable, _, _) =>
            ((SceneViewProxy)bindable).RefreshRegistration());

    /// <summary>Identifies the <see cref="Scene"/> bindable property.</summary>
    public static readonly BindableProperty SceneProperty = BindableProperty.Create(
        nameof(Scene),
        typeof(Scene),
        typeof(SceneViewProxy),
        default(Scene),
        propertyChanged: static (bindable, _, _) =>
            ((SceneViewProxy)bindable).InvalidateScene(clearPresentedState: true));

    /// <summary>Identifies the <see cref="Camera"/> bindable property.</summary>
    public static readonly BindableProperty CameraProperty = BindableProperty.Create(
        nameof(Camera),
        typeof(Camera),
        typeof(SceneViewProxy),
        default(Camera),
        propertyChanged: static (bindable, _, _) =>
            ((SceneViewProxy)bindable).InvalidateScene(clearPresentedState: true));

    /// <summary>Identifies the <see cref="SceneBackgroundColor"/> bindable property.</summary>
    public static readonly BindableProperty SceneBackgroundColorProperty = BindableProperty.Create(
        nameof(SceneBackgroundColor),
        typeof(MauiColor),
        typeof(SceneViewProxy),
        Colors.Transparent,
        validateValue: static (_, value) =>
            value is MauiColor color && color.Alpha is 0f or 1f,
        propertyChanged: static (bindable, _, _) =>
            ((SceneViewProxy)bindable).OnSceneBackgroundColorChanged());

    /// <summary>Identifies the <see cref="IsRenderingEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsRenderingEnabledProperty = BindableProperty.Create(
        nameof(IsRenderingEnabled),
        typeof(bool),
        typeof(SceneViewProxy),
        true,
        propertyChanged: static (bindable, _, value) =>
        {
            SceneViewProxy proxy = (SceneViewProxy)bindable;
            if ((bool)value)
            {
                proxy.QueueFrame();
            }
            else
            {
                proxy.ResetPresentedState();
                proxy.registeredHost?.SuspendProxy(proxy);
            }
        });

    /// <summary>Identifies the <see cref="RenderRevision"/> bindable property.</summary>
    public static readonly BindableProperty RenderRevisionProperty = BindableProperty.Create(
        nameof(RenderRevision),
        typeof(long),
        typeof(SceneViewProxy),
        0L,
        propertyChanged: static (bindable, _, _) =>
            ((SceneViewProxy)bindable).InvalidateScene(clearPresentedState: false));

    /// <summary>Identifies the <see cref="Underlay"/> bindable property.</summary>
    public static readonly BindableProperty UnderlayProperty = BindableProperty.Create(
        nameof(Underlay),
        typeof(View),
        typeof(SceneViewProxy),
        default(View),
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((SceneViewProxy)bindable).OnUnderlayChanged(
                oldValue as View,
                newValue as View));

    /// <summary>Identifies the <see cref="Placeholder"/> bindable property.</summary>
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(View),
        typeof(SceneViewProxy),
        default(View),
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((SceneViewProxy)bindable).OnPlaceholderChanged(
                oldValue as View,
                newValue as View));

    /// <summary>Identifies the read-only <see cref="HasPresentedFrame"/> bindable property.</summary>
    public static readonly BindableProperty HasPresentedFrameProperty =
        HasPresentedFramePropertyKey.BindableProperty;

    /// <summary>Initializes one shared-device Surface proxy.</summary>
    public SceneViewProxy()
    {
        surfaceView = new Mu3DView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            OutputSettings = DefaultProxyOutputSettings,
            SurfaceManagement = SurfaceManagementMode.Manual,
        };
        surfaceView.NativeSurfaceChanged += OnNativeSurfaceChanged;
        surfaceView.TexturePresentationSinkChanged += OnTexturePresentationSinkChanged;
        surfaceView.SurfaceError += OnSurfaceError;
        surfaceView.SurfaceSizeChanged += OnSurfaceSizeChanged;
        Children.Add(surfaceView);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        HandlerChanged += OnHandlerChanged;
    }

    /// <summary>Gets or sets the explicit host, or null to find the nearest host ancestor.</summary>
    public SceneViewProxyHost? Host
    {
        get => (SceneViewProxyHost?)GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    /// <summary>Gets or sets the borrowed scene rendered into this Surface.</summary>
    public Scene? Scene
    {
        get => (Scene?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>Gets or sets the borrowed camera used to render this Surface.</summary>
    public Camera? Camera
    {
        get => (Camera?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    /// <summary>
    /// Gets or sets the XAML-friendly scene clear color for opaque or binary-mask presentation.
    /// </summary>
    /// <remarks>
    /// Use <c>Transparent</c> for a zero-alpha cutout or an opaque color for a filled background.
    /// Fractional background alpha remains deferred. The host converts opaque sRGB RGB to linear
    /// light. On Apple, transparent FP16 frames are converted on-GPU from the renderer's
    /// premultiplied representation to Metal straight alpha. On Windows, opaque output uses the
    /// direct HDR Surface while transparent output uses the compositor-owned binary-mask drawing
    /// surface; applications must not assume every alpha carrier retains physical HDR headroom.
    /// </remarks>
    public MauiColor SceneBackgroundColor
    {
        get => (MauiColor)GetValue(SceneBackgroundColorProperty);
        set => SetValue(SceneBackgroundColorProperty, value);
    }

    /// <summary>Gets or sets whether the host may create and present frames for this proxy.</summary>
    public bool IsRenderingEnabled
    {
        get => (bool)GetValue(IsRenderingEnabledProperty);
        set => SetValue(IsRenderingEnabledProperty, value);
    }

    /// <summary>Gets or sets an application revision for mutable scene or camera state.</summary>
    public long RenderRevision
    {
        get => (long)GetValue(RenderRevisionProperty);
        set => SetValue(RenderRevisionProperty, value);
    }

    /// <summary>Gets whether this proxy Surface has presented at least one current frame.</summary>
    public bool HasPresentedFrame => (bool)GetValue(HasPresentedFrameProperty);

    /// <summary>
    /// Gets or sets ordinary MAUI content placed behind the native 3D presentation carrier.
    /// </summary>
    /// <remarks>
    /// The underlay remains in the MAUI visual, input and accessibility trees. Platforms that can
    /// composite native alpha reveal it directly. The experimental Windows synchronized-HDR path
    /// may replay this visual above an opaque FP16 carrier through a renderer-derived binary mask.
    /// Applications should keep the underlay self-contained within this proxy and paint its full
    /// desired background instead of relying on an ancestor background to be sampled implicitly.
    /// On Windows, use an explicit child visual such as a fill-sized <see cref="BoxView"/> when the
    /// synchronized HDR-mask path must replay a solid backdrop.
    /// </remarks>
    public View? Underlay
    {
        get => (View?)GetValue(UnderlayProperty);
        set => SetValue(UnderlayProperty, value);
    }

    /// <summary>Gets or sets ordinary MAUI content shown until the first current frame presents.</summary>
    public View? Placeholder
    {
        get => (View?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Occurs after the current Surface presents a frame.</summary>
    public event EventHandler<SceneViewProxyFramePresentedEventArgs>? FramePresented;

    /// <summary>Occurs when Surface creation, resize, rendering or presentation fails.</summary>
    public event EventHandler<SceneViewProxySurfaceErrorEventArgs>? SurfaceError;

    /// <summary>Requests another Surface frame after application-owned state changes.</summary>
    public void InvalidateScene() => InvalidateScene(clearPresentedState: false);

    internal NativeSurfaceSource? NativeSurfaceSource => surfaceView.NativeSurfaceSource;

    internal IWgpuTexturePresentationSink? TexturePresentationSink =>
        surfaceView.TexturePresentationSink;

    internal (uint Width, uint Height) GetSurfaceSize() =>
        surfaceView.GetSurfaceConfigurationSize();

    internal long RenderVersion => renderVersion;

    internal OutputSettings EffectiveOutputSettings => surfaceView.OutputSettings;

    internal static SurfaceAlphaMode PlatformTransparentAlphaMode
    {
        get
        {
#if IOS || MACCATALYST
            // wgpu-native Metal advertises Opaque and straight-alpha (PostMultiplied) surfaces.
            return SurfaceAlphaMode.Unpremultiplied;
#elif ANDROID
            // Android's native-window compositor contract is exposed by wgpu as Inherit.
            return SurfaceAlphaMode.Inherit;
#else
            return SurfaceAlphaMode.Premultiplied;
#endif
        }
    }

    internal void SetOutputSettings(OutputSettings settings)
    {
        hostOutputSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        SurfaceAlphaMode alphaMode = SceneBackgroundColor.Alpha == 1f
            ? SurfaceAlphaMode.Opaque
            : settings.AlphaMode switch
            {
                SurfaceAlphaMode.Automatic or SurfaceAlphaMode.Opaque =>
                    PlatformTransparentAlphaMode,
                _ => settings.AlphaMode,
            };
        OutputSettings effective = settings with { AlphaMode = alphaMode };
        if (surfaceView.OutputSettings != effective)
        {
            surfaceView.OutputSettings = effective;
        }
    }

    internal void NotifyFramePresented(
        long version,
        uint pixelWidth,
        uint pixelHeight,
        PresentationFormat format)
    {
        if (version != renderVersion || !IsLoaded || registeredHost is null)
        {
            return;
        }

        SetValue(HasPresentedFramePropertyKey, true);
        if (Placeholder is View placeholder)
        {
            placeholder.IsVisible = false;
        }
        FramePresented?.Invoke(
            this,
            new SceneViewProxyFramePresentedEventArgs(pixelWidth, pixelHeight, format));
    }

    internal void ReportSurfaceError(long version, Exception exception)
    {
        if (version == renderVersion)
        {
            SurfaceError?.Invoke(this, new SceneViewProxySurfaceErrorEventArgs(exception));
        }
    }

    internal void ResetForSuspension() => ResetPresentedState();

    private void OnLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshRegistration();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        SetRegisteredHost(null);
        ResetPresentedState();
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (Handler is null)
        {
            SetRegisteredHost(null);
        }
        else if (IsLoaded)
        {
            RefreshRegistration();
        }
    }

    private void OnNativeSurfaceChanged(object? sender, NativeSurfaceChangedEventArgs e)
    {
        _ = sender;
        registeredHost?.OnProxyNativeSurfaceChanged(this, e.Source);
    }

    private void OnTexturePresentationSinkChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        registeredHost?.OnProxyTexturePresentationSinkChanged(
            this,
            TexturePresentationSink);
    }

    private void OnSurfaceSizeChanged(object? sender, SurfaceSizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        QueueFrame();
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        ReportSurfaceError(renderVersion, e.Exception);
    }

    private void OnSceneBackgroundColorChanged()
    {
        // Binary-mask proxies use a compositor-owned alpha carrier. A fully opaque background
        // must stay on the direct native Surface so Windows can retain its HDR presentation path.
        SetOutputSettings(hostOutputSettings);
        InvalidateScene(clearPresentedState: false);
    }

    private void OnUnderlayChanged(View? oldValue, View? newValue)
    {
        if (oldValue is not null)
        {
            Children.Remove(oldValue);
        }
        if (newValue is not null)
        {
            Children.Insert(0, newValue);
        }
#if WINDOWS
        surfaceView.SetWindowsTransparentUnderlay(newValue);
#endif
    }

    private void OnPlaceholderChanged(View? oldValue, View? newValue)
    {
        if (oldValue is not null)
        {
            Children.Remove(oldValue);
        }
        if (newValue is not null)
        {
            newValue.IsVisible = !HasPresentedFrame;
            Children.Add(newValue);
        }
    }

    private void InvalidateScene(bool clearPresentedState)
    {
        renderVersion = renderVersion == long.MaxValue ? 0L : renderVersion + 1L;
        if (clearPresentedState)
        {
            ResetPresentedState();
        }
        QueueFrame();
    }

    private void ResetPresentedState()
    {
        SetValue(HasPresentedFramePropertyKey, false);
        if (Placeholder is View placeholder)
        {
            placeholder.IsVisible = true;
        }
    }

    private void QueueFrame()
    {
        if (IsRenderingEnabled)
        {
            registeredHost?.QueueFrame(this);
        }
    }

    private void RefreshRegistration()
    {
        SceneViewProxyHost? nextHost = IsLoaded && Handler is not null
            ? Host ?? FindAncestorHost()
            : null;
        SetRegisteredHost(nextHost);
    }

    private SceneViewProxyHost? FindAncestorHost()
    {
        Element? ancestor = Parent;
        while (ancestor is not null)
        {
            if (ancestor is SceneViewProxyHost host)
            {
                return host;
            }
            ancestor = ancestor.Parent;
        }
        return null;
    }

    private void SetRegisteredHost(SceneViewProxyHost? host)
    {
        if (ReferenceEquals(registeredHost, host))
        {
            QueueFrame();
            return;
        }

        SceneViewProxyHost? previous = registeredHost;
        registeredHost = null;
        previous?.UnregisterProxy(this);
        registeredHost = host;
        registeredHost?.RegisterProxy(this);
    }
}

/// <summary>Reports one successfully presented Proxy Surface frame.</summary>
/// <param name="PixelWidth">Physical Surface width.</param>
/// <param name="PixelHeight">Physical Surface height.</param>
/// <param name="Format">Negotiated HDR or SDR presentation format.</param>
public sealed class SceneViewProxyFramePresentedEventArgs(
    uint PixelWidth,
    uint PixelHeight,
    PresentationFormat Format) : EventArgs
{
    /// <summary>Gets the physical Surface width.</summary>
    public uint PixelWidth { get; } = PixelWidth;

    /// <summary>Gets the physical Surface height.</summary>
    public uint PixelHeight { get; } = PixelHeight;

    /// <summary>Gets the negotiated presentation format.</summary>
    public PresentationFormat Format { get; } = Format;
}

/// <summary>Reports one rejected Proxy Surface operation.</summary>
/// <param name="Exception">The Surface creation, resize, render or presentation failure.</param>
public sealed class SceneViewProxySurfaceErrorEventArgs(Exception Exception) : EventArgs
{
    /// <summary>Gets the rejected operation.</summary>
    public Exception Exception { get; } =
        Exception ?? throw new ArgumentNullException(nameof(Exception));
}
