namespace Mu3D.Graphics;

/// <summary>Describes CPU wall-clock intervals around one presentation frame.</summary>
public readonly record struct PresentationSurfaceFrameTimings
{
    /// <summary>Creates presentation timing diagnostics.</summary>
    /// <param name="acquireMilliseconds">Time spent acquiring a surface texture.</param>
    /// <param name="renderMilliseconds">Time spent in caller rendering and submission.</param>
    /// <param name="presentMilliseconds">Time spent presenting and polling backend errors.</param>
    /// <param name="totalMilliseconds">Total acquire-render-present time.</param>
    public PresentationSurfaceFrameTimings(
        double acquireMilliseconds,
        double renderMilliseconds,
        double presentMilliseconds,
        double totalMilliseconds)
    {
        AcquireMilliseconds = acquireMilliseconds;
        RenderMilliseconds = renderMilliseconds;
        PresentMilliseconds = presentMilliseconds;
        TotalMilliseconds = totalMilliseconds;
    }

    /// <summary>Gets time spent acquiring the surface texture.</summary>
    public double AcquireMilliseconds { get; }

    /// <summary>Gets time spent in caller rendering, command encoding, and queue submission.</summary>
    public double RenderMilliseconds { get; }

    /// <summary>Gets time spent presenting and polling backend validation errors.</summary>
    public double PresentMilliseconds { get; }

    /// <summary>Gets total time inside the acquire-render-present operation.</summary>
    public double TotalMilliseconds { get; }
}
