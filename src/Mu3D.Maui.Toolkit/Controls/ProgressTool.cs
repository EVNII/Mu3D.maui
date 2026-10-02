using System.Collections.ObjectModel;
using System.ComponentModel;
using Mu3D.Maui.Controls;
using Mu3D.Toolkit.Animation;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Identifies one bindable declarative scene-node transform component.</summary>
public enum SceneNodeProgressProperty
{
    /// <summary>Local X translation.</summary>
    X,

    /// <summary>Local Y translation.</summary>
    Y,

    /// <summary>Local Z translation.</summary>
    Z,

    /// <summary>Local X rotation in degrees.</summary>
    RotationX,

    /// <summary>Local Y rotation in degrees.</summary>
    RotationY,

    /// <summary>Local Z rotation in degrees.</summary>
    RotationZ,

    /// <summary>Local X scale.</summary>
    ScaleX,

    /// <summary>Local Y scale.</summary>
    ScaleY,

    /// <summary>Local Z scale.</summary>
    ScaleZ,
}

/// <summary>Linearly maps normalized progress onto one explicit declarative node component.</summary>
/// <remarks>
/// A camera is also a <see cref="SceneNode3D"/>, so the same element expresses camera paths.
/// Setting the target's bindable property uses the normal declarative-scene invalidation path.
/// </remarks>
public sealed class NodeTransformProgress : BindableObject, IViewportProgressMapping
{
    /// <summary>Identifies the <see cref="Target"/> bindable property.</summary>
    public static readonly BindableProperty TargetProperty = BindableProperty.Create(
        nameof(Target),
        typeof(SceneNode3D),
        typeof(NodeTransformProgress),
        default(SceneNode3D));

    /// <summary>Identifies the <see cref="Property"/> bindable property.</summary>
    public static readonly BindableProperty PropertyProperty = BindableProperty.Create(
        nameof(Property),
        typeof(SceneNodeProgressProperty),
        typeof(NodeTransformProgress),
        SceneNodeProgressProperty.X,
        validateValue: static (_, value) => value is SceneNodeProgressProperty property &&
            Enum.IsDefined(property));

    /// <summary>Identifies the <see cref="From"/> bindable property.</summary>
    public static readonly BindableProperty FromProperty = CreateFiniteProperty(nameof(From), 0f);

    /// <summary>Identifies the <see cref="To"/> bindable property.</summary>
    public static readonly BindableProperty ToProperty = CreateFiniteProperty(nameof(To), 1f);

    /// <summary>Gets or sets the explicit declarative node or camera to mutate.</summary>
    public SceneNode3D? Target
    {
        get => (SceneNode3D?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>Gets or sets the transform component driven by progress.</summary>
    public SceneNodeProgressProperty Property
    {
        get => (SceneNodeProgressProperty)GetValue(PropertyProperty);
        set => SetValue(PropertyProperty, value);
    }

    /// <summary>Gets or sets the value applied at progress zero.</summary>
    public float From
    {
        get => (float)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>Gets or sets the value applied at progress one.</summary>
    public float To
    {
        get => (float)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <inheritdoc />
    public void Apply(double progress)
    {
        SceneNode3D target = Target ??
            throw new InvalidOperationException("NodeTransformProgress requires an explicit Target.");
        float value = From + ((To - From) * (float)progress);
        switch (Property)
        {
            case SceneNodeProgressProperty.X:
                target.X = value;
                break;
            case SceneNodeProgressProperty.Y:
                target.Y = value;
                break;
            case SceneNodeProgressProperty.Z:
                target.Z = value;
                break;
            case SceneNodeProgressProperty.RotationX:
                target.RotationX = value;
                break;
            case SceneNodeProgressProperty.RotationY:
                target.RotationY = value;
                break;
            case SceneNodeProgressProperty.RotationZ:
                target.RotationZ = value;
                break;
            case SceneNodeProgressProperty.ScaleX:
                target.ScaleX = value;
                break;
            case SceneNodeProgressProperty.ScaleY:
                target.ScaleY = value;
                break;
            case SceneNodeProgressProperty.ScaleZ:
                target.ScaleZ = value;
                break;
            default:
                throw new InvalidOperationException($"Unknown node progress property {Property}.");
        }
    }

    private static BindableProperty CreateFiniteProperty(string name, float defaultValue) =>
        BindableProperty.Create(
            name,
            typeof(float),
            typeof(NodeTransformProgress),
            defaultValue,
            validateValue: static (_, value) => value is float number && float.IsFinite(number));
}

/// <summary>
/// Bridges one bindable application progress value into ordered viewport mappings.
/// </summary>
/// <remarks>
/// Bind <see cref="Progress"/> from a view model, assign it from a `Scrolled` callback, or animate
/// it with MAUI. This tool owns no source clock, `ScrollView`, navigation policy, target, or mapping.
/// It applies every mapping and requests one coalesced frame only while attached and enabled.
/// </remarks>
[ContentProperty(nameof(Mappings))]
public sealed class ProgressTool : BindableObject, IViewportTool
{
    private readonly MappingCollection mappings;
    private ToolAttachment? attachment;

    /// <summary>Identifies the <see cref="Progress"/> bindable property.</summary>
    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress),
        typeof(double),
        typeof(ProgressTool),
        0d,
        validateValue: static (_, value) => value is double number && double.IsFinite(number),
        propertyChanged: static (bindable, _, value) =>
            ((ProgressTool)bindable).ApplyProgress((double)value));

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(ProgressTool),
        true,
        propertyChanged: static (bindable, _, value) =>
            ((ProgressTool)bindable).ApplyEnabled((bool)value));

    /// <summary>Initializes an empty progress-mapping tool.</summary>
    public ProgressTool() => mappings = new MappingCollection(this);

    /// <summary>Gets or sets finite application progress; values are clamped to zero through one.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Gets or sets whether mappings are applied and frames requested.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets the ordered mappings populated by nested XAML elements.</summary>
    public IList<IViewportProgressMapping> Mappings => mappings;

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException("A ProgressTool can attach only once at a time.");
        }

        ToolAttachment created = new(this, context);
        attachment = created;
        AttachMappingObservers();
        try
        {
            created.Rebuild();
        }
        catch
        {
            attachment = null;
            created.Dispose();
            throw;
        }
        return created;
    }

    private void ApplyProgress(double value)
    {
        if (IsEnabled)
        {
            attachment?.Controller?.SetProgress(value);
        }
    }

    private void ApplyEnabled(bool enabled)
    {
        if (enabled)
        {
            attachment?.ApplyCurrent();
        }
    }

    private void OnMappingConfigurationChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (IsEnabled)
        {
            attachment?.ApplyCurrent();
        }
    }

    private void RefreshMappings()
    {
        if (attachment is not null)
        {
            attachment.Rebuild();
        }
    }

    private void AttachMappingObservers()
    {
        foreach (IViewportProgressMapping mapping in mappings)
        {
            mappings.Subscribe(mapping);
        }
    }

    private void DetachMappingObservers()
    {
        foreach (IViewportProgressMapping mapping in mappings)
        {
            mappings.Unsubscribe(mapping);
        }
    }

    private sealed class MappingCollection(ProgressTool owner) : Collection<IViewportProgressMapping>
    {
        protected override void InsertItem(int index, IViewportProgressMapping item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (this.Any(candidate => ReferenceEquals(candidate, item)))
            {
                throw new InvalidOperationException("A progress mapping cannot appear twice in one tool.");
            }
            base.InsertItem(index, item);
            if (owner.attachment is not null)
            {
                Subscribe(item);
            }
            owner.RefreshMappings();
        }

        protected override void SetItem(int index, IViewportProgressMapping item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (ReferenceEquals(this[index], item))
            {
                return;
            }
            if (this.Any(candidate => ReferenceEquals(candidate, item)))
            {
                throw new InvalidOperationException("A progress mapping cannot appear twice in one tool.");
            }
            IViewportProgressMapping removed = this[index];
            Unsubscribe(removed);
            base.SetItem(index, item);
            if (owner.attachment is not null)
            {
                Subscribe(item);
            }
            owner.RefreshMappings();
        }

        protected override void RemoveItem(int index)
        {
            IViewportProgressMapping removed = this[index];
            Unsubscribe(removed);
            base.RemoveItem(index);
            owner.RefreshMappings();
        }

        protected override void ClearItems()
        {
            if (Count == 0)
            {
                return;
            }
            foreach (IViewportProgressMapping mapping in this)
            {
                Unsubscribe(mapping);
            }
            base.ClearItems();
            owner.RefreshMappings();
        }

        internal void Subscribe(IViewportProgressMapping mapping)
        {
            if (mapping is INotifyPropertyChanged observable)
            {
                observable.PropertyChanged += owner.OnMappingConfigurationChanged;
            }
        }

        internal void Unsubscribe(IViewportProgressMapping mapping)
        {
            if (mapping is INotifyPropertyChanged observable)
            {
                observable.PropertyChanged -= owner.OnMappingConfigurationChanged;
            }
        }
    }

    private sealed class ToolAttachment(ProgressTool owner, ViewportToolContext context) : IDisposable
    {
        private bool disposed;

        internal ViewportProgressController? Controller { get; private set; }

        internal void Rebuild()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Controller = new ViewportProgressController(
                new ViewportToolFrameRequester(context),
                owner.Mappings);
            if (owner.IsEnabled)
            {
                ApplyCurrent();
            }
        }

        internal void ApplyCurrent()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ViewportProgressController controller = Controller ??
                throw new InvalidOperationException("The progress controller is not initialized.");
            if (!controller.SetProgress(owner.Progress))
            {
                controller.Reapply();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Controller = null;
            owner.DetachMappingObservers();
            if (ReferenceEquals(owner.attachment, this))
            {
                owner.attachment = null;
            }
        }
    }
}
