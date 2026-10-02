using Mu3D.Color;

namespace Mu3D.Maui.Controls;

/// <summary>Declares an explicit final AgX, Filmic or ACES 2 display view in MAUI XAML.</summary>
/// <remarks>
/// Attach to <c>Mu3DSceneView.DisplayTransform</c>. Scene lighting and reference accumulation remain
/// linear. HDR presets require an extended-linear HDR surface. Reference white is a normalization
/// selected by the application, not a measurement of the connected display.
/// </remarks>
public sealed class ColorView3D : BindableObject
{
    private ColorViewTransform? cached;
    private readonly WeakEventManager changedEvents = new();

    /// <summary>Identifies the selected official display-view preset.</summary>
    public static readonly BindableProperty PresetProperty = BindableProperty.Create(
        nameof(Preset), typeof(ColorViewPreset), typeof(ColorView3D), ColorViewPreset.AgXSdr,
        validateValue: static (_, value) => value is ColorViewPreset preset && Enum.IsDefined(preset),
        propertyChanged: static (owner, _, _) => ((ColorView3D)owner).Invalidate());

    /// <summary>Identifies the exposure applied before display rendering.</summary>
    public static readonly BindableProperty ExposureStopsProperty = BindableProperty.Create(
        nameof(ExposureStops), typeof(float), typeof(ColorView3D), 0f,
        validateValue: static (_, value) => value is float stops && float.IsFinite(stops) && stops is >= -32 and <= 32,
        propertyChanged: static (owner, _, _) => ((ColorView3D)owner).Invalidate());

    /// <summary>Identifies the explicit luminance represented by one display-linear output unit.</summary>
    public static readonly BindableProperty ReferenceWhiteNitsProperty = BindableProperty.Create(
        nameof(ReferenceWhiteNits), typeof(float), typeof(ColorView3D), 100f,
        validateValue: static (_, value) => value is float nits && float.IsFinite(nits) && nits is >= 1 and <= 10000,
        propertyChanged: static (owner, _, _) => ((ColorView3D)owner).Invalidate());

    /// <summary>Gets or sets the SDR or 1000-nit HDR view. No view is enabled until this component is attached.</summary>
    public ColorViewPreset Preset { get => (ColorViewPreset)GetValue(PresetProperty); set => SetValue(PresetProperty, value); }

    /// <summary>Gets or sets exposure in stops, from -32 through +32, before the view.</summary>
    public float ExposureStops { get => (float)GetValue(ExposureStopsProperty); set => SetValue(ExposureStopsProperty, value); }

    /// <summary>Gets or sets output reference white in nits, from 1 through 10000. Defaults to 100.</summary>
    public float ReferenceWhiteNits { get => (float)GetValue(ReferenceWhiteNitsProperty); set => SetValue(ReferenceWhiteNitsProperty, value); }

    /// <summary>Occurs when a view parameter changes and attached views need another frame.</summary>
    public event EventHandler Changed
    {
        add => changedEvents.AddEventHandler(value);
        remove => changedEvents.RemoveEventHandler(value);
    }

    /// <summary>Creates or reuses an immutable CPU transform for the supplied scene-linear source space.</summary>
    public ColorViewTransform ToTransform(ColorSpaceReference sourceSpace)
    {
        if (cached is null || cached.SourceSpace != sourceSpace)
            cached = new ColorViewTransform(Preset, sourceSpace, ExposureStops, ReferenceWhiteNits);
        return cached;
    }

    private void Invalidate() { cached = null; changedEvents.HandleEvent(this, EventArgs.Empty, nameof(Changed)); }
}
