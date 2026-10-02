namespace Mu3D.Toolkit.Controls;

/// <summary>Provides conventional priorities for cooperating viewport controllers.</summary>
public static class ViewportControlPriorities
{
    /// <summary>The conventional priority for camera navigation controllers.</summary>
    public const int Camera = 0;

    /// <summary>The conventional priority for direct transform-gizmo manipulation.</summary>
    public const int Gizmo = 100;

    /// <summary>The conventional priority for an interactive viewport overlay UI.</summary>
    public const int OverlayUi = 200;
}

/// <summary>
/// Arbitrates temporary viewport control ownership with explicit, revocable leases.
/// </summary>
/// <remarks>
/// A higher-priority request revokes the active lower-priority lease. Equal- and lower-priority
/// requests are denied. The host remains responsible for raw input, pointer capture and mapping
/// input gestures to controllers. This arbiter is not thread-safe.
/// </remarks>
public sealed class ViewportControlArbiter : IDisposable
{
    private ViewportControlLease? currentLease;
    private bool revoking;
    private bool disposed;

    /// <summary>Gets the currently active borrowed lease, or null when control is unclaimed.</summary>
    public ViewportControlLease? CurrentLease => currentLease;

    /// <summary>
    /// Attempts to acquire control under a diagnostic owner name and priority.
    /// </summary>
    /// <returns>
    /// A disposable active lease, or null when an equal- or higher-priority lease already owns
    /// control.
    /// </returns>
    public ViewportControlLease? TryAcquire(string ownerName, int priority)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerName);
        if (revoking)
        {
            throw new InvalidOperationException(
                "Viewport control cannot be acquired from a lease-revocation callback.");
        }
        if (currentLease is ViewportControlLease active && priority <= active.Priority)
        {
            return null;
        }

        ViewportControlLease next = new(this, ownerName, priority);
        ViewportControlLease? previous = currentLease;
        currentLease = next;
        if (previous is not null)
        {
            revoking = true;
            try
            {
                previous.Revoke();
            }
            finally
            {
                revoking = false;
            }
        }
        return next;
    }

    /// <summary>Revokes the active lease and prevents subsequent acquisition.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ViewportControlLease? active = currentLease;
        currentLease = null;
        active?.Revoke();
    }

    internal void Release(ViewportControlLease lease)
    {
        if (ReferenceEquals(currentLease, lease))
        {
            currentLease = null;
        }
        lease.Release();
    }
}

/// <summary>Represents one temporary claim on shared viewport control.</summary>
public sealed class ViewportControlLease : IDisposable
{
    private ViewportControlArbiter? arbiter;

    internal ViewportControlLease(
        ViewportControlArbiter arbiter,
        string ownerName,
        int priority)
    {
        this.arbiter = arbiter;
        OwnerName = ownerName;
        Priority = priority;
    }

    /// <summary>Occurs when a higher-priority claim or arbiter disposal revokes this lease.</summary>
    public event EventHandler? Revoked;

    /// <summary>Gets the diagnostic owner name supplied when the lease was acquired.</summary>
    public string OwnerName { get; }

    /// <summary>Gets the priority supplied when the lease was acquired.</summary>
    public int Priority { get; }

    /// <summary>Gets whether this lease still owns viewport control.</summary>
    public bool IsActive => arbiter is not null;

    /// <summary>Voluntarily releases this lease. Repeated disposal is harmless.</summary>
    public void Dispose()
    {
        ViewportControlArbiter? currentArbiter = arbiter;
        if (currentArbiter is null)
        {
            return;
        }
        currentArbiter.Release(this);
    }

    internal void Revoke()
    {
        if (arbiter is null)
        {
            return;
        }
        arbiter = null;
        Revoked?.Invoke(this, EventArgs.Empty);
        Revoked = null;
    }

    internal void Release()
    {
        arbiter = null;
        Revoked = null;
    }
}
