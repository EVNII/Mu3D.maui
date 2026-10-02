using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Mu3D.Color;

/// <summary>Identifies an authoritative display view with a fixed peak and limiting gamut.</summary>
public enum ColorViewPreset
{
    /// <summary>Blender 5.0 AgX Base, 100-nit SDR Rec.709.</summary>
    AgXSdr,
    /// <summary>Blender 5.0 AgX, 1000-nit HDR with P3 D65 limiting gamut.</summary>
    AgXHdr1000,
    /// <summary>ACES 2.0, 100-nit SDR with Rec.709 limiting gamut.</summary>
    Aces2Sdr,
    /// <summary>ACES 2.0, 1000-nit HDR with Rec.2020 limiting gamut.</summary>
    Aces2Hdr1000,
    /// <summary>Blender 5.0 AgX Base, SDR Rec.2020 limiting gamut; output preserves extended linear sRGB.</summary>
    AgXSdrRec2020,
    /// <summary>ACES 2.0, 1000-nit HDR with P3 D65 limiting gamut.</summary>
    Aces2Hdr1000P3,
    /// <summary>Blender 5.0 Filmic base view without an additional Look, 100-nit SDR Rec.709.</summary>
    FilmicSdr,
}

/// <summary>
/// Applies pinned official AgX, Filmic or ACES 2.0 display rendering to scene-linear RGB.
/// Output is unpremultiplied display-linear sRGB, with one equal to <see cref="ReferenceWhiteNits"/>.
/// Exposure precedes the view; alpha is preserved. No PQ, HLG or sRGB encoding is returned.
/// </summary>
public sealed class ColorViewTransform : ILinearRgbTransform
{
    /// <summary>
    /// Gets the maximum supported absolute scene component after exposure. This conservative
    /// FP32 domain leaves room for the official input matrices, including negative RGB values.
    /// </summary>
    public const float MaximumExposedComponentMagnitude = 1e30f;

    private readonly ColorViewEvaluator evaluator;
    private readonly float exposure;

    /// <summary>
    /// Creates a fixed official display preset from any built-in standard linear RGB space.
    /// Exposure is limited to [-32,32] stops and reference white to [1,10000] nits.
    /// Reference-white normalization does not change the preset's fixed mastering peak or gamut.
    /// Exposed RGB must remain within <see cref="MaximumExposedComponentMagnitude"/> in absolute value.
    /// </summary>
    public ColorViewTransform(ColorViewPreset preset, ColorSpaceReference sourceSpace,
        float exposureStops = 0, float referenceWhiteNits = 100)
    {
        if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
        ArgumentNullException.ThrowIfNull(sourceSpace);
        if (sourceSpace is not StandardRgbColorSpaceReference standardSpace)
            throw new ArgumentException("Display presets require a built-in standard linear RGB space.", nameof(sourceSpace));
        if (!float.IsFinite(exposureStops) || exposureStops is < -32 or > 32)
            throw new ArgumentOutOfRangeException(nameof(exposureStops));
        if (!float.IsFinite(referenceWhiteNits) || referenceWhiteNits is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(referenceWhiteNits));
        Preset = preset; SourceSpace = sourceSpace; ExposureStops = exposureStops;
        ReferenceWhiteNits = referenceWhiteNits; exposure = MathF.Pow(2, exposureStops);
        Program = ColorViewProgram.Get(preset, standardSpace);
        evaluator = Program.Evaluator;
    }

    /// <summary>Gets the fixed official display preset.</summary>
    public ColorViewPreset Preset { get; }
    /// <summary>Gets the scene-linear source RGB identity.</summary>
    public ColorSpaceReference SourceSpace { get; }
    /// <summary>Gets the display-linear sRGB output identity; extended values are preserved.</summary>
    public ColorSpaceReference DestinationSpace => StandardColorSpaces.LinearSrgb;
    /// <summary>Gets the scene exposure adjustment in stops, applied before display rendering.</summary>
    public float ExposureStops { get; }
    /// <summary>Gets the luminance represented by output value one, in nits.</summary>
    public float ReferenceWhiteNits { get; }
    /// <summary>Gets whether the preset targets a 1000-nit HDR display.</summary>
    public bool IsHdr => Preset is ColorViewPreset.AgXHdr1000 or ColorViewPreset.Aces2Hdr1000 or ColorViewPreset.Aces2Hdr1000P3;
    /// <summary>Gets the fixed mastering peak in nits; normalization does not alter it.</summary>
    public float PeakLuminanceNits => IsHdr ? 1000 : 100;
    /// <summary>Gets the immutable portable FP32 program and original lookup resources.</summary>
    public ColorViewProgram Program { get; }

    /// <inheritdoc />
    public LinearRgba Transform(LinearRgba source)
    {
        if (source.ColorSpace != SourceSpace) throw new ArgumentException("The source color-space tag must match the view.", nameof(source));
        var r = source.Red * exposure; var g = source.Green * exposure; var b = source.Blue * exposure;
        if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b) ||
            MathF.Abs(r) > MaximumExposedComponentMagnitude ||
            MathF.Abs(g) > MaximumExposedComponentMagnitude ||
            MathF.Abs(b) > MaximumExposedComponentMagnitude)
            throw new ArgumentOutOfRangeException(nameof(source), "Exposed scene components must have absolute values at most 1e30.");
        var value = evaluator.Evaluate(new float4(r, g, b, source.Alpha));
        var scale = 100 / ReferenceWhiteNits;
        return new(value.r * scale, value.g * scale, value.b * scale, source.Alpha, DestinationSpace);
    }
}

/// <summary>
/// Contains a portable FP32 evaluator and its immutable lookup resources, generated from OCIO 2.5.2.
/// The original AgX LUT uses tetrahedral interpolation; ACES 2.0 uses its analytic transform and hue tables.
/// </summary>
public sealed class ColorViewProgram
{
    private static readonly Dictionary<string, ColorViewProgram> Programs = new();
    private static readonly Dictionary<ColorViewPreset, Vector4[]> Tables = new();
    private ColorViewProgram(string source, ColorViewPreset preset, Vector4[] table)
    {
        var name = preset + source;
        using var stream = Resource(name + ".wgsl");
        using var reader = new StreamReader(stream);
        WgslSource = reader.ReadToEnd(); Table = table;
        Evaluator = name switch
        {
            "AgXSdrRec2020LinearSrgb" => new AgXSdrRec2020LinearSrgb(table),
            "AgXSdrRec2020AcesCg" => new AgXSdrRec2020AcesCg(table),
            "AgXSdrLinearSrgb" => new AgXSdrLinearSrgb(table),
            "AgXSdrAcesCg" => new AgXSdrAcesCg(table),
            "AgXHdr1000LinearSrgb" => new AgXHdr1000LinearSrgb(table),
            "AgXHdr1000AcesCg" => new AgXHdr1000AcesCg(table),
            "Aces2SdrLinearSrgb" => new Aces2SdrLinearSrgb(table),
            "Aces2SdrAcesCg" => new Aces2SdrAcesCg(table),
            "Aces2Hdr1000P3LinearSrgb" => new Aces2Hdr1000P3LinearSrgb(table),
            "Aces2Hdr1000P3AcesCg" => new Aces2Hdr1000P3AcesCg(table),
            "Aces2Hdr1000LinearSrgb" => new Aces2Hdr1000LinearSrgb(table),
            "Aces2Hdr1000AcesCg" => new Aces2Hdr1000AcesCg(table),
            "FilmicSdrLinearSrgb" => new FilmicSdrLinearSrgb(table),
            "FilmicSdrAcesCg" => new FilmicSdrAcesCg(table),
            _ => throw new InvalidOperationException("Unknown generated color program."),
        };
    }
    /// <summary>
    /// Gets WGSL defining mu3d_color_view(vec3&lt;f32&gt;), with a read-only vec4 storage table at group 1 binding 0.
    /// Input is scene RGB in the transform's SourceSpace; output is display-linear sRGB normalized to 100 nits.
    /// Apply exposure before this function and multiply its result by 100/ReferenceWhiteNits afterward.
    /// Each exposed input component must be finite and have absolute value at most
    /// <see cref="ColorViewTransform.MaximumExposedComponentMagnitude"/>.
    /// </summary>
    public string WgslSource { get; }
    /// <summary>Gets the FP32 RGBA lookup entries consumed by the program's single storage binding.</summary>
    public ReadOnlyMemory<Vector4> Table { get; }
    internal ColorViewEvaluator Evaluator { get; }
    private ColorViewProgram(ColorViewProgram basis, StandardRgbColorSpaceReference source)
    {
        Table = basis.Table;
        Evaluator = new AdaptedEvaluator(basis.Evaluator, source);
        var columns = Enumerable.Range(0, 3).Select(i => StandardLinearRgbConverter.Convert(
            new LinearRgba(i == 0 ? 1 : 0, i == 1 ? 1 : 0, i == 2 ? 1 : 0, 1, source),
            StandardColorSpaces.AcesCg)).ToArray();
        var coefficients = columns.SelectMany(c => new[] { c.Red, c.Green, c.Blue })
            .Select(v => v.ToString("E9", System.Globalization.CultureInfo.InvariantCulture) + "f");
        WgslSource = basis.WgslSource.Replace("fn mu3d_color_view(", "fn mu3d_aces_color_view(", StringComparison.Ordinal) +
            "\nfn mu3d_color_view(rgb: vec3<f32>) -> vec3<f32> { return mu3d_aces_color_view(mat3x3<f32>(" +
            string.Join(",", coefficients) + ") * rgb); }\n";
    }

    private sealed class AdaptedEvaluator(ColorViewEvaluator basis, StandardRgbColorSpaceReference source) : ColorViewEvaluator([])
    {
        internal override float4 Evaluate(float4 value)
        {
            var c = StandardLinearRgbConverter.Convert(new LinearRgba(value.r, value.g, value.b, value.a, source), StandardColorSpaces.AcesCg);
            return basis.Evaluate(new float4(c.Red, c.Green, c.Blue, c.Alpha));
        }
    }

    internal static ColorViewProgram Get(ColorViewPreset preset, StandardRgbColorSpaceReference source)
    {
        if (source == StandardColorSpaces.LinearSrgb) return Get(preset, "LinearSrgb");
        if (source == StandardColorSpaces.AcesCg) return Get(preset, "AcesCg");
        lock (Programs)
        {
            var key = preset + source.Name;
            if (!Programs.TryGetValue(key, out var program))
            {
                program = new ColorViewProgram(Get(preset, "AcesCg"), source);
                Programs.Add(key, program);
            }
            return program;
        }
    }
    internal static ColorViewProgram Get(ColorViewPreset preset, string source)
    {
        lock (Programs)
        {
            var name = preset + source;
            if (Programs.TryGetValue(name, out var program)) return program;
            if (!Tables.TryGetValue(preset, out var table))
            {
                using var stream = Resource(preset + ".bin");
                var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
                table = MemoryMarshal.Cast<byte, Vector4>(bytes).ToArray(); Tables.Add(preset, table);
            }
            program = new(source, preset, table); Programs.Add(name, program); return program;
        }
    }
    private static Stream Resource(string name) => typeof(ColorViewProgram).Assembly.GetManifestResourceStream(
        "Mu3D.Color.ColorViews.Generated." + name) ?? throw new InvalidOperationException("Missing pinned color resource: " + name);
}
