using Mu3D.SceneGraph;

namespace Mu3D.Maui.Controls;

/// <summary>Declares a right-handed perspective camera with degree-based XAML settings.</summary>
public sealed class PerspectiveCamera3D : Camera3D
{
    /// <summary>Identifies the <see cref="FieldOfView"/> bindable property.</summary>
    public static readonly BindableProperty FieldOfViewProperty = BindableProperty.Create(
        nameof(FieldOfView),
        typeof(float),
        typeof(PerspectiveCamera3D),
        60f,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number > 0f && number < 180f,
        propertyChanged: static (bindable, _, value) =>
        {
            PerspectiveCamera3D camera = (PerspectiveCamera3D)bindable;
            camera.PerspectiveCamera.FieldOfViewRadians = (float)value * (MathF.PI / 180f);
            camera.NotifyChanged();
        });

    /// <summary>Identifies the <see cref="NearClip"/> bindable property.</summary>
    public static readonly BindableProperty NearClipProperty = BindableProperty.Create(
        nameof(NearClip),
        typeof(float),
        typeof(PerspectiveCamera3D),
        0.1f,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number > 0f,
        propertyChanged: static (bindable, _, value) =>
        {
            PerspectiveCamera3D camera = (PerspectiveCamera3D)bindable;
            camera.PerspectiveCamera.NearClip = (float)value;
            camera.NotifyChanged();
        });

    /// <summary>Identifies the <see cref="FarClip"/> bindable property.</summary>
    public static readonly BindableProperty FarClipProperty = BindableProperty.Create(
        nameof(FarClip),
        typeof(float),
        typeof(PerspectiveCamera3D),
        1000f,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number > 0f,
        propertyChanged: static (bindable, _, value) =>
        {
            PerspectiveCamera3D camera = (PerspectiveCamera3D)bindable;
            camera.PerspectiveCamera.FarClip = (float)value;
            camera.NotifyChanged();
        });

    /// <summary>Initializes a perspective camera with a 60-degree vertical field of view.</summary>
    public PerspectiveCamera3D()
        : base(new PerspectiveCamera())
    {
    }

    /// <summary>Gets or sets the vertical field of view in degrees.</summary>
    public float FieldOfView
    {
        get => (float)GetValue(FieldOfViewProperty);
        set => SetValue(FieldOfViewProperty, value);
    }

    /// <summary>Gets or sets the positive near clipping distance.</summary>
    public float NearClip
    {
        get => (float)GetValue(NearClipProperty);
        set => SetValue(NearClipProperty, value);
    }

    /// <summary>Gets or sets the far clipping distance, which must exceed <see cref="NearClip"/>.</summary>
    public float FarClip
    {
        get => (float)GetValue(FarClipProperty);
        set => SetValue(FarClipProperty, value);
    }

    /// <summary>Gets the underlying Core perspective camera.</summary>
    public PerspectiveCamera PerspectiveCamera => (PerspectiveCamera)Camera;
}
