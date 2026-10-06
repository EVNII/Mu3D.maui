using System.Collections.Concurrent;

namespace Mu3D.Graphics;

// Keeps a host-owned native producer/consumer alive through asynchronous surface creation and
// presentation. The host owns the initial reference; sessions acquire independent leases.
internal sealed class NativeSurfaceLifetime : IDisposable
{
    private static readonly ConcurrentDictionary<int, NativeSurfaceLifetime> lifetimes = new();
    private static int nextToken;
    private readonly object gate = new();
    private readonly Action release;
    private int references = 1;
    private bool hostDisposed;

    internal NativeSurfaceLifetime(Action release)
    {
        this.release = release ?? throw new ArgumentNullException(nameof(release));
        Token = AllocateToken();
        if (!lifetimes.TryAdd(Token, this))
        {
            throw new InvalidOperationException("A native surface lifetime token was reused.");
        }
    }

    // The public native-source struct stores only this token, preserving its unmanaged contract.
    internal int Token { get; }

    internal static IDisposable Acquire(int token)
    {
        if (!lifetimes.TryGetValue(token, out NativeSurfaceLifetime? lifetime))
        {
            throw new ObjectDisposedException(nameof(NativeSurfaceLifetime));
        }
        return lifetime.Acquire();
    }

    private static int AllocateToken()
    {
        while (true)
        {
            int previous = Volatile.Read(ref nextToken);
            if (previous == int.MaxValue)
            {
                throw new InvalidOperationException("Native surface lifetime tokens are exhausted.");
            }
            int token = checked(previous + 1);
            if (Interlocked.CompareExchange(ref nextToken, token, previous) == previous) return token;
        }
    }

    internal IDisposable Acquire()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(hostDisposed, this);
            references = checked(references + 1);
            return new Lease(this);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (hostDisposed) return;
            hostDisposed = true;
            lifetimes.TryRemove(Token, out _);
        }
        ReleaseReference();
    }

    private void ReleaseReference()
    {
        bool final;
        lock (gate)
        {
            final = --references == 0;
        }
        // Never invoke platform teardown while holding the reference-count lock.
        if (final) release();
    }

    private sealed class Lease(NativeSurfaceLifetime owner) : IDisposable
    {
        private NativeSurfaceLifetime? owner = owner;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.ReleaseReference();
    }
}
