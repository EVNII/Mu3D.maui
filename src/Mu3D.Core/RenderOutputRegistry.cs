namespace Mu3D.Rendering;

/// <summary>Creates an explicitly registered render pass for one output identifier.</summary>
/// <param name="renderer">The borrowed renderer associated with the current surface.</param>
/// <param name="options">The attachment clear or load policy.</param>
/// <param name="name">The stable diagnostic pass name.</param>
/// <returns>A new caller-owned render pass.</returns>
public delegate IRenderPass RenderOutputPassFactory(
    SceneRenderer renderer,
    SceneRenderPassOptions options,
    string name);

/// <summary>Maps stable render-output identifiers to explicit, AOT-safe pass factories.</summary>
/// <remarks>
/// Registration is application controlled and performs no reflection or assembly discovery. The
/// registry is intended to be configured on the UI/render coordination thread before rendering; it
/// is not thread-safe. A caller owns each pass returned by <see cref="CreatePass"/> and must dispose
/// it when it also implements <see cref="IDisposable"/>.
/// </remarks>
public sealed class RenderOutputRegistry
{
    private readonly Dictionary<RenderOutputId, RenderOutputPassFactory> factories = [];

    /// <summary>Gets a revision that changes after every successful registration or removal.</summary>
    public ulong Revision { get; private set; }

    /// <summary>Gets the currently registered identifiers in registration order.</summary>
    /// <remarks>Do not enumerate while changing registrations from another thread.</remarks>
    public IReadOnlyCollection<RenderOutputId> Outputs => factories.Keys;

    /// <summary>Creates a registry containing all outputs implemented by <see cref="SceneRenderer"/>.</summary>
    /// <returns>A mutable registry with the built-in factories registered.</returns>
    public static RenderOutputRegistry CreateDefault()
    {
        RenderOutputRegistry registry = new();
        foreach (SceneRenderLayer layer in Enum.GetValues<SceneRenderLayer>())
        {
            RenderOutputId output = RenderOutputIds.FromSceneRenderLayer(layer);
            registry.Register(
                output,
                (renderer, options, name) =>
                    new SceneRenderOutputPass(renderer, output, options, name));
        }
        return registry;
    }

    /// <summary>Registers one pass factory.</summary>
    /// <param name="output">The stable output identifier.</param>
    /// <param name="factory">The explicit pass factory.</param>
    /// <param name="replace">Whether an existing registration may be replaced.</param>
    public void Register(
        RenderOutputId output,
        RenderOutputPassFactory factory,
        bool replace = false)
    {
        Validate(output);
        ArgumentNullException.ThrowIfNull(factory);
        if (!replace && factories.ContainsKey(output))
        {
            throw new InvalidOperationException(
                $"Render output '{output}' already has a registered factory.");
        }

        factories[output] = factory;
        Revision = Revision == ulong.MaxValue ? 0 : Revision + 1;
    }

    /// <summary>Removes a pass factory without disposing any pass it previously created.</summary>
    /// <param name="output">The stable output identifier.</param>
    /// <returns>True when a registration was removed.</returns>
    public bool Remove(RenderOutputId output)
    {
        Validate(output);
        if (!factories.Remove(output))
        {
            return false;
        }

        Revision = Revision == ulong.MaxValue ? 0 : Revision + 1;
        return true;
    }

    /// <summary>Gets whether a pass factory is registered for an output.</summary>
    /// <param name="output">The stable output identifier.</param>
    /// <returns>True when the output can be created.</returns>
    public bool Contains(RenderOutputId output)
    {
        Validate(output);
        return factories.ContainsKey(output);
    }

    /// <summary>Creates a pass through the factory registered for an output.</summary>
    /// <param name="output">The stable output identifier.</param>
    /// <param name="renderer">The borrowed renderer associated with the current surface.</param>
    /// <param name="options">The attachment clear or load policy.</param>
    /// <param name="name">The stable diagnostic pass name.</param>
    /// <returns>The newly created caller-owned pass.</returns>
    public IRenderPass CreatePass(
        RenderOutputId output,
        SceneRenderer renderer,
        SceneRenderPassOptions options,
        string name)
    {
        Validate(output);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!factories.TryGetValue(output, out RenderOutputPassFactory? factory))
        {
            throw new KeyNotFoundException(
                $"No render-output factory is registered for '{output}'.");
        }

        return factory(renderer, options, name) ??
            throw new InvalidOperationException(
                $"The render-output factory for '{output}' returned null.");
    }

    private static void Validate(RenderOutputId output)
    {
        if (!output.IsValid)
        {
            throw new ArgumentException(
                "A render-output identifier must be initialized.",
                nameof(output));
        }
    }
}
