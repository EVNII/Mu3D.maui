using System.Numerics;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering.OpenPbr;

// Matched by resolve_material in eng/openpbr/render.slang. Every authored input has an explicit slot.
internal static class OpenPbrGpuMaterial
{
    internal const int Stride = 19;

    internal static Vector4[] Pack(OpenPbrMaterial material, float metersPerSceneUnit)
    {
        OpenPbrSurface s = PackingConstants(material.Surface);
        if ((s.Graph?.Maximum(OpenPbrInput.EmissionLuminance, s.EmissionLuminance) ?? s.EmissionLuminance) > 0 && material.NitsPerSceneUnit is null)
            throw new InvalidOperationException("Active OpenPBR emission requires NitsPerSceneUnit on its material.");
        float distanceScale = s.MetersPerUnit / metersPerSceneUnit;
        Vector4[] result =
        [
            Color(s.BaseColor, s.BaseWeight),
            Color(s.SpecularColor, s.BaseMetalness),
            new(s.BaseDiffuseRoughness, s.SpecularWeight, s.SpecularRoughness, s.SpecularIor),
            new(s.SpecularRoughnessAnisotropy, s.TransmissionWeight, s.TransmissionDepth * distanceScale, s.TransmissionScatterAnisotropy),
            Color(s.TransmissionColor, s.TransmissionDispersionScale),
            Color(s.TransmissionScatter, s.TransmissionDispersionAbbeNumber),
            Color(s.SubsurfaceColor, s.SubsurfaceWeight),
            new(s.SubsurfaceRadiusScale, s.SubsurfaceRadius * distanceScale),
            new(s.SubsurfaceScatterAnisotropy, s.FuzzWeight, s.FuzzRoughness, s.CoatWeight),
            Color(s.FuzzColor, s.CoatRoughness),
            Color(s.CoatColor, s.CoatIor),
            new(s.CoatRoughnessAnisotropy, s.CoatDarkening, s.ThinFilmWeight, s.ThinFilmThickness),
            new(s.ThinFilmIor, s.EmissionLuminance, s.GeometryOpacity, s.GeometryThinWalled ? 1 : 0),
            Color(s.EmissionColor, s.MetersPerUnit, bounded: false),
            Direction(s.GeometryNormal), Direction(s.GeometryTangent),
            Direction(s.GeometryCoatNormal), Direction(s.GeometryCoatTangent),
            new(material.NitsPerSceneUnit ?? 1, 0, 0, 0),
        ];
        foreach (Vector4 value in result)
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
                throw new ArgumentOutOfRangeException(nameof(material), "Material conversion exceeds finite FP32 storage.");
        // The upstream multiplies luminance by color before converting nits to scene units.
        // Validate both intermediates so extreme but individually finite inputs cannot overflow.
        Vector3 emission = new(result[13].X, result[13].Y, result[13].Z);
        emission *= s.EmissionLuminance;
        Vector3 sceneEmission = emission / (material.NitsPerSceneUnit ?? 1);
        if (!float.IsFinite(emission.X) || !float.IsFinite(emission.Y) || !float.IsFinite(emission.Z) ||
            !float.IsFinite(sceneEmission.X) || !float.IsFinite(sceneEmission.Y) || !float.IsFinite(sceneEmission.Z))
            throw new ArgumentOutOfRangeException(nameof(material), "Emission conversion exceeds finite FP32 radiance.");
        if (s.Graph is { } graph)
        {
            float luminance = graph.Maximum(OpenPbrInput.EmissionLuminance, s.EmissionLuminance);
            Vector4 color = graph.Bindings.TryGetValue(OpenPbrInput.EmissionColor, out var node) ? node.Maximum : result[13];
            double maximum = Math.Max(color.X, Math.Max(color.Y, color.Z)) * (double)luminance;
            if (maximum > float.MaxValue || maximum / (material.NitsPerSceneUnit ?? 1) > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(material), "Graph emission exceeds finite FP32 radiance.");
            float depth = graph.Maximum(OpenPbrInput.TransmissionDepth, s.TransmissionDepth);
            float radius = graph.Maximum(OpenPbrInput.SubsurfaceRadius, s.SubsurfaceRadius);
            if (!float.IsFinite(depth * distanceScale) || !float.IsFinite(radius * distanceScale))
                throw new ArgumentOutOfRangeException(nameof(material), "Graph distance conversion exceeds finite FP32.");
        }
        return result;
    }

    private static OpenPbrSurface PackingConstants(OpenPbrSurface authored)
    {
        if (authored.Graph is null || authored.Graph.Bindings.Count == 0) return authored;
        // Connected values dominate their stored authoring fallback, even when that fallback is
        // outside the physical renderer's domain. Never mutate the application-owned surface.
        var surface = authored.Clone(); var defaults = new OpenPbrSurface();
        foreach (var input in authored.Graph.Bindings.Keys)
            switch (input)
            {
                case OpenPbrInput.BaseWeight: surface.BaseWeight = defaults.BaseWeight; break;
                case OpenPbrInput.BaseColor: surface.BaseColor = defaults.BaseColor; break;
                case OpenPbrInput.BaseDiffuseRoughness: surface.BaseDiffuseRoughness = defaults.BaseDiffuseRoughness; break;
                case OpenPbrInput.BaseMetalness: surface.BaseMetalness = defaults.BaseMetalness; break;
                case OpenPbrInput.SpecularWeight: surface.SpecularWeight = defaults.SpecularWeight; break;
                case OpenPbrInput.SpecularColor: surface.SpecularColor = defaults.SpecularColor; break;
                case OpenPbrInput.SpecularRoughness: surface.SpecularRoughness = defaults.SpecularRoughness; break;
                case OpenPbrInput.SpecularIor: surface.SpecularIor = defaults.SpecularIor; break;
                case OpenPbrInput.SpecularRoughnessAnisotropy: surface.SpecularRoughnessAnisotropy = defaults.SpecularRoughnessAnisotropy; break;
                case OpenPbrInput.TransmissionWeight: surface.TransmissionWeight = defaults.TransmissionWeight; break;
                case OpenPbrInput.TransmissionColor: surface.TransmissionColor = defaults.TransmissionColor; break;
                case OpenPbrInput.TransmissionDepth: surface.TransmissionDepth = defaults.TransmissionDepth; break;
                case OpenPbrInput.TransmissionScatter: surface.TransmissionScatter = defaults.TransmissionScatter; break;
                case OpenPbrInput.TransmissionScatterAnisotropy: surface.TransmissionScatterAnisotropy = defaults.TransmissionScatterAnisotropy; break;
                case OpenPbrInput.TransmissionDispersionScale: surface.TransmissionDispersionScale = defaults.TransmissionDispersionScale; break;
                case OpenPbrInput.TransmissionDispersionAbbeNumber: surface.TransmissionDispersionAbbeNumber = defaults.TransmissionDispersionAbbeNumber; break;
                case OpenPbrInput.SubsurfaceWeight: surface.SubsurfaceWeight = defaults.SubsurfaceWeight; break;
                case OpenPbrInput.SubsurfaceColor: surface.SubsurfaceColor = defaults.SubsurfaceColor; break;
                case OpenPbrInput.SubsurfaceRadius: surface.SubsurfaceRadius = defaults.SubsurfaceRadius; break;
                case OpenPbrInput.SubsurfaceRadiusScale: surface.SubsurfaceRadiusScale = defaults.SubsurfaceRadiusScale; break;
                case OpenPbrInput.SubsurfaceScatterAnisotropy: surface.SubsurfaceScatterAnisotropy = defaults.SubsurfaceScatterAnisotropy; break;
                case OpenPbrInput.FuzzWeight: surface.FuzzWeight = defaults.FuzzWeight; break;
                case OpenPbrInput.FuzzColor: surface.FuzzColor = defaults.FuzzColor; break;
                case OpenPbrInput.FuzzRoughness: surface.FuzzRoughness = defaults.FuzzRoughness; break;
                case OpenPbrInput.CoatWeight: surface.CoatWeight = defaults.CoatWeight; break;
                case OpenPbrInput.CoatColor: surface.CoatColor = defaults.CoatColor; break;
                case OpenPbrInput.CoatRoughness: surface.CoatRoughness = defaults.CoatRoughness; break;
                case OpenPbrInput.CoatRoughnessAnisotropy: surface.CoatRoughnessAnisotropy = defaults.CoatRoughnessAnisotropy; break;
                case OpenPbrInput.CoatIor: surface.CoatIor = defaults.CoatIor; break;
                case OpenPbrInput.CoatDarkening: surface.CoatDarkening = defaults.CoatDarkening; break;
                case OpenPbrInput.ThinFilmWeight: surface.ThinFilmWeight = defaults.ThinFilmWeight; break;
                case OpenPbrInput.ThinFilmThickness: surface.ThinFilmThickness = defaults.ThinFilmThickness; break;
                case OpenPbrInput.ThinFilmIor: surface.ThinFilmIor = defaults.ThinFilmIor; break;
                case OpenPbrInput.EmissionLuminance: surface.EmissionLuminance = defaults.EmissionLuminance; break;
                case OpenPbrInput.EmissionColor: surface.EmissionColor = defaults.EmissionColor; break;
                case OpenPbrInput.GeometryOpacity: surface.GeometryOpacity = defaults.GeometryOpacity; break;
                case OpenPbrInput.GeometryThinWalled: surface.GeometryThinWalled = defaults.GeometryThinWalled; break;
                case OpenPbrInput.GeometryNormal: surface.GeometryNormal = defaults.GeometryNormal; break;
                case OpenPbrInput.GeometryCoatNormal: surface.GeometryCoatNormal = defaults.GeometryCoatNormal; break;
                case OpenPbrInput.GeometryTangent: surface.GeometryTangent = defaults.GeometryTangent; break;
                case OpenPbrInput.GeometryCoatTangent: surface.GeometryCoatTangent = defaults.GeometryCoatTangent; break;
            }
        return surface;
    }

    private static Vector4 Direction(Vector3? direction) => direction is Vector3 v ? new(v, 1) : Vector4.Zero;

    private static Vector4 Color(LinearRgba value, float fourth, bool bounded = true)
    {
        LinearRgba c = StandardLinearRgbConverter.Convert(value, StandardColorSpaces.AcesCg);
        Vector3 rgb = new(c.Red, c.Green, c.Blue);
        // An achromatic standard RGB input stays achromatic under white-point adaptation.
        // Preserve that invariant exactly instead of rejecting unit white after matrix roundoff.
        if (value.Red == value.Green && value.Green == value.Blue) rgb = new(value.Red);
        if (rgb.X < 0 || rgb.Y < 0 || rgb.Z < 0 || bounded && (rgb.X > 1 || rgb.Y > 1 || rgb.Z > 1))
            throw new ArgumentOutOfRangeException(nameof(value),
                "OpenPBR transport requires nonnegative ACEScg inputs and reflectance values in [0,1]; authoring values are not silently clipped.");
        return new(rgb, fourth);
    }
}
