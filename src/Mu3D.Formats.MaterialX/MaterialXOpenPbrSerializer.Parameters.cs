using System.Numerics;
using System.Xml.Linq;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Formats.MaterialX;

// Static typed mapping to the pinned OpenPBR 1.1.1 definition; no runtime reflection.
public static partial class MaterialXOpenPbrSerializer
{
    private static void SetParameter(OpenPbrSurface surface, string name, string type, string value,
        string? colorSpace, StandardRgbColorSpaceReference? inheritedColorSpace)
    {
        switch (name)
        {
            case "base_weight":
                CheckType(name, type, "float", colorSpace);
                surface.BaseWeight = ParseFloat(value);
                break;
            case "base_color":
                CheckType(name, type, "color3", colorSpace, isColor: true);
                surface.BaseColor = ParseColor(value, colorSpace, inheritedColorSpace);
                break;
            case "base_diffuse_roughness":
                CheckType(name, type, "float", colorSpace);
                surface.BaseDiffuseRoughness = ParseFloat(value);
                break;
            case "base_metalness":
                CheckType(name, type, "float", colorSpace);
                surface.BaseMetalness = ParseFloat(value);
                break;
            case "specular_weight":
                CheckType(name, type, "float", colorSpace);
                surface.SpecularWeight = ParseFloat(value);
                break;
            case "specular_color":
                CheckType(name, type, "color3", colorSpace, isColor: true);
                surface.SpecularColor = ParseColor(value, colorSpace, inheritedColorSpace);
                break;
            case "specular_roughness":
                CheckType(name, type, "float", colorSpace);
                surface.SpecularRoughness = ParseFloat(value);
                break;
            case "specular_ior":
                CheckType(name, type, "float", colorSpace);
                surface.SpecularIor = ParseFloat(value);
                break;
            case "specular_roughness_anisotropy":
                CheckType(name, type, "float", colorSpace);
                surface.SpecularRoughnessAnisotropy = ParseFloat(value);
                break;
            case "transmission_weight":
                CheckType(name, type, "float", colorSpace);
                surface.TransmissionWeight = ParseFloat(value);
                break;
            case "transmission_color":
                CheckType(name, type, "color3", colorSpace, isColor: true);
                surface.TransmissionColor = ParseColor(value, colorSpace, inheritedColorSpace);
                break;
            case "transmission_depth":
                CheckType(name, type, "float", colorSpace);
                surface.TransmissionDepth = ParseFloat(value);
                break;
            case "transmission_scatter":
                CheckType(name, type, "color3", colorSpace, isColor: true);
                surface.TransmissionScatter = ParseColor(value, colorSpace, inheritedColorSpace);
                break;
            case "transmission_scatter_anisotropy":
                CheckType(name, type, "float", colorSpace);
                surface.TransmissionScatterAnisotropy = ParseFloat(value);
                break;
            case "transmission_dispersion_scale":
                CheckType(name, type, "float", colorSpace);
                surface.TransmissionDispersionScale = ParseFloat(value);
                break;
            case "transmission_dispersion_abbe_number":
                CheckType(name, type, "float", colorSpace);
                surface.TransmissionDispersionAbbeNumber = ParseFloat(value);
                break;
            case "subsurface_weight":
                CheckType(name, type, "float", colorSpace);
                surface.SubsurfaceWeight = ParseFloat(value);
                break;
            case "subsurface_color":
                CheckType(name, type, "color3", colorSpace, isColor: true);
                surface.SubsurfaceColor = ParseColor(value, colorSpace, inheritedColorSpace);
                break;
            case "subsurface_radius":
                CheckType(name, type, "float", colorSpace);
                surface.SubsurfaceRadius = ParseFloat(value);
                break;
            case "subsurface_radius_scale":
                CheckType(name, type, "color3", colorSpace);
                surface.SubsurfaceRadiusScale = ParseVector(value);
                break;
            case "subsurface_scatter_anisotropy":
                CheckType(name, type, "float", colorSpace);
                surface.SubsurfaceScatterAnisotropy = ParseFloat(value);
                break;
            case "fuzz_weight":
                CheckType(name, type, "float", colorSpace);
                surface.FuzzWeight = ParseFloat(value);
                break;
            case "fuzz_color":
                CheckType(name, type, "color3", colorSpace, isColor: true);
                surface.FuzzColor = ParseColor(value, colorSpace, inheritedColorSpace);
                break;
            case "fuzz_roughness":
                CheckType(name, type, "float", colorSpace);
                surface.FuzzRoughness = ParseFloat(value);
                break;
            case "coat_weight":
                CheckType(name, type, "float", colorSpace);
                surface.CoatWeight = ParseFloat(value);
                break;
            case "coat_color":
                CheckType(name, type, "color3", colorSpace, isColor: true);
                surface.CoatColor = ParseColor(value, colorSpace, inheritedColorSpace);
                break;
            case "coat_roughness":
                CheckType(name, type, "float", colorSpace);
                surface.CoatRoughness = ParseFloat(value);
                break;
            case "coat_roughness_anisotropy":
                CheckType(name, type, "float", colorSpace);
                surface.CoatRoughnessAnisotropy = ParseFloat(value);
                break;
            case "coat_ior":
                CheckType(name, type, "float", colorSpace);
                surface.CoatIor = ParseFloat(value);
                break;
            case "coat_darkening":
                CheckType(name, type, "float", colorSpace);
                surface.CoatDarkening = ParseFloat(value);
                break;
            case "thin_film_weight":
                CheckType(name, type, "float", colorSpace);
                surface.ThinFilmWeight = ParseFloat(value);
                break;
            case "thin_film_thickness":
                CheckType(name, type, "float", colorSpace);
                surface.ThinFilmThickness = ParseFloat(value);
                break;
            case "thin_film_ior":
                CheckType(name, type, "float", colorSpace);
                surface.ThinFilmIor = ParseFloat(value);
                break;
            case "emission_luminance":
                CheckType(name, type, "float", colorSpace);
                surface.EmissionLuminance = ParseFloat(value);
                break;
            case "emission_color":
                CheckType(name, type, "color3", colorSpace, isColor: true);
                surface.EmissionColor = ParseColor(value, colorSpace, inheritedColorSpace);
                break;
            case "geometry_opacity":
                CheckType(name, type, "float", colorSpace);
                surface.GeometryOpacity = ParseFloat(value);
                break;
            case "geometry_thin_walled":
                CheckType(name, type, "boolean", colorSpace);
                surface.GeometryThinWalled = value switch { "true" => true, "false" => false, _ => throw new InvalidDataException("Expected MaterialX boolean true or false.") };
                break;
            case "geometry_normal":
                CheckType(name, type, "vector3", colorSpace);
                surface.GeometryNormal = ParseVector(value);
                break;
            case "geometry_coat_normal":
                CheckType(name, type, "vector3", colorSpace);
                surface.GeometryCoatNormal = ParseVector(value);
                break;
            case "geometry_tangent":
                CheckType(name, type, "vector3", colorSpace);
                surface.GeometryTangent = ParseVector(value);
                break;
            case "geometry_coat_tangent":
                CheckType(name, type, "vector3", colorSpace);
                surface.GeometryCoatTangent = ParseVector(value);
                break;
            default:
                throw new NotSupportedException($"OpenPBR input '{name}' is outside the pinned definition.");
        }
    }

    private static void SetDefaultColorSpace(OpenPbrSurface surface, StandardRgbColorSpaceReference space)
    {
        surface.BaseColor = Retag(surface.BaseColor, space);
        surface.SpecularColor = Retag(surface.SpecularColor, space);
        surface.TransmissionColor = Retag(surface.TransmissionColor, space);
        surface.TransmissionScatter = Retag(surface.TransmissionScatter, space);
        surface.SubsurfaceColor = Retag(surface.SubsurfaceColor, space);
        surface.FuzzColor = Retag(surface.FuzzColor, space);
        surface.CoatColor = Retag(surface.CoatColor, space);
        surface.EmissionColor = Retag(surface.EmissionColor, space);
    }

    private static LinearRgba Retag(LinearRgba value, StandardRgbColorSpaceReference space) =>
        new(value.Red, value.Green, value.Blue, 1f, space);

    private static void WriteParameters(XElement node, OpenPbrSurface surface)
    {
        Add(node, "base_weight", "float", Format(surface.BaseWeight));
        AddColor(node, "base_color", surface.BaseColor);
        Add(node, "base_diffuse_roughness", "float", Format(surface.BaseDiffuseRoughness));
        Add(node, "base_metalness", "float", Format(surface.BaseMetalness));
        Add(node, "specular_weight", "float", Format(surface.SpecularWeight));
        AddColor(node, "specular_color", surface.SpecularColor);
        Add(node, "specular_roughness", "float", Format(surface.SpecularRoughness));
        Add(node, "specular_ior", "float", Format(surface.SpecularIor));
        Add(node, "specular_roughness_anisotropy", "float", Format(surface.SpecularRoughnessAnisotropy));
        Add(node, "transmission_weight", "float", Format(surface.TransmissionWeight));
        AddColor(node, "transmission_color", surface.TransmissionColor);
        Add(node, "transmission_depth", "float", Format(surface.TransmissionDepth));
        AddColor(node, "transmission_scatter", surface.TransmissionScatter);
        Add(node, "transmission_scatter_anisotropy", "float", Format(surface.TransmissionScatterAnisotropy));
        Add(node, "transmission_dispersion_scale", "float", Format(surface.TransmissionDispersionScale));
        Add(node, "transmission_dispersion_abbe_number", "float", Format(surface.TransmissionDispersionAbbeNumber));
        Add(node, "subsurface_weight", "float", Format(surface.SubsurfaceWeight));
        AddColor(node, "subsurface_color", surface.SubsurfaceColor);
        Add(node, "subsurface_radius", "float", Format(surface.SubsurfaceRadius));
        Add(node, "subsurface_radius_scale", "color3", Format(surface.SubsurfaceRadiusScale));
        Add(node, "subsurface_scatter_anisotropy", "float", Format(surface.SubsurfaceScatterAnisotropy));
        Add(node, "fuzz_weight", "float", Format(surface.FuzzWeight));
        AddColor(node, "fuzz_color", surface.FuzzColor);
        Add(node, "fuzz_roughness", "float", Format(surface.FuzzRoughness));
        Add(node, "coat_weight", "float", Format(surface.CoatWeight));
        AddColor(node, "coat_color", surface.CoatColor);
        Add(node, "coat_roughness", "float", Format(surface.CoatRoughness));
        Add(node, "coat_roughness_anisotropy", "float", Format(surface.CoatRoughnessAnisotropy));
        Add(node, "coat_ior", "float", Format(surface.CoatIor));
        Add(node, "coat_darkening", "float", Format(surface.CoatDarkening));
        Add(node, "thin_film_weight", "float", Format(surface.ThinFilmWeight));
        Add(node, "thin_film_thickness", "float", Format(surface.ThinFilmThickness));
        Add(node, "thin_film_ior", "float", Format(surface.ThinFilmIor));
        Add(node, "emission_luminance", "float", Format(surface.EmissionLuminance));
        AddColor(node, "emission_color", surface.EmissionColor);
        Add(node, "geometry_opacity", "float", Format(surface.GeometryOpacity));
        Add(node, "geometry_thin_walled", "boolean", surface.GeometryThinWalled ? "true" : "false");
        if (surface.GeometryNormal is { } geometryNormal)
            Add(node, "geometry_normal", "vector3", Format(geometryNormal));
        if (surface.GeometryCoatNormal is { } geometryCoatNormal)
            Add(node, "geometry_coat_normal", "vector3", Format(geometryCoatNormal));
        if (surface.GeometryTangent is { } geometryTangent)
            Add(node, "geometry_tangent", "vector3", Format(geometryTangent));
        if (surface.GeometryCoatTangent is { } geometryCoatTangent)
            Add(node, "geometry_coat_tangent", "vector3", Format(geometryCoatTangent));
    }
}
