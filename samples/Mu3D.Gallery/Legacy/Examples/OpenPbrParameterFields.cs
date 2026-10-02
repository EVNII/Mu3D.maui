using System.Globalization;
using System.Numerics;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Examples;

// One explicit, trimming-safe authoring map for the native and Razor sample editors.
// Core owns every value's validation; host views only edit a detached candidate.
internal sealed record OpenPbrParameterField(OpenPbrInput? Input, string PropertyName, Type ValueType,
    Func<OpenPbrSurface, object?> Read, Action<OpenPbrSurface, object?> Write)
{
    internal string Name => Input is { } input ? OpenPbrInputInfo.Get(input).Name : PropertyName;
    internal string Group => Input?.ToString() is { } name
        ? OpenPbrParameterFields.Groups.First(group => name.StartsWith(group, StringComparison.Ordinal)) : "Authoring";
    internal string Hint => OpenPbrParameterFields.Hint(this);
}

internal sealed class OpenPbrParameterEdit(OpenPbrParameterField field, OpenPbrSurface surface)
{
    internal OpenPbrParameterField Field { get; } = field;
    internal object? Original { get; } = field.Read(surface);
    internal string Text { get; set; } = OpenPbrParameterFields.Format(field.Read(surface));
    internal bool Boolean { get; set; } = field.Read(surface) is true;
    internal OpenPbrNode? Connection { get; } = field.Input is { } input &&
        surface.Graph?.Bindings.TryGetValue(input, out var node) == true ? node : null;
    internal bool UseConnection { get; set; } = field.Input is { } input && surface.Graph?.Bindings.ContainsKey(input) == true;
}

internal static class OpenPbrParameterFields
{
    internal static readonly string[] Groups = ["Base", "Specular", "Coat", "Transmission", "Subsurface", "Fuzz", "ThinFilm", "Emission", "Geometry", "Authoring"];
    internal static readonly OpenPbrParameterField[] Fields =
    [
        Field(OpenPbrInput.BaseWeight, nameof(OpenPbrSurface.BaseWeight), static s => s.BaseWeight, static (s, value) => s.BaseWeight = value),
        Field(OpenPbrInput.BaseColor, nameof(OpenPbrSurface.BaseColor), static s => s.BaseColor, static (s, value) => s.BaseColor = value),
        Field(OpenPbrInput.BaseDiffuseRoughness, nameof(OpenPbrSurface.BaseDiffuseRoughness), static s => s.BaseDiffuseRoughness, static (s, value) => s.BaseDiffuseRoughness = value),
        Field(OpenPbrInput.BaseMetalness, nameof(OpenPbrSurface.BaseMetalness), static s => s.BaseMetalness, static (s, value) => s.BaseMetalness = value),
        Field(OpenPbrInput.SpecularWeight, nameof(OpenPbrSurface.SpecularWeight), static s => s.SpecularWeight, static (s, value) => s.SpecularWeight = value),
        Field(OpenPbrInput.SpecularColor, nameof(OpenPbrSurface.SpecularColor), static s => s.SpecularColor, static (s, value) => s.SpecularColor = value),
        Field(OpenPbrInput.SpecularRoughness, nameof(OpenPbrSurface.SpecularRoughness), static s => s.SpecularRoughness, static (s, value) => s.SpecularRoughness = value),
        Field(OpenPbrInput.SpecularIor, nameof(OpenPbrSurface.SpecularIor), static s => s.SpecularIor, static (s, value) => s.SpecularIor = value),
        Field(OpenPbrInput.SpecularRoughnessAnisotropy, nameof(OpenPbrSurface.SpecularRoughnessAnisotropy), static s => s.SpecularRoughnessAnisotropy, static (s, value) => s.SpecularRoughnessAnisotropy = value),
        Field(OpenPbrInput.TransmissionWeight, nameof(OpenPbrSurface.TransmissionWeight), static s => s.TransmissionWeight, static (s, value) => s.TransmissionWeight = value),
        Field(OpenPbrInput.TransmissionColor, nameof(OpenPbrSurface.TransmissionColor), static s => s.TransmissionColor, static (s, value) => s.TransmissionColor = value),
        Field(OpenPbrInput.TransmissionDepth, nameof(OpenPbrSurface.TransmissionDepth), static s => s.TransmissionDepth, static (s, value) => s.TransmissionDepth = value),
        Field(OpenPbrInput.TransmissionScatter, nameof(OpenPbrSurface.TransmissionScatter), static s => s.TransmissionScatter, static (s, value) => s.TransmissionScatter = value),
        Field(OpenPbrInput.TransmissionScatterAnisotropy, nameof(OpenPbrSurface.TransmissionScatterAnisotropy), static s => s.TransmissionScatterAnisotropy, static (s, value) => s.TransmissionScatterAnisotropy = value),
        Field(OpenPbrInput.TransmissionDispersionScale, nameof(OpenPbrSurface.TransmissionDispersionScale), static s => s.TransmissionDispersionScale, static (s, value) => s.TransmissionDispersionScale = value),
        Field(OpenPbrInput.TransmissionDispersionAbbeNumber, nameof(OpenPbrSurface.TransmissionDispersionAbbeNumber), static s => s.TransmissionDispersionAbbeNumber, static (s, value) => s.TransmissionDispersionAbbeNumber = value),
        Field(OpenPbrInput.SubsurfaceWeight, nameof(OpenPbrSurface.SubsurfaceWeight), static s => s.SubsurfaceWeight, static (s, value) => s.SubsurfaceWeight = value),
        Field(OpenPbrInput.SubsurfaceColor, nameof(OpenPbrSurface.SubsurfaceColor), static s => s.SubsurfaceColor, static (s, value) => s.SubsurfaceColor = value),
        Field(OpenPbrInput.SubsurfaceRadius, nameof(OpenPbrSurface.SubsurfaceRadius), static s => s.SubsurfaceRadius, static (s, value) => s.SubsurfaceRadius = value),
        Field(OpenPbrInput.SubsurfaceRadiusScale, nameof(OpenPbrSurface.SubsurfaceRadiusScale), static s => s.SubsurfaceRadiusScale, static (s, value) => s.SubsurfaceRadiusScale = value),
        Field(OpenPbrInput.SubsurfaceScatterAnisotropy, nameof(OpenPbrSurface.SubsurfaceScatterAnisotropy), static s => s.SubsurfaceScatterAnisotropy, static (s, value) => s.SubsurfaceScatterAnisotropy = value),
        Field(OpenPbrInput.FuzzWeight, nameof(OpenPbrSurface.FuzzWeight), static s => s.FuzzWeight, static (s, value) => s.FuzzWeight = value),
        Field(OpenPbrInput.FuzzColor, nameof(OpenPbrSurface.FuzzColor), static s => s.FuzzColor, static (s, value) => s.FuzzColor = value),
        Field(OpenPbrInput.FuzzRoughness, nameof(OpenPbrSurface.FuzzRoughness), static s => s.FuzzRoughness, static (s, value) => s.FuzzRoughness = value),
        Field(OpenPbrInput.CoatWeight, nameof(OpenPbrSurface.CoatWeight), static s => s.CoatWeight, static (s, value) => s.CoatWeight = value),
        Field(OpenPbrInput.CoatColor, nameof(OpenPbrSurface.CoatColor), static s => s.CoatColor, static (s, value) => s.CoatColor = value),
        Field(OpenPbrInput.CoatRoughness, nameof(OpenPbrSurface.CoatRoughness), static s => s.CoatRoughness, static (s, value) => s.CoatRoughness = value),
        Field(OpenPbrInput.CoatRoughnessAnisotropy, nameof(OpenPbrSurface.CoatRoughnessAnisotropy), static s => s.CoatRoughnessAnisotropy, static (s, value) => s.CoatRoughnessAnisotropy = value),
        Field(OpenPbrInput.CoatIor, nameof(OpenPbrSurface.CoatIor), static s => s.CoatIor, static (s, value) => s.CoatIor = value),
        Field(OpenPbrInput.CoatDarkening, nameof(OpenPbrSurface.CoatDarkening), static s => s.CoatDarkening, static (s, value) => s.CoatDarkening = value),
        Field(OpenPbrInput.ThinFilmWeight, nameof(OpenPbrSurface.ThinFilmWeight), static s => s.ThinFilmWeight, static (s, value) => s.ThinFilmWeight = value),
        Field(OpenPbrInput.ThinFilmThickness, nameof(OpenPbrSurface.ThinFilmThickness), static s => s.ThinFilmThickness, static (s, value) => s.ThinFilmThickness = value),
        Field(OpenPbrInput.ThinFilmIor, nameof(OpenPbrSurface.ThinFilmIor), static s => s.ThinFilmIor, static (s, value) => s.ThinFilmIor = value),
        Field(OpenPbrInput.EmissionLuminance, nameof(OpenPbrSurface.EmissionLuminance), static s => s.EmissionLuminance, static (s, value) => s.EmissionLuminance = value),
        Field(OpenPbrInput.EmissionColor, nameof(OpenPbrSurface.EmissionColor), static s => s.EmissionColor, static (s, value) => s.EmissionColor = value),
        Field(OpenPbrInput.GeometryOpacity, nameof(OpenPbrSurface.GeometryOpacity), static s => s.GeometryOpacity, static (s, value) => s.GeometryOpacity = value),
        Field(OpenPbrInput.GeometryThinWalled, nameof(OpenPbrSurface.GeometryThinWalled), static s => s.GeometryThinWalled, static (s, value) => s.GeometryThinWalled = value),
        Field(OpenPbrInput.GeometryNormal, nameof(OpenPbrSurface.GeometryNormal), static s => s.GeometryNormal, static (s, value) => s.GeometryNormal = value),
        Field(OpenPbrInput.GeometryCoatNormal, nameof(OpenPbrSurface.GeometryCoatNormal), static s => s.GeometryCoatNormal, static (s, value) => s.GeometryCoatNormal = value),
        Field(OpenPbrInput.GeometryTangent, nameof(OpenPbrSurface.GeometryTangent), static s => s.GeometryTangent, static (s, value) => s.GeometryTangent = value),
        Field(OpenPbrInput.GeometryCoatTangent, nameof(OpenPbrSurface.GeometryCoatTangent), static s => s.GeometryCoatTangent, static (s, value) => s.GeometryCoatTangent = value),
        Field(null, nameof(OpenPbrSurface.Name), static s => s.Name, static (s, value) => s.Name = value),
        Field(null, nameof(OpenPbrSurface.MetersPerUnit), static s => s.MetersPerUnit, static (s, value) => s.MetersPerUnit = value),
    ];

    private static OpenPbrParameterField Field<T>(OpenPbrInput? input, string name,
        Func<OpenPbrSurface, T> read, Action<OpenPbrSurface, T> write) =>
        new(input, name, typeof(T), surface => read(surface), (surface, value) => write(surface, (T)value!));

    internal static OpenPbrParameterEdit[] EditGroup(OpenPbrSurface surface, string group) =>
        Fields.Where(field => field.Group == group).Select(field => new OpenPbrParameterEdit(field, surface)).ToArray();

    internal static OpenPbrSurface Apply(OpenPbrSurface surface, IEnumerable<OpenPbrParameterEdit> edits)
    {
        OpenPbrSurface candidate = surface.Clone();
        Dictionary<OpenPbrInput, OpenPbrNode> bindings = surface.Graph?.Bindings.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
        foreach (var edit in edits)
        {
            if (edit.UseConnection) continue;
            object? value = edit.Field.ValueType == typeof(bool) ? edit.Boolean
                : edit.Text == Format(edit.Original) ? edit.Original : Parse(edit.Text, edit.Field.ValueType);
            try { edit.Field.Write(candidate, value); }
            catch (ArgumentException) { throw new ArgumentException($"{edit.Field.Name}: {edit.Field.Hint}"); }
            if (edit.Field.Input is { } input) bindings.Remove(input);
        }
        // Immutable graph and color-space identities survive no-op edits.
        if (bindings.Count != (surface.Graph?.Bindings.Count ?? 0))
            candidate.Graph = bindings.Count == 0 ? null : new OpenPbrGraph(bindings);
        return candidate;
    }

    internal static object? Parse(string text, Type type)
    {
        if (type == typeof(string)) return text;
        if (type == typeof(float)) return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (type == typeof(Vector3?) && string.IsNullOrWhiteSpace(text)) return null;
        if (type == typeof(LinearRgba))
        {
            string[] sections = text.Split(';');
            if (sections.Length != 2) throw new FormatException("Color requires r,g,b;linear color space.");
            Vector3 values = Vector(sections[0]);
            StandardRgbColorSpaceReference space = sections[1].Trim().ToLowerInvariant() switch
            {
                "acescg" => StandardColorSpaces.AcesCg,
                "lin_rec709" => StandardColorSpaces.LinearSrgb,
                "lin_displayp3" => StandardColorSpaces.LinearDisplayP3,
                "lin_rec2020" => StandardColorSpaces.LinearRec2020,
                "lin_adobergb" => StandardColorSpaces.LinearAdobeRgb,
                "lin_prophoto" => StandardColorSpaces.LinearProPhotoRgb,
                _ => throw new FormatException("Unknown linear color space."),
            };
            return new LinearRgba(values.X, values.Y, values.Z, 1, space);
        }
        return Vector(text);
    }

    private static Vector3 Vector(string text)
    {
        string[] parts = text.Split(',');
        if (parts.Length != 3) throw new FormatException("Enter three invariant decimal values separated by commas.");
        return new(float.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture),
            float.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture),
            float.Parse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    internal static string Format(object? value) => value switch
    {
        null => "",
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        Vector3 vector => FormattableString.Invariant($"{vector.X:R},{vector.Y:R},{vector.Z:R}"),
        LinearRgba color => FormattableString.Invariant($"{color.Red:R},{color.Green:R},{color.Blue:R};{Space(color)}"),
        _ => value.ToString() ?? "",
    };

    private static string Space(LinearRgba color) => color.ColorSpace == StandardColorSpaces.AcesCg ? "acescg"
        : color.ColorSpace == StandardColorSpaces.LinearSrgb ? "lin_rec709"
        : color.ColorSpace == StandardColorSpaces.LinearDisplayP3 ? "lin_displayp3"
        : color.ColorSpace == StandardColorSpaces.LinearAdobeRgb ? "lin_adobergb"
        : color.ColorSpace == StandardColorSpaces.LinearProPhotoRgb ? "lin_prophoto"
        : color.ColorSpace == StandardColorSpaces.LinearRec2020 ? "lin_rec2020" : color.ColorSpace.ToString()!;

    internal static string Hint(OpenPbrParameterField field)
    {
        if (field.Input is not { } input) return field.PropertyName == nameof(OpenPbrSurface.Name)
            ? "Optional material name" : "Metres per scene unit; greater than zero";
        var info = OpenPbrInputInfo.Get(input);
        if (info.Type == OpenPbrNodeType.Color3) return "r,g,b;space · linear acescg / lin_rec709 / lin_displayp3 / lin_rec2020 / lin_adobergb / lin_prophoto · alpha = 1";
        if (info.Type == OpenPbrNodeType.Boolean) return "Thin-walled surface; no solid interior";
        if (info.Type == OpenPbrNodeType.Vector3) return input == OpenPbrInput.SubsurfaceRadiusScale
            ? "Nonnegative R,G,B radius multipliers; numeric data" : "World-space x,y,z; nonzero. Empty = inherit mesh";
        string range = info.Maximum == float.MaxValue ? "Finite value ≥ 0" : FormattableString.Invariant($"{info.Minimum}–{info.Maximum}");
        return range + (input switch
        {
            OpenPbrInput.ThinFilmThickness => " · micrometres",
            OpenPbrInput.EmissionLuminance => " · nits (cd/m²)",
            OpenPbrInput.TransmissionDepth or OpenPbrInput.SubsurfaceRadius => " · scene units",
            OpenPbrInput.SpecularRoughness => " · base reflection only; does not change the coat",
            OpenPbrInput.CoatRoughness => " · coat reflection only",
            _ => "",
        });
    }
}
