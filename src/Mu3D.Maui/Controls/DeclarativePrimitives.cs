using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Maui.Controls;

/// <summary>Base class for a declarative primitive mesh with a nested material.</summary>
[ContentProperty(nameof(Material))]
public abstract class Primitive3D : SceneNode3D
{
    private static readonly LinearRgba DefaultLinearWhite = new(
        1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb);

    /// <summary>Identifies the <see cref="Material"/> bindable property.</summary>
    public static readonly BindableProperty MaterialProperty = BindableProperty.Create(
        nameof(Material),
        typeof(Material3D),
        typeof(Primitive3D),
        default(Material3D),
        validateValue: static (_, value) => value is Material3D,
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((Primitive3D)bindable).OnMaterialChanged(
                (Material3D?)oldValue,
                (Material3D)newValue));

    /// <summary>Identifies the <see cref="ShadowCastingMode"/> bindable property.</summary>
    public static readonly BindableProperty ShadowCastingModeProperty = BindableProperty.Create(
        nameof(ShadowCastingMode),
        typeof(MeshShadowCastingMode),
        typeof(Primitive3D),
        MeshShadowCastingMode.On,
        validateValue: static (_, value) => value is MeshShadowCastingMode mode && Enum.IsDefined(mode),
        propertyChanged: static (bindable, _, value) =>
        {
            Primitive3D primitive = (Primitive3D)bindable;
            primitive.Mesh.ShadowCastingMode = (MeshShadowCastingMode)value;
            primitive.NotifyChanged();
        });

    /// <summary>Initializes a primitive with immutable geometry and a default PBR material.</summary>
    protected Primitive3D(MeshGeometry geometry)
        : base(new Mesh(
            geometry ?? throw new ArgumentNullException(nameof(geometry)),
            new PbrMaterial(DefaultLinearWhite)))
    {
        Material = new PbrMaterial3D();
    }

    /// <summary>Gets or sets the declarative material used by this primitive.</summary>
    public Material3D Material
    {
        get => (Material3D)GetValue(MaterialProperty);
        set => SetValue(MaterialProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the primitive renders to camera passes, the directional-shadow map,
    /// or both.
    /// </summary>
    public MeshShadowCastingMode ShadowCastingMode
    {
        get => (MeshShadowCastingMode)GetValue(ShadowCastingModeProperty);
        set => SetValue(ShadowCastingModeProperty, value);
    }

    /// <summary>Gets the underlying Core mesh for selection and advanced mutation.</summary>
    public Mesh Mesh => (Mesh)CoreNode;

    /// <summary>Replaces this primitive's immutable geometry and invalidates attached views.</summary>
    protected void ReplaceGeometry(MeshGeometry geometry)
    {
        Mesh.Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        NotifyChanged();
    }

    private void OnMaterialChanged(Material3D? oldMaterial, Material3D newMaterial)
    {
        if (oldMaterial is not null)
        {
            oldMaterial.Changed -= OnMaterialValueChanged;
        }
        newMaterial.Changed += OnMaterialValueChanged;
        Mesh.Material = newMaterial.CoreMaterial;
        NotifyChanged();
    }

    private void OnMaterialValueChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        NotifyChanged();
    }
}

/// <summary>Declares an indexed UV sphere primitive.</summary>
public sealed class Sphere3D : Primitive3D
{
    /// <summary>Identifies the <see cref="Radius"/> bindable property.</summary>
    public static readonly BindableProperty RadiusProperty = BindableProperty.Create(
        nameof(Radius),
        typeof(float),
        typeof(Sphere3D),
        1f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, _) => ((Sphere3D)bindable).RebuildGeometry());

    /// <summary>Identifies the <see cref="LongitudeSegments"/> bindable property.</summary>
    public static readonly BindableProperty LongitudeSegmentsProperty = BindableProperty.Create(
        nameof(LongitudeSegments),
        typeof(int),
        typeof(Sphere3D),
        32,
        validateValue: static (_, value) => value is int number && number >= 3,
        propertyChanged: static (bindable, _, _) => ((Sphere3D)bindable).RebuildGeometry());

    /// <summary>Identifies the <see cref="LatitudeSegments"/> bindable property.</summary>
    public static readonly BindableProperty LatitudeSegmentsProperty = BindableProperty.Create(
        nameof(LatitudeSegments),
        typeof(int),
        typeof(Sphere3D),
        16,
        validateValue: static (_, value) => value is int number && number >= 2,
        propertyChanged: static (bindable, _, _) => ((Sphere3D)bindable).RebuildGeometry());

    /// <summary>Initializes a unit UV sphere.</summary>
    public Sphere3D()
        : base(MeshPrimitives.CreateUvSphere())
    {
    }

    /// <summary>Gets or sets the positive finite radius.</summary>
    public float Radius
    {
        get => (float)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    /// <summary>Gets or sets the number of segments around the Y axis; at least three.</summary>
    public int LongitudeSegments
    {
        get => (int)GetValue(LongitudeSegmentsProperty);
        set => SetValue(LongitudeSegmentsProperty, value);
    }

    /// <summary>Gets or sets the number of pole-to-pole segments; at least two.</summary>
    public int LatitudeSegments
    {
        get => (int)GetValue(LatitudeSegmentsProperty);
        set => SetValue(LatitudeSegmentsProperty, value);
    }

    private void RebuildGeometry() => ReplaceGeometry(MeshPrimitives.CreateUvSphere(
        Radius,
        LongitudeSegments,
        LatitudeSegments));

    private static bool IsPositiveFinite(object value) =>
        value is float number && float.IsFinite(number) && number > 0f;
}

/// <summary>Declares a closed Y-axis cone primitive.</summary>
public sealed class Cone3D : Primitive3D
{
    /// <summary>Identifies the <see cref="Radius"/> bindable property.</summary>
    public static readonly BindableProperty RadiusProperty = BindableProperty.Create(
        nameof(Radius),
        typeof(float),
        typeof(Cone3D),
        1f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, _) => ((Cone3D)bindable).RebuildGeometry());

    /// <summary>Identifies the <see cref="Height"/> bindable property.</summary>
    public static readonly BindableProperty HeightProperty = BindableProperty.Create(
        nameof(Height),
        typeof(float),
        typeof(Cone3D),
        2f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, _) => ((Cone3D)bindable).RebuildGeometry());

    /// <summary>Identifies the <see cref="RadialSegments"/> bindable property.</summary>
    public static readonly BindableProperty RadialSegmentsProperty = BindableProperty.Create(
        nameof(RadialSegments),
        typeof(int),
        typeof(Cone3D),
        32,
        validateValue: static (_, value) => value is int number && number >= 3,
        propertyChanged: static (bindable, _, _) => ((Cone3D)bindable).RebuildGeometry());

    /// <summary>Initializes a unit-radius, two-unit-high cone.</summary>
    public Cone3D()
        : base(MeshPrimitives.CreateCone())
    {
    }

    /// <summary>Gets or sets the positive finite base radius.</summary>
    public float Radius
    {
        get => (float)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    /// <summary>Gets or sets the positive finite tip-to-base height.</summary>
    public float Height
    {
        get => (float)GetValue(HeightProperty);
        set => SetValue(HeightProperty, value);
    }

    /// <summary>Gets or sets the number of segments around the Y axis; at least three.</summary>
    public int RadialSegments
    {
        get => (int)GetValue(RadialSegmentsProperty);
        set => SetValue(RadialSegmentsProperty, value);
    }

    private void RebuildGeometry() => ReplaceGeometry(MeshPrimitives.CreateCone(
        Radius,
        Height,
        RadialSegments));

    private static bool IsPositiveFinite(object value) =>
        value is float number && float.IsFinite(number) && number > 0f;
}
