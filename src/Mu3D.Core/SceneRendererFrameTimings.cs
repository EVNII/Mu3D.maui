namespace Mu3D.Rendering;

/// <summary>Describes CPU wall-clock intervals around one scene-renderer invocation.</summary>
public readonly record struct SceneRendererFrameTimings
{
    internal SceneRendererFrameTimings(
        double prepareMilliseconds,
        double encodeMilliseconds,
        double submitMilliseconds,
        double cacheTrimMilliseconds,
        double totalMilliseconds)
    {
        PrepareMilliseconds = prepareMilliseconds;
        EncodeMilliseconds = encodeMilliseconds;
        SubmitMilliseconds = submitMilliseconds;
        CacheTrimMilliseconds = cacheTrimMilliseconds;
        TotalMilliseconds = totalMilliseconds;
    }

    /// <summary>Gets time spent preparing scene state, resources, uniforms, and draw records.</summary>
    public double PrepareMilliseconds { get; }

    /// <summary>Gets time spent encoding and finishing the graphics command buffer.</summary>
    public double EncodeMilliseconds { get; }

    /// <summary>Gets time spent submitting the command buffer to the backend queue.</summary>
    public double SubmitMilliseconds { get; }

    /// <summary>Gets time spent finding and releasing cache entries absent from the scene.</summary>
    public double CacheTrimMilliseconds { get; }

    /// <summary>Gets total time inside the renderer invocation.</summary>
    public double TotalMilliseconds { get; }
}
