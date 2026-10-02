using System.Numerics;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

internal sealed class ModelLabPresenter : IDisposable
{
    private static readonly EquirectangularHdrEnvironment NeutralIndirectFill = new(
        4,
        2,
        Enumerable.Repeat(new Vector3(0.35f), 8),
        StandardColorSpaces.LinearSrgb,
        "Model Lab neutral indirect fill");
    private readonly IPresentationSurfaceSession session;
    private readonly object renderGate = new();
    private readonly Scene scene;
    private readonly GltfAsset asset;
    private readonly MaterialVariantManifest materialVariants;
    private readonly IReadOnlyList<AnimationClip> animations;
    private readonly IReadOnlyList<string> ignoredOptionalExtensions;
    private readonly bool applyAllAnimations;
    private readonly (PbrMaterial Material, float Factor)[] clearcoatMaterials;
    private readonly (PbrMaterial Material, float Strength, float Rotation)[] anisotropyMaterials;
    private readonly (
        PbrMaterial Material,
        float Factor,
        float IndexOfRefraction,
        float Thickness,
        float AttenuationDistance)[] transmissionMaterials;
    private readonly (PbrMaterial Material, float Dispersion)[] dispersionMaterials;
    private readonly (
        PbrMaterial Material,
        float Factor,
        float IndexOfRefraction,
        float ThicknessMinimum,
        float ThicknessMaximum)[] iridescenceMaterials;
    private readonly (PbrMaterial Material, float Factor, LinearRgba Color)[] specularMaterials;
    private readonly DirectionalLight light;
    private readonly Vector3 cameraTarget;
    private readonly Vector3 cameraDirection;
    private readonly float baseCameraDistance;
    private int animationIndex;
    private bool ambientOcclusionEnabled = true;
    private float ambientOcclusionRadius = 1.5f;
    private float ambientOcclusionStrength = 1f;
    private bool bloomEnabled;
    private float bloomThreshold = 1f;
    private float bloomSoftKnee = 0.5f;
    private float bloomIntensity = 0.15f;
    private float bloomRadiusPixels = 16f;
    private BloomCompositeMode bloomCompositeMode;
    private float cameraDistanceScale = 1f;
    private float cameraAzimuthDegrees;
    private float cameraElevationDegrees;
    private float lightAzimuthDegrees = -150f;
    private float lightElevationDegrees = 35f;
    private SceneRenderLayer renderLayer;
    private readonly PerspectiveCamera camera = new(aspectRatio: 1f);
    private SceneRenderer? renderer;
    private GraphicsTexture? depthTexture;
    private GraphicsExtent3D depthExtent;
    private bool disposed;

    internal ModelLabPresenter(
        IPresentationSurfaceSession session,
        GltfAsset asset,
        Vector3 cameraPosition,
        Vector3 cameraTarget,
        bool applyAllAnimations = false,
        EquirectangularHdrEnvironment? environment = null,
        bool directionalLightEnabled = true)
    {
        this.session = session;
        this.asset = asset;
        scene = asset.Scene;
        materialVariants = asset.MaterialShaderVariants;
        animations = asset.Animations;
        ignoredOptionalExtensions = asset.IgnoredOptionalExtensions;
        clearcoatMaterials = scene.Root.EnumerateDepthFirst()
            .OfType<Mesh>()
            .Select(static mesh => mesh.Material)
            .OfType<PbrMaterial>()
            .Distinct()
            .Where(static material => material.ClearcoatFactor > 0f)
            .Select(static material => (material, material.ClearcoatFactor))
            .ToArray();
        anisotropyMaterials = scene.Root.EnumerateDepthFirst()
            .OfType<Mesh>()
            .Select(static mesh => mesh.Material)
            .OfType<PbrMaterial>()
            .Distinct()
            .Where(static material => material.AnisotropyStrength > 0f)
            .Select(static material =>
                (material, material.AnisotropyStrength, material.AnisotropyRotation))
            .ToArray();
        transmissionMaterials = scene.Root.EnumerateDepthFirst()
            .OfType<Mesh>()
            .Select(static mesh => mesh.Material)
            .OfType<PbrMaterial>()
            .Distinct()
            .Where(static material => material.TransmissionFactor > 0f)
            .Select(static material => (
                material,
                material.TransmissionFactor,
                material.IndexOfRefraction,
                material.VolumeThicknessFactor,
                material.VolumeAttenuationDistance))
            .ToArray();
        dispersionMaterials = scene.Root.EnumerateDepthFirst()
            .OfType<Mesh>()
            .Select(static mesh => mesh.Material)
            .OfType<PbrMaterial>()
            .Distinct()
            .Where(static material => material.Dispersion > 0f)
            .Select(static material => (material, material.Dispersion))
            .ToArray();
        iridescenceMaterials = scene.Root.EnumerateDepthFirst()
            .OfType<Mesh>()
            .Select(static mesh => mesh.Material)
            .OfType<PbrMaterial>()
            .Distinct()
            .Where(static material => material.IridescenceFactor > 0f)
            .Select(static material => (
                material,
                material.IridescenceFactor,
                material.IridescenceIndexOfRefraction,
                material.IridescenceThicknessMinimum,
                material.IridescenceThicknessMaximum))
            .ToArray();
        specularMaterials = scene.Root.EnumerateDepthFirst()
            .OfType<Mesh>()
            .Select(static mesh => mesh.Material)
            .OfType<PbrMaterial>()
            .Distinct()
            .Where(static material =>
                material.SpecularFactor != 1f ||
                material.SpecularColor.Red != 1f || material.SpecularColor.Green != 1f ||
                material.SpecularColor.Blue != 1f ||
                material.SpecularTexture is not null || material.CompressedSpecularTexture is not null ||
                material.SpecularColorTexture is not null ||
                material.CompressedSpecularColorTexture is not null)
            .Select(static material => (material, material.SpecularFactor, material.SpecularColor))
            .ToArray();
        this.applyAllAnimations = applyAllAnimations;
        this.cameraTarget = cameraTarget;
        Vector3 cameraOffset = cameraPosition - cameraTarget;
        baseCameraDistance = cameraOffset.Length();
        cameraDirection = cameraOffset / baseCameraDistance;
        camera.NearClip = MathF.Max(baseCameraDistance / 1000f, 0.00001f);
        camera.FarClip = MathF.Max(baseCameraDistance * 100f, camera.NearClip * 1000f);
        light = new DirectionalLight(
            new LinearRgba(1f, 0.95f, 0.9f, 1f, StandardColorSpaces.LinearSrgb),
            intensity: directionalLightEnabled ? 3f : 0f,
            name: "model lab key light")
        {
            CastsShadows = true,
        };
        UpdateLightDirection();
        scene.Add(light);
        scene.Add(new ImageBasedLight(
            environment ?? NeutralIndirectFill,
            intensity: environment is null ? 0.45f : 1f,
            name: environment is null
                ? "Model Lab AO-visible neutral IBL"
                : $"Model Lab reference IBL: {environment.Name}"));
        UpdateCamera();
    }

    internal IReadOnlyList<string> IgnoredOptionalExtensions => ignoredOptionalExtensions;

    internal void ApplyMaterialVariant(int? index)
    {
        lock (renderGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            asset.ApplyMaterialVariant(index);
        }
    }

    internal float DispersionScale
    {
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                foreach ((PbrMaterial material, float authored) in dispersionMaterials)
                {
                    material.Dispersion = authored * value;
                }
            }
        }
    }

    internal int ObservedMaterialShaderVariantCount
    {
        get
        {
            lock (renderGate)
            {
                return renderer?.ObservedMaterialShaderVariantCount ?? 0;
            }
        }
    }

    internal int MaximumObservedMaterialSampledTextureCount
    {
        get
        {
            lock (renderGate)
            {
                return renderer?.MaximumObservedMaterialSampledTextureCount ?? 0;
            }
        }
    }

    internal int CachedMaterialShaderVariantCount
    {
        get
        {
            lock (renderGate)
            {
                return renderer?.CachedMaterialShaderVariantCount ?? 0;
            }
        }
    }

    internal long MaterialShaderVariantCacheHits
    {
        get
        {
            lock (renderGate)
            {
                return renderer?.MaterialShaderVariantCacheHits ?? 0;
            }
        }
    }

    internal long MaterialShaderVariantCacheMisses
    {
        get
        {
            lock (renderGate)
            {
                return renderer?.MaterialShaderVariantCacheMisses ?? 0;
            }
        }
    }

    internal SceneRendererFrameTimings LastRendererFrameTimings
    {
        get
        {
            lock (renderGate)
            {
                return renderer?.LastFrameTimings ?? default;
            }
        }
    }

    internal float ClearcoatFactorScale
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value < 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                foreach ((PbrMaterial material, float factor) in clearcoatMaterials)
                {
                    material.ClearcoatFactor = Math.Clamp(factor * value, 0f, 1f);
                }
            }
        }
    }

    internal float AnisotropyStrengthScale
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value is < 0f or > 1f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                foreach ((PbrMaterial material, float strength, _) in anisotropyMaterials)
                {
                    material.AnisotropyStrength = strength * value;
                }
            }
        }
    }

    internal float AnisotropyRotationOffset
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
                foreach ((PbrMaterial material, _, float rotation) in anisotropyMaterials)
                {
                    material.AnisotropyRotation = rotation + value;
                }
            }
        }
    }

    internal float TransmissionFactorScale
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value is < 0f or > 1f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                foreach ((PbrMaterial material, float factor, _, _, _) in transmissionMaterials)
                {
                    material.TransmissionFactor = factor * value;
                }
            }
        }
    }

    internal float TransmissionIndexOfRefractionOffset
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                foreach ((PbrMaterial material, _, float indexOfRefraction, _, _) in
                    transmissionMaterials)
                {
                    material.IndexOfRefraction = MathF.Max(1f, indexOfRefraction + value);
                }
            }
        }
    }

    internal float VolumeThicknessScale
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value < 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                foreach ((PbrMaterial material, _, _, float thickness, _) in transmissionMaterials)
                {
                    material.VolumeThicknessFactor = thickness * value;
                }
            }
        }
    }

    internal float VolumeAbsorptionStrength
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value < 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                foreach ((PbrMaterial material, _, _, _, float attenuationDistance) in
                    transmissionMaterials)
                {
                    material.VolumeAttenuationDistance = value == 0f ||
                        float.IsPositiveInfinity(attenuationDistance)
                            ? float.PositiveInfinity
                            : attenuationDistance / value;
                }
            }
        }
    }

    internal float IridescenceFactorScale
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value is < 0f or > 1f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                foreach ((PbrMaterial material, float factor, _, _, _) in iridescenceMaterials)
                {
                    material.IridescenceFactor = factor * value;
                }
            }
        }
    }

    internal float SpecularFactorScale
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value is < 0f or > 1f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                foreach ((PbrMaterial material, float factor, _) in specularMaterials)
                {
                    material.SpecularFactor = factor * value;
                }
            }
        }
    }

    internal float SpecularColorScale
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value < 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                foreach ((PbrMaterial material, _, LinearRgba color) in specularMaterials)
                {
                    material.SpecularColor = new LinearRgba(
                        color.Red * value,
                        color.Green * value,
                        color.Blue * value,
                        color.Alpha,
                        color.ColorSpace);
                }
            }
        }
    }

    internal float IridescenceIndexOfRefractionOffset
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
                foreach ((PbrMaterial material, _, float indexOfRefraction, _, _) in iridescenceMaterials)
                {
                    material.IridescenceIndexOfRefraction = MathF.Max(1f, indexOfRefraction + value);
                }
            }
        }
    }

    internal float IridescenceThicknessScale
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value < 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                foreach ((PbrMaterial material, _, _, float minimum, float maximum) in iridescenceMaterials)
                {
                    material.IridescenceThicknessMinimum = minimum * value;
                    material.IridescenceThicknessMaximum = maximum * value;
                }
            }
        }
    }

    internal bool AmbientOcclusionEnabled
    {
        get
        {
            lock (renderGate)
            {
                return ambientOcclusionEnabled;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                ambientOcclusionEnabled = value;
                if (renderer is not null)
                {
                    renderer.AmbientOcclusionEnabled = value;
                }
            }
        }
    }

    internal float AmbientOcclusionRadius
    {
        get
        {
            lock (renderGate)
            {
                return ambientOcclusionRadius;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value <= 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                ambientOcclusionRadius = value;
                if (renderer is not null)
                {
                    renderer.AmbientOcclusionRadius = value;
                }
            }
        }
    }

    internal float AmbientOcclusionStrength
    {
        get
        {
            lock (renderGate)
            {
                return ambientOcclusionStrength;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value < 0f || value > 1f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                ambientOcclusionStrength = value;
                if (renderer is not null)
                {
                    renderer.AmbientOcclusionStrength = value;
                }
            }
        }
    }

    internal bool BloomEnabled
    {
        get
        {
            lock (renderGate)
            {
                return bloomEnabled;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                bloomEnabled = value;
                if (renderer is not null)
                {
                    renderer.BloomEnabled = value;
                }
            }
        }
    }

    internal float BloomThreshold
    {
        get
        {
            lock (renderGate)
            {
                return bloomThreshold;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                bloomThreshold = value;
                if (renderer is not null)
                {
                    renderer.BloomThreshold = value;
                }
            }
        }
    }

    internal float BloomSoftKnee
    {
        get
        {
            lock (renderGate)
            {
                return bloomSoftKnee;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                bloomSoftKnee = value;
                if (renderer is not null)
                {
                    renderer.BloomSoftKnee = value;
                }
            }
        }
    }

    internal float BloomIntensity
    {
        get
        {
            lock (renderGate)
            {
                return bloomIntensity;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                bloomIntensity = value;
                if (renderer is not null)
                {
                    renderer.BloomIntensity = value;
                }
            }
        }
    }

    internal float BloomRadiusPixels
    {
        get
        {
            lock (renderGate)
            {
                return bloomRadiusPixels;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                bloomRadiusPixels = value;
                if (renderer is not null)
                {
                    renderer.BloomRadiusPixels = value;
                }
            }
        }
    }

    internal BloomCompositeMode BloomCompositeMode
    {
        get
        {
            lock (renderGate)
            {
                return bloomCompositeMode;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!Enum.IsDefined(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                bloomCompositeMode = value;
                if (renderer is not null)
                {
                    renderer.BloomCompositeMode = value;
                }
            }
        }
    }

    internal float CameraDistanceScale
    {
        get
        {
            lock (renderGate)
            {
                return cameraDistanceScale;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value <= 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                cameraDistanceScale = value;
                UpdateCamera();
            }
        }
    }

    internal float CameraAzimuthDegrees
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
                cameraAzimuthDegrees = value;
                UpdateCamera();
            }
        }
    }

    internal float CameraElevationDegrees
    {
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value is < -89f or > 89f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                cameraElevationDegrees = value;
                UpdateCamera();
            }
        }
    }

    internal float LightAzimuthDegrees
    {
        get
        {
            lock (renderGate)
            {
                return lightAzimuthDegrees;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                lightAzimuthDegrees = value;
                UpdateLightDirection();
            }
        }
    }

    internal float LightElevationDegrees
    {
        get
        {
            lock (renderGate)
            {
                return lightElevationDegrees;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!float.IsFinite(value) || value <= 0f || value >= 90f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                lightElevationDegrees = value;
                UpdateLightDirection();
            }
        }
    }

    internal float LightAngularDiameter
    {
        get
        {
            lock (renderGate)
            {
                return light.AngularDiameterRadians;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                light.AngularDiameterRadians = value;
            }
        }
    }

    internal bool ShadowsEnabled
    {
        get
        {
            lock (renderGate)
            {
                return light.CastsShadows;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                light.CastsShadows = value;
            }
        }
    }

    internal SceneRenderLayer RenderLayer
    {
        get
        {
            lock (renderGate)
            {
                return renderLayer;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!Enum.IsDefined(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                renderLayer = value;
                if (renderer is not null)
                {
                    renderer.RenderLayer = value;
                }
            }
        }
    }

    internal int AnimationIndex
    {
        get
        {
            lock (renderGate)
            {
                return animationIndex;
            }
        }
        set
        {
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if ((uint)value >= (uint)animations.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                animationIndex = value;
            }
        }
    }

    internal PresentationSurfaceFrameStatus Present(
        float animationTimeSeconds,
        AnimationWrapMode animationWrapMode)
    {
        lock (renderGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return session.RenderAndPresent(target => Render(
                target, session.Width, session.Height, animationTimeSeconds, animationWrapMode));
        }
    }

    internal void Render(
        GraphicsTexture target,
        uint width,
        uint height,
        float animationTimeSeconds,
        AnimationWrapMode animationWrapMode)
    {
        lock (renderGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (animations.Count != 0)
            {
                if (applyAllAnimations)
                {
                    foreach (AnimationClip animation in animations)
                    {
                        animation.Apply(animationTimeSeconds, animationWrapMode);
                    }
                }
                else
                {
                    animations[animationIndex].Apply(animationTimeSeconds, animationWrapMode);
                }
            }
            camera.AspectRatio = (float)width / height;
            GraphicsTexture depth = EnsureDepthTexture(width, height);
            if (renderer is null)
            {
                renderer = new SceneRenderer(session.Device, target.Descriptor.Format)
                {
                    AmbientOcclusionEnabled = ambientOcclusionEnabled,
                    AmbientOcclusionRadius = ambientOcclusionRadius,
                    AmbientOcclusionStrength = ambientOcclusionStrength,
                    BloomEnabled = bloomEnabled,
                    BloomThreshold = bloomThreshold,
                    BloomSoftKnee = bloomSoftKnee,
                    BloomIntensity = bloomIntensity,
                    BloomRadiusPixels = bloomRadiusPixels,
                    BloomCompositeMode = bloomCompositeMode,
                    RenderLayer = renderLayer,
                };
                renderer.PrewarmMaterialVariants(materialVariants);
            }
            renderer.Render(scene, camera, target, depth);
        }
    }

    public void Dispose()
    {
        lock (renderGate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            depthTexture?.Dispose();
            renderer?.Dispose();
        }
    }

    private GraphicsTexture EnsureDepthTexture(uint width, uint height)
    {
        GraphicsExtent3D extent = new(width, height);
        if (depthTexture is not null && depthExtent == extent)
        {
            return depthTexture;
        }
        depthTexture?.Dispose();
        depthTexture = session.Device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "Model lab depth"));
        depthExtent = extent;
        return depthTexture;
    }

    private void SetCameraLookAt(Vector3 eye, Vector3 target)
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        if (!Matrix4x4.Invert(view, out Matrix4x4 world) ||
            !Matrix4x4.Decompose(world, out Vector3 scale, out Quaternion rotation, out Vector3 position))
        {
            throw new InvalidOperationException("Model Lab camera look-at transform is not invertible.");
        }
        camera.Transform.Position = position;
        camera.Transform.Rotation = rotation;
        camera.Transform.Scale = scale;
    }

    private void UpdateCamera()
    {
        float azimuth = MathF.PI * cameraAzimuthDegrees / 180f;
        float elevation = MathF.PI * cameraElevationDegrees / 180f;
        Vector3 direction = Vector3.Transform(
            cameraDirection,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, azimuth));
        Vector3 right = Vector3.Cross(Vector3.UnitY, direction);
        if (right.LengthSquared() < 0.000001f)
        {
            right = Vector3.UnitX;
        }
        else
        {
            right = Vector3.Normalize(right);
        }
        direction = Vector3.Normalize(Vector3.Transform(
            direction,
            Quaternion.CreateFromAxisAngle(right, elevation)));
        SetCameraLookAt(
            cameraTarget + (direction * baseCameraDistance * cameraDistanceScale),
            cameraTarget);
    }

    private void UpdateLightDirection()
    {
        float azimuth = MathF.PI * lightAzimuthDegrees / 180f;
        float elevation = MathF.PI * lightElevationDegrees / 180f;
        float horizontal = MathF.Cos(elevation);
        Vector3 rayDirection = Vector3.Normalize(new Vector3(
            MathF.Sin(azimuth) * horizontal,
            -MathF.Sin(elevation),
            MathF.Cos(azimuth) * horizontal));
        light.Transform.Rotation = RotationFromTo(-Vector3.UnitZ, rayDirection);
    }

    private static Quaternion RotationFromTo(Vector3 source, Vector3 destination)
    {
        Vector3 from = Vector3.Normalize(source);
        Vector3 to = Vector3.Normalize(destination);
        float dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (dot < -0.999999f)
        {
            Vector3 axis = MathF.Abs(from.X) < 0.9f
                ? Vector3.Normalize(Vector3.Cross(from, Vector3.UnitX))
                : Vector3.Normalize(Vector3.Cross(from, Vector3.UnitY));
            return Quaternion.CreateFromAxisAngle(axis, MathF.PI);
        }
        Vector3 cross = Vector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(cross, 1f + dot));
    }
}
