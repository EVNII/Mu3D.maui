using System.Numerics;
using Mu3D.Color;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Selection;

namespace Mu3D.GalleryApp.Pages;

internal readonly record struct PenShadowInput(Vector2 PositionPixels, float? TiltXDegrees, float? TiltYDegrees,
    float? HoverDistanceNormalized, bool IsHover);

/// <summary>Defines the replaceable application-owned shadow for one hovering or contacting pen.</summary>
internal interface IPenTipShadowPreview
{
    /// <summary>Gets whether the hidden caster currently participates in the display-normal pass.</summary>
    bool IsVisible { get; }

    /// <summary>Gets or sets the maximum shadow opacity.</summary>
    float ShadowCoefficient { get; set; }

    /// <summary>Gets or sets Gaussian sigma growth in pixels per millimetre of surface separation.</summary>
    float BlurDecayCoefficient { get; set; }

    /// <summary>
    /// Gets or sets the normalized hover-distance fade curvature. Zero is linear; larger values
    /// make the shadow fall away sooner while preserving zero at maximum hover and one at contact.
    /// </summary>
    float ShadowDecayCoefficient { get; set; }

    /// <summary>Updates the hidden caster from one normalized sample and scene hit.</summary>
    void Update(
        PenShadowInput sample,
        SceneRaycastIntersection? hit,
        PerspectiveCamera camera,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        double viewportHeightLogicalUnits);

    /// <summary>Hides the detached caster and disables its display-normal pass.</summary>
    void Hide();
}

/// <summary>
/// Casts an Apple Pencil-style shadow from invisible, real-screen-size scene geometry. The
/// geometry remains application-owned and never becomes a Mu3D pen-input policy.
/// </summary>
internal sealed class ApplePencilTipShadowPreview : IPenTipShadowPreview
{
    private const float ApplePencilLengthMillimeters = 166f;
    private const float ApplePencilBodyRadiusMillimeters = 4.45f;
    private const float ApplePencilFlatSideMillimeters = 3.55f;
    private const float ApplePencilTipLengthMillimeters = 8.4f;
    private const float ContactSeparationMillimeters = 0.02f;
    private const float DefaultCompatibleIpadDisplayPointsPerInch = 132f;
    private const float MillimetersPerInch = 25.4f;
    private const float MaximumHoverDistanceMillimeters = 12f;
    private static readonly LinearRgba OpaqueCasterColor = new(
        1f,
        1f,
        1f,
        1f,
        StandardColorSpaces.LinearSrgb);

    private readonly float displayPointsPerMillimeter;
    private readonly PenTipDisplayNormalShadowState displayNormalShadow = new();
    private readonly SceneNode casterRoot = new("Invisible physical Apple Pencil shadow caster")
    {
        IsVisible = false,
    };
    private readonly Mesh[] casterMeshes;
    private bool hasFilteredHoverDistance;
    private float filteredHoverDistanceMillimeters;
    private float referenceViewDepth;

    /// <summary>
    /// Creates one detached hidden caster and its display-normal surface pass. The display point
    /// density is explicit so a host can calibrate the physical-size reference per device.
    /// </summary>
    internal ApplePencilTipShadowPreview(
        Action<IReadOnlyList<Mesh>, PenTipDisplayNormalShadowState> registerShadow,
        float displayPointsPerInch = DefaultCompatibleIpadDisplayPointsPerInch)
    {
        ArgumentNullException.ThrowIfNull(registerShadow);
        if (!float.IsFinite(displayPointsPerInch) || displayPointsPerInch <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(displayPointsPerInch));
        }
        displayPointsPerMillimeter = displayPointsPerInch / MillimetersPerInch;
        displayNormalShadow.MaximumHoverDistanceMillimeters = MaximumHoverDistanceMillimeters;
        UnlitMaterial casterMaterial = new(OpaqueCasterColor, "Detached mask-only material");
        Mesh body = new(
            CreateApplePencilBodyGeometry(),
            casterMaterial,
            "Physical Apple Pencil full body");

        Mesh taper = new(
            MeshPrimitives.CreateCone(
                radius: 2.3f,
                height: ApplePencilTipLengthMillimeters,
                radialSegments: 32),
            casterMaterial,
            "Physical Apple Pencil tip taper");
        taper.Transform.Position = new Vector3(
            0f,
            ApplePencilTipLengthMillimeters * 0.5f,
            0f);
        taper.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);

        Mesh sensingNib = new(
            MeshPrimitives.CreateUvSphere(radius: 1f, longitudeSegments: 16, latitudeSegments: 8),
            casterMaterial,
            "Physical Apple Pencil sensing nib");
        sensingNib.Transform.Position = new Vector3(0f, 0.55f, 0f);
        sensingNib.Transform.Scale = new Vector3(0.62f, 0.55f, 0.62f);

        casterMeshes = [body, taper, sensingNib];
        foreach (Mesh casterMesh in casterMeshes)
        {
            casterRoot.AddChild(casterMesh);
        }
        registerShadow(casterMeshes, displayNormalShadow);
    }

    /// <inheritdoc />
    public bool IsVisible => casterRoot.IsVisible;

    /// <inheritdoc />
    public float ShadowCoefficient
    {
        get => displayNormalShadow.ShadowCoefficient;
        set
        {
            if (!float.IsFinite(value) || value is < 0f or > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            displayNormalShadow.ShadowCoefficient = value;
        }
    }

    /// <inheritdoc />
    public float BlurDecayCoefficient
    {
        get => displayNormalShadow.BlurDecayCoefficientPixelsPerMillimeter;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            displayNormalShadow.BlurDecayCoefficientPixelsPerMillimeter = value;
        }
    }

    /// <inheritdoc />
    public float ShadowDecayCoefficient
    {
        get => displayNormalShadow.ShadowDecayCoefficientPerMillimeter;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            displayNormalShadow.ShadowDecayCoefficientPerMillimeter = value;
        }
    }

    /// <inheritdoc />
    public void Update(
        PenShadowInput sample,
        SceneRaycastIntersection? hit,
        PerspectiveCamera camera,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        double viewportHeightLogicalUnits)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfZero(viewportWidthPixels);
        ArgumentOutOfRangeException.ThrowIfZero(viewportHeightPixels);
        if (!double.IsFinite(viewportHeightLogicalUnits) || viewportHeightLogicalUnits <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeightLogicalUnits));
        }

        Matrix4x4 cameraWorld = camera.WorldMatrix;
        Vector3 cameraPosition = Vector3.Transform(Vector3.Zero, cameraWorld);
        Vector3 cameraForward = NormalizeOrFallback(
            Vector3.TransformNormal(-Vector3.UnitZ, cameraWorld),
            -Vector3.UnitZ);
        float viewDepth;
        Vector3 landingWorldPosition;
        if (hit is SceneRaycastIntersection intersection)
        {
            landingWorldPosition = intersection.WorldPosition;
            viewDepth = Vector3.Dot(
                landingWorldPosition - cameraPosition,
                cameraForward);
            referenceViewDepth = viewDepth;
        }
        else
        {
            viewDepth = GetFallbackViewDepth(camera, cameraPosition, cameraForward);
            landingWorldPosition = UnprojectLandingPosition(
                sample.PositionPixels,
                viewportWidthPixels,
                viewportHeightPixels,
                viewDepth,
                camera);
        }
        if (!float.IsFinite(viewDepth) || viewDepth <= 0f)
        {
            throw new InvalidOperationException(
                "The pen-shadow projection reference is behind the active camera.");
        }
        float physicalPixelsPerLogicalUnit =
            viewportHeightPixels / (float)viewportHeightLogicalUnits;
        float worldUnitsPerMillimeterPerViewDepth =
            2f * MathF.Tan(camera.FieldOfViewRadians * 0.5f) /
            viewportHeightPixels *
            physicalPixelsPerLogicalUnit *
            displayPointsPerMillimeter;

        Vector3 axis = CreateWorldAxis(sample, camera);
        float normalizedHoverDistance = Math.Clamp(
            sample.HoverDistanceNormalized ?? 0f,
            0f,
            1f);
        float rawHoverDistanceMillimeters =
            normalizedHoverDistance * MaximumHoverDistanceMillimeters;
        const float HoverEndpointSnapThreshold = 0.02f;
        bool isHoverEndpoint =
            normalizedHoverDistance <= HoverEndpointSnapThreshold ||
            normalizedHoverDistance >= 1f - HoverEndpointSnapThreshold;
        if (sample.IsHover &&
            hasFilteredHoverDistance &&
            !isHoverEndpoint)
        {
            const float HoverDistanceResponse = 0.28f;
            filteredHoverDistanceMillimeters +=
                (rawHoverDistanceMillimeters - filteredHoverDistanceMillimeters) *
                HoverDistanceResponse;
        }
        else
        {
            filteredHoverDistanceMillimeters = isHoverEndpoint
                ? normalizedHoverDistance >= 0.5f
                    ? MaximumHoverDistanceMillimeters
                    : 0f
                : rawHoverDistanceMillimeters;
            hasFilteredHoverDistance = true;
        }
        float hoverDistanceMillimeters = filteredHoverDistanceMillimeters;
        float casterGapMillimeters =
            ContactSeparationMillimeters + hoverDistanceMillimeters;
        float depthScaleDenominator =
            1f - Vector3.Dot(axis, cameraForward) *
            casterGapMillimeters *
            worldUnitsPerMillimeterPerViewDepth;
        if (!float.IsFinite(depthScaleDenominator) || depthScaleDenominator <= 0f)
        {
            throw new InvalidOperationException(
                "The pen-shadow physical-size projection is degenerate at this camera depth.");
        }
        float casterViewDepth = viewDepth / depthScaleDenominator;
        float worldUnitsPerMillimeter =
            casterViewDepth * worldUnitsPerMillimeterPerViewDepth;

        casterRoot.Transform.Position =
            landingWorldPosition + axis *
            (casterGapMillimeters * worldUnitsPerMillimeter);
        casterRoot.Transform.Rotation = CreateFromToRotation(Vector3.UnitY, axis);
        casterRoot.Transform.Scale = new Vector3(worldUnitsPerMillimeter);
        casterRoot.IsVisible = true;
        displayNormalShadow.CasterWorldPosition = casterRoot.Transform.Position;
        displayNormalShadow.LandingUv = new Vector2(
            sample.PositionPixels.X / viewportWidthPixels,
            sample.PositionPixels.Y / viewportHeightPixels);
        displayNormalShadow.TipDisplayDistanceMillimeters = hoverDistanceMillimeters;
        displayNormalShadow.WorldUnitsPerMillimeter = worldUnitsPerMillimeter;
        displayNormalShadow.IsVisible = true;
    }

    private float GetFallbackViewDepth(
        PerspectiveCamera camera,
        Vector3 cameraPosition,
        Vector3 cameraForward)
    {
        if (float.IsFinite(referenceViewDepth) && referenceViewDepth > camera.NearClip)
        {
            return referenceViewDepth;
        }
        float sceneOriginViewDepth = Vector3.Dot(-cameraPosition, cameraForward);
        if (float.IsFinite(sceneOriginViewDepth) && sceneOriginViewDepth > camera.NearClip)
        {
            return sceneOriginViewDepth;
        }
        return MathF.Max(camera.NearClip * 10f, 1f);
    }

    private static Vector3 UnprojectLandingPosition(
        Vector2 positionPixels,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        float viewDepth,
        PerspectiveCamera camera)
    {
        float normalizedX = positionPixels.X / viewportWidthPixels * 2f - 1f;
        float normalizedY = 1f - positionPixels.Y / viewportHeightPixels * 2f;
        Matrix4x4 projection = camera.ProjectionMatrix;
        Vector3 viewPosition = new(
            normalizedX * viewDepth / projection.M11,
            normalizedY * viewDepth / projection.M22,
            -viewDepth);
        return Vector3.Transform(viewPosition, camera.WorldMatrix);
    }

    /// <inheritdoc />
    public void Hide()
    {
        casterRoot.IsVisible = false;
        displayNormalShadow.IsVisible = false;
        hasFilteredHoverDistance = false;
    }

    private static Vector3 CreateWorldAxis(
        PenShadowInput sample,
        PerspectiveCamera camera)
    {
        Matrix4x4 cameraWorld = camera.WorldMatrix;
        Vector3 towardCamera = NormalizeOrFallback(
            Vector3.TransformNormal(Vector3.UnitZ, cameraWorld),
            Vector3.UnitZ);
        Vector3 right = NormalizeOrFallback(
            Vector3.TransformNormal(Vector3.UnitX, cameraWorld),
            Vector3.UnitX);
        Vector3 up = NormalizeOrFallback(
            Vector3.TransformNormal(Vector3.UnitY, cameraWorld),
            Vector3.UnitY);

        float tiltX = sample.TiltXDegrees ?? 0f;
        float tiltY = sample.TiltYDegrees ?? 0f;
        float rawTiltMagnitude = MathF.Sqrt(tiltX * tiltX + tiltY * tiltY);
        float tiltMagnitude = Math.Clamp(rawTiltMagnitude, 0f, 80f);
        if (tiltMagnitude <= 0.001f || rawTiltMagnitude <= 0.001f)
        {
            return towardCamera;
        }

        float inverseMagnitude = 1f / rawTiltMagnitude;
        Vector3 screenTilt =
            right * (tiltX * inverseMagnitude) -
            up * (tiltY * inverseMagnitude);
        float tiltRadians = tiltMagnitude * MathF.PI / 180f;
        return NormalizeOrFallback(
            towardCamera * MathF.Cos(tiltRadians) + screenTilt * MathF.Sin(tiltRadians),
            towardCamera);
    }

    private static MeshGeometry CreateApplePencilBodyGeometry()
    {
        const int radialSegments = 32;
        (float Height, float Radius)[] rings =
        [
            (ApplePencilTipLengthMillimeters, 2.3f),
            (9.2f, 3.75f),
            (10.1f, ApplePencilBodyRadiusMillimeters),
            (160.8f, ApplePencilBodyRadiusMillimeters),
            (162.4f, 4.2f),
            (164f, 3.45f),
            (165.25f, 2.25f),
        ];
        int verticesPerRing = radialSegments + 1;
        int topCenterIndex = checked(rings.Length * verticesPerRing);
        Vector3[] positions = new Vector3[topCenterIndex + 1];
        Vector3[] normals = new Vector3[positions.Length];
        Vector2[] textureCoordinates = new Vector2[positions.Length];
        uint[] indices = new uint[checked(
            (rings.Length - 1) * radialSegments * 6 + radialSegments * 3)];

        for (int ringIndex = 0; ringIndex < rings.Length; ringIndex++)
        {
            (float height, float radius) = rings[ringIndex];
            for (int segment = 0; segment <= radialSegments; segment++)
            {
                float u = segment / (float)radialSegments;
                float angle = u * MathF.PI * 2f;
                float rawX = MathF.Cos(angle) * radius;
                float z = MathF.Sin(angle) * radius;
                bool liesOnFlatSide = rawX > ApplePencilFlatSideMillimeters;
                int vertex = ringIndex * verticesPerRing + segment;
                positions[vertex] = new Vector3(
                    liesOnFlatSide ? ApplePencilFlatSideMillimeters : rawX,
                    height,
                    z);
                normals[vertex] = liesOnFlatSide
                    ? Vector3.UnitX
                    : Vector3.Normalize(new Vector3(rawX, 0f, z));
                textureCoordinates[vertex] = new Vector2(
                    u,
                    height / ApplePencilLengthMillimeters);
            }
        }

        positions[topCenterIndex] = new Vector3(0f, ApplePencilLengthMillimeters, 0f);
        normals[topCenterIndex] = Vector3.UnitY;
        textureCoordinates[topCenterIndex] = new Vector2(0.5f, 1f);

        int output = 0;
        for (int ringIndex = 0; ringIndex < rings.Length - 1; ringIndex++)
        {
            int lowerRing = ringIndex * verticesPerRing;
            int upperRing = lowerRing + verticesPerRing;
            for (int segment = 0; segment < radialSegments; segment++)
            {
                uint lower = checked((uint)(lowerRing + segment));
                uint upper = checked((uint)(upperRing + segment));
                indices[output++] = lower;
                indices[output++] = upper;
                indices[output++] = lower + 1;
                indices[output++] = lower + 1;
                indices[output++] = upper;
                indices[output++] = upper + 1;
            }
        }

        int topRing = (rings.Length - 1) * verticesPerRing;
        for (int segment = 0; segment < radialSegments; segment++)
        {
            indices[output++] = checked((uint)(topRing + segment));
            indices[output++] = checked((uint)topCenterIndex);
            indices[output++] = checked((uint)(topRing + segment + 1));
        }
        return new MeshGeometry(positions, indices, normals, textureCoordinates);
    }

    private static Quaternion CreateFromToRotation(Vector3 source, Vector3 destination)
    {
        Vector3 from = Vector3.Normalize(source);
        Vector3 to = Vector3.Normalize(destination);
        float dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (dot > 0.99999f)
        {
            return Quaternion.Identity;
        }
        if (dot < -0.99999f)
        {
            Vector3 axis = MathF.Abs(from.X) < 0.9f
                ? Vector3.Normalize(Vector3.Cross(from, Vector3.UnitX))
                : Vector3.Normalize(Vector3.Cross(from, Vector3.UnitZ));
            return Quaternion.CreateFromAxisAngle(axis, MathF.PI);
        }
        Vector3 cross = Vector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(cross, 1f + dot));
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        float lengthSquared = value.LengthSquared();
        return float.IsFinite(lengthSquared) && lengthSquared > 0.000001f
            ? value / MathF.Sqrt(lengthSquared)
            : fallback;
    }
}
