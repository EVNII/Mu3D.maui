using MauiColor = Microsoft.Maui.Graphics.Color;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Maui.Controls;

/// <summary>Base class for bindable declarative material wrappers.</summary>
public abstract class Material3D : BindableObject
{
    /// <summary>Identifies the <see cref="Name"/> bindable property.</summary>
    public static readonly BindableProperty NameProperty = BindableProperty.Create(
        nameof(Name),
        typeof(string),
        typeof(Material3D),
        default(string),
        propertyChanged: static (bindable, _, value) =>
        {
            Material3D material = (Material3D)bindable;
            material.CoreMaterial.Name = (string?)value;
            material.OnMaterialMetadataChanged();
            material.NotifyChanged();
        });

    /// <summary>Identifies the <see cref="IsDoubleSided"/> bindable property.</summary>
    public static readonly BindableProperty IsDoubleSidedProperty = BindableProperty.Create(
        nameof(IsDoubleSided),
        typeof(bool),
        typeof(Material3D),
        false,
        propertyChanged: static (bindable, _, value) =>
        {
            Material3D material = (Material3D)bindable;
            material.CoreMaterial.IsDoubleSided = (bool)value;
            material.OnMaterialMetadataChanged();
            material.NotifyChanged();
        });

    /// <summary>Initializes a declarative wrapper over the supplied Core material.</summary>
    protected Material3D(Material coreMaterial) =>
        CoreMaterial = coreMaterial ?? throw new ArgumentNullException(nameof(coreMaterial));

    /// <summary>Gets or sets the optional application-facing material name.</summary>
    public string? Name
    {
        get => (string?)GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    /// <summary>Gets or sets whether both geometric face orientations are rendered.</summary>
    public bool IsDoubleSided
    {
        get => (bool)GetValue(IsDoubleSidedProperty);
        set => SetValue(IsDoubleSidedProperty, value);
    }

    /// <summary>Gets the underlying Core material for advanced mutation.</summary>
    public Material CoreMaterial { get; }

    /// <summary>Allows a specialized wrapper to reconcile derived metadata after name or sidedness changes.</summary>
    protected virtual void OnMaterialMetadataChanged() { }

    internal event EventHandler? Changed;

    internal void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Declares an HDR-capable metallic/roughness material from an sRGB UI color.</summary>
public sealed class PbrMaterial3D : Material3D
{
    private static readonly LinearRgba DefaultLinearWhite = new(
        1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb);

    /// <summary>Identifies the <see cref="Color"/> bindable property.</summary>
    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color),
        typeof(MauiColor),
        typeof(PbrMaterial3D),
        Colors.White,
        validateValue: static (_, value) => value is MauiColor,
        propertyChanged: static (bindable, _, value) =>
        {
            PbrMaterial3D material = (PbrMaterial3D)bindable;
            material.PbrMaterial.BaseColor = DeclarativeColors.ToLinearSrgb((MauiColor)value);
            material.NotifyChanged();
        });

    /// <summary>Identifies the <see cref="Metallic"/> bindable property.</summary>
    public static readonly BindableProperty MetallicProperty = CreateUnitProperty(
        nameof(Metallic),
        0f,
        static (material, value) => material.PbrMaterial.Metallic = value);

    /// <summary>Identifies the <see cref="Roughness"/> bindable property.</summary>
    public static readonly BindableProperty RoughnessProperty = CreateUnitProperty(
        nameof(Roughness),
        0.5f,
        static (material, value) => material.PbrMaterial.Roughness = value);

    /// <summary>Initializes a white dielectric material with 0.5 perceptual roughness.</summary>
    public PbrMaterial3D()
        : base(new PbrMaterial(DefaultLinearWhite))
    {
    }

    /// <summary>
    /// Gets or sets the sRGB UI color decoded into explicitly tagged linear sRGB for rendering.
    /// </summary>
    public MauiColor Color
    {
        get => (MauiColor)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Gets or sets the metallic factor in the inclusive range zero to one.</summary>
    public float Metallic
    {
        get => (float)GetValue(MetallicProperty);
        set => SetValue(MetallicProperty, value);
    }

    /// <summary>Gets or sets perceptual roughness in the inclusive range zero to one.</summary>
    public float Roughness
    {
        get => (float)GetValue(RoughnessProperty);
        set => SetValue(RoughnessProperty, value);
    }

    /// <summary>Gets the underlying Core PBR material.</summary>
    public PbrMaterial PbrMaterial => (PbrMaterial)CoreMaterial;

    private static BindableProperty CreateUnitProperty(
        string name,
        float defaultValue,
        Action<PbrMaterial3D, float> apply) => BindableProperty.Create(
        name,
        typeof(float),
        typeof(PbrMaterial3D),
        defaultValue,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number is >= 0f and <= 1f,
        propertyChanged: (bindable, _, value) =>
        {
            PbrMaterial3D material = (PbrMaterial3D)bindable;
            apply(material, (float)value);
            material.NotifyChanged();
        });
}

/// <summary>Declares an unlit material from an sRGB UI color.</summary>
public sealed class UnlitMaterial3D : Material3D
{
    private static readonly LinearRgba DefaultLinearWhite = new(
        1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb);

    /// <summary>Identifies the explicitly tagged linear HDR texture property.</summary>
    public static readonly BindableProperty TextureProperty = BindableProperty.Create(
        nameof(Texture), typeof(LinearRgbaImage), typeof(UnlitMaterial3D), null,
        propertyChanged: static (bindable, _, value) =>
        {
            UnlitMaterial3D material = (UnlitMaterial3D)bindable;
            material.UnlitMaterial.BaseColorTexture = (LinearRgbaImage?)value;
            material.NotifyChanged();
        });

    /// <summary>
    /// Gets or sets an immutable linear-light image, for example an application-owned HDR canvas
    /// snapshot. The renderer uses the image's color metadata and preserves HDR through FP16 upload.
    /// </summary>
    public LinearRgbaImage? Texture
    {
        get => (LinearRgbaImage?)GetValue(TextureProperty);
        set => SetValue(TextureProperty, value);
    }

    /// <summary>Identifies the <see cref="Color"/> bindable property.</summary>
    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color),
        typeof(MauiColor),
        typeof(UnlitMaterial3D),
        Colors.White,
        validateValue: static (_, value) => value is MauiColor,
        propertyChanged: static (bindable, _, value) =>
        {
            UnlitMaterial3D material = (UnlitMaterial3D)bindable;
            material.UnlitMaterial.Color = DeclarativeColors.ToLinearSrgb((MauiColor)value);
            material.NotifyChanged();
        });

    /// <summary>Initializes an opaque white unlit material.</summary>
    public UnlitMaterial3D()
        : base(new UnlitMaterial(DefaultLinearWhite))
    {
    }

    /// <summary>
    /// Gets or sets the sRGB UI color decoded into explicitly tagged linear sRGB for rendering.
    /// </summary>
    public MauiColor Color
    {
        get => (MauiColor)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Gets the underlying Core unlit material.</summary>
    public UnlitMaterial UnlitMaterial => (UnlitMaterial)CoreMaterial;
}
