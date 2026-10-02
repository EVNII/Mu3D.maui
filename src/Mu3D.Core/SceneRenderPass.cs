using Mu3D.Graphics;

namespace Mu3D.Rendering;

/// <summary>Adapts one <see cref="SceneRenderer"/> invocation to ordered render composition.</summary>
/// <remarks>
/// The renderer and scene-pass options are borrowed and are not disposed by this pass. The caller
/// must keep the renderer alive and must not execute it concurrently from another thread.
/// </remarks>
public sealed class SceneRenderPass : IRenderPass
{
    /// <summary>Initializes a scene pass with the renderer's default clear policy.</summary>
    /// <param name="renderer">The borrowed scene renderer.</param>
    /// <param name="name">The stable diagnostic pass name.</param>
    public SceneRenderPass(SceneRenderer renderer, string name = "Scene")
        : this(renderer, SceneRenderPassOptions.Default, name)
    {
    }

    /// <summary>Initializes a scene pass with explicit attachment behavior.</summary>
    /// <param name="renderer">The borrowed scene renderer.</param>
    /// <param name="options">The immutable color/depth clear or load policy.</param>
    /// <param name="name">The stable diagnostic pass name.</param>
    public SceneRenderPass(
        SceneRenderer renderer,
        SceneRenderPassOptions options,
        string name = "Scene")
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(options);
        Renderer = renderer;
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
                "The scene pass and composition context use different working color spaces.");
        }
        if (!ReferenceEquals(context.Device, Renderer.Device))
        {
            throw new InvalidOperationException(
                "The scene pass renderer and composition attachments belong to different devices.");
        }
        if (context.ColorTarget.Descriptor.Format != Renderer.ColorFormat)
        {
            throw new InvalidOperationException(
                "The scene pass renderer and composition color target use different formats.");
        }
        GraphicsTexture depthTarget = context.DepthTarget ??
            throw new InvalidOperationException("A scene pass requires a depth attachment.");

        Renderer.RenderPass(
            context.Scene,
            context.Camera,
            context.ColorTarget,
            depthTarget,
            Options);
    }
}
