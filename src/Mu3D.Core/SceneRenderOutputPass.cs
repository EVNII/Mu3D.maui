using Mu3D.Graphics;

namespace Mu3D.Rendering;

/// <summary>Renders one explicit built-in output without mutating renderer-wide compatibility state.</summary>
/// <remarks>
/// The renderer and options are borrowed and are not disposed by this pass. Use
/// <see cref="RenderOutputRegistry"/> when third-party output identifiers need different pass
/// implementations.
/// </remarks>
public sealed class SceneRenderOutputPass : IRenderPass
{
    /// <summary>Initializes a built-in output pass with the default attachment clear policy.</summary>
    /// <param name="renderer">The borrowed scene renderer.</param>
    /// <param name="output">A built-in output identifier.</param>
    /// <param name="name">The stable diagnostic pass name.</param>
    public SceneRenderOutputPass(
        SceneRenderer renderer,
        RenderOutputId output,
        string name = "Scene output")
        : this(renderer, output, SceneRenderPassOptions.Default, name)
    {
    }

    /// <summary>Initializes a built-in output pass with explicit attachment behavior.</summary>
    /// <param name="renderer">The borrowed scene renderer.</param>
    /// <param name="output">A built-in output identifier.</param>
    /// <param name="options">The immutable color/depth clear or load policy.</param>
    /// <param name="name">The stable diagnostic pass name.</param>
    public SceneRenderOutputPass(
        SceneRenderer renderer,
        RenderOutputId output,
        SceneRenderPassOptions options,
        string name = "Scene output")
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(options);
        if (!RenderOutputIds.TryGetSceneRenderLayer(output, out _))
        {
            throw new ArgumentException(
                $"Render output '{output}' is not implemented by SceneRenderer.",
                nameof(output));
        }

        Renderer = renderer;
        Output = output;
        Options = options;
        Descriptor = new RenderPassDescriptor(
            name,
            renderer.WorkingColorSpace,
            new RenderPassColorAttachmentPolicy(
                options.ColorLoadOperation,
                options.ClearColor),
            new RenderPassDepthAttachmentPolicy(
                options.DepthLoadOperation,
                options.ClearDepth),
            options.SizeDependency);
    }

    /// <summary>Gets the borrowed renderer used by this pass.</summary>
    public SceneRenderer Renderer { get; }

    /// <summary>Gets the stable output rendered by this pass.</summary>
    public RenderOutputId Output { get; }

    /// <summary>Gets the immutable scene attachment behavior.</summary>
    public SceneRenderPassOptions Options { get; }

    /// <inheritdoc />
    public RenderPassDescriptor Descriptor { get; }

    /// <inheritdoc />
    public void Execute(RenderPassContext context)
    {
        context.Validate();
        if (context.WorkingColorSpace != Renderer.WorkingColorSpace)
        {
            throw new InvalidOperationException(
                "The scene-output pass and composition context use different working color spaces.");
        }
        if (!ReferenceEquals(context.Device, Renderer.Device))
        {
            throw new InvalidOperationException(
                "The scene-output renderer and composition attachments belong to different devices.");
        }
        if (context.ColorTarget.Descriptor.Format != Renderer.ColorFormat)
        {
            throw new InvalidOperationException(
                "The scene-output renderer and composition color target use different formats.");
        }
        GraphicsTexture depthTarget = context.DepthTarget ??
            throw new InvalidOperationException("A scene-output pass requires a depth attachment.");

        Renderer.RenderOutputPass(
            context.Scene,
            context.Camera,
            context.ColorTarget,
            depthTarget,
            Output,
            Options);
    }
}
