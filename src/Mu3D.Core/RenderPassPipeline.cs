using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering;

/// <summary>Identifies the shared attachments written by an ordered render pass.</summary>
[Flags]
public enum RenderPassOutputs
{
    /// <summary>The pass does not write a shared attachment.</summary>
    None = 0,

    /// <summary>The pass writes and preserves the shared color attachment.</summary>
    Color = 1 << 0,

    /// <summary>The pass writes and preserves the shared depth attachment.</summary>
    Depth = 1 << 1,
}

/// <summary>Describes how one pass initializes and preserves the shared HDR color attachment.</summary>
/// <remarks>
/// <see cref="GraphicsLoadOperation.Load"/> declares an input dependency on color produced before
/// this pass. Clear colors remain explicitly tagged linear-light values; the pass implementation is
/// responsible for converting them into <see cref="RenderPassDescriptor.WorkingColorSpace"/>.
/// </remarks>
public sealed class RenderPassColorAttachmentPolicy
{
    /// <summary>Initializes an ordered color-attachment policy.</summary>
    /// <param name="loadOperation">Whether prior color is loaded or replaced.</param>
    /// <param name="clearColor">The tagged linear-light value used when clearing.</param>
    public RenderPassColorAttachmentPolicy(
        GraphicsLoadOperation loadOperation,
        LinearRgba clearColor)
    {
        if (!Enum.IsDefined(loadOperation))
        {
            throw new ArgumentOutOfRangeException(nameof(loadOperation));
        }
        if (clearColor.ColorSpace is null)
        {
            throw new ArgumentException(
                "A render-pass clear color must have an explicit linear color-space identity.",
                nameof(clearColor));
        }

        LoadOperation = loadOperation;
        ClearColor = clearColor;
    }

    /// <summary>Gets whether prior color is loaded or replaced.</summary>
    public GraphicsLoadOperation LoadOperation { get; }

    /// <summary>Gets the tagged linear-light value used when clearing.</summary>
    public LinearRgba ClearColor { get; }
}

/// <summary>Describes how one pass initializes and preserves the shared depth attachment.</summary>
/// <remarks>
/// <see cref="GraphicsLoadOperation.Load"/> declares an input dependency on depth produced before
/// this pass.
/// </remarks>
public sealed class RenderPassDepthAttachmentPolicy
{
    /// <summary>Initializes an ordered depth-attachment policy.</summary>
    /// <param name="loadOperation">Whether prior depth is loaded or replaced.</param>
    /// <param name="clearDepth">The normalized value used when clearing.</param>
    public RenderPassDepthAttachmentPolicy(
        GraphicsLoadOperation loadOperation,
        float clearDepth = 1f)
    {
        if (!Enum.IsDefined(loadOperation))
        {
            throw new ArgumentOutOfRangeException(nameof(loadOperation));
        }
        if (!float.IsFinite(clearDepth) || clearDepth is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clearDepth),
                "A depth clear value must be finite and between zero and one.");
        }

        LoadOperation = loadOperation;
        ClearDepth = clearDepth;
    }

    /// <summary>Gets whether prior depth is loaded or replaced.</summary>
    public GraphicsLoadOperation LoadOperation { get; }

    /// <summary>Gets the normalized value used when clearing.</summary>
    public float ClearDepth { get; }
}

/// <summary>
/// Declares one statically composed pass's HDR working space, attachment behavior, output, and size
/// dependency.
/// </summary>
/// <remarks>
/// A non-null attachment policy declares that the pass writes and preserves that shared attachment.
/// Loading additionally declares an input dependency on an earlier pass or initialized caller-owned
/// content. This initial composition contract is deliberately scene-linear; it performs no implicit
/// display encoding, gamut mapping, tone mapping, or clipping.
/// </remarks>
public sealed class RenderPassDescriptor
{
    /// <summary>Initializes an immutable ordered render-pass declaration.</summary>
    /// <param name="name">The stable diagnostic pass name.</param>
    /// <param name="workingColorSpace">The linear-light space read and written by the pass.</param>
    /// <param name="colorAttachment">Shared color behavior, or null when color is untouched.</param>
    /// <param name="depthAttachment">Shared depth behavior, or null when depth is untouched.</param>
    /// <param name="sizeDependency">How the pass derives its attachment extent.</param>
    public RenderPassDescriptor(
        string name,
        ColorSpaceReference workingColorSpace,
        RenderPassColorAttachmentPolicy? colorAttachment = null,
        RenderPassDepthAttachmentPolicy? depthAttachment = null,
        RenderPassSizeDependency sizeDependency = RenderPassSizeDependency.OutputExtent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(workingColorSpace);
        if (!workingColorSpace.IsLinear)
        {
            throw new ArgumentException(
                "The initial render-pass pipeline requires a linear-light working color space.",
                nameof(workingColorSpace));
        }
        if (colorAttachment is null && depthAttachment is null)
        {
            throw new ArgumentException(
                "A render pass must declare a shared color or depth output.",
                nameof(colorAttachment));
        }
        if (!Enum.IsDefined(sizeDependency))
        {
            throw new ArgumentOutOfRangeException(nameof(sizeDependency));
        }

        Name = name;
        WorkingColorSpace = workingColorSpace;
        ColorAttachment = colorAttachment;
        DepthAttachment = depthAttachment;
        SizeDependency = sizeDependency;
        Outputs =
            (colorAttachment is null ? RenderPassOutputs.None : RenderPassOutputs.Color) |
            (depthAttachment is null ? RenderPassOutputs.None : RenderPassOutputs.Depth);
    }

    /// <summary>Gets the stable diagnostic pass name.</summary>
    public string Name { get; }

    /// <summary>Gets the linear-light color space read and written by the pass.</summary>
    public ColorSpaceReference WorkingColorSpace { get; }

    /// <summary>Gets shared color behavior, or null when color is untouched.</summary>
    public RenderPassColorAttachmentPolicy? ColorAttachment { get; }

    /// <summary>Gets shared depth behavior, or null when depth is untouched.</summary>
    public RenderPassDepthAttachmentPolicy? DepthAttachment { get; }

    /// <summary>Gets the shared attachments written and preserved by the pass.</summary>
    public RenderPassOutputs Outputs { get; }

    /// <summary>Gets how the pass derives its attachment extent.</summary>
    public RenderPassSizeDependency SizeDependency { get; }
}

/// <summary>Supplies one ordered pass with application scene state and shared output attachments.</summary>
public readonly struct RenderPassContext
{
    /// <summary>Initializes one validated render-pass execution context.</summary>
    /// <param name="scene">The application-owned scene for this frame.</param>
    /// <param name="camera">The application-owned camera for this frame.</param>
    /// <param name="colorTarget">The shared single-sampled color attachment.</param>
    /// <param name="depthTarget">The optional matching Depth32Float attachment.</param>
    /// <param name="workingColorSpace">The linear-light space retained through composition.</param>
    /// <param name="colorTargetInitialized">Whether color may be loaded by the first pass.</param>
    /// <param name="depthTargetInitialized">Whether depth may be loaded by the first pass.</param>
    public RenderPassContext(
        Scene scene,
        Camera camera,
        GraphicsTexture colorTarget,
        GraphicsTexture? depthTarget,
        ColorSpaceReference workingColorSpace,
        bool colorTargetInitialized = false,
        bool depthTargetInitialized = false)
        : this(
            scene,
            camera,
            colorTarget,
            depthTarget,
            workingColorSpace,
            colorTargetInitialized,
            depthTargetInitialized,
            validate: true)
    {
    }

    private RenderPassContext(
        Scene scene,
        Camera camera,
        GraphicsTexture colorTarget,
        GraphicsTexture? depthTarget,
        ColorSpaceReference workingColorSpace,
        bool colorTargetInitialized,
        bool depthTargetInitialized,
        bool validate)
    {
        Scene = scene;
        Camera = camera;
        ColorTarget = colorTarget;
        DepthTarget = depthTarget;
        WorkingColorSpace = workingColorSpace;
        ColorTargetInitialized = colorTargetInitialized;
        DepthTargetInitialized = depthTargetInitialized;
        if (validate)
        {
            Validate();
        }
    }

    /// <summary>Gets the application-owned scene for this frame.</summary>
    public Scene Scene { get; }

    /// <summary>Gets the application-owned camera for this frame.</summary>
    public Camera Camera { get; }

    /// <summary>Gets the device that owns the shared attachments.</summary>
    public GraphicsDevice Device => ColorTarget.Device;

    /// <summary>Gets the shared color attachment.</summary>
    public GraphicsTexture ColorTarget { get; }

    /// <summary>Gets the optional shared depth attachment.</summary>
    public GraphicsTexture? DepthTarget { get; }

    /// <summary>Gets the shared output extent.</summary>
    public GraphicsExtent3D OutputExtent => ColorTarget.Descriptor.Size;

    /// <summary>Gets the linear-light space retained through composition.</summary>
    public ColorSpaceReference WorkingColorSpace { get; }

    /// <summary>Gets whether color may be loaded at this point in the chain.</summary>
    public bool ColorTargetInitialized { get; }

    /// <summary>Gets whether depth may be loaded at this point in the chain.</summary>
    public bool DepthTargetInitialized { get; }

    internal RenderPassContext WithAttachmentState(
        bool colorTargetInitialized,
        bool depthTargetInitialized) => new(
            Scene,
            Camera,
            ColorTarget,
            DepthTarget,
            WorkingColorSpace,
            colorTargetInitialized,
            depthTargetInitialized,
            validate: false);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Scene);
        ArgumentNullException.ThrowIfNull(Camera);
        ArgumentNullException.ThrowIfNull(ColorTarget);
        ArgumentNullException.ThrowIfNull(WorkingColorSpace);
        if (!WorkingColorSpace.IsLinear)
        {
            throw new ArgumentException(
                "Ordered rendering requires a linear-light working color space.",
                nameof(WorkingColorSpace));
        }
        ObjectDisposedException.ThrowIf(ColorTarget.IsDisposed, ColorTarget);
        GraphicsTextureDescriptor color = ColorTarget.Descriptor;
        if ((color.Usage & GraphicsTextureUsage.RenderAttachment) == 0 ||
            color.Format is GraphicsTextureFormat.Depth32Float or
                GraphicsTextureFormat.Depth32FloatStencil8 ||
            color.SampleCount != 1 ||
            color.Size.DepthOrArrayLayers != 1)
        {
            throw new ArgumentException(
                "The color target must be a single-sampled, non-array color render attachment.",
                nameof(ColorTarget));
        }
        if (DepthTarget is null)
        {
            if (DepthTargetInitialized)
            {
                throw new ArgumentException(
                    "Depth cannot be initialized when no depth target is supplied.",
                    nameof(DepthTargetInitialized));
            }
            return;
        }

        ObjectDisposedException.ThrowIf(DepthTarget.IsDisposed, DepthTarget);
        GraphicsTextureDescriptor depth = DepthTarget.Descriptor;
        if (!ReferenceEquals(DepthTarget.Device, ColorTarget.Device) ||
            (depth.Usage & GraphicsTextureUsage.RenderAttachment) == 0 ||
            depth.Format != GraphicsTextureFormat.Depth32Float ||
            depth.SampleCount != 1 ||
            depth.Size.DepthOrArrayLayers != 1 ||
            depth.Size != color.Size)
        {
            throw new ArgumentException(
                "The depth target must be a matching single-sampled Depth32Float render attachment on the color device.",
                nameof(DepthTarget));
        }
    }
}

/// <summary>Defines one explicitly constructed pass in an ordered HDR-linear render pipeline.</summary>
public interface IRenderPass
{
    /// <summary>Gets the immutable attachment and working-space declaration snapshotted by a pipeline.</summary>
    RenderPassDescriptor Descriptor { get; }

    /// <summary>Executes the pass against the supplied shared attachments.</summary>
    /// <param name="context">The current scene, camera, targets, and attachment state.</param>
    void Execute(RenderPassContext context);
}

/// <summary>Reports which shared attachments contain preserved output after pipeline execution.</summary>
public readonly record struct RenderPassExecutionResult
{
    internal RenderPassExecutionResult(
        bool colorTargetInitialized,
        bool depthTargetInitialized)
    {
        ColorTargetInitialized = colorTargetInitialized;
        DepthTargetInitialized = depthTargetInitialized;
    }

    /// <summary>Gets whether the shared color attachment contains preserved output.</summary>
    public bool ColorTargetInitialized { get; }

    /// <summary>Gets whether the shared depth attachment contains preserved output.</summary>
    public bool DepthTargetInitialized { get; }
}

/// <summary>
/// Executes an immutable, explicitly supplied sequence of backend-independent render passes.
/// </summary>
/// <remarks>
/// The pipeline snapshots pass descriptors at construction, validates attachment Load dependencies
/// before each call, and never owns or disposes the supplied passes. All passes must retain the
/// context's scene-linear working space; use the low-level graphics boundary for an explicitly
/// authored display transform.
/// </remarks>
public sealed class RenderPassPipeline
{
    private readonly IRenderPass[] passes;
    private readonly RenderPassDescriptor[] descriptors;

    /// <summary>Initializes an immutable ordered pass sequence.</summary>
    /// <param name="passes">The explicitly constructed passes in execution order.</param>
    public RenderPassPipeline(IEnumerable<IRenderPass> passes)
    {
        ArgumentNullException.ThrowIfNull(passes);
        this.passes = passes.ToArray();
        if (this.passes.Length == 0)
        {
            throw new ArgumentException("A render-pass pipeline requires at least one pass.", nameof(passes));
        }

        descriptors = new RenderPassDescriptor[this.passes.Length];
        for (int index = 0; index < this.passes.Length; index++)
        {
            IRenderPass pass = this.passes[index] ??
                throw new ArgumentException("A render-pass pipeline cannot contain null.", nameof(passes));
            descriptors[index] = pass.Descriptor ??
                throw new ArgumentException("A render pass returned a null descriptor.", nameof(passes));
        }

        Passes = Array.AsReadOnly(this.passes);
        Descriptors = Array.AsReadOnly(descriptors);
    }

    /// <summary>Gets the application-owned passes in execution order.</summary>
    public IReadOnlyList<IRenderPass> Passes { get; }

    /// <summary>Gets the immutable pass declarations snapshotted at construction.</summary>
    public IReadOnlyList<RenderPassDescriptor> Descriptors { get; }

    /// <summary>Executes every pass in order after validating its declared input dependencies.</summary>
    /// <param name="context">The scene, camera, shared targets, and initial attachment state.</param>
    /// <returns>The shared attachments containing preserved output after the final pass.</returns>
    public RenderPassExecutionResult Execute(RenderPassContext context)
    {
        context.Validate();
        bool colorInitialized = context.ColorTargetInitialized;
        bool depthInitialized = context.DepthTargetInitialized;

        for (int index = 0; index < passes.Length; index++)
        {
            RenderPassDescriptor descriptor = descriptors[index];
            if (descriptor.WorkingColorSpace != context.WorkingColorSpace)
            {
                throw new InvalidOperationException(
                    $"Render pass '{descriptor.Name}' uses {descriptor.WorkingColorSpace.Name}, but the pipeline context uses {context.WorkingColorSpace.Name}.");
            }
            if (descriptor.ColorAttachment is RenderPassColorAttachmentPolicy color &&
                color.LoadOperation == GraphicsLoadOperation.Load &&
                !colorInitialized)
            {
                throw new InvalidOperationException(
                    $"Render pass '{descriptor.Name}' cannot load an uninitialized color attachment.");
            }
            if (descriptor.DepthAttachment is RenderPassDepthAttachmentPolicy depth)
            {
                if (context.DepthTarget is null)
                {
                    throw new InvalidOperationException(
                        $"Render pass '{descriptor.Name}' requires a depth attachment.");
                }
                if (depth.LoadOperation == GraphicsLoadOperation.Load && !depthInitialized)
                {
                    throw new InvalidOperationException(
                        $"Render pass '{descriptor.Name}' cannot load an uninitialized depth attachment.");
                }
            }

            passes[index].Execute(context.WithAttachmentState(colorInitialized, depthInitialized));
            colorInitialized |= descriptor.ColorAttachment is not null;
            depthInitialized |= descriptor.DepthAttachment is not null;
        }

        return new RenderPassExecutionResult(colorInitialized, depthInitialized);
    }
}
