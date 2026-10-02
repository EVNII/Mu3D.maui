using MauiColor = Microsoft.Maui.Graphics.Color;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Maui.Controls;

/// <summary>Declares a transformable directional light whose local negative Z axis emits rays.</summary>
public sealed class DirectionalLight3D : SceneNode3D
{
    private static readonly LinearRgba DefaultLinearWhite = new(
        1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb);

    /// <summary>Identifies the <see cref="Color"/> bindable property.</summary>
    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color),
        typeof(MauiColor),
        typeof(DirectionalLight3D),
        Colors.White,
        validateValue: static (_, value) => value is MauiColor,
        propertyChanged: static (bindable, _, value) =>
        {
            DirectionalLight3D light = (DirectionalLight3D)bindable;
            light.DirectionalLight.Color = DeclarativeColors.ToLinearSrgb((MauiColor)value, opaque: true);
            light.NotifyChanged();
        });

    /// <summary>Identifies the <see cref="Intensity"/> bindable property.</summary>
    public static readonly BindableProperty IntensityProperty = BindableProperty.Create(
        nameof(Intensity),
        typeof(float),
        typeof(DirectionalLight3D),
        1f,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number >= 0f,
        propertyChanged: static (bindable, _, value) =>
        {
            DirectionalLight3D light = (DirectionalLight3D)bindable;
            light.DirectionalLight.Intensity = (float)value;
            light.NotifyChanged();
        });

    /// <summary>Identifies the <see cref="CastsShadows"/> bindable property.</summary>
    public static readonly BindableProperty CastsShadowsProperty = BindableProperty.Create(
        nameof(CastsShadows),
        typeof(bool),
        typeof(DirectionalLight3D),
        false,
        propertyChanged: static (bindable, _, value) =>
        {
            DirectionalLight3D light = (DirectionalLight3D)bindable;
            light.DirectionalLight.CastsShadows = (bool)value;
            light.NotifyChanged();
        });

    /// <summary>Identifies the <see cref="ShadowOpacity"/> bindable property.</summary>
    public static readonly BindableProperty ShadowOpacityProperty = BindableProperty.Create(
        nameof(ShadowOpacity),
        typeof(float),
        typeof(DirectionalLight3D),
        1f,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number is >= 0f and <= 1f,
        propertyChanged: static (bindable, _, value) =>
        {
            DirectionalLight3D light = (DirectionalLight3D)bindable;
            light.DirectionalLight.ShadowOpacity = (float)value;
            light.NotifyChanged();
        });

    /// <summary>Identifies the <see cref="AngularDiameterDegrees"/> bindable property.</summary>
    public static readonly BindableProperty AngularDiameterDegreesProperty = BindableProperty.Create(
        nameof(AngularDiameterDegrees),
        typeof(float),
        typeof(DirectionalLight3D),
        0.0093f * 180f / MathF.PI,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number is >= 0f and < 180f,
        propertyChanged: static (bindable, _, value) =>
        {
            DirectionalLight3D light = (DirectionalLight3D)bindable;
            light.DirectionalLight.AngularDiameterRadians = (float)value * MathF.PI / 180f;
            light.NotifyChanged();
        });

    /// <summary>Initializes a white directional light with unit intensity.</summary>
    public DirectionalLight3D()
        : base(new DirectionalLight(DefaultLinearWhite))
    {
    }

    /// <summary>Gets or sets the sRGB UI color decoded to an opaque linear-light multiplier.</summary>
    public MauiColor Color
    {
        get => (MauiColor)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Gets or sets the non-negative finite light intensity.</summary>
    public float Intensity
    {
        get => (float)GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, value);
    }

    /// <summary>Gets or sets whether the light requests the renderer's directional shadow map.</summary>
    public bool CastsShadows
    {
        get => (bool)GetValue(CastsShadowsProperty);
        set => SetValue(CastsShadowsProperty, value);
    }

    /// <summary>Gets or sets shadow opacity in the inclusive range zero to one.</summary>
    public float ShadowOpacity
    {
        get => (float)GetValue(ShadowOpacityProperty);
        set => SetValue(ShadowOpacityProperty, value);
    }

    /// <summary>
    /// Gets or sets the apparent angular diameter of the directional emitter in degrees. Larger
    /// emitters produce wider distance-dependent penumbrae.
    /// </summary>
    public float AngularDiameterDegrees
    {
        get => (float)GetValue(AngularDiameterDegreesProperty);
        set => SetValue(AngularDiameterDegreesProperty, value);
    }

    /// <summary>Gets the underlying Core directional light.</summary>
    public DirectionalLight DirectionalLight => (DirectionalLight)CoreNode;
}
