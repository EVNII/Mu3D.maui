using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Maui.Controls;

/// <summary>
/// Renders a bindable OpenPBR surface through the explicitly approximate existing PBR renderer.
/// Failed preview conversion hides the preview and exposes an error while retaining authoring data.
/// </summary>
[ContentProperty(nameof(Surface))]
public sealed class OpenPbrPreviewMaterial3D : Material3D
{
    private static readonly BindablePropertyKey PreviewErrorPropertyKey = BindableProperty.CreateReadOnly(
        nameof(PreviewError), typeof(string), typeof(OpenPbrPreviewMaterial3D), null);
    private static readonly BindablePropertyKey IsPreviewAvailablePropertyKey = BindableProperty.CreateReadOnly(
        nameof(IsPreviewAvailable), typeof(bool), typeof(OpenPbrPreviewMaterial3D), false);

    /// <summary>Identifies the read-only preview error property.</summary>
    public static readonly BindableProperty PreviewErrorProperty = PreviewErrorPropertyKey.BindableProperty;
    /// <summary>Identifies whether the current authoring state has a visible preview.</summary>
    public static readonly BindableProperty IsPreviewAvailableProperty = IsPreviewAvailablePropertyKey.BindableProperty;

    /// <summary>Identifies the nested OpenPBR authoring component.</summary>
    public static readonly BindableProperty SurfaceProperty = BindableProperty.Create(
        nameof(Surface), typeof(OpenPbrSurface3D), typeof(OpenPbrPreviewMaterial3D), null,
        validateValue: static (_, value) => value is OpenPbrSurface3D,
        propertyChanged: static (bindable, oldValue, newValue) =>
        {
            OpenPbrPreviewMaterial3D owner = (OpenPbrPreviewMaterial3D)bindable;
            if (oldValue is OpenPbrSurface3D oldSurface) oldSurface.Changed -= owner.OnSurfaceChanged;
            ((OpenPbrSurface3D)newValue).Changed += owner.OnSurfaceChanged;
            SetInheritedBindingContext((BindableObject)newValue, owner.BindingContext);
            owner.RefreshPreview();
        },
        defaultValueCreator: static _ => new OpenPbrSurface3D());

    /// <summary>Identifies the unsupported-parameter preview policy.</summary>
    public static readonly BindableProperty PreviewPolicyProperty = BindableProperty.Create(
        nameof(PreviewPolicy), typeof(OpenPbrPreviewPolicy), typeof(OpenPbrPreviewMaterial3D),
        OpenPbrPreviewPolicy.RejectUnsupported,
        validateValue: static (_, value) => value is OpenPbrPreviewPolicy policy && Enum.IsDefined(policy),
        propertyChanged: static (bindable, _, _) => ((OpenPbrPreviewMaterial3D)bindable).RefreshPreview());

    /// <summary>Identifies the explicit emission conversion scale.</summary>
    public static readonly BindableProperty NitsPerSceneUnitProperty = BindableProperty.Create(
        nameof(NitsPerSceneUnit), typeof(float?), typeof(OpenPbrPreviewMaterial3D), null,
        validateValue: static (_, value) => value is null || value is float number && float.IsFinite(number) && number > 0,
        propertyChanged: static (bindable, _, _) => ((OpenPbrPreviewMaterial3D)bindable).RefreshPreview());

    /// <summary>Initializes the pinned default surface and its approximate PBR preview.</summary>
    public OpenPbrPreviewMaterial3D()
        : base(new PbrMaterial(new LinearRgba(0.8f, 0.8f, 0.8f, 1f, StandardColorSpaces.AcesCg))) =>
        Surface = new OpenPbrSurface3D();

    /// <summary>Gets or sets the nested authoring component; bindings and imported values remain independent of rendering.</summary>
    public OpenPbrSurface3D Surface
    {
        get => (OpenPbrSurface3D)GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }
    /// <summary>Gets or sets whether unsupported active parameters reject or explicitly permit lossy approximation.</summary>
    public OpenPbrPreviewPolicy PreviewPolicy
    {
        get => (OpenPbrPreviewPolicy)GetValue(PreviewPolicyProperty);
        set => SetValue(PreviewPolicyProperty, value);
    }
    /// <summary>Gets or sets the number of nits represented by one scene emission unit; required for active emission.</summary>
    public float? NitsPerSceneUnit
    {
        get => (float?)GetValue(NitsPerSceneUnitProperty);
        set => SetValue(NitsPerSceneUnitProperty, value);
    }
    /// <summary>Gets the current conversion error, or null for a successfully constructed approximate preview.</summary>
    public string? PreviewError => (string?)GetValue(PreviewErrorProperty);
    /// <summary>Gets whether the current authoring state is visible under the selected preview policy.</summary>
    public bool IsPreviewAvailable => (bool)GetValue(IsPreviewAvailableProperty);
    /// <summary>Gets the last successful conversion and its limitations; null when current conversion failed.</summary>
    public OpenPbrPreviewResult? PreviewResult { get; private set; }

    /// <summary>Rebuilds the preview in place without replacing the material borrowed by attached meshes.</summary>
    public void RefreshPreview()
    {
        if (Surface is null) return;
        PbrMaterial target = (PbrMaterial)CoreMaterial;
        try
        {
            PreviewResult = Surface.ToSurface().ApplyToPbrPreview(target, new OpenPbrPreviewOptions
            {
                Policy = PreviewPolicy,
                NitsPerSceneUnit = NitsPerSceneUnit,
            });
            // MAUI material metadata remains owned by this wrapper, independently of surface geometry.
            target.Name = Name ?? Surface.Name;
            target.IsDoubleSided = IsDoubleSided || Surface.GeometryThinWalled;
            SetValue(PreviewErrorPropertyKey, null);
            SetValue(IsPreviewAvailablePropertyKey, true);
        }
        catch (Exception error) when (error is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            PreviewResult = null;
            target.BaseColor = new LinearRgba(0, 0, 0, 0, StandardColorSpaces.LinearSrgb);
            target.AlphaMode = MaterialAlphaMode.Blend;
            target.EmissiveStrength = 0;
            target.TransmissionFactor = 0;
            SetValue(PreviewErrorPropertyKey, error.Message);
            SetValue(IsPreviewAvailablePropertyKey, false);
        }
        OnPropertyChanged(nameof(PreviewResult));
        NotifyChanged();
    }

    private void OnSurfaceChanged(object? sender, EventArgs e) => RefreshPreview();

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
        CoreMaterial.IsDoubleSided = IsDoubleSided || Surface.GeometryThinWalled;
    }
}
