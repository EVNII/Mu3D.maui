namespace Mu3D.Toolkit.Viewports;

/// <summary>
/// Describes current usage of an application-owned <see cref="SceneViewportBudget"/>.
/// </summary>
/// <param name="ActiveViewportCount">Number of live viewport leases.</param>
/// <param name="PendingViewportCount">Number of requests waiting for a live viewport slot.</param>
/// <param name="ActiveLoadCount">Number of active asset-loading leases.</param>
/// <param name="PendingLoadCount">Number of requests waiting for an asset-loading slot.</param>
public readonly record struct SceneViewportBudgetSnapshot(
    int ActiveViewportCount,
    int PendingViewportCount,
    int ActiveLoadCount,
    int PendingLoadCount);

/// <summary>
/// Provides application-owned upper bounds for simultaneously live 3D viewports and asset loads.
/// </summary>
/// <remarks>
/// UI virtualization decides which items request leases. The budget does not retain views, assets,
/// scenes or native handles and performs no loading itself. Scope one instance to the product feed,
/// page or application region whose resource policy it represents. Capacity properties must be set
/// before the first lease request.
/// </remarks>
public sealed class SceneViewportBudget
{
    private readonly object gate = new();
    private SemaphoreSlim? viewportSlots;
    private SemaphoreSlim? loadSlots;
    private int maximumLiveViewports = 4;
    private int maximumConcurrentLoads = 2;
    private int activeViewportCount;
    private int pendingViewportCount;
    private int activeLoadCount;
    private int pendingLoadCount;

    /// <summary>Gets or sets the positive maximum number of simultaneous live viewport leases.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when changed after the first lease request.
    /// </exception>
    public int MaximumLiveViewports
    {
        get
        {
            lock (gate)
            {
                return maximumLiveViewports;
            }
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            lock (gate)
            {
                ThrowIfStarted();
                maximumLiveViewports = value;
            }
        }
    }

    /// <summary>Gets or sets the positive maximum number of simultaneous asset-loading leases.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when changed after the first lease request.
    /// </exception>
    public int MaximumConcurrentLoads
    {
        get
        {
            lock (gate)
            {
                return maximumConcurrentLoads;
            }
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            lock (gate)
            {
                ThrowIfStarted();
                maximumConcurrentLoads = value;
            }
        }
    }

    /// <summary>Gets a thread-safe snapshot of current active and pending lease counts.</summary>
    public SceneViewportBudgetSnapshot Snapshot => new(
        Volatile.Read(ref activeViewportCount),
        Volatile.Read(ref pendingViewportCount),
        Volatile.Read(ref activeLoadCount),
        Volatile.Read(ref pendingLoadCount));

    /// <summary>
    /// Occurs after a snapshot counter changes. The event is raised on the thread that changed it.
    /// </summary>
    public event EventHandler? SnapshotChanged;

    /// <summary>
    /// Waits for one live viewport slot. Dispose the returned lease when the viewport leaves the
    /// virtualized live set.
    /// </summary>
    public ValueTask<SceneViewportBudgetLease> AcquireViewportAsync(
        CancellationToken cancellationToken = default) => AcquireAsync(
            BudgetKind.Viewport,
            cancellationToken);

    /// <summary>
    /// Waits for one asset-loading slot. Dispose the returned lease as soon as loading and decoding
    /// finish; retaining a loaded asset is controlled by a separate application-owned cache.
    /// </summary>
    public ValueTask<SceneViewportBudgetLease> AcquireLoadAsync(
        CancellationToken cancellationToken = default) => AcquireAsync(
            BudgetKind.Load,
            cancellationToken);

    private async ValueTask<SceneViewportBudgetLease> AcquireAsync(
        BudgetKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SemaphoreSlim slots = GetSlots(kind);
        IncrementPending(kind);
        RaiseSnapshotChanged();
        try
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DecrementPending(kind);
            RaiseSnapshotChanged();
        }

        IncrementActive(kind);
        RaiseSnapshotChanged();
        return new SceneViewportBudgetLease(() => Release(kind, slots));
    }

    private SemaphoreSlim GetSlots(BudgetKind kind)
    {
        lock (gate)
        {
            viewportSlots ??= new SemaphoreSlim(maximumLiveViewports, maximumLiveViewports);
            loadSlots ??= new SemaphoreSlim(maximumConcurrentLoads, maximumConcurrentLoads);
            return kind == BudgetKind.Viewport ? viewportSlots : loadSlots;
        }
    }

    private void IncrementPending(BudgetKind kind)
    {
        if (kind == BudgetKind.Viewport)
        {
            Interlocked.Increment(ref pendingViewportCount);
            return;
        }
        Interlocked.Increment(ref pendingLoadCount);
    }

    private void DecrementPending(BudgetKind kind)
    {
        if (kind == BudgetKind.Viewport)
        {
            Interlocked.Decrement(ref pendingViewportCount);
            return;
        }
        Interlocked.Decrement(ref pendingLoadCount);
    }

    private void IncrementActive(BudgetKind kind)
    {
        if (kind == BudgetKind.Viewport)
        {
            Interlocked.Increment(ref activeViewportCount);
            return;
        }
        Interlocked.Increment(ref activeLoadCount);
    }

    private void Release(BudgetKind kind, SemaphoreSlim slots)
    {
        int remaining = kind == BudgetKind.Viewport
            ? Interlocked.Decrement(ref activeViewportCount)
            : Interlocked.Decrement(ref activeLoadCount);
        if (remaining < 0)
        {
            if (kind == BudgetKind.Viewport)
            {
                Interlocked.Increment(ref activeViewportCount);
            }
            else
            {
                Interlocked.Increment(ref activeLoadCount);
            }
            throw new InvalidOperationException("A scene viewport budget lease was released twice.");
        }
        slots.Release();
        RaiseSnapshotChanged();
    }

    private void ThrowIfStarted()
    {
        if (viewportSlots is not null || loadSlots is not null)
        {
            throw new InvalidOperationException(
                "Scene viewport budget capacities cannot change after the first lease request.");
        }
    }

    private void RaiseSnapshotChanged() => SnapshotChanged?.Invoke(this, EventArgs.Empty);

    private enum BudgetKind
    {
        Viewport,
        Load,
    }
}

/// <summary>
/// Releases one slot acquired from a <see cref="SceneViewportBudget"/>. Disposal is idempotent.
/// </summary>
public sealed class SceneViewportBudgetLease : IDisposable
{
    private Action? release;

    internal SceneViewportBudgetLease(Action release) => this.release = release;

    /// <summary>Returns the acquired slot to its owning budget.</summary>
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}
