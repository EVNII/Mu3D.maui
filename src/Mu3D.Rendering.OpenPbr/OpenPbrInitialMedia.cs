using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering.OpenPbr;

// Camera-inside initialization validates closed manifold boundaries, winding and strict nesting.
// Concave boundaries are supported; touching, intersecting and ambiguous media fail explicitly.
internal static class OpenPbrInitialMedia
{
    internal static Vector4[] Resolve(OpenPbrSceneSnapshot snapshot, Vector3 origin,
        int maximumDepth = 8, long maximumFaceTests = 8_000_000)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        OpenPbrSceneCompiler.RequireFinite(origin);
        if (maximumDepth is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFaceTests);
        Dictionary<int, ObjectBoundary> objects = [];
        for (int index = 0; index < snapshot.TriangleCount; index++)
        {
            int offset = index * 12;
            int material = (int)snapshot.Triangles[offset].W;
            var surface = snapshot.SourceMaterials[material].Surface;
            if (Maximum(surface, OpenPbrInput.GeometryThinWalled, surface.GeometryThinWalled ? 1 : 0) == 1 ||
                Minimum(surface, OpenPbrInput.BaseMetalness, surface.BaseMetalness) == 1 ||
                (Maximum(surface, OpenPbrInput.TransmissionWeight, surface.TransmissionWeight) == 0 &&
                 Maximum(surface, OpenPbrInput.SubsurfaceWeight, surface.SubsurfaceWeight) == 0)) continue;
            int objectId = (int)snapshot.Triangles[offset + 1].W;
            if (!objects.TryGetValue(objectId, out ObjectBoundary? boundary))
            {
                boundary = new(objectId, material);
                objects.Add(objectId, boundary);
            }
            for (int vertex = 0; vertex < 3; vertex++)
            {
                Vector3 p = Xyz(snapshot.Triangles[offset + vertex]);
                boundary.Minimum = Vector3.Min(boundary.Minimum, p);
                boundary.Maximum = Vector3.Max(boundary.Maximum, p);
            }
        }

        foreach (int key in objects.Where(pair => !pair.Value.BoundsContain(origin)).Select(pair => pair.Key).ToArray())
            objects.Remove(key);
        if (objects.Count == 0) return [];
        for (int index = 0; index < snapshot.TriangleCount; index++)
        {
            int offset = index * 12;
            if (objects.TryGetValue((int)snapshot.Triangles[offset + 1].W, out ObjectBoundary? boundary))
                boundary.Faces.Add(new Face(Xyz(snapshot.Triangles[offset]), Xyz(snapshot.Triangles[offset + 1]),
                    Xyz(snapshot.Triangles[offset + 2])));
        }

        List<ObjectBoundary> containing = [];
        foreach (ObjectBoundary boundary in objects.Values)
        {
            double angle = 0d;
            double tolerance = boundary.Tolerance;
            foreach (Face face in boundary.Faces)
            {
                if (face.OnTriangle(origin, tolerance))
                    throw new NotSupportedException("The camera lies on an OpenPBR volume boundary; move it away from the surface.");
                angle += face.SolidAngle(origin);
            }
            double winding = angle / (4d * Math.PI);
            if (Math.Abs(winding) < 1e-8d) continue;
            if (Math.Abs(winding - 1d) > 1e-8d)
                throw new NotSupportedException("The camera's OpenPBR medium is ambiguous: the surrounding geometry is open, intersecting or inconsistently oriented.");
            var surface = snapshot.SourceMaterials[boundary.MaterialIndex].Surface;
            if (surface.Graph is { } graph && graph.Bindings.Any(b => !b.Value.IsUniform && IsVolumeInput(b.Key)))
                throw new NotSupportedException("A camera inside a homogeneous medium requires uniform volume/IOR inputs; spatial fields need a volume representation rather than a surface UV assignment.");
            if (Minimum(surface, OpenPbrInput.GeometryOpacity, surface.GeometryOpacity) != 1f)
                throw new NotSupportedException("Camera-inside OpenPBR volumes require full geometry opacity.");
            boundary.ValidateClosed(ref maximumFaceTests);
            containing.Add(boundary);
            if (containing.Count > maximumDepth)
                throw new NotSupportedException("The camera starts inside more OpenPBR volumes than the medium-stack limit.");
        }

        // Establish strict containment for every pair. Sorting on AABB size alone would silently
        // accept overlapping volumes, which do not admit an ordered nested-medium stack.
        Dictionary<int, int> enclosing = containing.ToDictionary(item => item.ObjectId, _ => 0);
        for (int first = 0; first < containing.Count; first++)
        for (int second = first + 1; second < containing.Count; second++)
        {
            ObjectBoundary a = containing[first], b = containing[second];
            bool aContainsB = a.StrictlyContains(b, ref maximumFaceTests);
            bool bContainsA = b.StrictlyContains(a, ref maximumFaceTests);
            if (aContainsB == bContainsA)
                throw new NotSupportedException("Camera-inside OpenPBR volumes must be strictly nested; overlapping or touching boundaries are unsupported.");
            enclosing[aContainsB ? b.ObjectId : a.ObjectId]++;
        }
        return containing.OrderBy(item => enclosing[item.ObjectId])
            .Select(item => new Vector4(item.MaterialIndex, item.ObjectId, 0f, 0f)).ToArray();
    }

    private static float Maximum(OpenPbrSurface s, OpenPbrInput input, float fallback) => s.Graph?.Maximum(input, fallback) ?? fallback;
    private static float Minimum(OpenPbrSurface s, OpenPbrInput input, float fallback) => s.Graph?.Minimum(input, fallback) ?? fallback;
    private static bool IsVolumeInput(OpenPbrInput input) => input is
        OpenPbrInput.BaseMetalness or OpenPbrInput.SpecularWeight or OpenPbrInput.SpecularIor or
        OpenPbrInput.TransmissionWeight or OpenPbrInput.TransmissionColor or OpenPbrInput.TransmissionDepth or
        OpenPbrInput.TransmissionScatter or OpenPbrInput.TransmissionScatterAnisotropy or
        OpenPbrInput.TransmissionDispersionScale or OpenPbrInput.TransmissionDispersionAbbeNumber or
        OpenPbrInput.SubsurfaceWeight or OpenPbrInput.SubsurfaceColor or OpenPbrInput.SubsurfaceRadius or
        OpenPbrInput.SubsurfaceRadiusScale or OpenPbrInput.SubsurfaceScatterAnisotropy or
        OpenPbrInput.CoatWeight or OpenPbrInput.CoatIor;

    private static Vector3 Xyz(Vector4 value) => new(value.X, value.Y, value.Z);

    private sealed class ObjectBoundary(int objectId, int materialIndex)
    {
        internal int ObjectId { get; } = objectId;
        internal int MaterialIndex { get; } = materialIndex;
        internal Vector3 Minimum = new(float.PositiveInfinity);
        internal Vector3 Maximum = new(float.NegativeInfinity);
        internal List<Face> Faces { get; } = [];
        private Vector3[] vertices = [];
        // FP32-generated coplanar quads can differ by a few ulps after triangulation. The same
        // conservative margin rejects uncertain camera/touching boundaries; it never welds edges.
        internal double Tolerance => Math.Max((double)Maximum.X - Minimum.X,
            Math.Max((double)Maximum.Y - Minimum.Y, (double)Maximum.Z - Minimum.Z)) * (4d / 8388608d);

        internal bool BoundsContain(Vector3 point) => point.X >= Minimum.X && point.X <= Maximum.X &&
            point.Y >= Minimum.Y && point.Y <= Maximum.Y && point.Z >= Minimum.Z && point.Z <= Maximum.Z;

        internal void ValidateClosed(ref long budget)
        {
            HashSet<Vector3> unique = [];
            Dictionary<(Vector3, Vector3), (int Count, int Orientation)> edges = [];
            foreach (Face face in Faces)
            {
                unique.Add(face.A); unique.Add(face.B); unique.Add(face.C);
                AddEdge(face.A, face.B); AddEdge(face.B, face.C); AddEdge(face.C, face.A);
            }
            if (edges.Values.Any(edge => edge.Count != 2 || edge.Orientation != 0))
                throw new NotSupportedException("Camera-inside OpenPBR volumes require an exactly closed, consistently wound mesh; open seams and non-manifold edges are unsupported.");
            vertices = unique.ToArray();
            Spend(ref budget, checked((long)Faces.Count * (Faces.Count - 1) / 2));
            for (int i = 0; i < Faces.Count; i++)
            for (int j = i + 1; j < Faces.Count; j++)
                if (Faces[i].ImproperIntersection(Faces[j], Tolerance))
                    throw new NotSupportedException("Camera-inside media require non-self-intersecting closed boundaries.");

            void AddEdge(Vector3 a, Vector3 b)
            {
                bool forward = a.X < b.X || (a.X == b.X && (a.Y < b.Y || (a.Y == b.Y && a.Z < b.Z)));
                var key = forward ? (a, b) : (b, a);
                edges.TryGetValue(key, out var edge);
                edges[key] = (edge.Count + 1, edge.Orientation + (forward ? 1 : -1));
            }
        }

        internal bool StrictlyContains(ObjectBoundary other, ref long budget)
        {
            Spend(ref budget, checked((long)other.vertices.Length * Faces.Count + (long)other.Faces.Count * Faces.Count));
            foreach (Face a in Faces)
            foreach (Face b in other.Faces)
                if (a.Touches(b, Math.Max(Tolerance, other.Tolerance))) return false;
            foreach (Vector3 vertex in other.vertices)
            {
                double angle = 0;
                foreach (Face face in Faces) angle += face.SolidAngle(vertex);
                if (Math.Abs(angle / (4 * Math.PI) - 1) > 1e-8) return false;
            }
            return true;
        }
    }

    private static void Spend(ref long budget, long count)
    {
        if (count > budget)
            throw new NotSupportedException("Camera-inside OpenPBR geometry exceeds the explicit volume-classification budget.");
        budget -= count;
    }

    private readonly struct Face
    {
        internal Face(Vector3 a, Vector3 b, Vector3 c)
        {
            A = a; B = b; C = c;
            D3 cross = D3.Cross(new D3(b) - new D3(a), new D3(c) - new D3(a));
            normal = cross / cross.Length;
        }
        internal Vector3 A { get; }
        internal Vector3 B { get; }
        internal Vector3 C { get; }
        private readonly D3 normal;
        internal double Distance(Vector3 point) => D3.Dot(normal, new D3(point) - new D3(A));

        internal bool OnTriangle(Vector3 point, double tolerance)
        {
            if (Math.Abs(Distance(point)) > tolerance) return false;
            D3 edge0 = new D3(B) - new D3(A), edge1 = new D3(C) - new D3(A), delta = new D3(point) - new D3(A);
            double area = D3.Cross(edge0, edge1).Length;
            double u = D3.Dot(D3.Cross(delta, edge1), normal) / area;
            double v = D3.Dot(D3.Cross(edge0, delta), normal) / area;
            return u >= -1e-10d && v >= -1e-10d && u + v <= 1d + 1e-10d;
        }

        internal bool Touches(Face other, double tolerance)
        {
            Vector3 lo = Vector3.Min(A, Vector3.Min(B, C)), hi = Vector3.Max(A, Vector3.Max(B, C));
            Vector3 otherLo = Vector3.Min(other.A, Vector3.Min(other.B, other.C)), otherHi = Vector3.Max(other.A, Vector3.Max(other.B, other.C));
            if (hi.X < otherLo.X - tolerance || hi.Y < otherLo.Y - tolerance || hi.Z < otherLo.Z - tolerance ||
                otherHi.X < lo.X - tolerance || otherHi.Y < lo.Y - tolerance || otherHi.Z < lo.Z - tolerance) return false;
            D3[] a = [new(A), new(B), new(C)], b = [new(other.A), new(other.B), new(other.C)];
            D3[] ea = [a[1] - a[0], a[2] - a[1], a[0] - a[2]], eb = [b[1] - b[0], b[2] - b[1], b[0] - b[2]];
            if (Separated(normal) || Separated(other.normal)) return false;
            foreach (D3 x in ea)
            {
                if (Separated(D3.Cross(normal, x))) return false; // Coplanar separation.
                foreach (D3 y in eb) if (Separated(D3.Cross(x, y))) return false;
            }
            foreach (D3 y in eb) if (Separated(D3.Cross(other.normal, y))) return false;
            return true;
            bool Separated(D3 axis)
            {
                if (axis.Length < 1e-30) return false;
                axis /= axis.Length;
                double amin = double.PositiveInfinity, amax = double.NegativeInfinity, bmin = amin, bmax = amax;
                foreach (D3 p in a) { double d = D3.Dot(axis, p); amin = Math.Min(amin, d); amax = Math.Max(amax, d); }
                foreach (D3 p in b) { double d = D3.Dot(axis, p); bmin = Math.Min(bmin, d); bmax = Math.Max(bmax, d); }
                return amax < bmin - tolerance || bmax < amin - tolerance;
            }
        }
        internal bool ImproperIntersection(Face other, double tolerance)
        {
            if (!Touches(other, tolerance)) return false;
            Vector3[] a = [A, B, C], b = [other.A, other.B, other.C];
            int shared = a.Count(p => b.Contains(p));
            if (shared == 0 || shared == 3) return true;
            bool coplanar = a.All(p => Math.Abs(other.Distance(p)) <= tolerance);
            if (!coplanar)
            {
                if (shared == 2) return false;
                for (int i = 0; i < 3; i++)
                    if (other.Pierces(a[i], a[(i + 1) % 3], tolerance) || Pierces(b[i], b[(i + 1) % 3], tolerance)) return true;
                return false;
            }
            // Coplanar neighboring faces may share an edge/vertex but not overlapping area.
            for (int i = 0; i < 3; i++)
            {
                if (!b.Contains(a[i]) && other.OnTriangle(a[i], tolerance)) return true;
                if (!a.Contains(b[i]) && OnTriangle(b[i], tolerance)) return true;
                for (int j = 0; j < 3; j++)
                {
                    D3 p = new(a[i]), q = new(a[(i + 1) % 3]), r = new(b[j]), s = new(b[(j + 1) % 3]);
                    double d1 = D3.Dot(D3.Cross(q - p, r - p), normal), d2 = D3.Dot(D3.Cross(q - p, s - p), normal);
                    double d3 = D3.Dot(D3.Cross(s - r, p - r), normal), d4 = D3.Dot(D3.Cross(s - r, q - r), normal);
                    if (d1 * d2 < 0 && d3 * d4 < 0) return true;
                }
            }
            return false;
        }
        private bool Pierces(Vector3 a, Vector3 b, double tolerance)
        {
            double da = Distance(a), db = Distance(b);
            if (!(da > tolerance && db < -tolerance || da < -tolerance && db > tolerance)) return false;
            Vector3 p = Vector3.Lerp(a, b, (float)(da / (da - db)));
            return OnTriangle(p, tolerance * 2);
        }

        internal double SolidAngle(Vector3 point)
        {
            D3 a = new D3(A) - new D3(point), b = new D3(B) - new D3(point), c = new D3(C) - new D3(point);
            double la = a.Length, lb = b.Length, lc = c.Length;
            return 2d * Math.Atan2(D3.Dot(a, D3.Cross(b, c)),
                la * lb * lc + D3.Dot(a, b) * lc + D3.Dot(b, c) * la + D3.Dot(c, a) * lb);
        }
    }

    private readonly record struct D3(double X, double Y, double Z)
    {
        internal D3(Vector3 value) : this(value.X, value.Y, value.Z) { }
        internal double Length => Math.Sqrt(Dot(this, this));
        public static D3 operator -(D3 a, D3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static D3 operator /(D3 value, double scale) => new(value.X / scale, value.Y / scale, value.Z / scale);
        internal static double Dot(D3 a, D3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        internal static D3 Cross(D3 a, D3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }
}
