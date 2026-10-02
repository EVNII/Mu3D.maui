using System.Collections.ObjectModel;
using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>Controls whether a mesh participates in camera and directional-shadow passes.</summary>
public enum MeshShadowCastingMode
{
    /// <summary>Renders the mesh to camera passes without adding it to the shadow map.</summary>
    Off,

    /// <summary>Renders the mesh to camera passes and the directional-shadow map.</summary>
    On,

    /// <summary>Omits the mesh from camera passes while retaining it in the shadow map.</summary>
    ShadowsOnly,
}

/// <summary>Stores immutable indexed triangle geometry in backend-independent FP32 form.</summary>
public sealed class MeshGeometry
{
    /// <summary>Initializes indexed triangle geometry and copies all supplied data.</summary>
    public MeshGeometry(
        IEnumerable<Vector3> positions,
        IEnumerable<uint> indices,
        IEnumerable<Vector3>? normals = null,
        IEnumerable<Vector2>? textureCoordinates = null,
        IEnumerable<Vector3>? bentNormals = null,
        IEnumerable<JointIndices4>? jointIndices = null,
        IEnumerable<Vector4>? jointWeights = null,
        IEnumerable<MorphTarget>? morphTargets = null,
        IEnumerable<Vector4>? tangents = null)
        : this(
            positions,
            indices,
            normals,
            textureCoordinates,
            bentNormals,
            jointIndices,
            jointWeights,
            morphTargets,
            tangents,
            null)
    {
    }

    /// <summary>
    /// Creates indexed triangle geometry with both texture-coordinate sets while preserving the
    /// original constructor contract for callers that only need the primary set.
    /// </summary>
    public static MeshGeometry CreateWithTextureCoordinateSets(
        IEnumerable<Vector3> positions,
        IEnumerable<uint> indices,
        IEnumerable<Vector2> textureCoordinates1,
        IEnumerable<Vector3>? normals = null,
        IEnumerable<Vector2>? textureCoordinates = null,
        IEnumerable<Vector3>? bentNormals = null,
        IEnumerable<JointIndices4>? jointIndices = null,
        IEnumerable<Vector4>? jointWeights = null,
        IEnumerable<MorphTarget>? morphTargets = null,
        IEnumerable<Vector4>? tangents = null) => new(
            positions,
            indices,
            normals,
            textureCoordinates,
            bentNormals,
            jointIndices,
            jointWeights,
            morphTargets,
            tangents,
            textureCoordinates1);

    private MeshGeometry(
        IEnumerable<Vector3> positions,
        IEnumerable<uint> indices,
        IEnumerable<Vector3>? normals,
        IEnumerable<Vector2>? textureCoordinates,
        IEnumerable<Vector3>? bentNormals,
        IEnumerable<JointIndices4>? jointIndices,
        IEnumerable<Vector4>? jointWeights,
        IEnumerable<MorphTarget>? morphTargets,
        IEnumerable<Vector4>? tangents,
        IEnumerable<Vector2>? textureCoordinates1)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        Vector3[] positionArray = [.. positions];
        uint[] indexArray = [.. indices];
        Vector3[] normalArray = normals is null ? [] : [.. normals];
        Vector2[] textureCoordinateArray = textureCoordinates is null ? [] : [.. textureCoordinates];
        Vector2[] textureCoordinate1Array = textureCoordinates1 is null ? [] : [.. textureCoordinates1];
        Vector3[] bentNormalArray = bentNormals is null ? [] : [.. bentNormals];
        JointIndices4[] jointIndexArray = jointIndices is null ? [] : [.. jointIndices];
        Vector4[] jointWeightArray = jointWeights is null ? [] : [.. jointWeights];
        MorphTarget[] morphTargetArray = morphTargets is null ? [] : [.. morphTargets];
        Vector4[] tangentArray = tangents is null ? [] : [.. tangents];
        if (positionArray.Length == 0)
        {
            throw new ArgumentException("A mesh requires at least one position.", nameof(positions));
        }
        if (indexArray.Length == 0 || indexArray.Length % 3 != 0)
        {
            throw new ArgumentException("Triangle indices must be non-empty and divisible by three.", nameof(indices));
        }
        if (normalArray.Length != 0 && normalArray.Length != positionArray.Length)
        {
            throw new ArgumentException("Normals must be empty or match the position count.", nameof(normals));
        }
        if (textureCoordinateArray.Length != 0 && textureCoordinateArray.Length != positionArray.Length)
        {
            throw new ArgumentException(
                "Texture coordinates must be empty or match the position count.",
                nameof(textureCoordinates));
        }
        if (textureCoordinate1Array.Length != 0 && textureCoordinate1Array.Length != positionArray.Length)
        {
            throw new ArgumentException(
                "Second texture coordinates must be empty or match the position count.",
                nameof(textureCoordinates1));
        }
        if (bentNormalArray.Length != 0 && bentNormalArray.Length != positionArray.Length)
        {
            throw new ArgumentException(
                "Bent normals must be empty or match the position count.",
                nameof(bentNormals));
        }
        if (bentNormalArray.Length != 0 && normalArray.Length == 0)
        {
            throw new ArgumentException("Bent normals require geometric normals.", nameof(bentNormals));
        }
        if (tangentArray.Length != 0 && tangentArray.Length != positionArray.Length)
        {
            throw new ArgumentException(
                "Tangents must be empty or match the position count.",
                nameof(tangents));
        }
        if (tangentArray.Length != 0 && normalArray.Length == 0)
        {
            throw new ArgumentException("Tangents require geometric normals.", nameof(tangents));
        }
        if ((jointIndexArray.Length == 0) != (jointWeightArray.Length == 0))
        {
            throw new ArgumentException("Joint indices and weights must be supplied together.");
        }
        if (jointIndexArray.Length != 0 && jointIndexArray.Length != positionArray.Length)
        {
            throw new ArgumentException("Joint indices must match the position count.", nameof(jointIndices));
        }
        if (jointWeightArray.Length != 0 && jointWeightArray.Length != positionArray.Length)
        {
            throw new ArgumentException("Joint weights must match the position count.", nameof(jointWeights));
        }
        if (morphTargetArray.Any(static target => target is null))
        {
            throw new ArgumentException("Morph targets cannot contain null.", nameof(morphTargets));
        }
        foreach (MorphTarget target in morphTargetArray)
        {
            if (target.PositionDeltas.Count != positionArray.Length)
            {
                throw new ArgumentException(
                    "Every morph target must match the position count.",
                    nameof(morphTargets));
            }
            if (target.NormalDeltas.Count != 0 && normalArray.Length == 0)
            {
                throw new ArgumentException(
                    "Morph normal deltas require base geometric normals.",
                    nameof(morphTargets));
            }
            if (target.TangentDeltas.Count != 0 && tangentArray.Length == 0)
            {
                throw new ArgumentException(
                    "Morph tangent deltas require base tangents.",
                    nameof(morphTargets));
            }
        }
        for (int index = 0; index < positionArray.Length; index++)
        {
            ThrowIfNotFinite(positionArray[index], nameof(positions));
            if (normalArray.Length != 0)
            {
                ThrowIfNotFinite(normalArray[index], nameof(normals));
                if (normalArray[index].LengthSquared() <= 1e-12f)
                {
                    throw new ArgumentOutOfRangeException(nameof(normals), "Mesh normals must be non-zero.");
                }
            }
            if (textureCoordinateArray.Length != 0)
            {
                ThrowIfNotFinite(textureCoordinateArray[index], nameof(textureCoordinates));
            }
            if (textureCoordinate1Array.Length != 0)
            {
                ThrowIfNotFinite(textureCoordinate1Array[index], nameof(textureCoordinates1));
            }
            if (bentNormalArray.Length != 0)
            {
                ThrowIfNotFinite(bentNormalArray[index], nameof(bentNormals));
                if (bentNormalArray[index].LengthSquared() <= 1e-12f)
                {
                    throw new ArgumentOutOfRangeException(nameof(bentNormals), "Bent normals must be non-zero.");
                }
                if (Vector3.Dot(normalArray[index], bentNormalArray[index]) <= 0f)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(bentNormals),
                        "A bent normal must remain in the geometric normal's hemisphere.");
                }
            }
            if (tangentArray.Length != 0)
            {
                Vector4 tangent = tangentArray[index];
                ThrowIfNotFinite(tangent, nameof(tangents));
                if (new Vector3(tangent.X, tangent.Y, tangent.Z).LengthSquared() <= 1e-12f)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(tangents),
                        "Mesh tangent directions must be non-zero.");
                }
                if (tangent.W is not (-1f or 1f))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(tangents),
                        "Mesh tangent handedness must be exactly minus one or one.");
                }
            }
            if (jointWeightArray.Length != 0)
            {
                Vector4 weights = jointWeightArray[index];
                ThrowIfNotFinite(weights, nameof(jointWeights));
                if (weights.X < 0f || weights.Y < 0f || weights.Z < 0f || weights.W < 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(jointWeights), "Joint weights cannot be negative.");
                }
                float sum = weights.X + weights.Y + weights.Z + weights.W;
                if (sum <= 1e-8f)
                {
                    throw new ArgumentOutOfRangeException(nameof(jointWeights), "Joint weights must have a positive sum.");
                }
                jointWeightArray[index] = weights / sum;
            }
        }
        if (indexArray.Any(index => index >= positionArray.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(indices), "An index exceeds the position count.");
        }

        Positions = Array.AsReadOnly(positionArray);
        Indices = Array.AsReadOnly(indexArray);
        Normals = Array.AsReadOnly(normalArray);
        TextureCoordinates = Array.AsReadOnly(textureCoordinateArray);
        TextureCoordinates1 = Array.AsReadOnly(textureCoordinate1Array);
        BentNormals = Array.AsReadOnly(bentNormalArray);
        JointIndices = Array.AsReadOnly(jointIndexArray);
        JointWeights = Array.AsReadOnly(jointWeightArray);
        MorphTargets = Array.AsReadOnly(morphTargetArray);
        Tangents = Array.AsReadOnly(tangentArray);
    }

    /// <summary>Gets immutable FP32 local-space positions.</summary>
    public IReadOnlyList<Vector3> Positions { get; }

    /// <summary>Gets immutable unsigned triangle indices.</summary>
    public IReadOnlyList<uint> Indices { get; }

    /// <summary>Gets immutable FP32 normals, or an empty list when absent.</summary>
    public IReadOnlyList<Vector3> Normals { get; }

    /// <summary>Gets immutable FP32 texture coordinates, or an empty list when absent.</summary>
    public IReadOnlyList<Vector2> TextureCoordinates { get; }

    /// <summary>Gets immutable FP32 texture-coordinate set one, or an empty list when absent.</summary>
    public IReadOnlyList<Vector2> TextureCoordinates1 { get; }

    /// <summary>
    /// Gets optional per-vertex local-space directions toward unoccluded indirect illumination.
    /// An empty list means the renderer uses the corresponding geometric normals.
    /// </summary>
    public IReadOnlyList<Vector3> BentNormals { get; }

    /// <summary>Gets optional four-way joint indices, or an empty list for rigid geometry.</summary>
    public IReadOnlyList<JointIndices4> JointIndices { get; }

    /// <summary>
    /// Gets optional normalized four-way FP32 joint weights, or an empty list for rigid geometry.
    /// </summary>
    public IReadOnlyList<Vector4> JointWeights { get; }

    /// <summary>Gets immutable morph targets applied before skinning.</summary>
    public IReadOnlyList<MorphTarget> MorphTargets { get; }

    /// <summary>
    /// Gets optional FP32 local-space tangent directions with handedness in W, or an empty list
    /// when the renderer should reconstruct the tangent frame from position and UV derivatives.
    /// </summary>
    public IReadOnlyList<Vector4> Tangents { get; }

    private static void ThrowIfNotFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Mesh vector components must be finite.");
        }
    }

    private static void ThrowIfNotFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Mesh vector components must be finite.");
        }
    }

    private static void ThrowIfNotFinite(Vector4 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) || !float.IsFinite(value.W))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Mesh vector components must be finite.");
        }
    }
}

/// <summary>Associates immutable geometry and a material with a transformable scene node.</summary>
public sealed class Mesh : SceneNode
{
    private MeshGeometry geometry;
    private Material material;
    private Skin? skin;
    private IReadOnlyList<float> morphWeights = Array.Empty<float>();
    private MeshShadowCastingMode shadowCastingMode = MeshShadowCastingMode.On;

    /// <summary>Initializes a mesh node.</summary>
    public Mesh(MeshGeometry geometry, Material material, string? name = null)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(material);
        this.geometry = geometry;
        this.material = material;
        morphWeights = Array.AsReadOnly(new float[geometry.MorphTargets.Count]);
    }

    /// <summary>Gets or sets the immutable CPU geometry.</summary>
    public MeshGeometry Geometry
    {
        get => geometry;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            geometry = value;
            morphWeights = Array.AsReadOnly(new float[value.MorphTargets.Count]);
        }
    }

    /// <summary>Gets or sets the renderer material.</summary>
    public Material Material
    {
        get => material;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            material = value;
        }
    }

    /// <summary>
    /// Gets or sets whether this mesh renders to camera passes, the directional-shadow map, or
    /// both. <see cref="MeshShadowCastingMode.On"/> preserves the default behavior.
    /// </summary>
    public MeshShadowCastingMode ShadowCastingMode
    {
        get => shadowCastingMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            shadowCastingMode = value;
        }
    }

    /// <summary>
    /// Gets or sets the optional skeleton used by geometry carrying joint indices and weights.
    /// </summary>
    public Skin? Skin
    {
        get => skin;
        set => skin = value;
    }

    /// <summary>
    /// Gets or sets finite FP32 morph weights matching <see cref="MeshGeometry.MorphTargets"/>.
    /// Negative and above-one weights are preserved for authored overshoot.
    /// </summary>
    public IReadOnlyList<float> MorphWeights
    {
        get => morphWeights;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetMorphWeights([.. value]);
        }
    }

    internal void SetMorphWeights(ReadOnlySpan<float> weights)
    {
        if (weights.Length != geometry.MorphTargets.Count)
        {
            throw new ArgumentException("Morph weights must match the geometry target count.", nameof(weights));
        }
        foreach (float weight in weights)
        {
            if (!float.IsFinite(weight))
            {
                throw new ArgumentOutOfRangeException(nameof(weights), "Morph weights must be finite.");
            }
        }
        morphWeights = Array.AsReadOnly(weights.ToArray());
    }
}
