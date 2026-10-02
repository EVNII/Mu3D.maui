using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Creative;
using Mu3D.SceneGraph;
using Mu3D.Web.Validation;

if (RuntimeInformation.ProcessArchitecture != Architecture.Wasm || IntPtr.Size != 4)
    throw new InvalidOperationException("This probe must actually execute inside wasm32.");

Scene scene = new("WASM shared scene");
SceneNode parent = new("parent");
SceneNode child = new("child");
parent.Transform.Position = new Vector3(10, 0, 0);
child.Transform.Position = new Vector3(1, 2, 3);
scene.Add(parent);
parent.AddChild(child);
if (Vector3.Transform(Vector3.Zero, child.WorldMatrix) != new Vector3(11, 2, 3))
    throw new InvalidOperationException("Shared scene transforms differ on WASM.");

MeshGeometry cone = MeshPrimitives.CreateCone(radialSegments: 8);
if (cone.Indices.Count != 48)
    throw new InvalidOperationException("Shared FP32 mesh generation differs on WASM.");
PerspectiveCamera camera = new(MathF.PI / 3, 1, 0.1f, 100);
camera.Transform.Position = new Vector3(0, 0, 4);
Vector4 clip = Vector4.Transform(new Vector4(0, 0, 0, 1), camera.ViewProjectionMatrix);
if (clip.Z / clip.W is not (>= 0 and <= 1))
    throw new InvalidOperationException("Shared camera depth differs on WASM.");

HdrCanvas canvas = new(4, 4, StandardColorSpaces.LinearSrgb);
canvas.ApplyDab(new BrushDab(1.5f, 1.5f, 1, new LinearRgba(4, 2, -0.5f, 1,
    StandardColorSpaces.LinearSrgb), hardness: 1));
LinearRgba pixel = canvas.GetPixel(1, 1);
if (pixel.Red != 4 || pixel.Green != 2 || pixel.Blue != -0.5f)
    throw new InvalidOperationException("Shared FP16 HDR document lost extended-range values.");

await NativeProbe.VerifyAsync();
Console.WriteLine("Mu3D WASM smoke passed: wasm32 execution, Core scene/mesh/camera, Creative FP16 HDR, static C P/Invoke and asynchronous callback.");
return 0;
