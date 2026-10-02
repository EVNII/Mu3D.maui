using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>Creates immutable backend-independent geometry for standard 3D primitives.</summary>
public static class MeshPrimitives
{
    /// <summary>
    /// Creates a closed right circular cone centered on the origin, aligned to the Y axis, with a
    /// smooth side and a flat base. The apex is at positive Y.
    /// </summary>
    /// <param name="radius">The positive finite base radius.</param>
    /// <param name="height">The positive finite tip-to-base height.</param>
    /// <param name="radialSegments">The number of segments around the Y axis; at least three.</param>
    /// <returns>Indexed FP32 positions, normals and UV coordinates.</returns>
    public static MeshGeometry CreateCone(
        float radius = 1f,
        float height = 2f,
        int radialSegments = 32)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Cone radius must be positive and finite.");
        }
        if (!float.IsFinite(height) || height <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Cone height must be positive and finite.");
        }
        if (radialSegments < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radialSegments),
                "A cone requires at least three radial segments.");
        }

        int sideVertexCount = checked((radialSegments + 1) * 2);
        int baseCenter = sideVertexCount;
        int baseRingStart = checked(baseCenter + 1);
        int vertexCount = checked(baseRingStart + radialSegments + 1);
        Vector3[] positions = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] textureCoordinates = new Vector2[vertexCount];
        uint[] indices = new uint[checked(radialSegments * 6)];
        float halfHeight = height * 0.5f;
        float normalY = radius / height;

        for (int segment = 0; segment <= radialSegments; segment++)
        {
            float u = (float)segment / radialSegments;
            float angle = u * MathF.PI * 2f;
            float x = MathF.Cos(angle);
            float z = MathF.Sin(angle);
            Vector3 sideNormal = Vector3.Normalize(new Vector3(x, normalY, z));
            int sideBase = segment * 2;
            positions[sideBase] = new Vector3(x * radius, -halfHeight, z * radius);
            positions[sideBase + 1] = new Vector3(0f, halfHeight, 0f);
            normals[sideBase] = sideNormal;
            normals[sideBase + 1] = sideNormal;
            textureCoordinates[sideBase] = new Vector2(u, 1f);
            textureCoordinates[sideBase + 1] = new Vector2(u, 0f);

            int baseVertex = baseRingStart + segment;
            positions[baseVertex] = positions[sideBase];
            normals[baseVertex] = -Vector3.UnitY;
            textureCoordinates[baseVertex] = new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f);
        }

        positions[baseCenter] = new Vector3(0f, -halfHeight, 0f);
        normals[baseCenter] = -Vector3.UnitY;
        textureCoordinates[baseCenter] = new Vector2(0.5f);

        int output = 0;
        for (int segment = 0; segment < radialSegments; segment++)
        {
            uint sideBase = checked((uint)(segment * 2));
            indices[output++] = sideBase;
            indices[output++] = sideBase + 1;
            indices[output++] = sideBase + 2;

            indices[output++] = checked((uint)baseCenter);
            indices[output++] = checked((uint)(baseRingStart + segment));
            indices[output++] = checked((uint)(baseRingStart + segment + 1));
        }

        return new MeshGeometry(positions, indices, normals, textureCoordinates);
    }

    /// <summary>
    /// Creates a right-handed UV sphere centered at the origin with outward unit normals, a seam on
    /// the negative/positive U boundary and counter-clockwise outward triangle winding.
    /// Duplicate seam and pole vertices have bit-identical positions while UV endpoints remain distinct.
    /// </summary>
    /// <param name="radius">The positive finite sphere radius.</param>
    /// <param name="longitudeSegments">The number of segments around the Y axis; at least three.</param>
    /// <param name="latitudeSegments">The number of pole-to-pole segments; at least two.</param>
    /// <returns>Indexed FP32 positions, normals and UV coordinates.</returns>
    public static MeshGeometry CreateUvSphere(
        float radius = 1f,
        int longitudeSegments = 32,
        int latitudeSegments = 16)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Sphere radius must be positive and finite.");
        }
        if (longitudeSegments < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longitudeSegments),
                "A UV sphere requires at least three longitude segments.");
        }
        if (latitudeSegments < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(latitudeSegments),
                "A UV sphere requires at least two latitude segments.");
        }

        int stride = checked(longitudeSegments + 1);
        int vertexCount = checked((latitudeSegments + 1) * stride);
        int indexCount = checked(6 * longitudeSegments * (latitudeSegments - 1));
        Vector3[] positions = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] textureCoordinates = new Vector2[vertexCount];
        uint[] indices = new uint[indexCount];

        int vertex = 0;
        for (int latitude = 0; latitude <= latitudeSegments; latitude++)
        {
            float v = (float)latitude / latitudeSegments;
            float polar = v * MathF.PI;
            float ringRadius = MathF.Sin(polar);
            float y = MathF.Cos(polar);
            for (int longitude = 0; longitude <= longitudeSegments; longitude++)
            {
                float u = (float)longitude / longitudeSegments;
                float azimuth = u * 2f * MathF.PI;
                Vector3 normal = new(
                    ringRadius * MathF.Cos(azimuth),
                    y,
                    ringRadius * MathF.Sin(azimuth));
                // The UV seam duplicates vertices, but its geometric position must be bit-exact.
                // Evaluating sin(2*pi) independently leaves a small open crack in a closed volume.
                if (longitude == longitudeSegments)
                {
                    normal = normals[vertex - longitudeSegments];
                }
                if (latitude == 0)
                {
                    normal = Vector3.UnitY;
                }
                else if (latitude == latitudeSegments)
                {
                    normal = -Vector3.UnitY;
                }
                normals[vertex] = normal;
                positions[vertex] = normal * radius;
                textureCoordinates[vertex] = new Vector2(u, v);
                vertex++;
            }
        }

        int output = 0;
        for (int latitude = 0; latitude < latitudeSegments; latitude++)
        {
            for (int longitude = 0; longitude < longitudeSegments; longitude++)
            {
                uint a = checked((uint)((latitude * stride) + longitude));
                uint b = checked(a + (uint)stride);
                uint c = b + 1;
                uint d = a + 1;
                if (latitude == 0)
                {
                    indices[output++] = a;
                    indices[output++] = c;
                    indices[output++] = b;
                }
                else if (latitude == latitudeSegments - 1)
                {
                    indices[output++] = a;
                    indices[output++] = d;
                    indices[output++] = b;
                }
                else
                {
                    indices[output++] = a;
                    indices[output++] = d;
                    indices[output++] = b;
                    indices[output++] = d;
                    indices[output++] = c;
                    indices[output++] = b;
                }
            }
        }

        return new MeshGeometry(positions, indices, normals, textureCoordinates);
    }
}
