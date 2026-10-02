using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

internal sealed class AnimationLabPresenter : IDisposable
{
    private readonly IPresentationSurfaceSession? session;
    private readonly Scene scene = new("animation lab");
    private readonly PerspectiveCamera camera = new(aspectRatio: 1f);
    private readonly Mesh strip;
    private readonly AnimationLayer primaryLayer;
    private readonly AnimationLayer secondaryLayer;
    private readonly AnimationMixer mixer;
    private SceneRenderer? renderer;
    private GraphicsTexture? depthTexture;
    private GraphicsExtent3D depthExtent;

    internal AnimationLabPresenter(IPresentationSurfaceSession? session = null)
    {
        this.session = session;
        SceneNode lowerJoint = new("lower joint");
        lowerJoint.Transform.Position = new Vector3(0, -1.25f, 0);
        SceneNode upperJoint = new("upper joint");
        upperJoint.Transform.Position = new Vector3(0, 1.25f, 0);
        lowerJoint.AddChild(upperJoint);
        scene.Add(lowerJoint);

        Skin skin = new(
            [lowerJoint, upperJoint],
            [Matrix4x4.CreateTranslation(0, 1.25f, 0), Matrix4x4.Identity],
            "two-joint strip skin");
        strip = new Mesh(
            CreateStripGeometry(),
            new UnlitMaterial(new LinearRgba(
                0.08f,
                0.7f,
                1.8f,
                1f,
                StandardColorSpaces.LinearSrgb)),
            "weighted strip")
        {
            Skin = skin,
        };
        scene.Add(strip);

        MeshGeometry markerGeometry = MeshPrimitives.CreateUvSphere(0.09f, 12, 6);
        lowerJoint.AddChild(new Mesh(
            markerGeometry,
            new UnlitMaterial(new LinearRgba(2f, 0.15f, 0.05f, 1f, StandardColorSpaces.LinearSrgb)),
            "lower joint marker"));
        upperJoint.AddChild(new Mesh(
            markerGeometry,
            new UnlitMaterial(new LinearRgba(0.1f, 2f, 0.2f, 1f, StandardColorSpaces.LinearSrgb)),
            "upper joint marker"));

        AnimationClip primaryClip = new(
            [
                new QuaternionAnimationTrack(
                    upperJoint,
                    [0f, 0.5f, 1f, 1.5f, 2f],
                    [
                        Quaternion.Identity,
                        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.75f),
                        Quaternion.Identity,
                        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.75f),
                        Quaternion.Identity,
                    ]),
            ],
            "upper-joint bend");
        AnimationClip secondaryClip = new(
            [
                new QuaternionAnimationTrack(
                    upperJoint,
                    [0f, 0.5f, 1f, 1.5f, 2f],
                    [
                        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.35f),
                        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.85f),
                        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.35f),
                        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.85f),
                        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.35f),
                    ]),
            ],
            "opposing upper-joint bend");
        primaryLayer = new AnimationLayer(primaryClip);
        secondaryLayer = new AnimationLayer(secondaryClip) { Weight = 0f };
        mixer = new AnimationMixer([primaryLayer, secondaryLayer]);
        camera.Transform.Position = new Vector3(0, 0, 4.2f);
    }

    internal float Duration => primaryLayer.Clip.Duration;

    internal float Blend
    {
        set
        {
            float amount = Math.Clamp(value, 0f, 1f);
            primaryLayer.Weight = 1f - amount;
            secondaryLayer.Weight = amount;
        }
    }

    internal float MorphWeight
    {
        set => strip.MorphWeights = [value];
    }

    internal PresentationSurfaceFrameStatus Present(float timeSeconds)
    {
        IPresentationSurfaceSession activeSession = session ?? throw new InvalidOperationException("Present requires an owned presentation surface.");
        GraphicsTexture depth = EnsureDepthTexture(activeSession);
        return activeSession.RenderAndPresent(target => Draw(activeSession.Device, target, depth, timeSeconds));
    }

    // One portable rendering seam; the host alone owns presentation, depth and frame scheduling.
    internal void Draw(GraphicsDevice device, GraphicsTexture target, GraphicsTexture depth, float timeSeconds)
    {
        primaryLayer.Time = timeSeconds;
        secondaryLayer.Time = timeSeconds;
        mixer.Apply();
        camera.AspectRatio = (float)target.Descriptor.Size.Width / target.Descriptor.Size.Height;
        renderer ??= new SceneRenderer(device, target.Descriptor.Format);
        renderer.Render(scene, camera, target, depth);
    }

    public void Dispose()
    {
        depthTexture?.Dispose();
        renderer?.Dispose();
    }

    private GraphicsTexture EnsureDepthTexture(IPresentationSurfaceSession session)
    {
        GraphicsExtent3D extent = new(session.Width, session.Height);
        if (depthTexture is not null && depthExtent == extent)
        {
            return depthTexture;
        }
        depthTexture?.Dispose();
        depthTexture = session.Device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "Animation lab depth"));
        depthExtent = extent;
        return depthTexture;
    }

    private static MeshGeometry CreateStripGeometry()
    {
        const int segments = 16;
        Vector3[] positions = new Vector3[(segments + 1) * 2];
        Vector3[] normals = new Vector3[positions.Length];
        Vector2[] uvs = new Vector2[positions.Length];
        JointIndices4[] joints = new JointIndices4[positions.Length];
        Vector4[] weights = new Vector4[positions.Length];
        Vector3[] morphDeltas = new Vector3[positions.Length];
        List<uint> indices = new(segments * 6);
        for (int row = 0; row <= segments; row++)
        {
            float amount = (float)row / segments;
            float y = -1.25f + amount * 2.5f;
            float upperWeight = Math.Clamp((amount - 0.35f) / 0.65f, 0f, 1f);
            for (int column = 0; column < 2; column++)
            {
                int vertex = row * 2 + column;
                positions[vertex] = new Vector3(column == 0 ? -0.38f : 0.38f, y, 0);
                normals[vertex] = Vector3.UnitZ;
                uvs[vertex] = new Vector2(column, amount);
                joints[vertex] = new JointIndices4(0, 1);
                weights[vertex] = new Vector4(1f - upperWeight, upperWeight, 0, 0);
                float signedWidth = column == 0 ? -1f : 1f;
                float bulge = MathF.Sin(amount * MathF.PI);
                morphDeltas[vertex] = new Vector3(signedWidth * 0.28f * bulge, 0, 0);
            }
            if (row < segments)
            {
                uint bottom = checked((uint)(row * 2));
                uint top = bottom + 2;
                indices.AddRange([bottom, bottom + 1, top + 1, bottom, top + 1, top]);
            }
        }
        return new MeshGeometry(
            positions,
            indices,
            normals,
            uvs,
            jointIndices: joints,
            jointWeights: weights,
            morphTargets: [new MorphTarget(morphDeltas, name: "strip width bulge")]);
    }
}
