using Mu3D.Maui.Controls;
using Mu3D.Toolkit.Controls;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>
/// Statically attaches a reusable <see cref="ViewportNavigationBehavior"/> to one scene view.
/// </summary>
/// <remarks>
/// The feature and assigned controller/arbiter remain application-owned. Handler teardown removes
/// the behavior but does not dispose those borrowed objects, so the feature can attach again after
/// native-view recreation.
/// </remarks>
public sealed class OrbitSceneViewFeature : BindableObject, ISceneViewFeature
{
    private readonly ViewportNavigationBehavior behavior = new();
    private Mu3DSceneView? attachedView;

    /// <summary>Identifies the <see cref="Controller"/> bindable property.</summary>
    public static readonly BindableProperty ControllerProperty = BindableProperty.Create(
        nameof(Controller),
        typeof(OrbitController),
        typeof(OrbitSceneViewFeature),
        default(OrbitController),
        propertyChanged: static (bindable, _, value) =>
            ((OrbitSceneViewFeature)bindable).behavior.Controller = (OrbitController?)value);

    /// <summary>Identifies the <see cref="ControlArbiter"/> bindable property.</summary>
    public static readonly BindableProperty ControlArbiterProperty = BindableProperty.Create(
        nameof(ControlArbiter),
        typeof(ViewportControlArbiter),
        typeof(OrbitSceneViewFeature),
        default(ViewportControlArbiter),
        propertyChanged: static (bindable, _, value) =>
            ((OrbitSceneViewFeature)bindable).behavior.ControlArbiter =
                (ViewportControlArbiter?)value);

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(OrbitSceneViewFeature),
        true,
        propertyChanged: static (bindable, _, value) =>
            ((OrbitSceneViewFeature)bindable).behavior.IsEnabled = (bool)value);

    /// <summary>Gets or sets the borrowed UI-independent orbit controller.</summary>
    public OrbitController? Controller
    {
        get => (OrbitController?)GetValue(ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    /// <summary>Gets or sets the optional borrowed viewport-control arbiter.</summary>
    public ViewportControlArbiter? ControlArbiter
    {
        get => (ViewportControlArbiter?)GetValue(ControlArbiterProperty);
        set => SetValue(ControlArbiterProperty, value);
    }

    /// <summary>Gets or sets whether orbit input is active.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets the reusable behavior for advanced gesture and hardware-input settings.</summary>
    /// <remarks>Do not add this behavior to another visual while the feature is attached.</remarks>
    public ViewportNavigationBehavior Behavior => behavior;

    /// <inheritdoc />
    public IDisposable Attach(SceneViewFeatureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachedView is not null)
        {
            throw new InvalidOperationException("An orbit feature can attach to only one scene view at a time.");
        }

        Mu3DSceneView view = context.View;
        attachedView = view;
        view.Behaviors.Add(behavior);
        return new Attachment(this, view);
    }

    private void Detach(Mu3DSceneView view)
    {
        if (!ReferenceEquals(attachedView, view))
        {
            return;
        }

        view.Behaviors.Remove(behavior);
        attachedView = null;
    }

    private sealed class Attachment(OrbitSceneViewFeature owner, Mu3DSceneView view) : IDisposable
    {
        private OrbitSceneViewFeature? owner = owner;

        public void Dispose()
        {
            OrbitSceneViewFeature? released = Interlocked.Exchange(ref owner, null);
            released?.Detach(view);
        }
    }
}
