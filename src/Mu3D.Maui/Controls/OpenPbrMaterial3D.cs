using Mu3D.SceneGraph;

namespace Mu3D.Maui.Controls;

/// <summary>
/// Declares an OpenPBR material for an explicitly selected OpenPBR render pass.
/// The same authored surface can drive interactive and reference rendering without conversion
/// to the glTF metallic/roughness material model.
/// </summary>
[ContentProperty(nameof(Surface))]
public sealed class OpenPbrMaterial3D : Material3D
{
    /// <summary>Identifies the nested bindable OpenPBR authoring component.</summary>
    public static readonly BindableProperty SurfaceProperty = BindableProperty.Create(
        nameof(Surface), typeof(OpenPbrSurface3D), typeof(OpenPbrMaterial3D), null,
        validateValue: static (_, value) => value is OpenPbrSurface3D,
        propertyChanged: static (bindable, oldValue, newValue) =>
        {
            OpenPbrMaterial3D owner = (OpenPbrMaterial3D)bindable;
            if (oldValue is OpenPbrSurface3D previous) previous.Changed -= owner.OnSurfaceChanged;
            ((OpenPbrSurface3D)newValue).Changed += owner.OnSurfaceChanged;
            SetInheritedBindingContext((BindableObject)newValue, owner.BindingContext);
            owner.Synchronize();
        },
        defaultValueCreator: static _ => new OpenPbrSurface3D());

    /// <summary>Identifies the explicit emission-luminance conversion scale.</summary>
    public static readonly BindableProperty NitsPerSceneUnitProperty = BindableProperty.Create(
        nameof(NitsPerSceneUnit), typeof(float?), typeof(OpenPbrMaterial3D), null,
        validateValue: static (_, value) => value is null || value is float nits && float.IsFinite(nits) && nits > 0,
        propertyChanged: static (bindable, _, _) => ((OpenPbrMaterial3D)bindable).Synchronize());

    /// <summary>Initializes a default OpenPBR material with independent authoring state.</summary>
    public OpenPbrMaterial3D() : base(new OpenPbrMaterial(new OpenPbrSurface())) => Surface = new OpenPbrSurface3D();

    /// <summary>Gets or sets the nested authoring component, including all pinned OpenPBR inputs.</summary>
    public OpenPbrSurface3D Surface
    {
        get => (OpenPbrSurface3D)GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    /// <summary>
    /// Gets or sets the positive number of nits represented by one scene-linear emission unit.
    /// A value is required when emission is active; it does not select a display transform.
    /// </summary>
    public float? NitsPerSceneUnit
    {
        get => (float?)GetValue(NitsPerSceneUnitProperty);
        set => SetValue(NitsPerSceneUnitProperty, value);
    }

    /// <summary>Gets the stable backend-independent OpenPBR material borrowed by attached meshes.</summary>
    public OpenPbrMaterial OpenPbrMaterial => (OpenPbrMaterial)CoreMaterial;

    private void OnSurfaceChanged(object? sender, EventArgs e) => Synchronize();

    private void Synchronize()
    {
        if (Surface is null) return;
        OpenPbrMaterial.Surface = Surface.ToSurface();
        OpenPbrMaterial.NitsPerSceneUnit = NitsPerSceneUnit;
        OnMaterialMetadataChanged();
        NotifyChanged();
    }

    /// <inheritdoc />
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (Surface is not null) SetInheritedBindingContext(Surface, BindingContext);
    }

    /// <inheritdoc />
    protected override void OnMaterialMetadataChanged()
    {
        if (Surface is null) return;
        CoreMaterial.Name = Name ?? Surface.Name;
        CoreMaterial.IsDoubleSided = IsDoubleSided ||
            (Surface.Graph?.Maximum(OpenPbrInput.GeometryThinWalled, Surface.GeometryThinWalled ? 1 : 0) ??
                (Surface.GeometryThinWalled ? 1 : 0)) > 0;
    }
}
