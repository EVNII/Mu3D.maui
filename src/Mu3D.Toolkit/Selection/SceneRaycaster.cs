using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Toolkit.Selection;

/// <summary>Describes the closest visible triangle hit for one scene mesh.</summary>
public sealed class SceneRaycastHit
{
    internal SceneRaycastHit(
        Mesh mesh,
        int triangleIndex,
        Vector3 worldPosition,
        Vector3 localPosition,
        float distance)
    {
        Mesh = mesh;
        TriangleIndex = triangleIndex;
        WorldPosition = worldPosition;
        LocalPosition = localPosition;
        Distance = distance;
    }

    /// <summary>Gets the borrowed mesh whose current rigid/morph geometry was intersected.</summary>
    public Mesh Mesh { get; }

    /// <summary>Gets the zero-based triangle index within the mesh index buffer.</summary>
    public int TriangleIndex { get; }

    /// <summary>Gets the FP32 world-space intersection position.</summary>
    public Vector3 WorldPosition { get; }

    /// <summary>Gets the FP32 intersection position in the mesh's current morphed local space.</summary>
    public Vector3 LocalPosition { get; }

    /// <summary>Gets the positive world-space distance from the camera to the intersection.</summary>
    public float Distance { get; }
}

/// <summary>
/// Describes one value-type scene intersection returned by allocation-sensitive raycast paths.
/// </summary>
/// <remarks>
/// The mesh is borrowed from the scene. Retaining this value does not retain or snapshot geometry.
/// </remarks>
public readonly record struct SceneRaycastIntersection
{
    internal SceneRaycastIntersection(
        Mesh mesh,
        int triangleIndex,
        Vector3 worldPosition,
        Vector3 localPosition,
        float distance)
    {
        Mesh = mesh;
        TriangleIndex = triangleIndex;
        WorldPosition = worldPosition;
        LocalPosition = localPosition;
        Distance = distance;
    }

    /// <summary>Gets the borrowed mesh whose current rigid/morph geometry was intersected.</summary>
    public Mesh Mesh { get; }

    /// <summary>Gets the zero-based triangle index within the mesh index buffer.</summary>
    public int TriangleIndex { get; }

    /// <summary>Gets the FP32 world-space intersection position.</summary>
    public Vector3 WorldPosition { get; }

    /// <summary>Gets the FP32 intersection position in the mesh's current morphed local space.</summary>
    public Vector3 LocalPosition { get; }

    /// <summary>Gets the positive world-space distance from the camera to the intersection.</summary>
    public float Distance { get; }
}

/// <summary>Raycasts visible indexed triangle meshes from physical viewport coordinates.</summary>
/// <remarks>
/// Results contain the closest triangle from each intersected mesh and are ordered by increasing
/// camera distance. This class determines geometric intersections only; it does not choose a
/// selection, acquire input, mutate scene nodes, or own the scene or camera. Each request applies
/// current FP32 morph position deltas in target order before rigid world transformation, matching
/// the renderer's morph stage. Skin deformation and material alpha masks are not evaluated.
/// Results use current scene state, not a snapshot of a previously presented frame.
/// </remarks>
public sealed class SceneRaycaster
{
    private const float IntersectionEpsilon = 0.000001f;

    /// <summary>Returns ordered visible-mesh intersections for one viewport position.</summary>
    /// <param name="scene">The borrowed scene whose visible meshes are tested.</param>
    /// <param name="camera">The borrowed perspective camera defining the rendered view.</param>
    /// <param name="viewportWidthPixels">The positive physical viewport width.</param>
    /// <param name="viewportHeightPixels">The positive physical viewport height.</param>
    /// <param name="viewportPositionPixels">
    /// The finite top-left-origin physical-pixel position inside the viewport bounds.
    /// </param>
    public IReadOnlyList<SceneRaycastHit> HitTest(
        Scene scene,
        PerspectiveCamera camera,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        Vector2 viewportPositionPixels)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfZero(viewportWidthPixels);
        ArgumentOutOfRangeException.ThrowIfZero(viewportHeightPixels);
        ValidateViewportPosition(
            viewportPositionPixels,
            viewportWidthPixels,
            viewportHeightPixels);

        Ray ray = CreateRay(
            camera,
            viewportWidthPixels,
            viewportHeightPixels,
            viewportPositionPixels);
        List<SceneRaycastHit> hits = [];
        foreach (Mesh mesh in scene.EnumerateVisible(camera.VisibilityMask).OfType<Mesh>())
        {
            if (TryHitMesh(mesh, ray, out SceneRaycastHit hit))
            {
                hits.Add(hit);
            }
        }
        hits.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
        return hits.AsReadOnly();
    }

    /// <summary>
    /// Attempts to return only the closest accepted visible-mesh intersection without allocating
    /// a result collection or hit object.
    /// </summary>
    /// <param name="scene">The borrowed scene whose visible meshes are tested.</param>
    /// <param name="camera">The borrowed perspective camera defining the rendered view.</param>
    /// <param name="viewportWidthPixels">The positive physical viewport width.</param>
    /// <param name="viewportHeightPixels">The positive physical viewport height.</param>
    /// <param name="viewportPositionPixels">
    /// The finite top-left-origin physical-pixel position inside the viewport bounds.
    /// </param>
    /// <param name="intersection">
    /// Receives the closest accepted intersection when this method returns <see langword="true"/>.
    /// </param>
    /// <param name="meshFilter">
    /// An optional application-owned filter. Cache the delegate when this method is called from a
    /// high-frequency input path; a null filter accepts every visible mesh.
    /// </param>
    /// <returns><see langword="true"/> when an accepted visible triangle was intersected.</returns>
    public bool TryHitClosest(
        Scene scene,
        PerspectiveCamera camera,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        Vector2 viewportPositionPixels,
        out SceneRaycastIntersection intersection,
        Predicate<Mesh>? meshFilter = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfZero(viewportWidthPixels);
        ArgumentOutOfRangeException.ThrowIfZero(viewportHeightPixels);
        ValidateViewportPosition(
            viewportPositionPixels,
            viewportWidthPixels,
            viewportHeightPixels);

        Ray ray = CreateRay(
            camera,
            viewportWidthPixels,
            viewportHeightPixels,
            viewportPositionPixels);
        intersection = default;
        float closestDistance = float.PositiveInfinity;
        IReadOnlyList<SceneNode> children = scene.Root.Children;
        for (int index = 0; index < children.Count; index++)
        {
            VisitClosest(
                children[index],
                camera.VisibilityMask,
                ray,
                meshFilter,
                ref closestDistance,
                ref intersection);
        }
        return closestDistance < float.PositiveInfinity;
    }

    private static void VisitClosest(
        SceneNode node,
        SceneVisibilityMask visibilityMask,
        Ray ray,
        Predicate<Mesh>? meshFilter,
        ref float closestDistance,
        ref SceneRaycastIntersection intersection)
    {
        if (!node.IsVisible)
        {
            return;
        }
        if (node is Mesh mesh &&
            mesh.VisibilityMask.Intersects(visibilityMask) &&
            (meshFilter is null || meshFilter(mesh)) &&
            TryHitMeshValue(mesh, ray, out SceneRaycastIntersection candidate) &&
            candidate.Distance < closestDistance)
        {
            closestDistance = candidate.Distance;
            intersection = candidate;
        }
        IReadOnlyList<SceneNode> children = node.Children;
        for (int index = 0; index < children.Count; index++)
        {
            VisitClosest(
                children[index],
                visibilityMask,
                ray,
                meshFilter,
                ref closestDistance,
                ref intersection);
        }
    }

    private static Ray CreateRay(
        PerspectiveCamera camera,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        Vector2 viewportPositionPixels)
    {
        if (!Matrix4x4.Invert(camera.ViewProjectionMatrix, out Matrix4x4 inverseViewProjection))
        {
            throw new InvalidOperationException("The camera view-projection matrix is not invertible.");
        }

        float normalizedX = viewportPositionPixels.X / viewportWidthPixels * 2f - 1f;
        float normalizedY = 1f - viewportPositionPixels.Y / viewportHeightPixels * 2f;
        Vector3 near = Unproject(new Vector4(normalizedX, normalizedY, 0f, 1f), inverseViewProjection);
        Vector3 far = Unproject(new Vector4(normalizedX, normalizedY, 1f, 1f), inverseViewProjection);
        Vector3 origin = Vector3.Transform(Vector3.Zero, camera.WorldMatrix);
        Vector3 direction = far - origin;
        if (!IsFinite(direction) || direction.LengthSquared() <= IntersectionEpsilon)
        {
            throw new InvalidOperationException("The viewport position produced a degenerate camera ray.");
        }
        direction = Vector3.Normalize(direction);
        float minimumDistance = Vector3.Dot(near - origin, direction);
        float maximumDistance = Vector3.Dot(far - origin, direction);
        if (!float.IsFinite(minimumDistance) || !float.IsFinite(maximumDistance) ||
            minimumDistance < 0f || maximumDistance <= minimumDistance)
        {
            throw new InvalidOperationException("The camera clip range produced an invalid ray interval.");
        }
        return new Ray(origin, direction, minimumDistance, maximumDistance);
    }

    private static Vector3 Unproject(Vector4 clip, Matrix4x4 inverseViewProjection)
    {
        Vector4 world = Vector4.Transform(clip, inverseViewProjection);
        if (!float.IsFinite(world.X) || !float.IsFinite(world.Y) ||
            !float.IsFinite(world.Z) || !float.IsFinite(world.W) ||
            MathF.Abs(world.W) <= IntersectionEpsilon)
        {
            throw new InvalidOperationException("The camera produced a non-finite unprojected point.");
        }
        Vector3 position = new(world.X / world.W, world.Y / world.W, world.Z / world.W);
        if (!IsFinite(position))
        {
            throw new InvalidOperationException("The camera produced a non-finite unprojected point.");
        }
        return position;
    }

    private static bool TryHitMesh(Mesh mesh, Ray ray, out SceneRaycastHit hit)
    {
        hit = null!;
        if (!TryHitMeshValue(mesh, ray, out SceneRaycastIntersection intersection))
        {
            return false;
        }
        hit = new SceneRaycastHit(
            intersection.Mesh,
            intersection.TriangleIndex,
            intersection.WorldPosition,
            intersection.LocalPosition,
            intersection.Distance);
        return true;
    }

    private static bool TryHitMeshValue(
        Mesh mesh,
        Ray ray,
        out SceneRaycastIntersection intersection)
    {
        intersection = default;
        Matrix4x4 world = mesh.WorldMatrix;
        if (!Matrix4x4.Invert(world, out Matrix4x4 inverseWorld))
        {
            return false;
        }
        Vector3 localOrigin = Vector3.Transform(ray.Origin, inverseWorld);
        Vector3 localDirection = Vector3.TransformNormal(ray.Direction, inverseWorld);
        if (!IsFinite(localOrigin) || !IsFinite(localDirection) ||
            localDirection.LengthSquared() <= IntersectionEpsilon)
        {
            return false;
        }

        MeshGeometry geometry = mesh.Geometry;
        IReadOnlyList<Vector3> positions = geometry.Positions;
        IReadOnlyList<uint> indices = geometry.Indices;
        IReadOnlyList<MorphTarget> morphTargets = geometry.MorphTargets;
        IReadOnlyList<float> morphWeights = mesh.MorphWeights;
        float closestDistance = float.PositiveInfinity;
        int closestTriangle = -1;
        Vector3 closestLocalPosition = default;
        for (int offset = 0; offset < indices.Count; offset += 3)
        {
            Vector3 first = GetCurrentPosition(checked((int)indices[offset]), positions, morphTargets, morphWeights);
            Vector3 second = GetCurrentPosition(checked((int)indices[offset + 1]), positions, morphTargets, morphWeights);
            Vector3 third = GetCurrentPosition(checked((int)indices[offset + 2]), positions, morphTargets, morphWeights);
            if (!IsFinite(first) || !IsFinite(second) || !IsFinite(third) ||
                !TryIntersectTriangle(
                    localOrigin,
                    localDirection,
                    first,
                    second,
                    third,
                    out float distance) ||
                distance < ray.MinimumDistance || distance > ray.MaximumDistance ||
                distance >= closestDistance)
            {
                continue;
            }
            closestDistance = distance;
            closestTriangle = offset / 3;
            closestLocalPosition = localOrigin + localDirection * distance;
        }
        if (closestTriangle < 0)
        {
            return false;
        }

        Vector3 worldPosition = ray.Origin + ray.Direction * closestDistance;
        intersection = new SceneRaycastIntersection(
            mesh,
            closestTriangle,
            worldPosition,
            closestLocalPosition,
            closestDistance);
        return true;
    }

    private static Vector3 GetCurrentPosition(
        int vertexIndex,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<MorphTarget> morphTargets,
        IReadOnlyList<float> morphWeights)
    {
        Vector3 position = positions[vertexIndex];
        for (int targetIndex = 0; targetIndex < morphTargets.Count; targetIndex++)
        {
            float weight = morphWeights[targetIndex];
            if (weight != 0f)
            {
                position += morphTargets[targetIndex].PositionDeltas[vertexIndex] * weight;
            }
        }
        return position;
    }

    private static bool TryIntersectTriangle(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        out float distance)
    {
        Vector3 firstEdge = second - first;
        Vector3 secondEdge = third - first;
        Vector3 perpendicular = Vector3.Cross(rayDirection, secondEdge);
        float determinant = Vector3.Dot(firstEdge, perpendicular);
        if (MathF.Abs(determinant) <= IntersectionEpsilon)
        {
            distance = default;
            return false;
        }

        float inverseDeterminant = 1f / determinant;
        Vector3 fromFirst = rayOrigin - first;
        float firstBarycentric = Vector3.Dot(fromFirst, perpendicular) * inverseDeterminant;
        if (firstBarycentric < 0f || firstBarycentric > 1f)
        {
            distance = default;
            return false;
        }
        Vector3 cross = Vector3.Cross(fromFirst, firstEdge);
        float secondBarycentric = Vector3.Dot(rayDirection, cross) * inverseDeterminant;
        if (secondBarycentric < 0f || firstBarycentric + secondBarycentric > 1f)
        {
            distance = default;
            return false;
        }
        distance = Vector3.Dot(secondEdge, cross) * inverseDeterminant;
        return float.IsFinite(distance) && distance >= 0f;
    }

    private static void ValidateViewportPosition(
        Vector2 position,
        uint viewportWidthPixels,
        uint viewportHeightPixels)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            position.X < 0f || position.Y < 0f ||
            position.X > viewportWidthPixels || position.Y > viewportHeightPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "The viewport position must be finite and inside the viewport bounds.");
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private readonly record struct Ray(
        Vector3 Origin,
        Vector3 Direction,
        float MinimumDistance,
        float MaximumDistance);
}
