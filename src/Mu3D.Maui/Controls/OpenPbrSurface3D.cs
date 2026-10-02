using System.ComponentModel;
using System.Numerics;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Maui.Controls;

/// <summary>Declares all constant OpenPBR 1.1.1 inputs in bindable MAUI XAML, independently of a renderer.</summary>
public sealed class OpenPbrSurface3D : BindableObject
{
    private static readonly OpenPbrSurface Defaults = new();
    private bool loading;
    private readonly WeakEventManager changedEvents = new();
    internal event EventHandler Changed
    {
        add => changedEvents.AddEventHandler(value);
        remove => changedEvents.RemoveEventHandler(value);
    }

    /// <summary>Identifies the <see cref="Name"/> bindable property.</summary>
    public static readonly BindableProperty NameProperty = CreateProperty(
        nameof(Name), typeof(string), Defaults.Name,
        static (surface, value) => surface.Name = (string?)value!);

    /// <summary>Identifies the <see cref="MetersPerUnit"/> bindable property.</summary>
    public static readonly BindableProperty MetersPerUnitProperty = CreateProperty(
        nameof(MetersPerUnit), typeof(float), Defaults.MetersPerUnit,
        static (surface, value) => surface.MetersPerUnit = (float)value!);

    /// <summary>Identifies the <see cref="BaseWeight"/> bindable property.</summary>
    public static readonly BindableProperty BaseWeightProperty = CreateProperty(
        nameof(BaseWeight), typeof(float), Defaults.BaseWeight,
        static (surface, value) => surface.BaseWeight = (float)value!);

    /// <summary>Identifies the <see cref="BaseDiffuseRoughness"/> bindable property.</summary>
    public static readonly BindableProperty BaseDiffuseRoughnessProperty = CreateProperty(
        nameof(BaseDiffuseRoughness), typeof(float), Defaults.BaseDiffuseRoughness,
        static (surface, value) => surface.BaseDiffuseRoughness = (float)value!);

    /// <summary>Identifies the <see cref="BaseMetalness"/> bindable property.</summary>
    public static readonly BindableProperty BaseMetalnessProperty = CreateProperty(
        nameof(BaseMetalness), typeof(float), Defaults.BaseMetalness,
        static (surface, value) => surface.BaseMetalness = (float)value!);

    /// <summary>Identifies the <see cref="SpecularWeight"/> bindable property.</summary>
    public static readonly BindableProperty SpecularWeightProperty = CreateProperty(
        nameof(SpecularWeight), typeof(float), Defaults.SpecularWeight,
        static (surface, value) => surface.SpecularWeight = (float)value!);

    /// <summary>Identifies the <see cref="SpecularRoughness"/> bindable property.</summary>
    public static readonly BindableProperty SpecularRoughnessProperty = CreateProperty(
        nameof(SpecularRoughness), typeof(float), Defaults.SpecularRoughness,
        static (surface, value) => surface.SpecularRoughness = (float)value!);

    /// <summary>Identifies the <see cref="SpecularIor"/> bindable property.</summary>
    public static readonly BindableProperty SpecularIorProperty = CreateProperty(
        nameof(SpecularIor), typeof(float), Defaults.SpecularIor,
        static (surface, value) => surface.SpecularIor = (float)value!);

    /// <summary>Identifies the <see cref="SpecularRoughnessAnisotropy"/> bindable property.</summary>
    public static readonly BindableProperty SpecularRoughnessAnisotropyProperty = CreateProperty(
        nameof(SpecularRoughnessAnisotropy), typeof(float), Defaults.SpecularRoughnessAnisotropy,
        static (surface, value) => surface.SpecularRoughnessAnisotropy = (float)value!);

    /// <summary>Identifies the <see cref="TransmissionWeight"/> bindable property.</summary>
    public static readonly BindableProperty TransmissionWeightProperty = CreateProperty(
        nameof(TransmissionWeight), typeof(float), Defaults.TransmissionWeight,
        static (surface, value) => surface.TransmissionWeight = (float)value!);

    /// <summary>Identifies the <see cref="TransmissionDepth"/> bindable property.</summary>
    public static readonly BindableProperty TransmissionDepthProperty = CreateProperty(
        nameof(TransmissionDepth), typeof(float), Defaults.TransmissionDepth,
        static (surface, value) => surface.TransmissionDepth = (float)value!);

    /// <summary>Identifies the <see cref="TransmissionScatterAnisotropy"/> bindable property.</summary>
    public static readonly BindableProperty TransmissionScatterAnisotropyProperty = CreateProperty(
        nameof(TransmissionScatterAnisotropy), typeof(float), Defaults.TransmissionScatterAnisotropy,
        static (surface, value) => surface.TransmissionScatterAnisotropy = (float)value!);

    /// <summary>Identifies the <see cref="TransmissionDispersionScale"/> bindable property.</summary>
    public static readonly BindableProperty TransmissionDispersionScaleProperty = CreateProperty(
        nameof(TransmissionDispersionScale), typeof(float), Defaults.TransmissionDispersionScale,
        static (surface, value) => surface.TransmissionDispersionScale = (float)value!);

    /// <summary>Identifies the <see cref="TransmissionDispersionAbbeNumber"/> bindable property.</summary>
    public static readonly BindableProperty TransmissionDispersionAbbeNumberProperty = CreateProperty(
        nameof(TransmissionDispersionAbbeNumber), typeof(float), Defaults.TransmissionDispersionAbbeNumber,
        static (surface, value) => surface.TransmissionDispersionAbbeNumber = (float)value!);

    /// <summary>Identifies the <see cref="SubsurfaceWeight"/> bindable property.</summary>
    public static readonly BindableProperty SubsurfaceWeightProperty = CreateProperty(
        nameof(SubsurfaceWeight), typeof(float), Defaults.SubsurfaceWeight,
        static (surface, value) => surface.SubsurfaceWeight = (float)value!);

    /// <summary>Identifies the <see cref="SubsurfaceRadius"/> bindable property.</summary>
    public static readonly BindableProperty SubsurfaceRadiusProperty = CreateProperty(
        nameof(SubsurfaceRadius), typeof(float), Defaults.SubsurfaceRadius,
        static (surface, value) => surface.SubsurfaceRadius = (float)value!);

    /// <summary>Identifies the <see cref="SubsurfaceScatterAnisotropy"/> bindable property.</summary>
    public static readonly BindableProperty SubsurfaceScatterAnisotropyProperty = CreateProperty(
        nameof(SubsurfaceScatterAnisotropy), typeof(float), Defaults.SubsurfaceScatterAnisotropy,
        static (surface, value) => surface.SubsurfaceScatterAnisotropy = (float)value!);

    /// <summary>Identifies the <see cref="FuzzWeight"/> bindable property.</summary>
    public static readonly BindableProperty FuzzWeightProperty = CreateProperty(
        nameof(FuzzWeight), typeof(float), Defaults.FuzzWeight,
        static (surface, value) => surface.FuzzWeight = (float)value!);

    /// <summary>Identifies the <see cref="FuzzRoughness"/> bindable property.</summary>
    public static readonly BindableProperty FuzzRoughnessProperty = CreateProperty(
        nameof(FuzzRoughness), typeof(float), Defaults.FuzzRoughness,
        static (surface, value) => surface.FuzzRoughness = (float)value!);

    /// <summary>Identifies the <see cref="CoatWeight"/> bindable property.</summary>
    public static readonly BindableProperty CoatWeightProperty = CreateProperty(
        nameof(CoatWeight), typeof(float), Defaults.CoatWeight,
        static (surface, value) => surface.CoatWeight = (float)value!);

    /// <summary>Identifies the <see cref="CoatRoughness"/> bindable property.</summary>
    public static readonly BindableProperty CoatRoughnessProperty = CreateProperty(
        nameof(CoatRoughness), typeof(float), Defaults.CoatRoughness,
        static (surface, value) => surface.CoatRoughness = (float)value!);

    /// <summary>Identifies the <see cref="CoatRoughnessAnisotropy"/> bindable property.</summary>
    public static readonly BindableProperty CoatRoughnessAnisotropyProperty = CreateProperty(
        nameof(CoatRoughnessAnisotropy), typeof(float), Defaults.CoatRoughnessAnisotropy,
        static (surface, value) => surface.CoatRoughnessAnisotropy = (float)value!);

    /// <summary>Identifies the <see cref="CoatIor"/> bindable property.</summary>
    public static readonly BindableProperty CoatIorProperty = CreateProperty(
        nameof(CoatIor), typeof(float), Defaults.CoatIor,
        static (surface, value) => surface.CoatIor = (float)value!);

    /// <summary>Identifies the <see cref="CoatDarkening"/> bindable property.</summary>
    public static readonly BindableProperty CoatDarkeningProperty = CreateProperty(
        nameof(CoatDarkening), typeof(float), Defaults.CoatDarkening,
        static (surface, value) => surface.CoatDarkening = (float)value!);

    /// <summary>Identifies the <see cref="ThinFilmWeight"/> bindable property.</summary>
    public static readonly BindableProperty ThinFilmWeightProperty = CreateProperty(
        nameof(ThinFilmWeight), typeof(float), Defaults.ThinFilmWeight,
        static (surface, value) => surface.ThinFilmWeight = (float)value!);

    /// <summary>Identifies the <see cref="ThinFilmThickness"/> bindable property.</summary>
    public static readonly BindableProperty ThinFilmThicknessProperty = CreateProperty(
        nameof(ThinFilmThickness), typeof(float), Defaults.ThinFilmThickness,
        static (surface, value) => surface.ThinFilmThickness = (float)value!);

    /// <summary>Identifies the <see cref="ThinFilmIor"/> bindable property.</summary>
    public static readonly BindableProperty ThinFilmIorProperty = CreateProperty(
        nameof(ThinFilmIor), typeof(float), Defaults.ThinFilmIor,
        static (surface, value) => surface.ThinFilmIor = (float)value!);

    /// <summary>Identifies the <see cref="EmissionLuminance"/> bindable property.</summary>
    public static readonly BindableProperty EmissionLuminanceProperty = CreateProperty(
        nameof(EmissionLuminance), typeof(float), Defaults.EmissionLuminance,
        static (surface, value) => surface.EmissionLuminance = (float)value!);

    /// <summary>Identifies the <see cref="GeometryOpacity"/> bindable property.</summary>
    public static readonly BindableProperty GeometryOpacityProperty = CreateProperty(
        nameof(GeometryOpacity), typeof(float), Defaults.GeometryOpacity,
        static (surface, value) => surface.GeometryOpacity = (float)value!);

    /// <summary>Identifies the <see cref="BaseColor"/> bindable property.</summary>
    public static readonly BindableProperty BaseColorProperty = CreateProperty(
        nameof(BaseColor), typeof(LinearRgba), Defaults.BaseColor,
        static (surface, value) => surface.BaseColor = (LinearRgba)value!);

    /// <summary>Identifies the <see cref="SpecularColor"/> bindable property.</summary>
    public static readonly BindableProperty SpecularColorProperty = CreateProperty(
        nameof(SpecularColor), typeof(LinearRgba), Defaults.SpecularColor,
        static (surface, value) => surface.SpecularColor = (LinearRgba)value!);

    /// <summary>Identifies the <see cref="TransmissionColor"/> bindable property.</summary>
    public static readonly BindableProperty TransmissionColorProperty = CreateProperty(
        nameof(TransmissionColor), typeof(LinearRgba), Defaults.TransmissionColor,
        static (surface, value) => surface.TransmissionColor = (LinearRgba)value!);

    /// <summary>Identifies the <see cref="TransmissionScatter"/> bindable property.</summary>
    public static readonly BindableProperty TransmissionScatterProperty = CreateProperty(
        nameof(TransmissionScatter), typeof(LinearRgba), Defaults.TransmissionScatter,
        static (surface, value) => surface.TransmissionScatter = (LinearRgba)value!);

    /// <summary>Identifies the <see cref="SubsurfaceColor"/> bindable property.</summary>
    public static readonly BindableProperty SubsurfaceColorProperty = CreateProperty(
        nameof(SubsurfaceColor), typeof(LinearRgba), Defaults.SubsurfaceColor,
        static (surface, value) => surface.SubsurfaceColor = (LinearRgba)value!);

    /// <summary>Identifies the <see cref="FuzzColor"/> bindable property.</summary>
    public static readonly BindableProperty FuzzColorProperty = CreateProperty(
        nameof(FuzzColor), typeof(LinearRgba), Defaults.FuzzColor,
        static (surface, value) => surface.FuzzColor = (LinearRgba)value!);

    /// <summary>Identifies the <see cref="CoatColor"/> bindable property.</summary>
    public static readonly BindableProperty CoatColorProperty = CreateProperty(
        nameof(CoatColor), typeof(LinearRgba), Defaults.CoatColor,
        static (surface, value) => surface.CoatColor = (LinearRgba)value!);

    /// <summary>Identifies the <see cref="EmissionColor"/> bindable property.</summary>
    public static readonly BindableProperty EmissionColorProperty = CreateProperty(
        nameof(EmissionColor), typeof(LinearRgba), Defaults.EmissionColor,
        static (surface, value) => surface.EmissionColor = (LinearRgba)value!);

    /// <summary>Identifies the <see cref="SubsurfaceRadiusScale"/> bindable property.</summary>
    public static readonly BindableProperty SubsurfaceRadiusScaleProperty = CreateProperty(
        nameof(SubsurfaceRadiusScale), typeof(Vector3), Defaults.SubsurfaceRadiusScale,
        static (surface, value) => surface.SubsurfaceRadiusScale = (Vector3)value!);

    /// <summary>Identifies the <see cref="GeometryThinWalled"/> bindable property.</summary>
    public static readonly BindableProperty GeometryThinWalledProperty = CreateProperty(
        nameof(GeometryThinWalled), typeof(bool), Defaults.GeometryThinWalled,
        static (surface, value) => surface.GeometryThinWalled = (bool)value!);

    /// <summary>Identifies the <see cref="GeometryNormal"/> bindable property.</summary>
    public static readonly BindableProperty GeometryNormalProperty = CreateProperty(
        nameof(GeometryNormal), typeof(Vector3?), Defaults.GeometryNormal,
        static (surface, value) => surface.GeometryNormal = (Vector3?)value!);

    /// <summary>Identifies the <see cref="GeometryCoatNormal"/> bindable property.</summary>
    public static readonly BindableProperty GeometryCoatNormalProperty = CreateProperty(
        nameof(GeometryCoatNormal), typeof(Vector3?), Defaults.GeometryCoatNormal,
        static (surface, value) => surface.GeometryCoatNormal = (Vector3?)value!);

    /// <summary>Identifies the <see cref="GeometryTangent"/> bindable property.</summary>
    public static readonly BindableProperty GeometryTangentProperty = CreateProperty(
        nameof(GeometryTangent), typeof(Vector3?), Defaults.GeometryTangent,
        static (surface, value) => surface.GeometryTangent = (Vector3?)value!);

    /// <summary>Identifies the <see cref="GeometryCoatTangent"/> bindable property.</summary>
    public static readonly BindableProperty GeometryCoatTangentProperty = CreateProperty(
        nameof(GeometryCoatTangent), typeof(Vector3?), Defaults.GeometryCoatTangent,
        static (surface, value) => surface.GeometryCoatTangent = (Vector3?)value!);

    /// <summary>Identifies immutable input connections usable with XAML bindings or resources.</summary>
    public static readonly BindableProperty GraphProperty = CreateProperty(
        nameof(Graph), typeof(OpenPbrGraph), null, static (surface, value) => surface.Graph = (OpenPbrGraph?)value);

    /// <summary>Gets or sets texture and expression connections; replacing this value publishes one change.</summary>
    public OpenPbrGraph? Graph
    {
        get => (OpenPbrGraph?)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    /// <summary>Initializes the pinned OpenPBR defaults.</summary>
    public OpenPbrSurface3D() { }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.Name"/> authoring value.</summary>
    public string? Name
    {
        get => (string?)GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.MetersPerUnit"/> authoring value.</summary>
    public float MetersPerUnit
    {
        get => (float)GetValue(MetersPerUnitProperty);
        set => SetValue(MetersPerUnitProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.BaseWeight"/> authoring value.</summary>
    public float BaseWeight
    {
        get => (float)GetValue(BaseWeightProperty);
        set => SetValue(BaseWeightProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.BaseDiffuseRoughness"/> authoring value.</summary>
    public float BaseDiffuseRoughness
    {
        get => (float)GetValue(BaseDiffuseRoughnessProperty);
        set => SetValue(BaseDiffuseRoughnessProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.BaseMetalness"/> authoring value.</summary>
    public float BaseMetalness
    {
        get => (float)GetValue(BaseMetalnessProperty);
        set => SetValue(BaseMetalnessProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.SpecularWeight"/> authoring value.</summary>
    public float SpecularWeight
    {
        get => (float)GetValue(SpecularWeightProperty);
        set => SetValue(SpecularWeightProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.SpecularRoughness"/> authoring value.</summary>
    public float SpecularRoughness
    {
        get => (float)GetValue(SpecularRoughnessProperty);
        set => SetValue(SpecularRoughnessProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.SpecularIor"/> authoring value.</summary>
    public float SpecularIor
    {
        get => (float)GetValue(SpecularIorProperty);
        set => SetValue(SpecularIorProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.SpecularRoughnessAnisotropy"/> authoring value.</summary>
    public float SpecularRoughnessAnisotropy
    {
        get => (float)GetValue(SpecularRoughnessAnisotropyProperty);
        set => SetValue(SpecularRoughnessAnisotropyProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.TransmissionWeight"/> authoring value.</summary>
    public float TransmissionWeight
    {
        get => (float)GetValue(TransmissionWeightProperty);
        set => SetValue(TransmissionWeightProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.TransmissionDepth"/> authoring value.</summary>
    public float TransmissionDepth
    {
        get => (float)GetValue(TransmissionDepthProperty);
        set => SetValue(TransmissionDepthProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.TransmissionScatterAnisotropy"/> authoring value.</summary>
    public float TransmissionScatterAnisotropy
    {
        get => (float)GetValue(TransmissionScatterAnisotropyProperty);
        set => SetValue(TransmissionScatterAnisotropyProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.TransmissionDispersionScale"/> authoring value.</summary>
    public float TransmissionDispersionScale
    {
        get => (float)GetValue(TransmissionDispersionScaleProperty);
        set => SetValue(TransmissionDispersionScaleProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.TransmissionDispersionAbbeNumber"/> authoring value.</summary>
    public float TransmissionDispersionAbbeNumber
    {
        get => (float)GetValue(TransmissionDispersionAbbeNumberProperty);
        set => SetValue(TransmissionDispersionAbbeNumberProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.SubsurfaceWeight"/> authoring value.</summary>
    public float SubsurfaceWeight
    {
        get => (float)GetValue(SubsurfaceWeightProperty);
        set => SetValue(SubsurfaceWeightProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.SubsurfaceRadius"/> authoring value.</summary>
    public float SubsurfaceRadius
    {
        get => (float)GetValue(SubsurfaceRadiusProperty);
        set => SetValue(SubsurfaceRadiusProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.SubsurfaceScatterAnisotropy"/> authoring value.</summary>
    public float SubsurfaceScatterAnisotropy
    {
        get => (float)GetValue(SubsurfaceScatterAnisotropyProperty);
        set => SetValue(SubsurfaceScatterAnisotropyProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.FuzzWeight"/> authoring value.</summary>
    public float FuzzWeight
    {
        get => (float)GetValue(FuzzWeightProperty);
        set => SetValue(FuzzWeightProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.FuzzRoughness"/> authoring value.</summary>
    public float FuzzRoughness
    {
        get => (float)GetValue(FuzzRoughnessProperty);
        set => SetValue(FuzzRoughnessProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.CoatWeight"/> authoring value.</summary>
    public float CoatWeight
    {
        get => (float)GetValue(CoatWeightProperty);
        set => SetValue(CoatWeightProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.CoatRoughness"/> authoring value.</summary>
    public float CoatRoughness
    {
        get => (float)GetValue(CoatRoughnessProperty);
        set => SetValue(CoatRoughnessProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.CoatRoughnessAnisotropy"/> authoring value.</summary>
    public float CoatRoughnessAnisotropy
    {
        get => (float)GetValue(CoatRoughnessAnisotropyProperty);
        set => SetValue(CoatRoughnessAnisotropyProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.CoatIor"/> authoring value.</summary>
    public float CoatIor
    {
        get => (float)GetValue(CoatIorProperty);
        set => SetValue(CoatIorProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.CoatDarkening"/> authoring value.</summary>
    public float CoatDarkening
    {
        get => (float)GetValue(CoatDarkeningProperty);
        set => SetValue(CoatDarkeningProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.ThinFilmWeight"/> authoring value.</summary>
    public float ThinFilmWeight
    {
        get => (float)GetValue(ThinFilmWeightProperty);
        set => SetValue(ThinFilmWeightProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.ThinFilmThickness"/> authoring value.</summary>
    public float ThinFilmThickness
    {
        get => (float)GetValue(ThinFilmThicknessProperty);
        set => SetValue(ThinFilmThicknessProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.ThinFilmIor"/> authoring value.</summary>
    public float ThinFilmIor
    {
        get => (float)GetValue(ThinFilmIorProperty);
        set => SetValue(ThinFilmIorProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.EmissionLuminance"/> authoring value.</summary>
    public float EmissionLuminance
    {
        get => (float)GetValue(EmissionLuminanceProperty);
        set => SetValue(EmissionLuminanceProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.GeometryOpacity"/> authoring value.</summary>
    public float GeometryOpacity
    {
        get => (float)GetValue(GeometryOpacityProperty);
        set => SetValue(GeometryOpacityProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.BaseColor"/> authoring value.</summary>
    [TypeConverter(typeof(LinearRgbaTypeConverter))]
    public LinearRgba BaseColor
    {
        get => (LinearRgba)GetValue(BaseColorProperty);
        set => SetValue(BaseColorProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.SpecularColor"/> authoring value.</summary>
    [TypeConverter(typeof(LinearRgbaTypeConverter))]
    public LinearRgba SpecularColor
    {
        get => (LinearRgba)GetValue(SpecularColorProperty);
        set => SetValue(SpecularColorProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.TransmissionColor"/> authoring value.</summary>
    [TypeConverter(typeof(LinearRgbaTypeConverter))]
    public LinearRgba TransmissionColor
    {
        get => (LinearRgba)GetValue(TransmissionColorProperty);
        set => SetValue(TransmissionColorProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.TransmissionScatter"/> authoring value.</summary>
    [TypeConverter(typeof(LinearRgbaTypeConverter))]
    public LinearRgba TransmissionScatter
    {
        get => (LinearRgba)GetValue(TransmissionScatterProperty);
        set => SetValue(TransmissionScatterProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.SubsurfaceColor"/> authoring value.</summary>
    [TypeConverter(typeof(LinearRgbaTypeConverter))]
    public LinearRgba SubsurfaceColor
    {
        get => (LinearRgba)GetValue(SubsurfaceColorProperty);
        set => SetValue(SubsurfaceColorProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.FuzzColor"/> authoring value.</summary>
    [TypeConverter(typeof(LinearRgbaTypeConverter))]
    public LinearRgba FuzzColor
    {
        get => (LinearRgba)GetValue(FuzzColorProperty);
        set => SetValue(FuzzColorProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.CoatColor"/> authoring value.</summary>
    [TypeConverter(typeof(LinearRgbaTypeConverter))]
    public LinearRgba CoatColor
    {
        get => (LinearRgba)GetValue(CoatColorProperty);
        set => SetValue(CoatColorProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.EmissionColor"/> authoring value.</summary>
    [TypeConverter(typeof(LinearRgbaTypeConverter))]
    public LinearRgba EmissionColor
    {
        get => (LinearRgba)GetValue(EmissionColorProperty);
        set => SetValue(EmissionColorProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.SubsurfaceRadiusScale"/> authoring value.</summary>
    [TypeConverter(typeof(LinearVector3TypeConverter))]
    public Vector3 SubsurfaceRadiusScale
    {
        get => (Vector3)GetValue(SubsurfaceRadiusScaleProperty);
        set => SetValue(SubsurfaceRadiusScaleProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.GeometryThinWalled"/> authoring value.</summary>
    public bool GeometryThinWalled
    {
        get => (bool)GetValue(GeometryThinWalledProperty);
        set => SetValue(GeometryThinWalledProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.GeometryNormal"/> authoring value.</summary>
    [TypeConverter(typeof(LinearVector3TypeConverter))]
    public Vector3? GeometryNormal
    {
        get => (Vector3?)GetValue(GeometryNormalProperty);
        set => SetValue(GeometryNormalProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.GeometryCoatNormal"/> authoring value.</summary>
    [TypeConverter(typeof(LinearVector3TypeConverter))]
    public Vector3? GeometryCoatNormal
    {
        get => (Vector3?)GetValue(GeometryCoatNormalProperty);
        set => SetValue(GeometryCoatNormalProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.GeometryTangent"/> authoring value.</summary>
    [TypeConverter(typeof(LinearVector3TypeConverter))]
    public Vector3? GeometryTangent
    {
        get => (Vector3?)GetValue(GeometryTangentProperty);
        set => SetValue(GeometryTangentProperty, value);
    }

    /// <summary>Gets or sets the <see cref="OpenPbrSurface.GeometryCoatTangent"/> authoring value.</summary>
    [TypeConverter(typeof(LinearVector3TypeConverter))]
    public Vector3? GeometryCoatTangent
    {
        get => (Vector3?)GetValue(GeometryCoatTangentProperty);
        set => SetValue(GeometryCoatTangentProperty, value);
    }

    /// <summary>Returns an independent Core snapshot; subsequent XAML edits do not mutate it.</summary>
    public OpenPbrSurface ToSurface() => new()
    {
        Graph = Graph,
        Name = Name,
        MetersPerUnit = MetersPerUnit,
        BaseWeight = BaseWeight,
        BaseDiffuseRoughness = BaseDiffuseRoughness,
        BaseMetalness = BaseMetalness,
        SpecularWeight = SpecularWeight,
        SpecularRoughness = SpecularRoughness,
        SpecularIor = SpecularIor,
        SpecularRoughnessAnisotropy = SpecularRoughnessAnisotropy,
        TransmissionWeight = TransmissionWeight,
        TransmissionDepth = TransmissionDepth,
        TransmissionScatterAnisotropy = TransmissionScatterAnisotropy,
        TransmissionDispersionScale = TransmissionDispersionScale,
        TransmissionDispersionAbbeNumber = TransmissionDispersionAbbeNumber,
        SubsurfaceWeight = SubsurfaceWeight,
        SubsurfaceRadius = SubsurfaceRadius,
        SubsurfaceScatterAnisotropy = SubsurfaceScatterAnisotropy,
        FuzzWeight = FuzzWeight,
        FuzzRoughness = FuzzRoughness,
        CoatWeight = CoatWeight,
        CoatRoughness = CoatRoughness,
        CoatRoughnessAnisotropy = CoatRoughnessAnisotropy,
        CoatIor = CoatIor,
        CoatDarkening = CoatDarkening,
        ThinFilmWeight = ThinFilmWeight,
        ThinFilmThickness = ThinFilmThickness,
        ThinFilmIor = ThinFilmIor,
        EmissionLuminance = EmissionLuminance,
        GeometryOpacity = GeometryOpacity,
        BaseColor = BaseColor,
        SpecularColor = SpecularColor,
        TransmissionColor = TransmissionColor,
        TransmissionScatter = TransmissionScatter,
        SubsurfaceColor = SubsurfaceColor,
        FuzzColor = FuzzColor,
        CoatColor = CoatColor,
        EmissionColor = EmissionColor,
        SubsurfaceRadiusScale = SubsurfaceRadiusScale,
        GeometryThinWalled = GeometryThinWalled,
        GeometryNormal = GeometryNormal,
        GeometryCoatNormal = GeometryCoatNormal,
        GeometryTangent = GeometryTangent,
        GeometryCoatTangent = GeometryCoatTangent,
    };

    /// <summary>Copies an imported or application-authored surface as local values and publishes one change notification.</summary>
    /// <remarks>
    /// These explicit assignments take precedence over existing bindings. The application owns
    /// synchronization of editor controls or view models and must reestablish any bindings that
    /// should continue driving the imported parameters after loading.
    /// </remarks>
    public void LoadSurface(OpenPbrSurface source)
    {
        ArgumentNullException.ThrowIfNull(source);
        loading = true;
        try
        {
            Graph = source.Graph;
            Name = source.Name;
            MetersPerUnit = source.MetersPerUnit;
            BaseWeight = source.BaseWeight;
            BaseDiffuseRoughness = source.BaseDiffuseRoughness;
            BaseMetalness = source.BaseMetalness;
            SpecularWeight = source.SpecularWeight;
            SpecularRoughness = source.SpecularRoughness;
            SpecularIor = source.SpecularIor;
            SpecularRoughnessAnisotropy = source.SpecularRoughnessAnisotropy;
            TransmissionWeight = source.TransmissionWeight;
            TransmissionDepth = source.TransmissionDepth;
            TransmissionScatterAnisotropy = source.TransmissionScatterAnisotropy;
            TransmissionDispersionScale = source.TransmissionDispersionScale;
            TransmissionDispersionAbbeNumber = source.TransmissionDispersionAbbeNumber;
            SubsurfaceWeight = source.SubsurfaceWeight;
            SubsurfaceRadius = source.SubsurfaceRadius;
            SubsurfaceScatterAnisotropy = source.SubsurfaceScatterAnisotropy;
            FuzzWeight = source.FuzzWeight;
            FuzzRoughness = source.FuzzRoughness;
            CoatWeight = source.CoatWeight;
            CoatRoughness = source.CoatRoughness;
            CoatRoughnessAnisotropy = source.CoatRoughnessAnisotropy;
            CoatIor = source.CoatIor;
            CoatDarkening = source.CoatDarkening;
            ThinFilmWeight = source.ThinFilmWeight;
            ThinFilmThickness = source.ThinFilmThickness;
            ThinFilmIor = source.ThinFilmIor;
            EmissionLuminance = source.EmissionLuminance;
            GeometryOpacity = source.GeometryOpacity;
            BaseColor = source.BaseColor;
            SpecularColor = source.SpecularColor;
            TransmissionColor = source.TransmissionColor;
            TransmissionScatter = source.TransmissionScatter;
            SubsurfaceColor = source.SubsurfaceColor;
            FuzzColor = source.FuzzColor;
            CoatColor = source.CoatColor;
            EmissionColor = source.EmissionColor;
            SubsurfaceRadiusScale = source.SubsurfaceRadiusScale;
            GeometryThinWalled = source.GeometryThinWalled;
            GeometryNormal = source.GeometryNormal;
            GeometryCoatNormal = source.GeometryCoatNormal;
            GeometryTangent = source.GeometryTangent;
            GeometryCoatTangent = source.GeometryCoatTangent;
        }
        finally { loading = false; }
        changedEvents.HandleEvent(this, EventArgs.Empty, nameof(Changed));
    }

    private static BindableProperty CreateProperty(string name, Type type, object? defaultValue,
        Action<OpenPbrSurface, object?> validate) => BindableProperty.Create(
        name, type, typeof(OpenPbrSurface3D), defaultValue,
        validateValue: (_, value) =>
        {
            try { validate(new OpenPbrSurface(), value); return true; }
            catch (ArgumentException) { return false; }
        },
        propertyChanged: static (bindable, _, _) =>
        {
            OpenPbrSurface3D surface = (OpenPbrSurface3D)bindable;
            if (!surface.loading) surface.changedEvents.HandleEvent(surface, EventArgs.Empty, nameof(Changed));
        });
}
