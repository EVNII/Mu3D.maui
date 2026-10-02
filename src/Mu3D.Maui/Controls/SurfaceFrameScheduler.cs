namespace Mu3D.Maui.Controls;

/// <summary>Coalesces surface requests while ensuring a render callback is never re-entered.</summary>
internal sealed class SurfaceFrameScheduler
{
    private int queued;
    private int requested;
    private int generation;
    private long nextDispatchTicket;
    private long activeDispatchTicket;

    internal void Request(Func<Action, bool> dispatch, Action render)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(render);

        Interlocked.Exchange(ref requested, 1);
        TryQueue(dispatch, render, Volatile.Read(ref generation));
    }

    internal void CancelPending()
    {
        Interlocked.Increment(ref generation);
        Interlocked.Exchange(ref requested, 0);
    }

    private void TryQueue(Func<Action, bool> dispatch, Action render, int scheduledGeneration)
    {
        if (Interlocked.CompareExchange(ref queued, 1, 0) != 0)
        {
            return;
        }

        long dispatchTicket = Interlocked.Increment(ref nextDispatchTicket);
        Volatile.Write(ref activeDispatchTicket, dispatchTicket);
        try
        {
            if (dispatch(() => Run(dispatch, render, scheduledGeneration, dispatchTicket)))
            {
                return;
            }
        }
        catch
        {
            _ = Interlocked.CompareExchange(ref activeDispatchTicket, 0, dispatchTicket);
            Interlocked.Exchange(ref queued, 0);
            Interlocked.Exchange(ref requested, 0);
            throw;
        }

        _ = Interlocked.CompareExchange(ref activeDispatchTicket, 0, dispatchTicket);
        Interlocked.Exchange(ref queued, 0);
        Interlocked.Exchange(ref requested, 0);
    }

    private void Run(
        Func<Action, bool> dispatch,
        Action render,
        int scheduledGeneration,
        long dispatchTicket)
    {
        // A platform dispatcher must invoke an accepted callback once, but invalidation can race
        // window teardown and some hosts can still deliver a rejected or repeated callback. Claim
        // the ticket before rendering so neither case can re-enter the surface frame.
        if (Interlocked.CompareExchange(ref activeDispatchTicket, 0, dispatchTicket) !=
            dispatchTicket)
        {
            return;
        }

        bool isCurrent = scheduledGeneration == Volatile.Read(ref generation);
        if (isCurrent)
        {
            Interlocked.Exchange(ref requested, 0);
        }
        try
        {
            if (isCurrent)
            {
                render();
            }
        }
        finally
        {
            Interlocked.Exchange(ref queued, 0);
            if (Volatile.Read(ref requested) != 0)
            {
                TryQueue(dispatch, render, Volatile.Read(ref generation));
            }
        }
    }
}
