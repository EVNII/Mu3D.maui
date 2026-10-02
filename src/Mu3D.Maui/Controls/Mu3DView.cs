using Mu3D.Graphics;
using Mu3D.Native.Wgpu;

namespace Mu3D.Maui.Controls;

/// <summary>
/// Hosts an automatically configured Mu3D presentation surface in a .NET MAUI visual tree.
/// </summary>
/// <remarks>
/// In the default <see cref="SurfaceManagementMode.Automatic"/> mode the control owns the default
/// backend session, physical-pixel resize, native-surface recreation and disposal. Applications can
/// handle <see cref="Draw"/> and call <see cref="InvalidateSurface"/> without creating a native
/// session. Advanced engines can select <see cref="SurfaceManagementMode.Manual"/> and consume the
/// native lifecycle events directly.
/// </remarks>
public sealed class Mu3DView : View
{
    private NativeSurfaceSource? nativeSurfaceSource;
    private IWgpuTexturePresentationSink? texturePresentationSink;
    private IPresentationSurfaceSession? ownedPresentationSession;
    private CancellationTokenSource? sessionCreation;
    private uint surfaceWidth = 1;
    private uint surfaceHeight = 1;
    private int sessionGeneration;
    private readonly SurfaceFrameScheduler frameScheduler = new();
    private int automaticFrameInProgress;
    private int automaticFrameRequestedWhileBusy;
    private bool isControlLoaded;
    private bool automaticPresentationStopped;
    private Window? lifecycleWindow;
    private PresentationWhitePass? presentationWhitePass;
    private float? nativeWhiteUnitNits;
    private float? systemSdrWhiteNits;

    /// <summary>Gets the latest system SDR white in nits, or null when managed by the compositor or unavailable.</summary>
    public float? SystemSdrWhiteNits => systemSdrWhiteNits;

    /// <summary>Gets or sets optional asynchronous GPU preparation before an automatic session is published.</summary>
    /// <remarks>The control keeps the device alive until preparation finishes, even after lifecycle cancellation.
    /// The callback must not dispose the device, access UI from worker threads or retain the device after cancellation.
    /// Existing sessions are not restarted when this callback changes. Native compilation may not be interruptible.</remarks>
    public Func<GraphicsDevice, GraphicsTextureFormat, CancellationToken, Task>? PrepareGraphicsAsync { get; set; }

    /// <summary>Identifies the Windows-only SDR white matching switch.</summary>
    public static readonly BindableProperty WindowsMatchSdrWhiteProperty = BindableProperty.Create(
        nameof(WindowsMatchSdrWhite), typeof(bool), typeof(Mu3DView), true,
        propertyChanged: static (owner, _, _) => ((Mu3DView)owner).InvalidateSurface());

    /// <summary>Gets or sets Windows system SDR white matching. Defaults to true; ignored on Android and Apple.</summary>
    /// <remarks>Only affects System white mode. Changing it requests a frame without recreating the session.</remarks>
    public bool WindowsMatchSdrWhite
    {
        get => (bool)GetValue(WindowsMatchSdrWhiteProperty);
        set => SetValue(WindowsMatchSdrWhiteProperty, value);
    }

    internal void SetPlatformWhite(float? nativeUnitNits, float? sdrWhiteNits)
    {
        if (nativeWhiteUnitNits == nativeUnitNits && systemSdrWhiteNits == sdrWhiteNits) return;
        nativeWhiteUnitNits = nativeUnitNits;
        systemSdrWhiteNits = sdrWhiteNits;
        InvalidateSurface();
    }
#if WINDOWS
    private double platformSurfaceScale = 1.0;
    private View? windowsTransparentUnderlay;
#endif

    private bool HasPresentationTarget =>
        nativeSurfaceSource is not null || texturePresentationSink is not null;

    /// <summary>Identifies the <see cref="OutputSettings"/> bindable property.</summary>
    public static readonly BindableProperty OutputSettingsProperty = BindableProperty.Create(
        nameof(OutputSettings),
        typeof(OutputSettings),
        typeof(Mu3DView),
        OutputSettings.Default,
        propertyChanged: static (bindable, _, _) =>
            ((Mu3DView)bindable).RestartAutomaticSession());

    /// <summary>Identifies the <see cref="SurfaceManagement"/> bindable property.</summary>
    public static readonly BindableProperty SurfaceManagementProperty = BindableProperty.Create(
        nameof(SurfaceManagement),
        typeof(SurfaceManagementMode),
        typeof(Mu3DView),
        SurfaceManagementMode.Automatic,
        validateValue: static (_, value) =>
            value is SurfaceManagementMode mode && Enum.IsDefined(mode),
        propertyChanged: static (bindable, _, _) =>
            ((Mu3DView)bindable).RestartAutomaticSession());

    /// <summary>Initializes an automatic Mu3D presentation view.</summary>
    public Mu3DView()
    {
        Loaded += OnControlLoaded;
        Unloaded += OnControlUnloaded;
    }

    /// <summary>Gets or sets the HDR-first output policy.</summary>
    public OutputSettings OutputSettings
    {
        get => (OutputSettings)GetValue(OutputSettingsProperty);
        set => SetValue(OutputSettingsProperty, value);
    }

    /// <summary>Gets or sets whether this control or the application owns presentation.</summary>
    public SurfaceManagementMode SurfaceManagement
    {
        get => (SurfaceManagementMode)GetValue(SurfaceManagementProperty);
        set => SetValue(SurfaceManagementProperty, value);
    }

    /// <summary>Gets the automatically owned backend-independent presentation session.</summary>
    /// <remarks>
    /// The value is null before the platform surface is ready, in manual mode, or after the surface
    /// is invalidated. The control owns this object; application code must not dispose it.
    /// </remarks>
    public IPresentationSurfaceSession? PresentationSession => ownedPresentationSession;

    /// <summary>Gets the currently valid native presentation source, if one exists.</summary>
    /// <remarks>This property is intended for advanced manual presentation hosts.</remarks>
    public NativeSurfaceSource? NativeSurfaceSource => nativeSurfaceSource;

    internal IWgpuTexturePresentationSink? TexturePresentationSink => texturePresentationSink;

    internal event EventHandler? TexturePresentationSinkChanged;

    /// <summary>Gets the physical-pixel dimensions required by the current platform surface.</summary>
    /// <returns>The non-zero width and height expected by the platform presentation backend.</returns>
    /// <remarks>This method is intended for advanced manual presentation hosts.</remarks>
    public (uint Width, uint Height) GetSurfaceConfigurationSize() =>
        (surfaceWidth, surfaceHeight);

    /// <summary>
    /// Occurs when the automatic session becomes ready or is invalidated.
    /// </summary>
    public event EventHandler<PresentationSessionChangedEventArgs>? PresentationSessionChanged;

    /// <summary>
    /// Occurs when an on-demand frame should be encoded into the acquired presentation texture.
    /// </summary>
    /// <remarks>Automatic presentation applies OutputSettings.WhiteMode after this callback.
    /// Do not pre-scale for system white unless PlatformNative is selected.</remarks>
    public event EventHandler<SurfaceDrawEventArgs>? Draw;

    /// <summary>Occurs after an on-demand frame attempt finishes.</summary>
    public event EventHandler<SurfaceFramePresentedEventArgs>? FramePresented;

    /// <summary>Occurs when automatic session creation, resize, or drawing fails.</summary>
    public event EventHandler<SurfaceErrorEventArgs>? SurfaceError;

    /// <summary>Occurs after the authoritative physical-pixel surface size changes.</summary>
    public event EventHandler<SurfaceSizeChangedEventArgs>? SurfaceSizeChanged;

    /// <summary>Occurs when a native presentation source becomes available or is invalidated.</summary>
    /// <remarks>This event is intended for <see cref="SurfaceManagementMode.Manual"/> hosts.</remarks>
    public event EventHandler<NativeSurfaceChangedEventArgs>? NativeSurfaceChanged;

    /// <summary>
    /// Coalesces and requests one draw using the automatically owned presentation session.
    /// </summary>
    /// <remarks>
    /// Session creation and physical resize trigger this method automatically. Continuous animation
    /// remains an explicit application scheduling choice; this method does not create a timer.
    /// </remarks>
    public void InvalidateSurface()
    {
        if (SurfaceManagement != SurfaceManagementMode.Automatic ||
            ownedPresentationSession is null ||
            Draw is null)
        {
            return;
        }

        // Dispatch is an actual UI-queue post on every MAUI platform. In particular, the Windows
        // implementation uses DispatcherQueue.TryEnqueue. Do not express this defer boundary as a
        // zero-delay DispatchDelayed call: WinUI creates a native DispatcherQueueTimer for every
        // request, which can keep CoreMessaging active after a continuous-rendering window closes.
        frameScheduler.Request(Dispatcher.Dispatch, RenderAutomaticFrame);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == WindowProperty.PropertyName)
        {
            if (isControlLoaded && HasPresentationTarget)
            {
                ConnectWindowLifecycle(Window);
            }
            else if (Window is null)
            {
                DisconnectWindowLifecycle();
            }
        }
    }

    /// <inheritdoc />
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
#if WINDOWS
        double platformScale = platformSurfaceScale;
#else
        double platformScale = DeviceDisplay.MainDisplayInfo.Density;
#endif
        SetPlatformSurfaceSize(width * platformScale, height * platformScale);
    }

#if WINDOWS
    internal View? WindowsTransparentUnderlay => windowsTransparentUnderlay;

    internal void SetWindowsTransparentUnderlay(View? underlay)
    {
        if (ReferenceEquals(windowsTransparentUnderlay, underlay))
        {
            return;
        }

        windowsTransparentUnderlay = underlay;
        Handler?.UpdateValue(nameof(WindowsTransparentUnderlay));
    }

    internal void SetPlatformSurfaceScale(double scale)
    {
        platformSurfaceScale = double.IsFinite(scale) && scale > 0 ? scale : 1.0;
    }
#endif

    internal void SetPlatformSurfaceSize(double width, double height)
    {
        uint physicalWidth = ToPhysicalDimension(width);
        uint physicalHeight = ToPhysicalDimension(height);
        if (surfaceWidth == physicalWidth && surfaceHeight == physicalHeight)
        {
            return;
        }

        surfaceWidth = physicalWidth;
        surfaceHeight = physicalHeight;
        IPresentationSurfaceSession? session = ownedPresentationSession;
        if (SurfaceManagement == SurfaceManagementMode.Automatic && session is not null)
        {
            TryResize(session, physicalWidth, physicalHeight);
        }

        SurfaceSizeChanged?.Invoke(
            this,
            new SurfaceSizeChangedEventArgs(physicalWidth, physicalHeight));
        InvalidateSurface();
    }

    internal void SetNativeSurfaceSource(NativeSurfaceSource? source)
    {
        if (source is not null)
        {
            if (isControlLoaded)
            {
                ConnectWindowLifecycle(Window);
            }
        }
        else if (texturePresentationSink is null)
        {
            DisconnectWindowLifecycle();
        }

        if (nativeSurfaceSource == source)
        {
            return;
        }

        nativeSurfaceSource = source;
        RestartAutomaticSession();
        NativeSurfaceChanged?.Invoke(this, new NativeSurfaceChangedEventArgs(source));
    }

    internal void SetTexturePresentationSink(IWgpuTexturePresentationSink? sink)
    {
        if (sink is not null)
        {
            if (isControlLoaded)
            {
                ConnectWindowLifecycle(Window);
            }
        }
        else if (nativeSurfaceSource is null)
        {
            DisconnectWindowLifecycle();
        }

        if (ReferenceEquals(texturePresentationSink, sink))
        {
            return;
        }

        texturePresentationSink = sink;
        RestartAutomaticSession();
        TexturePresentationSinkChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void ReportPlatformSurfaceError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Dispatcher.IsDispatchRequired)
        {
            _ = Dispatcher.Dispatch(() => ReportPlatformSurfaceError(exception));
            return;
        }

        SurfaceError?.Invoke(
            this,
            new SurfaceErrorEventArgs(SurfaceOperation.Create, exception));
    }

    private void RestartAutomaticSession()
    {
        StopAutomaticSession();

        NativeSurfaceSource? source = nativeSurfaceSource;
        IWgpuTexturePresentationSink? textureSink = texturePresentationSink;
        if (!isControlLoaded ||
            automaticPresentationStopped ||
            SurfaceManagement != SurfaceManagementMode.Automatic ||
            (source is null && textureSink is null))
        {
            return;
        }

        int generation = sessionGeneration;
        CancellationTokenSource cancellation = new();
        sessionCreation = cancellation;
        _ = CreateAutomaticSessionAsync(source, textureSink, generation, cancellation.Token);
    }

    private void StopAutomaticSession()
    {
        _ = unchecked(++sessionGeneration);
        frameScheduler.CancelPending();
        sessionCreation?.Cancel();
        sessionCreation?.Dispose();
        sessionCreation = null;
        DetachOwnedSession();
    }

    private void ConnectWindowLifecycle(Window? window)
    {
        if (ReferenceEquals(lifecycleWindow, window))
        {
            return;
        }

        DisconnectWindowLifecycle();
        automaticPresentationStopped = false;
        lifecycleWindow = window;
        if (lifecycleWindow is not null)
        {
            lifecycleWindow.Stopped += OnWindowStopped;
            lifecycleWindow.Resumed += OnWindowResumed;
            lifecycleWindow.Destroying += OnWindowDestroying;
        }
    }

    private void DisconnectWindowLifecycle()
    {
        if (lifecycleWindow is not null)
        {
            lifecycleWindow.Stopped -= OnWindowStopped;
            lifecycleWindow.Resumed -= OnWindowResumed;
            lifecycleWindow.Destroying -= OnWindowDestroying;
            lifecycleWindow = null;
        }
    }

    private void OnControlLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (isControlLoaded)
        {
            return;
        }

        isControlLoaded = true;
        // A (re)load into the visual tree is fresh presentation intent: never carry a stale
        // window-stop latch across a load, or a Stopped event whose Resumed was missed before
        // subscription would starve the session forever.
        automaticPresentationStopped = false;
        if (HasPresentationTarget)
        {
            ConnectWindowLifecycle(Window);
            RestartAutomaticSession();
        }
    }

    private void OnControlUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (!isControlLoaded)
        {
            return;
        }

        isControlLoaded = false;
        DisconnectWindowLifecycle();
        StopAutomaticSession();
    }

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (automaticPresentationStopped)
        {
            return;
        }

        automaticPresentationStopped = true;
        StopAutomaticSession();
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (!automaticPresentationStopped)
        {
            return;
        }

        automaticPresentationStopped = false;
        if (isControlLoaded)
        {
            RestartAutomaticSession();
        }
    }

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        automaticPresentationStopped = true;
        DisconnectWindowLifecycle();
        SetNativeSurfaceSource(null);
        SetTexturePresentationSink(null);
    }

    private async Task CreateAutomaticSessionAsync(
        NativeSurfaceSource? source,
        IWgpuTexturePresentationSink? textureSink,
        int generation,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            IPresentationSurfaceSession? created = null;
            try
            {
                created = textureSink is not null
                    ? await WgpuTexturePresentationSession.TryCreateForLifecycleAsync(
                        textureSink,
                        surfaceWidth,
                        surfaceHeight,
                        OutputSettings,
                        TimeSpan.FromSeconds(30),
                        cancellationToken)
                    : await WgpuSurfaceSession.TryCreateForLifecycleAsync(
                        source ?? throw new InvalidOperationException(
                            "Automatic presentation requires a native surface or texture sink."),
                        surfaceWidth,
                        surfaceHeight,
                        OutputSettings,
                        TimeSpan.FromSeconds(30),
                        cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (created is null)
                {
                    // A cooperative timeout is not lifecycle cancellation: it used to return
                    // silently, leaving the control without a session and without an error.
                    // Retry while the control stays loaded and current, then report.
                    if (attempt >= 3 || generation != sessionGeneration || !isControlLoaded)
                    {
                        ReportErrorIfCurrent(generation, SurfaceOperation.Create, new TimeoutException(
                            "Automatic presentation session creation timed out after 30 s."));
                        return;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                if (PrepareGraphicsAsync is { } prepare)
                {
                    // Keep ownership in this async operation until preparation completes. Unload
                    // cancels publication, but never disposes a device beneath an active compiler.
                    await prepare(created.Device, created.OutputPlan.Output.Format switch
                    {
                        PresentationFormat.Rgba16Float => GraphicsTextureFormat.Rgba16Float,
                        PresentationFormat.Bgra8Unorm => GraphicsTextureFormat.Bgra8Unorm,
                        PresentationFormat.Bgra8UnormSrgb => GraphicsTextureFormat.Bgra8UnormSrgb,
                        PresentationFormat.Rgba8Unorm => GraphicsTextureFormat.Rgba8Unorm,
                        PresentationFormat.Rgba8UnormSrgb => GraphicsTextureFormat.Rgba8UnormSrgb,
                        _ => throw new NotSupportedException("Unsupported preparation target format."),
                    }, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                IPresentationSurfaceSession ready = created;
                created = null;
                void Publish()
                {
                    if (generation != sessionGeneration ||
                        SurfaceManagement != SurfaceManagementMode.Automatic ||
                        nativeSurfaceSource != source ||
                        !ReferenceEquals(texturePresentationSink, textureSink))
                    {
                        ready.Dispose();
                        return;
                    }

                    if (ready.Width != surfaceWidth || ready.Height != surfaceHeight)
                    {
                        ready.Resize(surfaceWidth, surfaceHeight);
                    }

                    ownedPresentationSession = ready;
                    PresentationSessionChanged?.Invoke(
                        this,
                        new PresentationSessionChangedEventArgs(ownedPresentationSession));
                    InvalidateSurface();
                }

                if (Dispatcher.IsDispatchRequired)
                {
                    // A rejected dispatch would otherwise leak the ready session and stay silent.
                    if (!Dispatcher.Dispatch(Publish))
                    {
                        ready.Dispose();
                    }
                }
                else
                {
                    Publish();
                }
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                ReportErrorIfCurrent(generation, SurfaceOperation.Create, exception);
                return;
            }
            finally
            {
                created?.Dispose();
            }
        }
    }

    private void DetachOwnedSession()
    {
        IPresentationSurfaceSession? session = ownedPresentationSession;
        if (session is null)
        {
            return;
        }

        ownedPresentationSession = null;
        try
        {
            ((IWgpuPresentationSessionLifecycle)session).DrainAndReleaseDependentResources(() =>
            {
                presentationWhitePass?.Dispose();
                presentationWhitePass = null;
                PresentationSessionChanged?.Invoke(
                    this,
                    new PresentationSessionChangedEventArgs(null));
            });
        }
        finally
        {
            session.Dispose();
        }
    }

    private void TryResize(IResizablePresentationSurface session, uint width, uint height)
    {
        if (session.Width == width && session.Height == height)
        {
            return;
        }

        try
        {
            session.Resize(width, height);
        }
        catch (Exception exception)
        {
            SurfaceError?.Invoke(
                this,
                new SurfaceErrorEventArgs(SurfaceOperation.Resize, exception));
        }
    }

    private void RenderAutomaticFrame()
    {
        if (Interlocked.CompareExchange(ref automaticFrameInProgress, 1, 0) != 0)
        {
            Interlocked.Exchange(ref automaticFrameRequestedWhileBusy, 1);
            return;
        }

        try
        {
            RenderAutomaticFrameCore();
        }
        finally
        {
            Volatile.Write(ref automaticFrameInProgress, 0);
            if (Interlocked.Exchange(ref automaticFrameRequestedWhileBusy, 0) != 0)
            {
                InvalidateSurface();
            }
        }
    }

    private void RenderAutomaticFrameCore()
    {
        IPresentationSurfaceSession? session = ownedPresentationSession;
        EventHandler<SurfaceDrawEventArgs>? draw = Draw;
        if (SurfaceManagement != SurfaceManagementMode.Automatic || session is null || draw is null)
        {
            return;
        }

        try
        {
            PresentationSurfaceFrameStatus status = session.RenderAndPresent(DrawFrame);
            if (status is PresentationSurfaceFrameStatus.Outdated or
                PresentationSurfaceFrameStatus.Lost)
            {
                session.Resize(surfaceWidth, surfaceHeight);
                status = session.RenderAndPresent(DrawFrame);
            }

            FramePresented?.Invoke(this, new SurfaceFramePresentedEventArgs(status));

            void DrawFrame(GraphicsTexture target)
            {
                float scale = PresentationWhite.Resolve(OutputSettings, session.OutputPlan.Output.Encoding,
                    nativeWhiteUnitNits, systemSdrWhiteNits, WindowsMatchSdrWhite);
                GraphicsTexture drawTarget = target;
                if (scale != 1)
                {
                    if (presentationWhitePass is null || presentationWhitePass.Format != target.Descriptor.Format)
                    {
                        var replacement = new PresentationWhitePass(session.Device, target.Descriptor.Format);
                        presentationWhitePass?.Dispose();
                        presentationWhitePass = replacement;
                    }
                    drawTarget = presentationWhitePass.GetInput(target);
                }
                draw(this, new SurfaceDrawEventArgs(session.Device, drawTarget,
                    session.OutputPlan, session.Width, session.Height));
                if (scale != 1) presentationWhitePass!.Apply(target, scale);
            }
        }
        catch (Exception exception)
        {
            SurfaceError?.Invoke(
                this,
                new SurfaceErrorEventArgs(SurfaceOperation.Draw, exception));
        }
    }

    private void ReportErrorIfCurrent(
        int generation,
        SurfaceOperation operation,
        Exception exception)
    {
        void Report()
        {
            if (generation == sessionGeneration)
            {
                SurfaceError?.Invoke(this, new SurfaceErrorEventArgs(operation, exception));
            }
        }

        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(Report);
        }
        else
        {
            Report();
        }
    }

    private static uint ToPhysicalDimension(double value)
    {
        if (!double.IsFinite(value) || value <= 1)
        {
            return 1;
        }

        return value >= uint.MaxValue ? uint.MaxValue : checked((uint)Math.Round(value));
    }
}

/// <summary>Controls ownership of a <see cref="Mu3DView"/> presentation session.</summary>
public enum SurfaceManagementMode
{
    /// <summary>The control owns the default backend, resize and native-surface lifecycle.</summary>
    Automatic,

    /// <summary>The application consumes native lifecycle events and owns presentation explicitly.</summary>
    Manual,
}

/// <summary>Reports a change to the control-owned presentation session.</summary>
/// <param name="Session">The ready session, or null after invalidation.</param>
public sealed class PresentationSessionChangedEventArgs(IPresentationSurfaceSession? Session)
    : EventArgs
{
    /// <summary>Gets the ready session, or null after invalidation.</summary>
    public IPresentationSurfaceSession? Session { get; } = Session;
}

/// <summary>Provides backend-independent state for one on-demand draw.</summary>
/// <param name="Device">The graphics device compatible with the target.</param>
/// <param name="Target">The acquired presentation texture.</param>
/// <param name="OutputPlan">The negotiated output format and fallback state.</param>
/// <param name="Width">The current physical-pixel width.</param>
/// <param name="Height">The current physical-pixel height.</param>
public sealed class SurfaceDrawEventArgs(
    GraphicsDevice Device,
    GraphicsTexture Target,
    SurfaceOutputPlan OutputPlan,
    uint Width,
    uint Height) : EventArgs
{
    /// <summary>Gets the graphics device compatible with the target.</summary>
    public GraphicsDevice Device { get; } = Device;

    /// <summary>Gets the acquired presentation texture.</summary>
    public GraphicsTexture Target { get; } = Target;

    /// <summary>Gets the negotiated output format and fallback state.</summary>
    public SurfaceOutputPlan OutputPlan { get; } = OutputPlan;

    /// <summary>Gets the current physical-pixel width.</summary>
    public uint Width { get; } = Width;

    /// <summary>Gets the current physical-pixel height.</summary>
    public uint Height { get; } = Height;
}

/// <summary>Reports the result of one control-managed frame attempt.</summary>
/// <param name="Status">The backend-independent presentation status.</param>
public sealed class SurfaceFramePresentedEventArgs(PresentationSurfaceFrameStatus Status) : EventArgs
{
    /// <summary>Gets the presentation status.</summary>
    public PresentationSurfaceFrameStatus Status { get; } = Status;
}

/// <summary>Identifies an automatic presentation operation.</summary>
public enum SurfaceOperation
{
    /// <summary>Creating and configuring the default presentation session.</summary>
    Create,

    /// <summary>Applying a physical-pixel surface resize.</summary>
    Resize,

    /// <summary>Encoding or presenting an on-demand frame.</summary>
    Draw,
}

/// <summary>Reports an automatic presentation failure.</summary>
/// <param name="Operation">The operation that failed.</param>
/// <param name="Exception">The underlying failure.</param>
public sealed class SurfaceErrorEventArgs(SurfaceOperation Operation, Exception Exception) : EventArgs
{
    /// <summary>Gets the operation that failed.</summary>
    public SurfaceOperation Operation { get; } = Operation;

    /// <summary>Gets the underlying failure.</summary>
    public Exception Exception { get; } = Exception;
}

/// <summary>Reports a new physical-pixel presentation extent.</summary>
/// <param name="Width">The non-zero physical-pixel width.</param>
/// <param name="Height">The non-zero physical-pixel height.</param>
public sealed class SurfaceSizeChangedEventArgs(uint Width, uint Height) : EventArgs
{
    /// <summary>Gets the non-zero physical-pixel width.</summary>
    public uint Width { get; } = Width;

    /// <summary>Gets the non-zero physical-pixel height.</summary>
    public uint Height { get; } = Height;
}

/// <summary>Reports creation or invalidation of a platform presentation source.</summary>
/// <param name="Source">The valid source, or null after invalidation.</param>
public sealed class NativeSurfaceChangedEventArgs(NativeSurfaceSource? Source) : EventArgs
{
    /// <summary>Gets the valid source, or null after invalidation.</summary>
    public NativeSurfaceSource? Source { get; } = Source;
}
