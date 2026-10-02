using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

if (args.Length >= 1 && args[0] == "--renderer-cache-measure")
{
    SceneRendererCacheChecks.Measure(args.Length > 1 ? args[1] : null);
    return 0;
}

List<string> failures = [];
if (args.Length >= 1 && args[0] == "--renderer-cache-checks")
{
    SceneRendererCacheChecks.Run(failures);
    if (failures.Count != 0) Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return failures.Count == 0 ? 0 : 1;
}

Transform3D transform = new()
{
    Position = new Vector3(1, 2, 3),
    Rotation = new Quaternion(0, 0, 0, 2),
    Scale = new Vector3(2),
};
Vector3 transformedOrigin = Vector3.Transform(Vector3.Zero, transform.LocalMatrix);
Expect(transformedOrigin == new Vector3(1, 2, 3), "transform translation order", failures);
Expect(transform.Rotation == Quaternion.Identity, "transform quaternion normalization", failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => transform.Position = new Vector3(float.NaN, 0, 0),
    "non-finite transform rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => transform.Rotation = default,
    "zero quaternion rejection",
    failures);

Scene scene = new("test scene");
SceneNode parent = new("parent");
SceneNode child = new("child");
parent.Transform.Position = new Vector3(10, 0, 0);
child.Transform.Position = new Vector3(1, 0, 0);
scene.Add(parent);
parent.AddChild(child);
Vector3 childWorldOrigin = Vector3.Transform(Vector3.Zero, child.WorldMatrix);
Expect(childWorldOrigin == new Vector3(11, 0, 0), "hierarchical world transform", failures);
Expect(
    scene.Root.EnumerateDepthFirst().Select(static node => node.Name)
        .SequenceEqual(["Root", "parent", "child"]),
    "stable depth-first traversal",
    failures);
ExpectThrows<InvalidOperationException>(() => child.AddChild(parent), "scene cycle rejection", failures);
ExpectThrows<InvalidOperationException>(
    () => parent.AddChild(scene.Root),
    "permanent scene root rejection",
    failures);

SceneNode secondParent = new("second parent");
scene.Add(secondParent);
secondParent.AddChild(child);
Expect(
    ReferenceEquals(child.Parent, secondParent) && parent.Children.Count == 0,
    "explicit node reparenting",
    failures);
secondParent.IsVisible = false;
Expect(!scene.EnumerateVisible().Contains(child), "invisible subtree suppression", failures);
secondParent.IsVisible = true;
Expect(
    secondParent.VisibilityMask == SceneVisibilityMask.Default &&
    SceneVisibilityMask.Default.IsEnabled(0),
    "default scene visibility layer",
    failures);
secondParent.VisibilityMask = SceneVisibilityMask.FromLayer(1);
child.VisibilityMask = SceneVisibilityMask.FromLayer(2);
Expect(
    scene.EnumerateVisible(SceneVisibilityMask.FromLayer(2)).SequenceEqual([child]),
    "scene layer filtering keeps independently layered children",
    failures);
secondParent.IsVisible = false;
Expect(
    !scene.EnumerateVisible(SceneVisibilityMask.FromLayer(2)).Contains(child),
    "node visibility still suppresses layered subtree",
    failures);
secondParent.IsVisible = true;
secondParent.VisibilityMask = SceneVisibilityMask.Default;
child.VisibilityMask = SceneVisibilityMask.Default;
Expect(
    (SceneVisibilityMask.FromLayer(1) | SceneVisibilityMask.FromLayer(31)).Value ==
        ((1u << 1) | (1u << 31)) &&
    SceneVisibilityMask.All.Intersects(SceneVisibilityMask.FromLayer(31)) &&
    !SceneVisibilityMask.None.Intersects(SceneVisibilityMask.All),
    "scene visibility mask composition",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => SceneVisibilityMask.FromLayer(SceneVisibilityMask.LayerCount),
    "scene visibility layer range validation",
    failures);

PerspectiveCamera camera = new(MathF.PI / 3f, 16f / 9f, 0.1f, 100f, "camera");
camera.Transform.Position = new Vector3(0, 0, 4);
Vector4 clip = Vector4.Transform(new Vector4(0, 0, 0, 1), camera.ViewProjectionMatrix);
Expect(
    float.IsFinite(clip.Z) && clip.W > 0 && clip.Z / clip.W is >= 0 and <= 1,
    "perspective camera visible depth range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => camera.NearClip = camera.FarClip,
    "camera clipping validation",
    failures);
camera.Transform.Scale = Vector3.Zero;
ExpectThrows<InvalidOperationException>(() => _ = camera.ViewMatrix, "singular camera rejection", failures);

PerspectiveCamera snapshotCamera = new(MathF.PI / 3f, 16f / 9f, 0.1f, 100f, "snapshot camera");
snapshotCamera.Transform.Position = new Vector3(0f, 0f, 4f);
ViewportFrameSnapshot frameSnapshot = new(
    7,
    snapshotCamera.ViewMatrix,
    snapshotCamera.ProjectionMatrix,
    320,
    180,
    160d,
    90d);
Expect(
    frameSnapshot.TryProject(Vector3.Zero, out ViewportProjection centeredProjection) &&
    centeredProjection.IsInsideViewport &&
    Math.Abs(centeredProjection.PixelPosition.X - 160f) < 0.001f &&
    Math.Abs(centeredProjection.PixelPosition.Y - 90f) < 0.001f &&
    Math.Abs(centeredProjection.LogicalPosition.X - 80d) < 0.001d &&
    Math.Abs(centeredProjection.LogicalPosition.Y - 45d) < 0.001d &&
    frameSnapshot.DisplayScaleX == 2d &&
    frameSnapshot.DisplayScaleY == 2d,
    "successful frame snapshot projects physical and logical center",
    failures);
Expect(
    frameSnapshot.TryProject(new Vector3(100f, 0f, 0f), out ViewportProjection clippedProjection) &&
    !clippedProjection.IsInsideViewport,
    "frame snapshot distinguishes front-facing viewport clipping",
    failures);
Expect(
    !frameSnapshot.TryProject(new Vector3(0f, 0f, 5f), out _),
    "frame snapshot rejects point behind camera",
    failures);
Expect(
    !frameSnapshot.TryProject(new Vector3(float.NaN, 0f, 0f), out _),
    "frame snapshot rejects non-finite point",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new ViewportFrameSnapshot(
        0,
        Matrix4x4.Identity,
        Matrix4x4.Identity,
        1,
        1,
        0d,
        1d),
    "frame snapshot logical extent validation",
    failures);

Vector3[] sourcePositions =
[
    new(-1, -1, 0),
    new(1, -1, 0),
    new(0, 1, 0),
];
Vector4[] authoredTangents =
[
    new Vector4(1, 0, 0, -1),
    new Vector4(1, 0, 0, -1),
    new Vector4(1, 0, 0, -1),
];
MorphTarget testMorph = new(
    [new Vector3(0.25f, 0, 0), Vector3.Zero, new Vector3(0, 0.5f, 0)],
    [Vector3.Zero, Vector3.Zero, new Vector3(0, 0.1f, 0)],
    "test morph",
    [Vector3.Zero, new Vector3(0.2f, 0, 0), Vector3.Zero]);
MeshGeometry geometry = MeshGeometry.CreateWithTextureCoordinateSets(
    sourcePositions,
    [0, 1, 2],
    [Vector2.One, Vector2.UnitY, Vector2.UnitX],
    normals: [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
    textureCoordinates: [Vector2.Zero, Vector2.UnitX, Vector2.UnitY],
    morphTargets: [testMorph],
    tangents: authoredTangents);
sourcePositions[0] = new Vector3(99);
Expect(geometry.Positions[0] == new Vector3(-1, -1, 0), "geometry input copied", failures);
Expect(geometry.Indices.Count == 3 && geometry.Normals.Count == 3, "geometry channels retained", failures);
Expect(
    geometry.Tangents.Count == 3 && geometry.Tangents[0] == new Vector4(1, 0, 0, -1),
    "geometry tangent channel and handedness retained",
    failures);
Expect(
    geometry.TextureCoordinates1.Count == 3 && geometry.TextureCoordinates1[0] == Vector2.One,
    "geometry second texture-coordinate channel retained",
    failures);
Expect(
    geometry.MorphTargets.Count == 1 &&
    geometry.MorphTargets[0].PositionDeltas[2].Y == 0.5f &&
    geometry.MorphTargets[0].TangentDeltas[1].X == 0.2f,
    "mesh morph target retained",
    failures);
Mesh morphMesh = new(
    geometry,
    new UnlitMaterial(new LinearRgba(0, 0, 0, 1, StandardColorSpaces.LinearSrgb)));
Expect(morphMesh.MorphWeights.SequenceEqual([0f]), "morph weights initialize to zero", failures);
morphMesh.MorphWeights = [-0.25f];
ExpectNear(morphMesh.MorphWeights[0], -0.25f, 0f, "morph weight overshoot preserved", failures);
ExpectThrows<ArgumentException>(
    () => morphMesh.MorphWeights = [],
    "morph weight count validation",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => morphMesh.MorphWeights = [float.NaN],
    "morph weight finiteness validation",
    failures);
Vector3 bentDirection = Vector3.Normalize(new Vector3(0.3f, 0.1f, 1f));
MeshGeometry bentGeometry = new(
    sourcePositions,
    [0, 1, 2],
    [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
    bentNormals: [bentDirection, bentDirection, bentDirection]);
Expect(
    bentGeometry.BentNormals.Count == 3 && bentGeometry.BentNormals[0] == bentDirection,
    "mesh bent-normal channel retained",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new MeshGeometry(
        sourcePositions,
        [0, 1, 2],
        [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
        bentNormals: [-Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]),
    "bent normal hemisphere validation",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new MeshGeometry(sourcePositions, [0, 1, 3]),
    "mesh index bounds",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new MeshGeometry(sourcePositions, [0, 1]),
    "triangle index count",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new MeshGeometry(sourcePositions, [0, 1, 2], [Vector3.UnitZ]),
    "mesh channel count",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new MeshGeometry(sourcePositions, [0, 1, 2], [Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ]),
    "zero mesh normal rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new MeshGeometry(
        sourcePositions,
        [0, 1, 2],
        [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
        tangents: [Vector4.UnitX, Vector4.UnitX, Vector4.UnitX]),
    "mesh tangent handedness validation",
    failures);
MeshGeometry skinnedGeometry = new(
    sourcePositions,
    [0, 1, 2],
    [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
    jointIndices:
    [
        new JointIndices4(0),
        new JointIndices4(0),
        new JointIndices4(0),
    ],
    jointWeights:
    [
        new Vector4(2, 0, 0, 0),
        new Vector4(1, 0, 0, 0),
        new Vector4(1, 0, 0, 0),
    ]);
Expect(
    skinnedGeometry.JointIndices.Count == 3 && skinnedGeometry.JointWeights[0].X == 1f,
    "mesh skin channels copied and normalized",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new MeshGeometry(
        sourcePositions,
        [0, 1, 2],
        jointIndices: [new JointIndices4(0), new JointIndices4(0), new JointIndices4(0)]),
    "mesh requires paired joint indices and weights",
    failures);
SceneNode animatedJoint = new("animated joint");
Skin skin = new([animatedJoint], [Matrix4x4.Identity], "test skin");
Expect(skin.Joints.Count == 1 && skin.InverseBindMatrices.Count == 1, "skin channels", failures);
ExpectThrows<ArgumentException>(
    () => _ = new Skin([animatedJoint, animatedJoint], [Matrix4x4.Identity, Matrix4x4.Identity]),
    "skin duplicate joint rejection",
    failures);
Vector3AnimationTrack positionTrack = new(
    animatedJoint,
    Vector3AnimationTarget.Position,
    [0f, 2f],
    [Vector3.Zero, new Vector3(2, 0, 0)]);
QuaternionAnimationTrack rotationTrack = new(
    animatedJoint,
    [0f, 2f],
    [Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI)]);
AnimationClip animation = new([positionTrack, rotationTrack], "joint motion");
animation.Apply(1f);
ExpectNear(animatedJoint.Transform.Position.X, 1f, 0.00001f, "linear animation sampling", failures);
Vector3 rotatedAnimationAxis = Vector3.Transform(Vector3.UnitX, animatedJoint.Transform.Rotation);
ExpectNear(MathF.Abs(rotatedAnimationAxis.Y), 1f, 0.00001f, "quaternion animation sampling", failures);
animation.Apply(2.5f, AnimationWrapMode.Loop);
ExpectNear(animatedJoint.Transform.Position.X, 0.5f, 0.00001f, "looped animation sampling", failures);
SceneNode cubicNode = new("cubic node");
Vector3AnimationTrack cubicPositionTrack = new(
    cubicNode,
    Vector3AnimationTarget.Position,
    [0f, 2f],
    [Vector3.Zero, new Vector3(2, 0, 0)],
    [Vector3.Zero, Vector3.Zero],
    [new Vector3(2, 0, 0), Vector3.Zero]);
cubicPositionTrack.Apply(1f);
ExpectNear(cubicNode.Transform.Position.X, 1.5f, 0.00001f, "cubic vector sampling", failures);
QuaternionAnimationTrack cubicRotationTrack = new(
    cubicNode,
    [0f, 1f],
    [Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI)],
    [Vector4.Zero, Vector4.Zero],
    [Vector4.Zero, Vector4.Zero]);
cubicRotationTrack.Apply(0.5f);
Vector3 cubicRotatedAxis = Vector3.Transform(Vector3.UnitX, cubicNode.Transform.Rotation);
ExpectNear(MathF.Abs(cubicRotatedAxis.Y), 1f, 0.00001f, "cubic rotation normalization", failures);
ExpectThrows<ArgumentException>(
    () => _ = new Vector3AnimationTrack(
        cubicNode,
        Vector3AnimationTarget.Position,
        [0f],
        [Vector3.Zero],
        AnimationInterpolation.CubicSpline),
    "cubic vector track requires tangents",
    failures);
MorphWeightAnimationTrack cubicMorphTrack = new(
    morphMesh,
    [0f, 1f],
    [new float[] { 0f }, new float[] { 1f }],
    [new float[] { 0f }, new float[] { 0f }],
    [new float[] { 2f }, new float[] { 0f }]);
cubicMorphTrack.Apply(0.5f);
ExpectNear(morphMesh.MorphWeights[0], 0.75f, 0.00001f, "cubic morph sampling", failures);
SceneNode mixedNode = new("mixed node");
mixedNode.Transform.Position = new Vector3(10, 0, 0);
AnimationLayer leftLayer = new(new AnimationClip(
    [new Vector3AnimationTrack(
        mixedNode,
        Vector3AnimationTarget.Position,
        [0f],
        [new Vector3(-2, 0, 0)])]))
{
    Weight = 0.25f,
};
AnimationLayer rightLayer = new(new AnimationClip(
    [new Vector3AnimationTrack(
        mixedNode,
        Vector3AnimationTarget.Position,
        [0f],
        [new Vector3(2, 0, 0)])]))
{
    Weight = 0.25f,
};
AnimationMixer mixer = new([leftLayer, rightLayer]);
mixer.Apply();
ExpectNear(mixedNode.Transform.Position.X, 5f, 0.00001f, "animation mixer preserves base contribution", failures);
mixer.Apply();
ExpectNear(mixedNode.Transform.Position.X, 5f, 0.00001f, "animation mixer avoids accumulated drift", failures);
leftLayer.Weight = 1f;
rightLayer.Weight = 3f;
mixer.Apply();
ExpectNear(mixedNode.Transform.Position.X, 1f, 0.00001f, "animation mixer normalizes overweight layers", failures);

float[] gltfPositionValues = [-1f, -1f, 0f, 1f, -1f, 0f, 0f, 1f, 0f];
byte[] gltfBuffer = new byte[42];
MemoryMarshal.AsBytes(gltfPositionValues.AsSpan()).CopyTo(gltfBuffer);
BinaryPrimitives.WriteUInt16LittleEndian(gltfBuffer.AsSpan(36), 0);
BinaryPrimitives.WriteUInt16LittleEndian(gltfBuffer.AsSpan(38), 1);
BinaryPrimitives.WriteUInt16LittleEndian(gltfBuffer.AsSpan(40), 2);
string gltfDataUri = Convert.ToBase64String(gltfBuffer);
string gltfJson = $$"""
    {
      "asset":{"version":"2.0"},
      "buffers":[{"byteLength":42,"uri":"data:application/octet-stream;base64,{{gltfDataUri}}"}],
      "bufferViews":[
        {"buffer":0,"byteOffset":0,"byteLength":36},
        {"buffer":0,"byteOffset":36,"byteLength":6}
      ],
      "accessors":[
        {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
        {"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}
      ],
      "materials":[{
        "name":"imported red",
        "pbrMetallicRoughness":{"baseColorFactor":[0.8,0.1,0.05,0.5],"metallicFactor":0.25,"roughnessFactor":0.75},
        "alphaMode":"BLEND","doubleSided":true
      }],
      "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1,"material":0}]}],
      "nodes":[{"name":"imported node","mesh":0,"translation":[1,2,3]}],
      "scenes":[{"name":"imported scene","nodes":[0]}],
      "scene":0
    }
    """;
Scene importedGltf = GltfImporter.Import(Encoding.UTF8.GetBytes(gltfJson));
SceneNode importedNode = importedGltf.Root.Children.Single();
Mesh importedMesh = (Mesh)importedNode.Children.Single();
PbrMaterial importedMaterial = (PbrMaterial)importedMesh.Material;
Expect(
    importedGltf.Name == "imported scene" && importedNode.Name == "imported node" &&
    importedNode.Transform.Position == new Vector3(1, 2, 3),
    "glTF scene hierarchy and TRS import",
    failures);
Expect(
    importedMesh.Geometry.Indices.SequenceEqual([0u, 1u, 2u]) &&
    importedMesh.Geometry.Normals.All(normal => Vector3.Distance(normal, Vector3.UnitZ) < 0.00001f),
    "glTF accessor and generated-normal import",
    failures);
Expect(
    importedMaterial.BaseColor.ColorSpace == StandardColorSpaces.LinearSrgb &&
    importedMaterial.AlphaMode == MaterialAlphaMode.Blend && importedMaterial.IsDoubleSided,
    "glTF linear base-color and raster policy import",
    failures);
byte[] gltfSourceBytes = Encoding.UTF8.GetBytes(gltfJson);
using MemoryStream asyncGltfStream = new(gltfSourceBytes);
GltfAsset asynchronouslyLoadedGltf = await GltfAssetLoader.LoadAsync(
    asyncGltfStream,
    new GltfAssetLoadOptions
    {
        MaximumSourceByteCount = gltfSourceBytes.Length,
        ImportOptions = new GltfImportOptions { Name = "async glTF" },
    });
Expect(
    asynchronouslyLoadedGltf.Scene.Name == "async glTF" &&
    asynchronouslyLoadedGltf.Scene.Root.EnumerateDepthFirst().OfType<Mesh>().Count() == 1 &&
    asynchronouslyLoadedGltf.SourceArchive is null && asyncGltfStream.CanRead &&
    asyncGltfStream.Position == asyncGltfStream.Length,
    "bounded asynchronous glTF stream load leaves caller stream open and source unretained",
    failures);
using MemoryStream retainedMainGltfStream = new(gltfSourceBytes);
GltfAsset retainedMainGltf = await GltfAssetLoader.LoadAsync(
    retainedMainGltfStream,
    new GltfAssetLoadOptions
    {
        MaximumSourceByteCount = gltfSourceBytes.Length,
        MaximumRetainedSourceByteCount = gltfSourceBytes.Length,
        SourceRetention = GltfSourceRetentionMode.MainSource,
    });
Expect(
    retainedMainGltf.SourceArchive is GltfSourceArchive retainedMainArchive &&
    retainedMainArchive.MainSource.Span.SequenceEqual(gltfSourceBytes) &&
    retainedMainArchive.ExternalResources.Count == 0 &&
    retainedMainArchive.TotalByteCount == gltfSourceBytes.Length,
    "asynchronous glTF explicit main-source retention owns an exact bounded copy",
    failures);
using MemoryStream oversizedGltfStream = new(gltfSourceBytes);
await ExpectThrowsAsync<InvalidDataException>(
    () => GltfAssetLoader.LoadAsync(
        oversizedGltfStream,
        new GltfAssetLoadOptions { MaximumSourceByteCount = gltfSourceBytes.Length - 1 }),
    "asynchronous glTF source byte limit",
    failures);
Expect(oversizedGltfStream.CanRead, "size rejection leaves glTF stream open", failures);
using CancellationTokenSource gltfLoadCancellation = new();
using CancellingReadStream cancellingGltfStream = new(gltfSourceBytes, gltfLoadCancellation);
await ExpectThrowsAsync<OperationCanceledException>(
    () => GltfAssetLoader.LoadAsync(
        cancellingGltfStream,
        cancellationToken: gltfLoadCancellation.Token),
    "asynchronous glTF stream cancellation",
    failures);
Expect(cancellingGltfStream.CanRead, "cancellation leaves glTF stream open", failures);
string optionalMaterialGltfJson = gltfJson.Replace(
    "\"name\":\"imported red\",",
    "\"name\":\"imported red\",\"emissiveFactor\":[0.1,0.2,0.3]," +
        "\"extensions\":{\"KHR_materials_clearcoat\":{}," +
        "\"KHR_materials_sheen\":{\"sheenColorFactor\":[0.2,0.4,0.6],\"sheenRoughnessFactor\":0.75}," +
        "\"KHR_materials_iridescence\":{\"iridescenceFactor\":0.8," +
        "\"iridescenceIor\":1.4,\"iridescenceThicknessMinimum\":125," +
        "\"iridescenceThicknessMaximum\":625}," +
        "\"KHR_materials_specular\":{\"specularFactor\":0.65," +
        "\"specularColorFactor\":[1.5,0.75,0.25]}," +
        "\"KHR_materials_diffuse_transmission\":{\"diffuseTransmissionFactor\":0.4," +
        "\"diffuseTransmissionColorFactor\":[0.8,0.3,0.1]}," +
        "\"KHR_materials_transmission\":{\"transmissionFactor\":1}," +
        "\"KHR_materials_volume\":{\"thicknessFactor\":0.5}," +
        "\"KHR_materials_dispersion\":{\"dispersion\":0.35}," +
        "\"KHR_materials_emissive_strength\":{\"emissiveStrength\":5}},",
    StringComparison.Ordinal);
GltfAsset optionalMaterialAsset = GltfImporter.ImportAsset(
    Encoding.UTF8.GetBytes(optionalMaterialGltfJson));
PbrMaterial optionalMaterial = (PbrMaterial)optionalMaterialAsset.Scene
    .Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material;
Expect(
    optionalMaterial.EmissiveColor == new LinearRgba(
        0.1f,
        0.2f,
        0.3f,
        1f,
        StandardColorSpaces.LinearSrgb) &&
    optionalMaterial.EmissiveStrength == 5f &&
    optionalMaterial.ClearcoatFactor == 0f &&
    optionalMaterial.ClearcoatRoughness == 0f &&
    optionalMaterial.SheenColor == new LinearRgba(
        0.2f, 0.4f, 0.6f, 1f, StandardColorSpaces.LinearSrgb) &&
    optionalMaterial.SheenRoughness == 0.75f &&
    optionalMaterial.IridescenceFactor == 0.8f &&
    optionalMaterial.IridescenceIndexOfRefraction == 1.4f &&
    optionalMaterial.IridescenceThicknessMinimum == 125f &&
    optionalMaterial.IridescenceThicknessMaximum == 625f &&
    optionalMaterial.SpecularFactor == 0.65f &&
    optionalMaterial.SpecularColor == new LinearRgba(
        1.5f, 0.75f, 0.25f, 1f, StandardColorSpaces.LinearSrgb) &&
    optionalMaterial.DiffuseTransmissionFactor == 0.4f &&
    optionalMaterial.DiffuseTransmissionColor == new LinearRgba(
        0.8f, 0.3f, 0.1f, 1f, StandardColorSpaces.LinearSrgb) &&
    optionalMaterial.TransmissionFactor == 1f &&
    optionalMaterial.VolumeThicknessFactor == 0.5f &&
    optionalMaterial.Dispersion == 0.35f &&
    optionalMaterialAsset.IgnoredOptionalExtensions.Count == 0,
    "glTF emissive strength and default clearcoat extension import",
    failures);
GltfAsset requiredDispersionAsset = GltfImporter.ImportAsset(Encoding.UTF8.GetBytes(
    optionalMaterialGltfJson.Replace(
        "\"buffers\":",
        "\"extensionsUsed\":[\"KHR_materials_dispersion\"]," +
            "\"extensionsRequired\":[\"KHR_materials_dispersion\"],\"buffers\":",
        StringComparison.Ordinal)));
PbrMaterial requiredDispersionMaterial = (PbrMaterial)requiredDispersionAsset.Scene.Root
    .EnumerateDepthFirst().OfType<Mesh>().Single().Material;
Expect(
    requiredDispersionMaterial.Dispersion == 0.35f &&
    MaterialShaderVariant.FromMaterial(requiredDispersionMaterial).Extensions.HasFlag(
        PbrMaterialExtensions.Dispersion),
    "required glTF dispersion import and exact shader variant",
    failures);
string materialVariantsGltfJson = gltfJson
    .Replace(
        "\"asset\":{\"version\":\"2.0\"},",
        "\"asset\":{\"version\":\"2.0\"}," +
            "\"extensionsUsed\":[\"KHR_materials_variants\"]," +
            "\"extensionsRequired\":[\"KHR_materials_variants\"]," +
            "\"extensions\":{\"KHR_materials_variants\":{\"variants\":[" +
            "{\"name\":\"Green\"},{\"name\":\"Blue\"}]}},",
        StringComparison.Ordinal)
    .Replace(
        "\"materials\":[{",
        "\"materials\":[" +
            "{\"name\":\"green\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[0,1,0,1]}}," +
            "{\"name\":\"blue\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[0,0,1,1]}},{",
        StringComparison.Ordinal)
    .Replace(
        "\"indices\":1,\"material\":0}",
        "\"indices\":1,\"material\":2,\"extensions\":{" +
            "\"KHR_materials_variants\":{\"mappings\":[" +
            "{\"material\":0,\"variants\":[0]},{\"material\":1,\"variants\":[1]}]}}}",
        StringComparison.Ordinal);
GltfAsset materialVariantsAsset = GltfImporter.ImportAsset(
    Encoding.UTF8.GetBytes(materialVariantsGltfJson));
Mesh variantMesh = materialVariantsAsset.Scene.Root.EnumerateDepthFirst().OfType<Mesh>().Single();
Material defaultVariantMaterial = variantMesh.Material;
materialVariantsAsset.ApplyMaterialVariant("Blue");
bool appliedBlueVariant = variantMesh.Material.Name == "blue";
materialVariantsAsset.ApplyMaterialVariant(0);
bool appliedGreenVariant = variantMesh.Material.Name == "green";
materialVariantsAsset.ApplyMaterialVariant((int?)null);
Expect(
    materialVariantsAsset.MaterialVariants.Select(static variant => variant.Name)
        .SequenceEqual(["Green", "Blue"]) &&
    appliedBlueVariant && appliedGreenVariant &&
    ReferenceEquals(variantMesh.Material, defaultVariantMaterial) &&
    materialVariantsAsset.MaterialShaderVariants.Variants.Count == 2,
    "KHR_materials_variants import, runtime switch, fallback and shader prewarm manifest",
    failures);
materialVariantsAsset.ApplyMaterialVariant("Blue");
GltfSceneAsset reusableGltfAsset = materialVariantsAsset.CreateSceneAsset("variant products");
materialVariantsAsset.ApplyMaterialVariant((int?)null);
GltfSceneInstance firstGltfInstance = reusableGltfAsset.CreateInstance("first product");
GltfSceneInstance secondGltfInstance = reusableGltfAsset.CreateInstance("second product");
Mesh firstVariantMesh = firstGltfInstance.Root.EnumerateDepthFirst().OfType<Mesh>().Single();
Mesh secondVariantMesh = secondGltfInstance.Root.EnumerateDepthFirst().OfType<Mesh>().Single();
Material firstInstanceDefaultMaterial = firstVariantMesh.Material;
Material secondInstanceDefaultMaterial = secondVariantMesh.Material;
firstGltfInstance.ApplyMaterialVariant("Green");
bool firstInstanceAppliedGreen = firstVariantMesh.Material.Name == "green";
secondGltfInstance.ApplyMaterialVariant("Blue");
bool secondInstanceAppliedBlue = secondVariantMesh.Material.Name == "blue";
Expect(
    reusableGltfAsset.Name == "variant products" &&
    reusableGltfAsset.MaterialVariants.Count == 2 &&
    firstInstanceDefaultMaterial.Name == defaultVariantMaterial.Name &&
    secondInstanceDefaultMaterial.Name == defaultVariantMaterial.Name &&
    firstInstanceAppliedGreen && secondInstanceAppliedBlue &&
    firstGltfInstance.ActiveMaterialVariantIndex == 0 &&
    secondGltfInstance.ActiveMaterialVariantIndex == 1,
    "reusable glTF instances preserve metadata and independent variant selection",
    failures);
Expect(
    ReferenceEquals(firstVariantMesh.Geometry, secondVariantMesh.Geometry) &&
    ReferenceEquals(firstVariantMesh.Geometry, variantMesh.Geometry) &&
    !ReferenceEquals(firstVariantMesh, secondVariantMesh) &&
    !ReferenceEquals(firstVariantMesh.Material, secondVariantMesh.Material),
    "reusable glTF instances share immutable geometry and isolate mutable state",
    failures);
firstGltfInstance.ApplyMaterialVariant((int?)null);
Expect(
    ReferenceEquals(firstVariantMesh.Material, firstInstanceDefaultMaterial) &&
    secondVariantMesh.Material.Name == "blue",
    "reusable glTF instance restores its own defaults without changing siblings",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => optionalMaterial.EmissiveStrength = -1f,
    "negative material emissive strength rejection",
    failures);
ExpectThrows<InvalidDataException>(
    () => GltfImporter.ImportAsset(Encoding.UTF8.GetBytes(optionalMaterialGltfJson.Replace(
        "\"emissiveStrength\":5",
        "\"emissiveStrength\":-1",
        StringComparison.Ordinal))),
    "negative glTF emissive strength rejection",
    failures);
GltfAsset requiredEmissiveStrengthAsset = GltfImporter.ImportAsset(Encoding.UTF8.GetBytes(
    optionalMaterialGltfJson.Replace(
        "\"buffers\":",
        "\"extensionsUsed\":[\"KHR_materials_emissive_strength\"]," +
            "\"extensionsRequired\":[\"KHR_materials_emissive_strength\"],\"buffers\":",
        StringComparison.Ordinal)));
Expect(
    ((PbrMaterial)requiredEmissiveStrengthAsset.Scene.Root
        .EnumerateDepthFirst().OfType<Mesh>().Single().Material).EmissiveStrength == 5f,
    "required glTF emissive strength support",
    failures);
GltfAsset requiredClearcoatAsset = GltfImporter.ImportAsset(Encoding.UTF8.GetBytes(
    optionalMaterialGltfJson.Replace(
        "\"buffers\":",
        "\"extensionsUsed\":[\"KHR_materials_clearcoat\"]," +
            "\"extensionsRequired\":[\"KHR_materials_clearcoat\"],\"buffers\":",
        StringComparison.Ordinal)));
Expect(
    requiredClearcoatAsset.IgnoredOptionalExtensions.Count == 0,
    "required glTF clearcoat support",
    failures);
GltfAsset requiredSheenAsset = GltfImporter.ImportAsset(Encoding.UTF8.GetBytes(
    optionalMaterialGltfJson.Replace(
        "\"buffers\":",
        "\"extensionsUsed\":[\"KHR_materials_sheen\"]," +
            "\"extensionsRequired\":[\"KHR_materials_sheen\"],\"buffers\":",
        StringComparison.Ordinal)));
Expect(
    ((PbrMaterial)requiredSheenAsset.Scene.Root
        .EnumerateDepthFirst().OfType<Mesh>().Single().Material).SheenRoughness == 0.75f,
    "required glTF sheen support",
    failures);
GltfAsset requiredIridescenceAsset = GltfImporter.ImportAsset(Encoding.UTF8.GetBytes(
    optionalMaterialGltfJson.Replace(
        "\"buffers\":",
        "\"extensionsUsed\":[\"KHR_materials_iridescence\"]," +
            "\"extensionsRequired\":[\"KHR_materials_iridescence\"],\"buffers\":",
        StringComparison.Ordinal)));
Expect(
    ((PbrMaterial)requiredIridescenceAsset.Scene.Root
        .EnumerateDepthFirst().OfType<Mesh>().Single().Material).IridescenceFactor == 0.8f,
    "required glTF iridescence support",
    failures);
GltfAsset requiredSpecularAsset = GltfImporter.ImportAsset(Encoding.UTF8.GetBytes(
    optionalMaterialGltfJson.Replace(
        "\"buffers\":",
        "\"extensionsUsed\":[\"KHR_materials_specular\"]," +
            "\"extensionsRequired\":[\"KHR_materials_specular\"],\"buffers\":",
        StringComparison.Ordinal)));
PbrMaterial requiredSpecularMaterial = (PbrMaterial)requiredSpecularAsset.Scene.Root
    .EnumerateDepthFirst().OfType<Mesh>().Single().Material;
Expect(
    requiredSpecularMaterial.SpecularFactor == 0.65f &&
    requiredSpecularMaterial.SpecularColor.Red == 1.5f,
    "required glTF specular support and unclamped color factor",
    failures);
GltfAsset requiredDiffuseTransmissionAsset = GltfImporter.ImportAsset(Encoding.UTF8.GetBytes(
    optionalMaterialGltfJson.Replace(
        "\"buffers\":",
        "\"extensionsUsed\":[\"KHR_materials_diffuse_transmission\"]," +
            "\"extensionsRequired\":[\"KHR_materials_diffuse_transmission\"],\"buffers\":",
        StringComparison.Ordinal)));
PbrMaterial requiredDiffuseTransmissionMaterial = (PbrMaterial)requiredDiffuseTransmissionAsset.Scene.Root
    .EnumerateDepthFirst().OfType<Mesh>().Single().Material;
Expect(
    requiredDiffuseTransmissionMaterial.DiffuseTransmissionFactor == 0.4f &&
    requiredDiffuseTransmissionMaterial.DiffuseTransmissionColor.Green == 0.3f,
    "required glTF diffuse transmission support",
    failures);
string punctualLightGltfJson = gltfJson
    .Replace(
        "\"asset\":{\"version\":\"2.0\"},",
        "\"asset\":{\"version\":\"2.0\"}," +
            "\"extensionsUsed\":[\"KHR_lights_punctual\"]," +
            "\"extensionsRequired\":[\"KHR_lights_punctual\"]," +
            "\"extensions\":{\"KHR_lights_punctual\":{\"lights\":[{" +
            "\"name\":\"warm point\",\"type\":\"point\"," +
            "\"color\":[1,0.5,0.25],\"intensity\":12,\"range\":4}]}},",
        StringComparison.Ordinal)
    .Replace(
        "\"name\":\"imported node\",\"mesh\":0,",
        "\"name\":\"imported node\",\"mesh\":0," +
            "\"extensions\":{\"KHR_lights_punctual\":{\"light\":0}},",
        StringComparison.Ordinal);
GltfAsset punctualLightAsset = GltfImporter.ImportAsset(
    Encoding.UTF8.GetBytes(punctualLightGltfJson));
PointLight importedPointLight = punctualLightAsset.Scene.Root
    .EnumerateDepthFirst().OfType<PointLight>().Single();
Expect(
    importedPointLight.Name == "warm point" &&
    importedPointLight.Intensity == 12f && importedPointLight.Range == 4f &&
    importedPointLight.Color == new LinearRgba(
        1f, 0.5f, 0.25f, 1f, StandardColorSpaces.LinearSrgb) &&
    importedPointLight.WorldPosition == new Vector3(1f, 2f, 3f) &&
    punctualLightAsset.IgnoredOptionalExtensions.Count == 0,
    "required glTF punctual point-light import and node transform inheritance",
    failures);
byte[] tangentGltfBuffer = new byte[90];
MemoryMarshal.AsBytes(gltfPositionValues.AsSpan()).CopyTo(tangentGltfBuffer);
MemoryMarshal.AsBytes(authoredTangents.AsSpan()).CopyTo(tangentGltfBuffer.AsSpan(36));
BinaryPrimitives.WriteUInt16LittleEndian(tangentGltfBuffer.AsSpan(84), 0);
BinaryPrimitives.WriteUInt16LittleEndian(tangentGltfBuffer.AsSpan(86), 1);
BinaryPrimitives.WriteUInt16LittleEndian(tangentGltfBuffer.AsSpan(88), 2);
string tangentGltfDataUri = Convert.ToBase64String(tangentGltfBuffer);
string tangentGltfJson = $$"""
    {
      "asset":{"version":"2.0"},
      "buffers":[{"byteLength":90,"uri":"data:application/octet-stream;base64,{{tangentGltfDataUri}}"}],
      "bufferViews":[
        {"buffer":0,"byteOffset":0,"byteLength":36},
        {"buffer":0,"byteOffset":36,"byteLength":48},
        {"buffer":0,"byteOffset":84,"byteLength":6}
      ],
      "accessors":[
        {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
        {"bufferView":1,"componentType":5126,"count":3,"type":"VEC4"},
        {"bufferView":2,"componentType":5123,"count":3,"type":"SCALAR"}
      ],
      "meshes":[{"primitives":[{"attributes":{"POSITION":0,"TANGENT":1},"indices":2}]}],
      "nodes":[{"mesh":0}],
      "scenes":[{"nodes":[0]}],
      "scene":0
    }
    """;
Mesh importedTangentMesh = GltfImporter.Import(Encoding.UTF8.GetBytes(tangentGltfJson))
    .Root.EnumerateDepthFirst().OfType<Mesh>().Single();
Expect(
    importedTangentMesh.Geometry.Tangents.SequenceEqual(authoredTangents),
    "glTF authored tangent and handedness import",
    failures);
byte[] glbJsonBytes = Encoding.UTF8.GetBytes(gltfJson.Replace(
    $",\"uri\":\"data:application/octet-stream;base64,{gltfDataUri}\"",
    string.Empty,
    StringComparison.Ordinal));
Scene importedGlb = GltfImporter.Import(CreateGlb(glbJsonBytes, gltfBuffer), name: "GLB override");
Expect(
    importedGlb.Name == "GLB override" && importedGlb.Root.EnumerateDepthFirst().OfType<Mesh>().Count() == 1,
    "GLB container import",
    failures);
byte[] cubicGltfBuffer = new byte[124];
gltfBuffer.CopyTo(cubicGltfBuffer, 0);
MemoryMarshal.AsBytes(new float[] { 0f, 2f }.AsSpan()).CopyTo(cubicGltfBuffer.AsSpan(44));
Vector3[] cubicTranslationTriplets =
[
    Vector3.Zero,
    Vector3.Zero,
    new Vector3(2, 0, 0),
    Vector3.Zero,
    new Vector3(2, 0, 0),
    Vector3.Zero,
];
MemoryMarshal.AsBytes(cubicTranslationTriplets.AsSpan()).CopyTo(cubicGltfBuffer.AsSpan(52));
string cubicGltfDataUri = Convert.ToBase64String(cubicGltfBuffer);
string cubicGltfJson = $$$"""
    {
      "asset":{"version":"2.0"},
      "buffers":[{"byteLength":124,"uri":"data:application/octet-stream;base64,{{{cubicGltfDataUri}}}"}],
      "bufferViews":[
        {"buffer":0,"byteOffset":0,"byteLength":36},
        {"buffer":0,"byteOffset":36,"byteLength":6},
        {"buffer":0,"byteOffset":44,"byteLength":8},
        {"buffer":0,"byteOffset":52,"byteLength":72}
      ],
      "accessors":[
        {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
        {"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"},
        {"bufferView":2,"componentType":5126,"count":2,"type":"SCALAR"},
        {"bufferView":3,"componentType":5126,"count":6,"type":"VEC3"}
      ],
      "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],
      "nodes":[{"name":"cubic animated node","mesh":0}],
      "scenes":[{"nodes":[0]}],
      "scene":0,
      "animations":[{
        "samplers":[{"input":2,"output":3,"interpolation":"CUBICSPLINE"}],
        "channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]
      }]
    }
    """;
GltfAsset cubicGltfAsset = GltfImporter.ImportAsset(Encoding.UTF8.GetBytes(cubicGltfJson));
Vector3AnimationTrack importedCubicTrack =
    (Vector3AnimationTrack)cubicGltfAsset.Animations.Single().Tracks.Single();
cubicGltfAsset.Animations[0].Apply(1f);
Expect(
    importedCubicTrack.Interpolation == AnimationInterpolation.CubicSpline &&
    importedCubicTrack.InTangents is not null &&
    importedCubicTrack.OutTangents is not null,
    "glTF cubic spline triplet import",
    failures);
ExpectNear(
    importedCubicTrack.Target.Transform.Position.X,
    1.5f,
    0.00001f,
    "glTF cubic spline interval scaling",
    failures);
GltfSceneAsset reusableAnimatedGltf = cubicGltfAsset.CreateSceneAsset("animated products");
GltfSceneInstance firstAnimatedGltf = reusableAnimatedGltf.CreateInstance("first animation");
GltfSceneInstance secondAnimatedGltf = reusableAnimatedGltf.CreateInstance("second animation");
SceneNode firstAnimatedTarget = firstAnimatedGltf.Animations.Single().Tracks.Single().Target;
SceneNode secondAnimatedTarget = secondAnimatedGltf.Animations.Single().Tracks.Single().Target;
firstAnimatedGltf.Animations[0].Apply(0f);
Expect(
    !ReferenceEquals(firstAnimatedTarget, secondAnimatedTarget) &&
    firstAnimatedTarget.Transform.Position == Vector3.Zero &&
    MathF.Abs(secondAnimatedTarget.Transform.Position.X - 1.5f) < 0.00001f,
    "reusable glTF animation targets remain independent between instances",
    failures);
ExpectThrows<NotSupportedException>(
    () => GltfImporter.Import(Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"1.0\"}}")),
    "glTF version validation",
    failures);
string texturedGltfJson = gltfJson.Replace(
    "\"metallicFactor\":0.25",
    "\"baseColorTexture\":{\"index\":0},\"metallicFactor\":0.25",
    StringComparison.Ordinal);
ExpectThrows<InvalidDataException>(
    () => GltfImporter.Import(Encoding.UTF8.GetBytes(texturedGltfJson)),
    "glTF malformed texture reference fails closed",
    failures);
ExpectThrows<NotSupportedException>(
    () => GltfImporter.Import(Encoding.UTF8.GetBytes(
        "{\"asset\":{\"version\":\"2.0\"},\"extensionsRequired\":[\"KHR_unknown\"]}")),
    "unknown required glTF extension fails closed",
    failures);
string basisGltfJson = """
    {
      "asset":{"version":"2.0"},
      "extensionsUsed":["KHR_texture_basisu"],
      "extensionsRequired":["KHR_texture_basisu"],
      "buffers":[{"byteLength":42,"uri":"data:application/octet-stream;base64,__BUFFER__"}],
      "bufferViews":[
        {"buffer":0,"byteOffset":0,"byteLength":36},
        {"buffer":0,"byteOffset":36,"byteLength":6}
      ],
      "accessors":[
        {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
        {"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}
      ],
      "images":[{"uri":"data:image/ktx2;base64,AA=="}],
      "textures":[{"extensions":{"KHR_texture_basisu":{"source":0}}}],
      "materials":[{
        "pbrMetallicRoughness":{"baseColorTexture":{"index":0}},
        "emissiveFactor":[0.2,0.3,0.4],
        "emissiveTexture":{"index":0},
        "normalTexture":{"index":0}
      }],
      "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1,"material":0}]}],
      "nodes":[{"mesh":0}],
      "scenes":[{"nodes":[0]}],
      "scene":0
    }
    """.Replace("__BUFFER__", gltfDataUri, StringComparison.Ordinal);
ExpectThrows<NotSupportedException>(
    () => GltfImporter.Import(Encoding.UTF8.GetBytes(basisGltfJson)),
    "KTX2 glTF requires an injected decoder",
    failures);
ExpectThrows<InvalidDataException>(
    () => GltfImporter.Import(
        Encoding.UTF8.GetBytes(basisGltfJson.Replace(
            "image/ktx2",
            "image/png",
            StringComparison.Ordinal)),
        imageDecoder: new TestEncodedImageDecoder()),
    "KHR_texture_basisu requires an image/ktx2 source",
    failures);
TestEncodedImageDecoder basisDecoder = new();
PbrMaterial basisMaterial = (PbrMaterial)GltfImporter.Import(
    Encoding.UTF8.GetBytes(basisGltfJson),
    imageDecoder: basisDecoder).Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material;
Expect(
    basisMaterial.BaseColorTexture is not null && basisMaterial.EmissiveTexture is not null &&
    basisMaterial.NormalTexture is not null &&
    basisDecoder.ColorDecodeCount == 1 && basisDecoder.DataDecodeCount == 1 &&
    basisDecoder.LastColorMimeType == "image/ktx2" && basisDecoder.LastDataMimeType == "image/ktx2",
    "KHR_texture_basisu delegates KTX2 color and data decoding",
    failures);
string transformedBasisGltfJson = basisGltfJson
    .Replace(
        "\"extensionsUsed\":[\"KHR_texture_basisu\"]",
        "\"extensionsUsed\":[\"KHR_texture_basisu\",\"KHR_texture_transform\"]",
        StringComparison.Ordinal)
    .Replace(
        "\"extensionsRequired\":[\"KHR_texture_basisu\"]",
        "\"extensionsRequired\":[\"KHR_texture_basisu\",\"KHR_texture_transform\"]",
        StringComparison.Ordinal)
    .Replace(
        "\"textures\":[{\"extensions\":{\"KHR_texture_basisu\":{\"source\":0}}}]",
        "\"samplers\":[{\"wrapS\":33071,\"wrapT\":10497,\"magFilter\":9728,\"minFilter\":9984}," +
            "{\"wrapS\":33648,\"wrapT\":33071,\"magFilter\":9729}]," +
            "\"textures\":[" +
            "{\"sampler\":0,\"extensions\":{\"KHR_texture_basisu\":{\"source\":0}}}," +
            "{\"sampler\":1,\"extensions\":{\"KHR_texture_basisu\":{\"source\":0}}}]",
        StringComparison.Ordinal)
    .Replace(
        "\"baseColorTexture\":{\"index\":0}",
        "\"baseColorTexture\":{\"index\":0,\"extensions\":{" +
            "\"KHR_texture_transform\":{\"offset\":[0.25,-0.5]," +
            "\"scale\":[2,3],\"rotation\":0.5,\"texCoord\":1}}}",
        StringComparison.Ordinal)
    .Replace(
        "\"emissiveTexture\":{\"index\":0}",
        "\"emissiveTexture\":{\"index\":1,\"extensions\":{" +
            "\"KHR_texture_transform\":{\"offset\":[-0.2,0.1]," +
            "\"rotation\":0.25,\"texCoord\":1}}}",
        StringComparison.Ordinal)
    .Replace("\"normalTexture\":{\"index\":0}", "\"normalTexture\":{\"index\":1}", StringComparison.Ordinal);
PbrMaterial transformedBasisMaterial = (PbrMaterial)GltfImporter.Import(
    Encoding.UTF8.GetBytes(transformedBasisGltfJson),
    imageDecoder: new TestEncodedImageDecoder()).Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material;
Expect(
    transformedBasisMaterial.BaseColorTextureMapping.TextureCoordinateSet == 1 &&
    transformedBasisMaterial.BaseColorTextureMapping.Scale == new Vector2(2f, 3f) &&
    transformedBasisMaterial.BaseColorTextureMapping.Offset == new Vector2(0.25f, -0.5f) &&
    transformedBasisMaterial.BaseColorTextureMapping.Rotation == 0.5f &&
    transformedBasisMaterial.BaseColorTextureMapping.Sampling.AddressModeU ==
        MaterialTextureAddressMode.ClampToEdge &&
    transformedBasisMaterial.BaseColorTextureMapping.Sampling.Filter ==
        MaterialTextureFilter.Nearest &&
    transformedBasisMaterial.EmissiveTexture is not null &&
    transformedBasisMaterial.EmissiveTextureMapping.TextureCoordinateSet == 1 &&
    transformedBasisMaterial.EmissiveTextureMapping.Offset == new Vector2(-0.2f, 0.1f) &&
    transformedBasisMaterial.EmissiveTextureMapping.Rotation == 0.25f &&
    transformedBasisMaterial.EmissiveTextureMapping.Sampling.AddressModeU ==
        MaterialTextureAddressMode.MirrorRepeat &&
    transformedBasisMaterial.NormalTextureMapping.Sampling.AddressModeU ==
        MaterialTextureAddressMode.MirrorRepeat &&
    transformedBasisMaterial.NormalTextureMapping.Sampling.Filter ==
        MaterialTextureFilter.Linear,
    "glTF per-slot samplers and KHR_texture_transform import",
    failures);
string externalBasisGltfJson = basisGltfJson.Replace(
    "data:image/ktx2;base64,AA==",
    "textures/material.ktx2?revision=1#image",
    StringComparison.Ordinal);
ExpectThrows<InvalidDataException>(
    () => GltfImporter.Import(
        Encoding.UTF8.GetBytes(externalBasisGltfJson),
        imageDecoder: new TestEncodedImageDecoder()),
    "external glTF image requires application resolver",
    failures);
string? resolvedImageUri = null;
TestEncodedImageDecoder externalBasisDecoder = new();
PbrMaterial externalBasisMaterial = (PbrMaterial)
    GltfImporter.ImportWithOptions(
        Encoding.UTF8.GetBytes(externalBasisGltfJson),
        new GltfImportOptions
        {
            ExternalImageResolver = uri =>
            {
                resolvedImageUri = uri;
                return new byte[] { 0xAB };
            },
            ImageDecoder = externalBasisDecoder,
        }).Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material;
Expect(
    resolvedImageUri == "textures/material.ktx2?revision=1#image" &&
    externalBasisMaterial.BaseColorTexture is not null &&
    externalBasisMaterial.NormalTexture is not null &&
    externalBasisDecoder.LastColorMimeType == "image/ktx2" &&
    externalBasisDecoder.LastDataMimeType == "image/ktx2",
    "external KTX2 URI uses application resolver and extension MIME inference",
    failures);
GraphicsCapabilities compressedTextureCapabilities = new()
{
    SupportsFloat16Textures = true,
    SupportsShaderFloat16 = true,
    SupportsHdrSurface = true,
    SupportsBcTextureCompression = true,
    SupportsEtc2TextureCompression = false,
    SupportsAstcTextureCompression = true,
    SurfaceFormats = [PresentationFormat.Rgba16Float],
};
TestEncodedImageDecoder unusedBasisDecoder = new();
TestEncodedTextureTranscoder basisTranscoder = new();
PbrMaterial compressedBasisMaterial = (PbrMaterial)
    GltfImporter.ImportWithOptions(
        Encoding.UTF8.GetBytes(basisGltfJson),
        new GltfImportOptions
        {
            ImageDecoder = unusedBasisDecoder,
            TextureTranscoder = basisTranscoder,
            GraphicsCapabilities = compressedTextureCapabilities,
        }).Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material;
Expect(
    compressedBasisMaterial.BaseColorTexture is null &&
    compressedBasisMaterial.CompressedBaseColorTexture?.Format ==
        GraphicsTextureFormat.Bc7RgbaUnormSrgb &&
    compressedBasisMaterial.NormalTexture is null &&
    compressedBasisMaterial.CompressedNormalTexture?.Format ==
        GraphicsTextureFormat.Astc4x4Unorm &&
    basisTranscoder.ColorTranscodeCount == 1 && basisTranscoder.DataTranscodeCount == 1 &&
    unusedBasisDecoder.ColorDecodeCount == 0 && unusedBasisDecoder.DataDecodeCount == 0,
    "KHR_texture_basisu selects compressed targets from explicit device capabilities",
    failures);
string basisOrmGltfJson = basisGltfJson
    .Replace("\"baseColorTexture\"", "\"metallicRoughnessTexture\"", StringComparison.Ordinal)
    .Replace("\"normalTexture\"", "\"occlusionTexture\"", StringComparison.Ordinal);
TestEncodedTextureTranscoder ormTranscoder = new();
PbrMaterial compressedOrmMaterial = (PbrMaterial)
    GltfImporter.ImportWithOptions(
        Encoding.UTF8.GetBytes(basisOrmGltfJson),
        new GltfImportOptions
        {
            TextureTranscoder = ormTranscoder,
            GraphicsCapabilities = compressedTextureCapabilities,
        }).Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material;
Expect(
    compressedOrmMaterial.OcclusionRoughnessMetallicTexture is null &&
    compressedOrmMaterial.CompressedOcclusionRoughnessMetallicTexture?.Format ==
        GraphicsTextureFormat.Astc4x4Unorm &&
    ormTranscoder.DataTranscodeCount == 1,
    "shared glTF occlusion and metallic-roughness KTX2 preserves compressed ORM",
    failures);
const int synthesizedOrmSide = 128;
NormalizedRgbaDataImage ormSourceData = new(synthesizedOrmSide, synthesizedOrmSide,
    Enumerable.Repeat(new Vector4(.5f, .5f, 1f, 1f), synthesizedOrmSide * synthesizedOrmSide));
TestEncodedImageDecoder synthesizedOrmDecoder = new() { DataImage = ormSourceData };
byte[] synthesizedOrmSource = Encoding.UTF8.GetBytes(basisOrmGltfJson.Replace(
    "\"occlusionTexture\":{\"index\":0}",
    "\"occlusionTexture\":{\"index\":0,\"strength\":0.25}", StringComparison.Ordinal));
long beforeOrmImport = GC.GetAllocatedBytesForCurrentThread();
PbrMaterial synthesizedOrmMaterial = (PbrMaterial)GltfImporter.Import(
    synthesizedOrmSource, imageDecoder: synthesizedOrmDecoder)
    .Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material;
long ormImportAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeOrmImport;
Expect(synthesizedOrmMaterial.OcclusionRoughnessMetallicTexture is { } synthesizedOrm &&
    synthesizedOrm.Width == synthesizedOrmSide && synthesizedOrm.Height == synthesizedOrmSide &&
    synthesizedOrm.Texels.All(static texel => texel == new Vector4(.875f, .5f, 1f, 1f)) &&
    !ReferenceEquals(synthesizedOrm, ormSourceData) && synthesizedOrmDecoder.DataDecodeCount == 1 &&
    ormSourceData.Texels[0] == new Vector4(.5f, .5f, 1f, 1f),
    "glTF ORM strength packing preserves normalized channels and immutable decoded input", failures);
Expect(ormImportAllocation < synthesizedOrmSide * synthesizedOrmSide * 16L + 65536,
    "glTF ORM packing allocation excludes a second FP32 image", failures);
TestEncodedImageDecoder fallbackBasisDecoder = new();
PbrMaterial fallbackBasisMaterial = (PbrMaterial)
    GltfImporter.ImportWithOptions(
        Encoding.UTF8.GetBytes(basisGltfJson),
        new GltfImportOptions
        {
            ImageDecoder = fallbackBasisDecoder,
            TextureTranscoder = new TestEncodedTextureTranscoder { ReturnNull = true },
            GraphicsCapabilities = compressedTextureCapabilities,
        }).Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material;
Expect(
    fallbackBasisMaterial.BaseColorTexture is not null &&
    fallbackBasisMaterial.NormalTexture is not null &&
    fallbackBasisMaterial.CompressedBaseColorTexture is null &&
    fallbackBasisMaterial.CompressedNormalTexture is null &&
    fallbackBasisDecoder.ColorDecodeCount == 1 && fallbackBasisDecoder.DataDecodeCount == 1,
    "KTX2 transcoder null result preserves decoded RGBA fallback",
    failures);
ExpectThrows<ArgumentException>(
    () => GltfImporter.ImportWithOptions(
        Encoding.UTF8.GetBytes(basisGltfJson),
        new GltfImportOptions { TextureTranscoder = new TestEncodedTextureTranscoder() }),
    "KTX2 transcoder requires explicit device capabilities",
    failures);
ExpectThrows<InvalidDataException>(
    () => GltfImporter.ImportWithOptions(
        Encoding.UTF8.GetBytes(basisGltfJson),
        new GltfImportOptions
        {
            TextureTranscoder = new TestEncodedTextureTranscoder { ReturnWrongContent = true },
            GraphicsCapabilities = compressedTextureCapabilities,
        }),
    "KTX2 transcoder wrong content semantic rejected",
    failures);
ExpectThrows<InvalidDataException>(
    () => GltfImporter.ImportWithOptions(
        Encoding.UTF8.GetBytes(basisGltfJson),
        new GltfImportOptions
        {
            TextureTranscoder = new TestEncodedTextureTranscoder(),
            GraphicsCapabilities = compressedTextureCapabilities with
            {
                SupportsBcTextureCompression = false,
                SupportsAstcTextureCompression = false,
            },
        }),
    "KTX2 transcoder unsupported destination format rejected",
    failures);
byte[] officialBoxBytes = File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "BoxInterleaved.glb"));
Scene officialBox = GltfImporter.Import(officialBoxBytes, name: "Khronos BoxInterleaved");
Mesh officialBoxMesh = officialBox.Root.EnumerateDepthFirst().OfType<Mesh>().Single();
Expect(
    officialBoxMesh.Geometry.Positions.Count == 24 &&
    officialBoxMesh.Geometry.Normals.Count == 24 &&
    officialBoxMesh.Geometry.Indices.Count == 36,
    "official Khronos interleaved GLB import",
    failures);
string officialTextureTransformDirectory = Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "TextureTransformTest");
byte[] officialTextureTransformBytes = File.ReadAllBytes(Path.Combine(
    officialTextureTransformDirectory,
    "TextureTransformTest.gltf"));
GltfAsset officialTextureTransform = GltfImporter.ImportAssetWithOptions(
    officialTextureTransformBytes,
    new GltfImportOptions
    {
        ExternalBufferResolver = uri => File.ReadAllBytes(Path.Combine(
            officialTextureTransformDirectory,
            uri)),
        ExternalImageResolver = uri => File.ReadAllBytes(Path.Combine(
            officialTextureTransformDirectory,
            uri)),
    });
int asyncExternalResourceOpenCount = 0;
using FileStream asyncExternalGltfStream = File.OpenRead(Path.Combine(
    officialTextureTransformDirectory,
    "TextureTransformTest.gltf"));
GltfAsset asyncExternalGltf = await GltfAssetLoader.LoadAsync(
    asyncExternalGltfStream,
    new GltfAssetLoadOptions
    {
        MaximumSourceByteCount = 16 * 1024,
        MaximumExternalResourceByteCount = 16 * 1024,
        MaximumTotalExternalResourceByteCount = 32 * 1024,
        ExternalResourceResolver = (uri, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            asyncExternalResourceOpenCount++;
            return ValueTask.FromResult<Stream>(File.OpenRead(Path.Combine(
                officialTextureTransformDirectory,
                uri)));
        },
        ImportOptions = new GltfImportOptions { Name = "async external glTF" },
    });
Expect(
    asyncExternalGltf.Scene.Name == "async external glTF" &&
    asyncExternalGltf.Scene.Root.EnumerateDepthFirst().OfType<Mesh>().Count() == 12 &&
    asyncExternalResourceOpenCount == 6 && asyncExternalGltfStream.CanRead &&
    asyncExternalGltf.SourceArchive is null,
    "bounded async external glTF resolution, stream ownership and default no-source-retention",
    failures);
GltfExternalResourceCache ownershipCache = new(4);
byte[] mutableCacheSource = [1, 2, 3];
bool ownershipCacheAdded = ownershipCache.TryAdd("resource.bin", mutableCacheSource);
mutableCacheSource[0] = 9;
bool ownershipCacheHit = ownershipCache.TryGet(
    "resource.bin",
    out ReadOnlyMemory<byte> ownedCacheSource);
bool ownershipCacheOverflowRejected = !ownershipCache.TryAdd("other.bin", new byte[] { 4, 5 });
ownershipCache.Clear();
Expect(
    ownershipCacheAdded && ownershipCacheHit &&
    ownedCacheSource.Span.SequenceEqual(new byte[] { 1, 2, 3 }) &&
    ownershipCacheOverflowRejected && ownershipCache.Count == 0 &&
    ownershipCache.TotalByteCount == 0 &&
    ownedCacheSource.Span.SequenceEqual(new byte[] { 1, 2, 3 }),
    "external glTF cache owns bounded copies that survive explicit clearing",
    failures);
GltfExternalResourceCache sharedExternalCache = new(32 * 1024);
int firstCachedExternalOpenCount = 0;
using MemoryStream firstCachedExternalGltfStream = new(officialTextureTransformBytes);
GltfAsset retainedExternalGltf = await GltfAssetLoader.LoadAsync(
    firstCachedExternalGltfStream,
    new GltfAssetLoadOptions
    {
        MaximumSourceByteCount = 16 * 1024,
        MaximumExternalResourceByteCount = 16 * 1024,
        MaximumTotalExternalResourceByteCount = 32 * 1024,
        MaximumRetainedSourceByteCount = 64 * 1024,
        SourceRetention = GltfSourceRetentionMode.MainSourceAndExternalResources,
        ExternalResourceCache = sharedExternalCache,
        ExternalResourceResolver = (uri, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            firstCachedExternalOpenCount++;
            return ValueTask.FromResult<Stream>(File.OpenRead(Path.Combine(
                officialTextureTransformDirectory,
                uri)));
        },
    });
GltfSourceArchive? retainedArchive = retainedExternalGltf.SourceArchive;
Expect(
    firstCachedExternalOpenCount == 6 && sharedExternalCache.Count == 6 &&
    retainedArchive is not null &&
    retainedArchive.MainSource.Span.SequenceEqual(officialTextureTransformBytes) &&
    retainedArchive.ExternalResources.Count == 6 &&
    retainedArchive.TotalByteCount ==
        officialTextureTransformBytes.Length + sharedExternalCache.TotalByteCount,
    "external glTF cache fill and explicit encoded-source archive ownership",
    failures);
using MemoryStream cacheHitGltfStream = new(officialTextureTransformBytes);
GltfAsset cacheHitGltf = await GltfAssetLoader.LoadAsync(
    cacheHitGltfStream,
    new GltfAssetLoadOptions
    {
        MaximumSourceByteCount = 16 * 1024,
        MaximumExternalResourceByteCount = 16 * 1024,
        MaximumTotalExternalResourceByteCount = 32 * 1024,
        ExternalResourceCache = sharedExternalCache,
    });
Expect(
    cacheHitGltf.Scene.Root.EnumerateDepthFirst().OfType<Mesh>().Count() == 12 &&
    cacheHitGltf.SourceArchive is null,
    "fully cached external glTF load needs no resolver and retains no source by default",
    failures);
List<TrackingMemoryStream> retainedLimitResources = [];
using MemoryStream retainedLimitGltfStream = new(officialTextureTransformBytes);
await ExpectThrowsAsync<InvalidDataException>(
    () => GltfAssetLoader.LoadAsync(
        retainedLimitGltfStream,
        new GltfAssetLoadOptions
        {
            MaximumSourceByteCount = 16 * 1024,
            MaximumExternalResourceByteCount = 16 * 1024,
            MaximumTotalExternalResourceByteCount = 32 * 1024,
            MaximumRetainedSourceByteCount = officialTextureTransformBytes.Length,
            SourceRetention = GltfSourceRetentionMode.MainSourceAndExternalResources,
            ExternalResourceResolver = (uri, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                TrackingMemoryStream resource = new(File.ReadAllBytes(Path.Combine(
                    officialTextureTransformDirectory,
                    uri)));
                retainedLimitResources.Add(resource);
                return ValueTask.FromResult<Stream>(resource);
            },
        }),
    "retained external glTF archive byte limit",
    failures);
Expect(
    retainedLimitGltfStream.CanRead && retainedLimitResources.Count == 6 &&
    retainedLimitResources.All(static resource => resource.IsDisposed),
    "retained-source limit failure preserves main stream and disposes resolved streams",
    failures);
sharedExternalCache.Clear();
Expect(
    sharedExternalCache.Count == 0 && sharedExternalCache.TotalByteCount == 0 &&
    retainedArchive?.ExternalResources.Count == 6,
    "application clears borrowed glTF cache without changing the asset-owned archive",
    failures);
using MemoryStream coldCacheGltfStream = new(officialTextureTransformBytes);
await ExpectThrowsAsync<InvalidDataException>(
    () => GltfAssetLoader.LoadAsync(
        coldCacheGltfStream,
        new GltfAssetLoadOptions
        {
            MaximumSourceByteCount = 16 * 1024,
            MaximumExternalResourceByteCount = 16 * 1024,
            MaximumTotalExternalResourceByteCount = 32 * 1024,
            ExternalResourceCache = sharedExternalCache,
        }),
    "cold external glTF cache requires an application resolver",
    failures);
Expect(
    coldCacheGltfStream.CanRead,
    "cold-cache failure leaves the main glTF stream open",
    failures);
byte[] duplicateExternalUriGltf = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(
    officialTextureTransformDirectory,
    "TextureTransformTest.gltf")).Replace(
        "\"uri\": \"Arrow.png\"",
        "\"uri\": \"UV.png\"",
        StringComparison.Ordinal));
int duplicateExternalResourceOpenCount = 0;
using MemoryStream duplicateExternalUriStream = new(duplicateExternalUriGltf);
_ = await GltfAssetLoader.LoadAsync(
    duplicateExternalUriStream,
    new GltfAssetLoadOptions
    {
        MaximumSourceByteCount = 16 * 1024,
        MaximumExternalResourceByteCount = 16 * 1024,
        MaximumTotalExternalResourceByteCount = 32 * 1024,
        ExternalResourceResolver = (uri, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            duplicateExternalResourceOpenCount++;
            return ValueTask.FromResult<Stream>(File.OpenRead(Path.Combine(
                officialTextureTransformDirectory,
                uri)));
        },
    });
Expect(
    duplicateExternalResourceOpenCount == 5,
    "async external glTF exact-URI resolution is deduplicated",
    failures);
TrackingMemoryStream? aggregateLimitedResource = null;
using FileStream aggregateLimitedGltfStream = File.OpenRead(Path.Combine(
    officialTextureTransformDirectory,
    "TextureTransformTest.gltf"));
await ExpectThrowsAsync<InvalidDataException>(
    () => GltfAssetLoader.LoadAsync(
        aggregateLimitedGltfStream,
        new GltfAssetLoadOptions
        {
            MaximumSourceByteCount = 16 * 1024,
            MaximumExternalResourceByteCount = 16 * 1024,
            MaximumTotalExternalResourceByteCount = 100,
            ExternalResourceResolver = (uri, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                aggregateLimitedResource = new TrackingMemoryStream(File.ReadAllBytes(Path.Combine(
                    officialTextureTransformDirectory,
                    uri)));
                return ValueTask.FromResult<Stream>(aggregateLimitedResource);
            },
        }),
    "async external glTF aggregate byte limit",
    failures);
Expect(
    aggregateLimitedGltfStream.CanRead && aggregateLimitedResource?.IsDisposed == true,
    "external glTF limit failure preserves main stream and disposes resolved stream",
    failures);
using CancellationTokenSource externalLoadCancellation = new();
CancellingReadStream? cancellingExternalResource = null;
using FileStream cancellingExternalGltfStream = File.OpenRead(Path.Combine(
    officialTextureTransformDirectory,
    "TextureTransformTest.gltf"));
await ExpectThrowsAsync<OperationCanceledException>(
    () => GltfAssetLoader.LoadAsync(
        cancellingExternalGltfStream,
        new GltfAssetLoadOptions
        {
            MaximumSourceByteCount = 16 * 1024,
            MaximumExternalResourceByteCount = 16 * 1024,
            MaximumTotalExternalResourceByteCount = 32 * 1024,
            ExternalResourceResolver = (uri, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                cancellingExternalResource = new CancellingReadStream(
                    File.ReadAllBytes(Path.Combine(officialTextureTransformDirectory, uri)),
                    externalLoadCancellation);
                return ValueTask.FromResult<Stream>(cancellingExternalResource);
            },
        },
        externalLoadCancellation.Token),
    "async external glTF resource-read cancellation",
    failures);
Expect(
    cancellingExternalGltfStream.CanRead && cancellingExternalResource?.IsDisposed == true,
    "external cancellation preserves main stream and disposes resolved stream",
    failures);
PbrMaterial[] officialTextureTransformMaterials = officialTextureTransform.Scene.Root
    .EnumerateDepthFirst()
    .OfType<Mesh>()
    .Select(static mesh => (PbrMaterial)mesh.Material)
    .Distinct()
    .ToArray();
Dictionary<string, PbrMaterial> officialTextureTransformByName =
    officialTextureTransformMaterials.ToDictionary(
        static material => material.Name!,
        StringComparer.Ordinal);
Expect(
    officialTextureTransformMaterials.Length == 9,
    "official Khronos texture-transform material count",
    failures);
Expect(
    officialTextureTransformByName["Offset U"].BaseColorTextureMapping.Offset ==
        new Vector2(0.5f, 0f) &&
    officialTextureTransformByName["Rotation"].BaseColorTextureMapping.Rotation > 0.3926f &&
    officialTextureTransformByName["Scale"].BaseColorTextureMapping.Scale == new Vector2(1.5f) &&
    officialTextureTransformByName["All"].BaseColorTextureMapping.Offset ==
        new Vector2(-0.2f, -0.1f),
    "official Khronos texture-transform values",
    failures);
Expect(
    new[] { "Offset U", "Offset V", "Offset UV", "Rotation", "Scale", "All" }
        .Select(name => officialTextureTransformByName[name].BaseColorTextureMapping.Sampling)
        .All(static sampling =>
            sampling.AddressModeU == MaterialTextureAddressMode.ClampToEdge &&
            !sampling.UseMipmaps),
    "official Khronos texture-transform sampler",
    failures);
GltfAsset officialTextureSettings = GltfImporter.ImportAsset(File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "TextureSettingsTest.glb")));
PbrMaterial[] officialTextureSettingsMaterials = officialTextureSettings.Scene.Root
    .EnumerateDepthFirst()
    .OfType<Mesh>()
    .Select(static mesh => (PbrMaterial)mesh.Material)
    .Distinct()
    .ToArray();
Expect(
    officialTextureSettingsMaterials.Select(static material =>
        material.BaseColorTextureMapping.Sampling.AddressModeU).Distinct().Count() == 3 &&
    officialTextureSettingsMaterials.Any(static material => material.IsDoubleSided) &&
    officialTextureSettingsMaterials.Any(static material => !material.IsDoubleSided),
    "official Khronos independent texture settings import",
    failures);
GltfAsset officialBoomBox = GltfImporter.ImportAsset(File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "BoomBox.glb")));
PbrMaterial officialBoomBoxMaterial = officialBoomBox.Scene.Root
    .EnumerateDepthFirst()
    .OfType<Mesh>()
    .Select(static mesh => mesh.Material)
    .OfType<PbrMaterial>()
    .Single();
Expect(
    officialBoomBoxMaterial.BaseColorTexture is not null &&
    officialBoomBoxMaterial.NormalTexture is not null &&
    officialBoomBoxMaterial.OcclusionRoughnessMetallicTexture is not null &&
    officialBoomBoxMaterial.EmissiveTexture is not null &&
    officialBoomBoxMaterial.EmissiveColor ==
        new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb),
    "official Khronos core emissive texture import",
    failures);
GltfAsset officialEmissiveStrengthAsset = GltfImporter.ImportAsset(File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "EmissiveStrengthTest.glb")));
float[] officialEmissiveStrengths = officialEmissiveStrengthAsset.Scene.Root
    .EnumerateDepthFirst()
    .OfType<Mesh>()
    .Select(static mesh => mesh.Material)
    .OfType<PbrMaterial>()
    .Where(static material => material.EmissiveColor.Blue > 0f)
    .Select(static material => material.EmissiveStrength)
    .Order()
    .ToArray();
Expect(
    officialEmissiveStrengths.SequenceEqual([1f, 2f, 4f, 8f, 16f]) &&
    officialEmissiveStrengthAsset.IgnoredOptionalExtensions.Count == 0,
    "official Khronos isolated emissive-strength progression import",
    failures);
GltfAsset officialSheenAsset = GltfImporter.ImportAsset(File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "SheenTestGrid.glb")));
PbrMaterial[] officialSheenMaterials = officialSheenAsset.Scene.Root
    .EnumerateDepthFirst()
    .OfType<Mesh>()
    .Select(static mesh => mesh.Material)
    .OfType<PbrMaterial>()
    .Distinct()
    .ToArray();
Expect(
    officialSheenMaterials.Length == 19 &&
    officialSheenMaterials.Count(static material => material.SheenColor.Blue > 0f) == 12 &&
    officialSheenMaterials.Select(static material => material.SheenRoughness).Distinct().Count() == 4 &&
    officialSheenAsset.IgnoredOptionalExtensions.Count == 0,
    "official Khronos sheen color/roughness grid import",
    failures);
GltfAsset officialDiffuseTransmissionPlant = GltfImporter.ImportAsset(File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "DiffuseTransmissionPlant.glb")),
    imageDecoder: new TestEncodedImageDecoder());
PointLight[] officialPlantLights = officialDiffuseTransmissionPlant.Scene.Root
    .EnumerateDepthFirst().OfType<PointLight>().ToArray();
Expect(
    officialPlantLights.Length > 0 &&
    officialPlantLights.All(static light => light.Intensity > 0f) &&
    officialDiffuseTransmissionPlant.IgnoredOptionalExtensions.Count == 0,
    "official diffuse-transmission plant punctual-light import",
    failures);
GltfAsset officialUnlitAsset = GltfImporter.ImportAsset(File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "UnlitTest.glb")));
UnlitMaterial[] officialUnlitMaterials = officialUnlitAsset.Scene.Root
    .EnumerateDepthFirst()
    .OfType<Mesh>()
    .Select(static mesh => mesh.Material)
    .OfType<UnlitMaterial>()
    .Distinct()
    .ToArray();
Expect(
    officialUnlitMaterials.Length == 2 &&
    officialUnlitMaterials.All(static material => material.BaseModel == MaterialBaseModel.Unlit) &&
    officialUnlitAsset.Scene.Root.EnumerateDepthFirst().OfType<Mesh>()
        .All(static mesh => mesh.Material is UnlitMaterial) &&
    officialUnlitAsset.IgnoredOptionalExtensions.Count == 0,
    "official Khronos required unlit materials import",
    failures);
GltfAsset officialTransmissionRoughnessAsset = GltfImporter.ImportAsset(
    File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "TransmissionRoughnessTest.glb")),
    imageDecoder: new TestEncodedImageDecoder());
PbrMaterial[] officialTransmissionRoughnessMaterials = officialTransmissionRoughnessAsset.Scene.Root
    .EnumerateDepthFirst()
    .OfType<Mesh>()
    .Select(static mesh => mesh.Material)
    .OfType<PbrMaterial>()
    .Distinct()
    .Where(static material => material.TransmissionFactor > 0f)
    .OrderBy(static material => material.IndexOfRefraction)
    .ToArray();
Expect(
    officialTransmissionRoughnessMaterials.Length == 5 &&
    officialTransmissionRoughnessMaterials.Select(static material => material.IndexOfRefraction)
        .SequenceEqual([1f, 1.33f, 1.5f, 1.76f, 2.42f]) &&
    officialTransmissionRoughnessMaterials.All(static material =>
        material.TransmissionFactor == 1f && material.VolumeThicknessFactor == 0.005f) &&
    officialTransmissionRoughnessAsset.IgnoredOptionalExtensions.Count == 0,
    "official Khronos transmission roughness and IOR progression import",
    failures);
GltfAsset officialClearcoatAsset = GltfImporter.ImportAsset(
    File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "ClearCoatTest.glb")),
    imageDecoder: new TestEncodedImageDecoder());
PbrMaterial[] officialClearcoatMaterials = officialClearcoatAsset.Scene.Root
    .EnumerateDepthFirst().OfType<Mesh>().Select(static mesh => mesh.Material)
    .OfType<PbrMaterial>().Distinct().ToArray();
Expect(
    officialClearcoatMaterials.Any(static material => material.ClearcoatFactor > 0f) &&
    officialClearcoatMaterials.Any(static material => material.ClearcoatTexture is not null) &&
    officialClearcoatMaterials.Any(static material => material.ClearcoatNormalTexture is not null) &&
    officialClearcoatAsset.IgnoredOptionalExtensions.Count == 0,
    "official Khronos required clearcoat factor, texture and independent-normal import",
    failures);
GltfAsset officialIorAsset = GltfImporter.ImportAsset(
    File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "IORTestGrid.glb")),
    imageDecoder: new TestEncodedImageDecoder());
float[] officialIors = officialIorAsset.Scene.Root.EnumerateDepthFirst().OfType<Mesh>()
    .Select(static mesh => mesh.Material).OfType<PbrMaterial>()
    .Select(static material => material.IndexOfRefraction).Distinct().Order().ToArray();
Expect(
    officialIors.Length > 3 && officialIors.First() == 1f && officialIors.Last() > 2f &&
    officialIorAsset.IgnoredOptionalExtensions.Count == 0,
    "official Khronos IOR grid imports the complete dielectric progression",
    failures);
GltfAsset officialTransmissionAsset = GltfImporter.ImportAsset(
    File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "TransmissionTest.glb")),
    imageDecoder: new TestEncodedImageDecoder());
PbrMaterial[] officialTransmissionMaterials = officialTransmissionAsset.Scene.Root
    .EnumerateDepthFirst().OfType<Mesh>().Select(static mesh => mesh.Material)
    .OfType<PbrMaterial>().Distinct().ToArray();
Expect(
    officialTransmissionMaterials.Any(static material => material.TransmissionFactor > 0f) &&
    officialTransmissionMaterials.Any(static material => material.TransmissionTexture is not null) &&
    officialTransmissionAsset.IgnoredOptionalExtensions.Count == 0,
    "official Khronos required transmission factor and texture import",
    failures);
GltfAsset officialVolumeAsset = GltfImporter.ImportAsset(
    File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory, "Assets", "GlassHurricaneCandleHolder.glb")),
    imageDecoder: new TestEncodedImageDecoder());
PbrMaterial[] officialVolumeMaterials = officialVolumeAsset.Scene.Root
    .EnumerateDepthFirst().OfType<Mesh>().Select(static mesh => mesh.Material)
    .OfType<PbrMaterial>().Distinct().ToArray();
Expect(
    officialVolumeMaterials.Any(static material =>
        material.TransmissionFactor > 0f && material.VolumeThicknessFactor > 0f &&
        float.IsFinite(material.VolumeAttenuationDistance)) &&
    officialVolumeAsset.IgnoredOptionalExtensions.Count == 0,
    "official Khronos finite volume thickness and absorption import",
    failures);
string officialKtxDirectory = Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "AnisotropyBarnLampKtx");
byte[] officialKtxGltf = File.ReadAllBytes(Path.Combine(
    officialKtxDirectory,
    "AnisotropyBarnLamp.gltf"));
TestEncodedTextureTranscoder officialKtxTranscoder = new();
GltfAsset officialKtxAsset = GltfImporter.ImportAssetWithOptions(
    officialKtxGltf,
    new GltfImportOptions
    {
        ExternalBufferResolver = uri => File.ReadAllBytes(Path.Combine(officialKtxDirectory, uri)),
        ExternalImageResolver = uri => File.ReadAllBytes(Path.Combine(officialKtxDirectory, uri)),
        TextureTranscoder = officialKtxTranscoder,
        GraphicsCapabilities = compressedTextureCapabilities,
    });
PbrMaterial[] officialKtxMaterials = officialKtxAsset.Scene.Root
    .EnumerateDepthFirst()
    .OfType<Mesh>()
    .Select(static mesh => mesh.Material)
    .OfType<PbrMaterial>()
    .Distinct()
    .ToArray();
Expect(
    officialKtxMaterials.Length == 3 &&
    officialKtxMaterials.Any(static material =>
        material.CompressedBaseColorTexture is not null &&
        material.CompressedNormalTexture is not null &&
        material.CompressedOcclusionRoughnessMetallicTexture is not null) &&
    officialKtxMaterials.Any(static material =>
        material.EmissiveColor.Red == 1f && material.EmissiveStrength == 25f) &&
    officialKtxMaterials.Any(static material =>
        material.ClearcoatFactor == 0.25f &&
        material.ClearcoatRoughness == 0.15f &&
        material.CompressedClearcoatNormalTexture is not null) &&
    officialKtxMaterials.Any(static material =>
        material.AnisotropyStrength == 1f &&
        material.AnisotropyRotation == 0f &&
        material.CompressedAnisotropyTexture is not null) &&
    officialKtxMaterials.Any(static material =>
        material.TransmissionFactor == 1f &&
        material.IndexOfRefraction == 1.5f &&
        material.VolumeThicknessFactor == 0.01f) &&
    officialKtxTranscoder.ColorTranscodeCount == 1 &&
    officialKtxTranscoder.DataTranscodeCount == 3 &&
    officialKtxAsset.IgnoredOptionalExtensions.Count == 0,
    "official Khronos external GLTF/BIN/KTX2 core fallback import",
    failures);
GltfAsset officialMorphAsset = GltfImporter.ImportAsset(File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "AnimatedMorphCube.glb")));
Mesh officialMorphMesh = officialMorphAsset.Scene.Root.EnumerateDepthFirst().OfType<Mesh>().Single();
MorphWeightAnimationTrack officialMorphTrack =
    (MorphWeightAnimationTrack)officialMorphAsset.Animations.Single().Tracks.Single();
officialMorphAsset.Animations[0].Apply(officialMorphTrack.Duration);
Expect(
    officialMorphMesh.Geometry.MorphTargets.Count == 2 &&
    officialMorphMesh.Geometry.Tangents.Count == officialMorphMesh.Geometry.Positions.Count &&
    officialMorphMesh.Geometry.MorphTargets.All(target =>
        target.TangentDeltas.Count == officialMorphMesh.Geometry.Positions.Count) &&
    officialMorphTrack.Values.Count == 127 &&
    officialMorphMesh.MorphWeights.SequenceEqual(officialMorphTrack.Values[^1]),
    "official Khronos morph targets and weight animation import",
    failures);
GltfAsset officialFoxAsset = GltfImporter.ImportAsset(File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "Fox.glb")));
Mesh officialFoxMesh = officialFoxAsset.Scene.Root.EnumerateDepthFirst().OfType<Mesh>().Single();
PbrMaterial officialFoxMaterial = (PbrMaterial)officialFoxMesh.Material;
Expect(
    officialFoxMesh.Geometry.Positions.Count == 1728 &&
    officialFoxMesh.Geometry.TextureCoordinates.Count == 1728 &&
    officialFoxMesh.Geometry.JointIndices.Count == 1728 &&
    officialFoxMesh.Skin?.Joints.Count == 24,
    "official Khronos Fox geometry and skin import",
    failures);
Expect(
    officialFoxMaterial.BaseColorTexture is { Width: 1024, Height: 1024 } foxTexture &&
    foxTexture.ColorSpace == StandardColorSpaces.LinearSrgb &&
    officialFoxMaterial.TextureSampling.Filter == MaterialTextureFilter.Linear,
    "official Khronos Fox PNG and sampler import",
    failures);
Expect(
    officialFoxAsset.Animations.Select(static clip => clip.Name)
        .SequenceEqual(["Survey", "Walk", "Run"]) &&
    officialFoxAsset.Animations.All(static clip => clip.Tracks.Count == 21) &&
    officialFoxAsset.Animations.All(static clip =>
        clip.Tracks.All(static track => track is QuaternionAnimationTrack or Vector3AnimationTrack)),
    "official Khronos Fox animation clips import",
    failures);
officialFoxAsset.Animations[1].Apply(officialFoxAsset.Animations[1].Duration * 0.5f);
Expect(
    officialFoxMesh.Skin!.Joints.All(static joint =>
        float.IsFinite(joint.Transform.Rotation.X) && float.IsFinite(joint.Transform.Rotation.W)),
    "official Khronos Fox animation sampling",
    failures);
GltfAsset officialInterpolationAsset = GltfImporter.ImportAsset(File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "InterpolationTest.glb")));
PbrMaterial interpolationLabelMaterial = officialInterpolationAsset.Scene.Root
    .EnumerateDepthFirst()
    .OfType<Mesh>()
    .Select(static mesh => mesh.Material)
    .OfType<PbrMaterial>()
    .Single(static material => material.BaseColorTexture is not null);
foreach (AnimationClip interpolationClip in officialInterpolationAsset.Animations)
{
    interpolationClip.Apply(1f, AnimationWrapMode.Clamp);
}
Expect(
    officialInterpolationAsset.Animations.Count == 9 &&
    officialInterpolationAsset.Animations.Count(static clip =>
        clip.Tracks.Single().Interpolation == AnimationInterpolation.CubicSpline) == 3,
    "official Khronos Step Linear CubicSpline animation import",
    failures);
Expect(
    interpolationLabelMaterial.BaseColorTexture is { Width: 1000, Height: 100 },
    "official Khronos indexed-color PNG import",
    failures);
GltfAsset officialTangentMirrorAsset = GltfImporter.ImportAsset(File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "NormalTangentMirrorTest.glb")));
Mesh[] officialTangentMirrorMeshes = officialTangentMirrorAsset.Scene.Root
    .EnumerateDepthFirst().OfType<Mesh>().ToArray();
NormalizedRgbaDataImage officialMirrorNormal = officialTangentMirrorMeshes
    .Select(static mesh => mesh.Material)
    .OfType<PbrMaterial>()
    .Select(static material => material.NormalTexture)
    .OfType<NormalizedRgbaDataImage>()
    .First();
Expect(
    officialTangentMirrorMeshes.Length != 0 &&
    officialTangentMirrorMeshes.Any(static mesh =>
        mesh.Geometry.Tangents.Count == mesh.Geometry.Positions.Count) &&
    officialTangentMirrorMeshes.Select(static mesh => mesh.Material)
        .OfType<PbrMaterial>()
        .Any(static material => material.NormalTexture is not null),
    "official Khronos authored tangent mirror and PNG normal-map import",
    failures);
Expect(
    officialMirrorNormal.Texels.Take(512).All(static texel =>
        IsByteNormalized(texel.X) && IsByteNormalized(texel.Y) &&
        IsByteNormalized(texel.Z) && IsByteNormalized(texel.W)),
    "PNG normal map preserves normalized bytes without sRGB transfer decode",
    failures);
GltfAsset officialDerivativeTangentAsset = GltfImporter.ImportAsset(File.ReadAllBytes(Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "NormalTangentTest.glb")));
Mesh[] officialDerivativeTangentMeshes = officialDerivativeTangentAsset.Scene.Root
    .EnumerateDepthFirst().OfType<Mesh>().ToArray();
Expect(
    officialDerivativeTangentMeshes.Length != 0 &&
    officialDerivativeTangentMeshes.All(static mesh => mesh.Geometry.Tangents.Count == 0) &&
    officialDerivativeTangentMeshes.Select(static mesh => mesh.Material)
        .OfType<PbrMaterial>()
        .Any(static material => material.NormalTexture is not null),
    "official Khronos derivative tangent fallback and PNG normal-map import",
    failures);

TestEncodedImageDecoder testImageDecoder = new();
GltfAsset officialAlphaAsset = GltfImporter.ImportAsset(
    File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "AlphaBlendModeTest.glb")),
    imageDecoder: testImageDecoder);
PbrMaterial[] officialAlphaMaterials = officialAlphaAsset.Scene.Root
    .EnumerateDepthFirst()
    .OfType<Mesh>()
    .Select(static mesh => (PbrMaterial)mesh.Material)
    .ToArray();
Expect(
    officialAlphaMaterials.Length == 9 &&
    officialAlphaMaterials.Count(static material => material.AlphaMode == MaterialAlphaMode.Opaque) == 4 &&
    officialAlphaMaterials.Count(static material => material.AlphaMode == MaterialAlphaMode.Mask) == 3 &&
    officialAlphaMaterials.Count(static material => material.AlphaMode == MaterialAlphaMode.Blend) == 2,
    "official Khronos alpha-mode import",
    failures);
Expect(
    officialAlphaMaterials.All(static material => material.BaseColorTexture is not null) &&
    officialAlphaMaterials.Count(static material => material.NormalTexture is not null) == 1 &&
    officialAlphaMaterials.Count(static material =>
        material.OcclusionRoughnessMetallicTexture is not null) == 1 &&
    testImageDecoder.ColorDecodeCount != 0 && testImageDecoder.DataDecodeCount != 0,
    "official Khronos JPEG color/data map semantics",
    failures);

MeshGeometry sphere = MeshPrimitives.CreateUvSphere(radius: 2f, longitudeSegments: 8, latitudeSegments: 4);
Expect(sphere.Positions.Count == 45, "UV sphere vertex count", failures);
Expect(sphere.Indices.Count == 144, "UV sphere index count", failures);
Expect(
    sphere.Normals.Count == sphere.Positions.Count &&
    sphere.TextureCoordinates.Count == sphere.Positions.Count,
    "UV sphere vertex channels",
    failures);
for (int index = 0; index < sphere.Positions.Count; index++)
{
    ExpectNear(sphere.Positions[index].Length(), 2f, 0.00001f, "UV sphere radius", failures);
    ExpectNear(sphere.Normals[index].Length(), 1f, 0.00001f, "UV sphere unit normal", failures);
}
Expect(
    Vector3.Distance(sphere.Positions[9], sphere.Positions[17]) < 0.00001f &&
    sphere.TextureCoordinates[9].X == 0f && sphere.TextureCoordinates[17].X == 1f,
    "UV sphere duplicated seam",
    failures);
for (int triangle = 0; triangle < sphere.Indices.Count; triangle += 3)
{
    Vector3 a = sphere.Positions[checked((int)sphere.Indices[triangle])];
    Vector3 b = sphere.Positions[checked((int)sphere.Indices[triangle + 1])];
    Vector3 c = sphere.Positions[checked((int)sphere.Indices[triangle + 2])];
    Vector3 outward = a + b + c;
    Expect(
        Vector3.Dot(Vector3.Cross(b - a, c - a), outward) > 0f,
        "UV sphere outward winding",
        failures);
}
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = MeshPrimitives.CreateUvSphere(radius: 0f),
    "UV sphere radius validation",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = MeshPrimitives.CreateUvSphere(longitudeSegments: 2),
    "UV sphere longitude validation",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = MeshPrimitives.CreateUvSphere(latitudeSegments: 1),
    "UV sphere latitude validation",
    failures);

MeshGeometry cone = MeshPrimitives.CreateCone(radius: 1.5f, height: 2f, radialSegments: 8);
Expect(cone.Positions.Count == 28, "cone vertex count", failures);
Expect(cone.Indices.Count == 48, "cone index count", failures);
Expect(
    cone.Normals.Count == cone.Positions.Count &&
    cone.TextureCoordinates.Count == cone.Positions.Count &&
    cone.Normals.All(static normal => MathF.Abs(normal.Length() - 1f) < 0.00001f),
    "cone normalized vertex channels",
    failures);
Expect(
    cone.Positions.Max(static position => position.Y) == 1f &&
    cone.Positions.Min(static position => position.Y) == -1f,
    "cone centered height",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = MeshPrimitives.CreateCone(radius: 0f),
    "cone radius validation",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = MeshPrimitives.CreateCone(radialSegments: 2),
    "cone segment validation",
    failures);

Vector3[] environmentSource =
[
    new(0, 0, 0),
    new(0.25f, 0.5f, 4f),
    new(-0.1f, 0.2f, 1f),
    new(1, 1, 1),
    new(2, 3, 5),
    new(0.18f),
    new(0.4f, 0.3f, 0.2f),
    new(8, 4, 2),
];
EquirectangularHdrEnvironment environment = new(
    4,
    2,
    environmentSource,
    StandardColorSpaces.AcesCg,
    "test environment");
environmentSource[1] = Vector3.Zero;
Expect(
    environment.Pixels[1].Z == 4f && environment.Pixels[2].X == -0.1f,
    "HDR environment copies and preserves extended values",
    failures);
Expect(
    environment.Width == 4 && environment.Height == 2 &&
    ReferenceEquals(environment.ColorSpace, StandardColorSpaces.AcesCg),
    "HDR environment dimensions and color identity",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new EquirectangularHdrEnvironment(
        2,
        2,
        [Vector3.Zero],
        StandardColorSpaces.LinearSrgb),
    "HDR environment pixel count",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new EquirectangularHdrEnvironment(
        1,
        1,
        [new Vector3(float.NaN)],
        StandardColorSpaces.LinearSrgb),
    "HDR environment finite pixels",
    failures);
ImageBasedLight imageBasedLight = new(environment, intensity: 2f, name: "environment light");
Expect(ReferenceEquals(imageBasedLight.Environment, environment), "IBL environment identity", failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => imageBasedLight.Intensity = float.PositiveInfinity,
    "IBL intensity validation",
    failures);
using (Stream radianceStream = CreateRadianceRleTestStream())
{
    EquirectangularHdrEnvironment decodedRadiance = RadianceHdrReader.Read(
        radianceStream,
        StandardColorSpaces.LinearSrgb,
        "test RGBE");
    Expect(
        decodedRadiance.Width == 8 && decodedRadiance.Height == 1 &&
        decodedRadiance.Pixels.Count == 8 && decodedRadiance.Name == "test RGBE",
        "Radiance RGBE dimensions and identity",
        failures);
    foreach (Vector3 pixel in decodedRadiance.Pixels)
    {
        ExpectNear(pixel.X, 1f, 0.000001f, "Radiance RGBE red decode", failures);
        ExpectNear(pixel.Y, 0.5f, 0.000001f, "Radiance RGBE green decode", failures);
        ExpectNear(pixel.Z, 0.25f, 0.000001f, "Radiance RGBE blue decode", failures);
    }
}
byte[] radianceSourceBytes;
using (Stream radianceSourceStream = CreateRadianceRleTestStream())
{
    using var copiedSource = new MemoryStream();
    radianceSourceStream.CopyTo(copiedSource);
    radianceSourceBytes = copiedSource.ToArray();
}
var radianceCache = new RadianceHdrSourceCache(radianceSourceBytes.Length * 2L);
var radianceOptions = new RadianceHdrEnvironmentLoadOptions(StandardColorSpaces.LinearSrgb)
{
    Name = "bounded RGBE",
    MaximumSourceByteCount = radianceSourceBytes.Length,
    MaximumOutputByteCount = 8 * 1 * 3 * sizeof(float),
};
int radianceResolverOpenCount = 0;
TrackingMemoryStream? resolvedRadianceStream = null;
RadianceHdrEnvironmentAsset radianceAsset = await RadianceHdrEnvironmentLoader.LoadAsync(
    "test/radiance.hdr",
    cancellationToken =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        radianceResolverOpenCount++;
        resolvedRadianceStream = new TrackingMemoryStream(radianceSourceBytes);
        return ValueTask.FromResult<Stream>(resolvedRadianceStream);
    },
    radianceCache,
    radianceOptions);
Expect(
    radianceResolverOpenCount == 1 &&
    resolvedRadianceStream?.IsDisposed == true &&
    radianceCache.Count == 1 &&
    radianceCache.TotalByteCount == radianceSourceBytes.Length,
    "bounded Radiance resolver ownership and source cache fill",
    failures);
Expect(
    radianceAsset.Environment.Width == 8 &&
    radianceAsset.OutputByteCount == 96 &&
    !radianceAsset.HasRetainedEncodedSource &&
    radianceAsset.RetainedEncodedSource.IsEmpty,
    "bounded Radiance decoded asset and default source retention",
    failures);
var retainedRadianceOptions = new RadianceHdrEnvironmentLoadOptions(StandardColorSpaces.LinearSrgb)
{
    Name = "retained RGBE",
    MaximumSourceByteCount = radianceSourceBytes.Length,
    MaximumOutputByteCount = 96,
    RetainEncodedSource = true,
};
RadianceHdrEnvironmentAsset retainedRadianceAsset = await RadianceHdrEnvironmentLoader.LoadAsync(
    "test/radiance.hdr",
    _ => throw new InvalidOperationException("A warm Radiance source-cache hit must skip the resolver."),
    radianceCache,
    retainedRadianceOptions);
Expect(
    radianceResolverOpenCount == 1 &&
    retainedRadianceAsset.HasRetainedEncodedSource &&
    retainedRadianceAsset.RetainedEncodedSource.Span.SequenceEqual(radianceSourceBytes),
    "Radiance source-cache warm hit and opt-in exact source retention",
    failures);
radianceCache.Clear();
Expect(
    radianceCache.Count == 0 &&
    radianceCache.TotalByteCount == 0 &&
    retainedRadianceAsset.RetainedEncodedSource.Span.SequenceEqual(radianceSourceBytes),
    "clearing application Radiance cache preserves asset-owned source",
    failures);
using (var callerOwnedRadianceStream = new TrackingMemoryStream(radianceSourceBytes))
{
    _ = await RadianceHdrEnvironmentLoader.LoadAsync(
        callerOwnedRadianceStream,
        radianceOptions);
    Expect(
        !callerOwnedRadianceStream.IsDisposed,
        "Radiance loader leaves caller-owned stream open",
        failures);
}
await ExpectThrowsAsync<InvalidDataException>(
    () => RadianceHdrEnvironmentLoader.LoadAsync(
        new MemoryStream(radianceSourceBytes),
        new RadianceHdrEnvironmentLoadOptions(StandardColorSpaces.LinearSrgb)
        {
            MaximumSourceByteCount = radianceSourceBytes.Length - 1,
            MaximumOutputByteCount = 96,
        }),
    "Radiance encoded-source byte limit",
    failures);
await ExpectThrowsAsync<InvalidDataException>(
    () => RadianceHdrEnvironmentLoader.LoadAsync(
        new MemoryStream(radianceSourceBytes),
        new RadianceHdrEnvironmentLoadOptions(StandardColorSpaces.LinearSrgb)
        {
            MaximumSourceByteCount = radianceSourceBytes.Length,
            MaximumOutputByteCount = 95,
        }),
    "Radiance decoded-output byte limit",
    failures);
using (var radianceLoadCancellation = new CancellationTokenSource())
using (var cancellingRadianceStream = new CancellingReadStream(
    radianceSourceBytes,
    radianceLoadCancellation))
{
    await ExpectThrowsAsync<OperationCanceledException>(
        () => RadianceHdrEnvironmentLoader.LoadAsync(
            cancellingRadianceStream,
            radianceOptions,
            radianceLoadCancellation.Token),
        "Radiance async source-read cancellation",
        failures);
    Expect(
        !cancellingRadianceStream.IsDisposed,
        "cancelled Radiance caller stream remains caller-owned",
        failures);
}
string studioEnvironmentPath = Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "studio_small_02_1k.hdr");
using (Stream studioEnvironmentStream = File.OpenRead(studioEnvironmentPath))
{
    RadianceHdrEnvironmentAsset studioEnvironmentAsset = await RadianceHdrEnvironmentLoader.LoadAsync(
        studioEnvironmentStream,
        new RadianceHdrEnvironmentLoadOptions(StandardColorSpaces.LinearSrgb)
        {
            Name = "Studio Small 02",
            MaximumSourceByteCount = 2 * 1024 * 1024,
            MaximumOutputByteCount = 8 * 1024 * 1024,
        });
    EquirectangularHdrEnvironment decodedStudioEnvironment = studioEnvironmentAsset.Environment;
    float studioMaximum = decodedStudioEnvironment.Pixels.Max(
        static pixel => MathF.Max(pixel.X, MathF.Max(pixel.Y, pixel.Z)));
    Expect(
        decodedStudioEnvironment.Width == 1024 &&
        decodedStudioEnvironment.Height == 512 &&
        studioMaximum > 1f &&
        studioEnvironmentAsset.OutputByteCount == 6 * 1024 * 1024 &&
        !studioEnvironmentAsset.HasRetainedEncodedSource &&
        studioEnvironmentStream.CanRead,
        "bounded Poly Haven Studio Small 02 dimensions, HDR range, and caller ownership",
        failures);
    PrefilteredEnvironmentCube studioSpecular = HdrEnvironmentConverter.CreateSpecularPrefilteredCube(
        decodedStudioEnvironment,
        16,
        5,
        128,
        StandardColorSpaces.LinearSrgb);
    float sharpStudioMaximum = studioSpecular.GetMipLevel(0).Pixels.Max(
        static pixel => MathF.Max(pixel.X, MathF.Max(pixel.Y, pixel.Z)));
    float roughStudioMaximum = studioSpecular.GetMipLevel(4).Pixels.Max(
        static pixel => MathF.Max(pixel.X, MathF.Max(pixel.Y, pixel.Z)));
    Expect(
        sharpStudioMaximum > 1f &&
        roughStudioMaximum > 1f &&
        roughStudioMaximum < sharpStudioMaximum * 0.25f,
        "solid-angle source LOD broadens extreme HDR highlights without clipping extended range",
        failures);
}

EquirectangularHdrEnvironment directionalEnvironment = CreateDirectionalEnvironment(128, 64);
HdrEnvironmentCube directionalCube = HdrEnvironmentConverter.ConvertToCube(
    directionalEnvironment,
    3,
    StandardColorSpaces.LinearSrgb,
    "direction cube");
Expect(
    directionalCube.FaceSize == 3 && directionalCube.Pixels.Count == 54 &&
    ReferenceEquals(directionalCube.ColorSpace, StandardColorSpaces.LinearSrgb) &&
    directionalCube.Name == "direction cube",
    "HDR cube dimensions, color identity, and name",
    failures);
(EnvironmentCubeFace Face, Vector3 Direction)[] faceDirections =
[
    (EnvironmentCubeFace.PositiveX, Vector3.UnitX),
    (EnvironmentCubeFace.NegativeX, -Vector3.UnitX),
    (EnvironmentCubeFace.PositiveY, Vector3.UnitY),
    (EnvironmentCubeFace.NegativeY, -Vector3.UnitY),
    (EnvironmentCubeFace.PositiveZ, Vector3.UnitZ),
    (EnvironmentCubeFace.NegativeZ, -Vector3.UnitZ),
];
foreach ((EnvironmentCubeFace face, Vector3 expectedDirection) in faceDirections)
{
    Vector3 sampledDirection = Vector3.Normalize(directionalCube.GetPixel(face, 1, 1));
    Expect(
        Vector3.Dot(sampledDirection, expectedDirection) > 0.999f,
        $"HDR cube {face} center direction",
        failures);
}
Expect(
    Vector3.Dot(
        Vector3.Normalize(directionalCube.GetPixel(EnvironmentCubeFace.NegativeX, 1, 1)),
        -Vector3.UnitX) > 0.999f,
    "HDR cube longitude seam wraps",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = HdrEnvironmentConverter.ConvertToCube(
        directionalEnvironment,
        0,
        StandardColorSpaces.LinearSrgb),
    "HDR cube face size validation",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = directionalCube.GetPixel(EnvironmentCubeFace.PositiveX, 3, 0),
    "HDR cube coordinate validation",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = directionalCube.GetPixel((EnvironmentCubeFace)6, 0, 0),
    "HDR cube face validation",
    failures);

Vector3 extendedEnvironmentValue = new(-0.25f, 2f, 4f);
EquirectangularHdrEnvironment extendedEnvironment = new(
    2,
    1,
    [extendedEnvironmentValue, extendedEnvironmentValue],
    StandardColorSpaces.LinearSrgb,
    "extended environment");
Task[] concurrentEnvironmentPreparation = Enumerable.Range(0, 4)
    .Select(_ => SceneRenderer.PrepareImageBasedLightingAsync(extendedEnvironment))
    .ToArray();
await Task.WhenAll(concurrentEnvironmentPreparation);
Expect(
    concurrentEnvironmentPreparation.All(static task => task.IsCompletedSuccessfully),
    "concurrent image-based-lighting preparation shares a successful cached result",
    failures);
HdrEnvironmentCube extendedCube = HdrEnvironmentConverter.ConvertToCube(
    extendedEnvironment,
    2,
    StandardColorSpaces.LinearSrgb);
foreach (Vector3 pixel in extendedCube.Pixels)
{
    ExpectNear(pixel.X, -0.25f, 0.000001f, "HDR cube preserves negative red", failures);
    ExpectNear(pixel.Y, 2f, 0.000001f, "HDR cube preserves above-one green", failures);
    ExpectNear(pixel.Z, 4f, 0.000001f, "HDR cube preserves above-one blue", failures);
}
PrefilteredEnvironmentCube constantSpecularCube = HdrEnvironmentConverter.CreateSpecularPrefilteredCube(
    extendedEnvironment,
    4,
    3,
    64,
    StandardColorSpaces.LinearSrgb);
Expect(
    constantSpecularCube.MipLevelCount == 3 &&
    constantSpecularCube.GetMipLevel(0).FaceSize == 4 &&
    constantSpecularCube.GetMipLevel(1).FaceSize == 2 &&
    constantSpecularCube.GetMipLevel(2).FaceSize == 1,
    "specular environment mip dimensions",
    failures);
foreach (HdrEnvironmentCube mip in constantSpecularCube.MipLevels)
{
    foreach (Vector3 pixel in mip.Pixels)
    {
        ExpectNear(pixel.X, -0.25f, 0.00001f, "specular prefilter preserves constant red", failures);
        ExpectNear(pixel.Y, 2f, 0.00001f, "specular prefilter preserves constant green", failures);
        ExpectNear(pixel.Z, 4f, 0.00001f, "specular prefilter preserves constant blue", failures);
    }
}
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = HdrEnvironmentConverter.CreateSpecularPrefilteredCube(
        extendedEnvironment,
        4,
        4,
        8,
        StandardColorSpaces.LinearSrgb),
    "specular environment mip count validation",
    failures);
SplitSumBrdfLut brdfLut = HdrEnvironmentConverter.CreateSplitSumBrdfLut(8, 128);
Expect(
    brdfLut.Size == 8 && brdfLut.Values.Count == 64 &&
    brdfLut.SheenDirectionalAlbedo.Count == 64,
    "GGX split-sum and Charlie energy LUT dimensions",
    failures);
foreach (Vector2 value in brdfLut.Values)
{
    Expect(
        float.IsFinite(value.X) && float.IsFinite(value.Y) && value.X >= 0f && value.Y >= 0f,
        "split-sum BRDF finite non-negative values",
        failures);
}
Expect(
    brdfLut.SheenDirectionalAlbedo.All(static value =>
        float.IsFinite(value) && value >= 0f && value <= 1f) &&
    brdfLut.SheenDirectionalAlbedo.Any(static value => value > 0f),
    "Charlie sheen directional-energy LUT finite nonzero values",
    failures);
PrefilteredEnvironmentCube constantSheenCube = HdrEnvironmentConverter.CreateSheenPrefilteredCube(
    extendedEnvironment,
    4,
    3,
    64,
    StandardColorSpaces.LinearSrgb);
Expect(
    constantSheenCube.MipLevelCount == 3 &&
    constantSheenCube.MipLevels.SelectMany(static level => level.Pixels).All(static pixel =>
        Vector3.Distance(pixel, new Vector3(-0.25f, 2f, 4f)) < 0.0001f),
    "Charlie prefilter preserves constant scene-linear HDR radiance",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = HdrEnvironmentConverter.CreateSplitSumBrdfLut(8, 0),
    "split-sum BRDF sample count validation",
    failures);

EquirectangularHdrEnvironment p3Environment = new(
    1,
    1,
    [Vector3.UnitX],
    StandardColorSpaces.LinearDisplayP3);
HdrEnvironmentCube convertedP3Cube = HdrEnvironmentConverter.ConvertToCube(
    p3Environment,
    1,
    StandardColorSpaces.LinearSrgb);
Vector3 convertedP3Red = convertedP3Cube.GetPixel(EnvironmentCubeFace.PositiveX, 0, 0);
ExpectNear(convertedP3Red.X, 1.224940f, 0.00001f, "HDR cube transforms P3 red", failures);
ExpectNear(convertedP3Red.Y, -0.042058f, 0.00001f, "HDR cube retains transformed negative green", failures);
ExpectNear(convertedP3Red.Z, -0.019642f, 0.00001f, "HDR cube retains transformed negative blue", failures);
HdrEnvironmentCube diffuseConstantCube = HdrEnvironmentConverter.CreateDiffuseIrradianceCube(
    extendedEnvironment,
    2,
    StandardColorSpaces.LinearSrgb);
foreach (Vector3 pixel in diffuseConstantCube.Pixels)
{
    ExpectNear(pixel.X, -0.25f, 0.00001f, "diffuse irradiance preserves constant negative red", failures);
    ExpectNear(pixel.Y, 2f, 0.00001f, "diffuse irradiance preserves constant HDR green", failures);
    ExpectNear(pixel.Z, 4f, 0.00001f, "diffuse irradiance preserves constant HDR blue", failures);
}
HdrEnvironmentCube directionalDiffuseCube = HdrEnvironmentConverter.CreateDiffuseIrradianceCube(
    directionalEnvironment,
    3,
    StandardColorSpaces.LinearSrgb);
foreach ((EnvironmentCubeFace face, Vector3 expectedDirection) in faceDirections)
{
    Vector3 irradianceOverPi = directionalDiffuseCube.GetPixel(face, 1, 1);
    Vector3 expectedIrradianceOverPi = expectedDirection * (2f / 3f);
    ExpectNear(
        irradianceOverPi.X,
        expectedIrradianceOverPi.X,
        0.012f,
        $"diffuse irradiance {face} red analytic response",
        failures);
    ExpectNear(
        irradianceOverPi.Y,
        expectedIrradianceOverPi.Y,
        0.012f,
        $"diffuse irradiance {face} green analytic response",
        failures);
    ExpectNear(
        irradianceOverPi.Z,
        expectedIrradianceOverPi.Z,
        0.012f,
        $"diffuse irradiance {face} blue analytic response",
        failures);
}

TestMaterial material = new("test material") { IsDoubleSided = true };
Mesh mesh = new(geometry, material, "triangle");
scene.Add(mesh);
Expect(
    ReferenceEquals(mesh.Geometry, geometry) &&
    ReferenceEquals(mesh.Material, material) &&
    scene.EnumerateVisible().Contains(mesh),
    "mesh scene integration",
    failures);

LinearRgba hdrBlue = new(0.05f, 0.15f, 2f, 1f, StandardColorSpaces.LinearSrgb);
Expect(hdrBlue.Blue == 2f, "linear color preserves HDR values", failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new LinearRgba(float.NaN, 0, 0, 1, StandardColorSpaces.LinearSrgb),
    "non-finite linear color rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new LinearRgba(0, 0, 0, 1.1f, StandardColorSpaces.LinearSrgb),
    "linear color alpha range",
    failures);
LinearRgbaImage materialImage = new(
    2,
    1,
    [new Vector4(1.2f, 0.1f, 0.2f, 1f), new Vector4(0.1f, 0.3f, 1.5f, 1f)],
    StandardColorSpaces.LinearDisplayP3,
    "test P3 material image");
LinearRgbaImage emissiveImage = new(
    1,
    1,
    [new Vector4(0.25f, 0.5f, 1.25f, 1f)],
    StandardColorSpaces.LinearDisplayP3,
    "test P3 emissive image");
Expect(
    materialImage.Width == 2 && materialImage.Height == 1 &&
    materialImage.Pixels.Count == 2 &&
    ReferenceEquals(materialImage.ColorSpace, StandardColorSpaces.LinearDisplayP3),
    "linear image dimensions and color identity",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new LinearRgbaImage(
        2,
        1,
        [Vector4.One],
        StandardColorSpaces.LinearSrgb),
    "linear image pixel count",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new LinearRgbaImage(
        1,
        1,
        [new Vector4(1, 1, 1, 1.1f)],
        StandardColorSpaces.LinearSrgb),
    "linear image alpha range",
    failures);
LinearRgbaImage translucentMaterialImage = new(
    1,
    1,
    [new Vector4(1, 1, 1, 0.5f)],
    StandardColorSpaces.LinearSrgb);
NormalizedRgbaDataImage normalDataImage = new(
    2,
    1,
    [new Vector4(0.5f, 0.5f, 1f, 1f), new Vector4(0.6f, 0.4f, 0.9f, 1f)],
    "test normal data");
NormalizedRgbaDataImage ormDataImage = new(
    2,
    1,
    [new Vector4(1f, 0.25f, 0f, 1f), new Vector4(0.5f, 0.75f, 1f, 1f)],
    "test ORM data");
Expect(
    normalDataImage.Texels.Count == 2 && normalDataImage.Name == "test normal data",
    "normalized material data image",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new NormalizedRgbaDataImage(
        1,
        1,
        [new Vector4(-0.1f, 0, 0, 1)]),
    "normalized material data range",
    failures);
Vector4[] ownedImagePixels = [new Vector4(-1f, 2f, 4f, .5f)];
LinearRgbaImage ownedImage = LinearRgbaImage.FromOwnedPixels(
    1, 1, ownedImagePixels, StandardColorSpaces.LinearDisplayP3);
Expect(ReferenceEquals(ownedImage.PixelStorage, ownedImagePixels) &&
    ownedImage.Pixels[0].Z == 4f && ownedImage.ColorSpace == StandardColorSpaces.LinearDisplayP3,
    "validated decoder image transfers FP32 storage and preserves HDR/color identity", failures);
Vector4[] ownedDataTexels = [new Vector4(.1f, .2f, .3f, .4f)];
NormalizedRgbaDataImage ownedData = NormalizedRgbaDataImage.FromOwnedTexels(1, 1, ownedDataTexels);
Expect(ReferenceEquals(ownedData.TexelStorage, ownedDataTexels),
    "validated decoder data transfers FP32 storage", failures);
Vector4[] callerPixels = [Vector4.One];
LinearRgbaImage copiedImage = new(1, 1, callerPixels, StandardColorSpaces.LinearSrgb);
NormalizedRgbaDataImage copiedData = new(1, 1, callerPixels);
callerPixels[0] = Vector4.Zero;
Expect(copiedImage.Pixels[0] == Vector4.One && copiedData.Texels[0] == Vector4.One,
    "public image constructors preserve immutable copies", failures);
foreach (Vector4 invalid in new[] { new Vector4(float.NaN, 0, 0, 1),
    new Vector4(0, float.PositiveInfinity, 0, 1), new Vector4(0, 0, float.NegativeInfinity, 1),
    new Vector4(0, 0, 0, -.1f), new Vector4(0, 0, 0, 1.1f), new Vector4(0, 0, 0, float.NaN) })
    ExpectThrows<ArgumentOutOfRangeException>(() => LinearRgbaImage.FromOwnedPixels(
        1, 1, [invalid], StandardColorSpaces.LinearSrgb), "owned image validates finite/alpha", failures);
foreach (Vector4 invalid in new[] { new Vector4(-.1f, 0, 0, 1), new Vector4(0, 1.1f, 0, 1),
    new Vector4(0, 0, float.NaN, 1), new Vector4(0, 0, 0, float.PositiveInfinity) })
    ExpectThrows<ArgumentOutOfRangeException>(() => NormalizedRgbaDataImage.FromOwnedTexels(
        1, 1, [invalid]), "owned data validates normalized/finite", failures);
ExpectThrows<ArgumentException>(() => LinearRgbaImage.FromOwnedPixels(
    2, 1, [Vector4.One], StandardColorSpaces.LinearSrgb), "owned image validates dimensions", failures);
ExpectThrows<ArgumentException>(() => NormalizedRgbaDataImage.FromOwnedTexels(
    2, 1, [Vector4.One]), "owned data validates dimensions", failures);
ExpectThrows<ArgumentOutOfRangeException>(() => LinearRgbaImage.FromOwnedPixels(
    0, 1, [], StandardColorSpaces.LinearSrgb), "owned image rejects zero extent", failures);
ExpectThrows<ArgumentOutOfRangeException>(() => NormalizedRgbaDataImage.FromOwnedTexels(
    1, 0, []), "owned data rejects zero extent", failures);
byte[] compressedBaseBytes = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
CompressedTextureMipLevel compressedBaseLevel = new(8, 8, compressedBaseBytes);
compressedBaseBytes[0] = 255;
CompressedMaterialTexture compressedBaseTexture = new(
    GraphicsTextureFormat.Bc7RgbaUnormSrgb,
    CompressedMaterialTextureContent.Color,
    [
        compressedBaseLevel,
        new CompressedTextureMipLevel(4, 4, new byte[16]),
        new CompressedTextureMipLevel(2, 2, new byte[16]),
    ],
    StandardColorSpaces.LinearSrgb,
    "compressed base color");
CompressedMaterialTexture compressedDataTexture = new(
    GraphicsTextureFormat.Astc4x4Unorm,
    CompressedMaterialTextureContent.Data,
    [
        new CompressedTextureMipLevel(8, 8, new byte[64]),
        new CompressedTextureMipLevel(4, 4, new byte[16]),
        new CompressedTextureMipLevel(2, 2, new byte[16]),
    ],
    name: "compressed material data");
Expect(
    compressedBaseTexture.MipLevels.Count == 3 &&
    compressedBaseTexture.MipLevels[0].Data.Span[0] == 0 &&
    compressedBaseTexture.ColorSpace == StandardColorSpaces.LinearSrgb,
    "immutable compressed material mip chain",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new CompressedMaterialTexture(
        GraphicsTextureFormat.Bc7RgbaUnorm,
        CompressedMaterialTextureContent.Color,
        [new CompressedTextureMipLevel(8, 8, new byte[63])],
        StandardColorSpaces.LinearSrgb),
    "compressed material exact block payload",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new CompressedMaterialTexture(
        GraphicsTextureFormat.Bc7RgbaUnorm,
        CompressedMaterialTextureContent.Color,
        [
            new CompressedTextureMipLevel(8, 8, new byte[64]),
            new CompressedTextureMipLevel(2, 2, new byte[16]),
        ],
        StandardColorSpaces.LinearSrgb),
    "compressed material contiguous mip dimensions",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new CompressedMaterialTexture(
        GraphicsTextureFormat.Astc4x4UnormSrgb,
        CompressedMaterialTextureContent.Data,
        [new CompressedTextureMipLevel(4, 4, new byte[16])]),
    "compressed data rejects hardware sRGB",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new CompressedMaterialTexture(
        GraphicsTextureFormat.Bc7RgbaUnormSrgb,
        CompressedMaterialTextureContent.Color,
        [new CompressedTextureMipLevel(4, 4, new byte[16])],
        StandardColorSpaces.LinearDisplayP3),
    "compressed hardware sRGB rejects wide-gamut relabeling",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new UnlitMaterial(default),
    "untagged material color rejection",
    failures);
Expect(
    new UnlitMaterial(hdrBlue).BaseModel == MaterialBaseModel.Unlit,
    "unlit base material model",
    failures);
PbrMaterial composedMaterial = new(hdrBlue);
Expect(
    composedMaterial.BaseModel == MaterialBaseModel.PbrMetallicRoughness &&
    ReferenceEquals(composedMaterial.Base, composedMaterial.Base) &&
    ReferenceEquals(composedMaterial.Clearcoat, composedMaterial.Clearcoat) &&
    composedMaterial.ShaderTemplate == MaterialShaderTemplate.FullPbr,
    "PBR base plus extension block composition",
    failures);
MaterialShaderTemplate baseOnlyTemplate = MaterialShaderTemplate.FullPbr
    .WithoutExtensions(PbrMaterialExtensions.Transmission)
    .WithoutExtensions(PbrMaterialExtensions.Emissive | PbrMaterialExtensions.Sheen)
    .WithoutTextureSlots(PbrMaterialTextureSlots.Emissive | PbrMaterialTextureSlots.Sheen);
Expect(
    !baseOnlyTemplate.Includes(PbrMaterialExtensions.Transmission) &&
    !baseOnlyTemplate.Includes(PbrMaterialExtensions.Volume) &&
    !baseOnlyTemplate.Includes(PbrMaterialExtensions.Emissive) &&
    !baseOnlyTemplate.Includes(PbrMaterialTextureSlots.Emissive) &&
    baseOnlyTemplate.Includes(PbrMaterialExtensions.Clearcoat),
    "material shader template normalizes dependencies and optional slots",
    failures);
Expect(
    MaterialShaderTemplate.BasePbr
        .WithExtensions(PbrMaterialExtensions.Volume)
        .Includes(PbrMaterialExtensions.Transmission),
    "volume template enables transmission dependency",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new PbrMaterial(hdrBlue, metallic: 1.1f),
    "metallic factor range",
    failures);
PbrMaterial transparentMaterial = new(
    new LinearRgba(1, 1, 1, 0.5f, StandardColorSpaces.LinearSrgb))
{
    AlphaMode = MaterialAlphaMode.Blend,
    AlphaCutoff = 0.25f,
};
Expect(
    transparentMaterial.BaseColor.Alpha == 0.5f &&
    transparentMaterial.AlphaMode == MaterialAlphaMode.Blend &&
    transparentMaterial.AlphaCutoff == 0.25f,
    "PBR material alpha modes",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => transparentMaterial.AlphaCutoff = 1.1f,
    "material alpha cutoff range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => transparentMaterial.AlphaMode = (MaterialAlphaMode)99,
    "material alpha mode range",
    failures);
PbrMaterial occlusionMaterial = new(hdrBlue);
occlusionMaterial.BaseColorTexture = translucentMaterialImage;
Expect(
    ReferenceEquals(occlusionMaterial.BaseColorTexture, translucentMaterialImage),
    "base-color image preserves texture alpha",
    failures);
ExpectThrows<InvalidOperationException>(
    () => occlusionMaterial.CompressedBaseColorTexture = compressedBaseTexture,
    "decoded and compressed base color are mutually exclusive",
    failures);
ExpectNear(occlusionMaterial.IndirectOcclusion, 1f, 0f, "default material indirect visibility", failures);
occlusionMaterial.IndirectOcclusion = 0.4f;
ExpectNear(occlusionMaterial.IndirectOcclusion, 0.4f, 0f, "material indirect visibility", failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.IndirectOcclusion = -0.1f,
    "material indirect visibility range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.NormalScale = -0.1f,
    "normal scale range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.ClearcoatFactor = 1.1f,
    "clearcoat factor range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.ClearcoatRoughness = -0.1f,
    "clearcoat roughness range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.ClearcoatNormalScale = float.PositiveInfinity,
    "clearcoat normal scale range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.TransmissionFactor = 1.1f,
    "transmission factor range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.SheenRoughness = 1.1f,
    "sheen roughness range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.IndexOfRefraction = 0.99f,
    "material IOR range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.VolumeThicknessFactor = -0.1f,
    "volume thickness range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.VolumeAttenuationDistance = 0f,
    "volume attenuation distance range",
    failures);
Expect(
    occlusionMaterial.TextureSampling == MaterialTextureSampling.RepeatingLinear,
    "default material texture sampling",
    failures);
occlusionMaterial.TextureSampling = new MaterialTextureSampling(
    MaterialTextureAddressMode.MirrorRepeat,
    MaterialTextureAddressMode.ClampToEdge,
    MaterialTextureFilter.Nearest);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.TextureSampling = new MaterialTextureSampling(
        (MaterialTextureAddressMode)99,
        MaterialTextureAddressMode.Repeat,
        MaterialTextureFilter.Linear),
    "material texture address range",
    failures);
occlusionMaterial.TextureCoordinateScale = new Vector2(2f, 3f);
occlusionMaterial.TextureCoordinateOffset = new Vector2(-0.25f, 0.5f);
occlusionMaterial.NormalTextureMapping = new MaterialTextureMapping(
    MaterialTextureSampling.RepeatingLinear,
    textureCoordinateSet: 1,
    scale: new Vector2(0.5f, 2f),
    offset: new Vector2(0.1f, 0.2f),
    rotation: 0.25f);
Expect(
    occlusionMaterial.NormalTextureMapping.TextureCoordinateSet == 1 &&
    occlusionMaterial.NormalTextureMapping.Rotation == 0.25f &&
    occlusionMaterial.BaseColorTextureMapping.TextureCoordinateSet == 0 &&
    occlusionMaterial.EmissiveTextureMapping.Scale == new Vector2(2f, 3f) &&
    occlusionMaterial.EmissiveTextureMapping.Offset == new Vector2(-0.25f, 0.5f),
    "independent material texture-slot mappings",
    failures);
PbrMaterial emissiveSourceExclusivity = new(
    new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb))
{
    EmissiveTexture = materialImage,
};
ExpectThrows<InvalidOperationException>(
    () => emissiveSourceExclusivity.CompressedEmissiveTexture = compressedBaseTexture,
    "decoded and compressed emissive source exclusivity",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new MaterialTextureMapping(MaterialTextureSampling.RepeatingLinear, 2),
    "material texture coordinate set range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => occlusionMaterial.TextureCoordinateScale = new Vector2(float.NaN, 1f),
    "material texture transform finite scale",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new DirectionalLight(
        new LinearRgba(1, 1, 1, 1, StandardColorSpaces.LinearSrgb),
        intensity: -1f),
    "directional light intensity range",
    failures);
DirectionalLight rotatedLight = new(
    new LinearRgba(1, 1, 1, 1, StandardColorSpaces.LinearSrgb));
ExpectNear(
    rotatedLight.AngularDiameterRadians,
    0.0093f,
    0.000001f,
    "directional light defaults to solar angular diameter",
    failures);
ExpectNear(
    rotatedLight.ShadowOpacity,
    1f,
    0.000001f,
    "directional shadow defaults to full opacity",
    failures);
rotatedLight.AngularDiameterRadians = 0.08f;
rotatedLight.ShadowOpacity = 0.45f;
ExpectThrows<ArgumentOutOfRangeException>(
    () => rotatedLight.AngularDiameterRadians = MathF.PI,
    "directional light angular diameter range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => rotatedLight.ShadowOpacity = 1.01f,
    "directional shadow opacity range",
    failures);
rotatedLight.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
ExpectNear(rotatedLight.WorldDirection.X, -1f, 0.00001f, "directional light world direction", failures);
PointLight pointLight = new(
    new LinearRgba(1, 0.5f, 0.25f, 1, StandardColorSpaces.LinearSrgb),
    intensity: 25f);
pointLight.Range = 8f;
pointLight.Transform.Position = new Vector3(2, 3, 4);
Expect(
    pointLight.WorldPosition == new Vector3(2, 3, 4) && pointLight.Range == 8f,
    "point-light position and finite range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => pointLight.Range = 0f,
    "point-light positive range",
    failures);
SpotLight spotLight = new(
    new LinearRgba(1, 1, 1, 1, StandardColorSpaces.LinearSrgb),
    intensity: 50f);
spotLight.OuterConeAngle = 0.8f;
spotLight.InnerConeAngle = 0.25f;
spotLight.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
ExpectNear(spotLight.WorldDirection.X, -1f, 0.00001f, "spot-light world direction", failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => spotLight.InnerConeAngle = spotLight.OuterConeAngle,
    "spot-light cone ordering",
    failures);

LinearRgba p3Red = StandardLinearRgbConverter.Convert(
    new LinearRgba(1, 0, 0, 0.5f, StandardColorSpaces.LinearDisplayP3),
    StandardColorSpaces.LinearSrgb);
ExpectNear(p3Red.Red, 1.224940f, 0.00001f, "Display P3 red to linear sRGB red", failures);
ExpectNear(p3Red.Green, -0.042058f, 0.00001f, "Display P3 red preserves negative green", failures);
ExpectNear(p3Red.Blue, -0.019642f, 0.00001f, "Display P3 red preserves negative blue", failures);
Expect(p3Red.Alpha == 0.5f, "linear RGB transform preserves alpha", failures);

foreach (StandardRgbColorSpaceReference space in new[]
         {
             StandardColorSpaces.LinearSrgb,
             StandardColorSpaces.LinearDisplayP3,
             StandardColorSpaces.LinearAdobeRgb,
             StandardColorSpaces.LinearProPhotoRgb,
             StandardColorSpaces.LinearRec2020,
             StandardColorSpaces.AcesCg,
         })
{
    LinearRgba source = new(0.17f, 0.42f, 1.8f, 1f, space);
    LinearRgba inSrgb = StandardLinearRgbConverter.Convert(source, StandardColorSpaces.LinearSrgb);
    LinearRgba roundTrip = StandardLinearRgbConverter.Convert(inSrgb, space);
    ExpectNear(roundTrip.Red, source.Red, 0.00002f, $"{space.Name} red round trip", failures);
    ExpectNear(roundTrip.Green, source.Green, 0.00002f, $"{space.Name} green round trip", failures);
    ExpectNear(roundTrip.Blue, source.Blue, 0.00002f, $"{space.Name} blue round trip", failures);
}

foreach (StandardRgbColorSpaceReference adaptedSpace in new[]
         {
             StandardColorSpaces.LinearProPhotoRgb,
             StandardColorSpaces.AcesCg,
         })
{
    LinearRgba adaptedWhite = StandardLinearRgbConverter.Convert(
        new LinearRgba(1, 1, 1, 1, adaptedSpace),
        StandardColorSpaces.LinearSrgb);
    ExpectNear(adaptedWhite.Red, 1f, 0.0002f, $"{adaptedSpace.Name} adapted white red", failures);
    ExpectNear(adaptedWhite.Green, 1f, 0.0002f, $"{adaptedSpace.Name} adapted white green", failures);
    ExpectNear(adaptedWhite.Blue, 1f, 0.0002f, $"{adaptedSpace.Name} adapted white blue", failures);
}

using RecordingGraphicsDevice graphicsDevice = new();
using (EnvironmentCubeGpuResources environmentResources =
       EnvironmentCubeGpuResources.Create(graphicsDevice, extendedCube))
{
    Expect(
        environmentResources.Texture.Descriptor.Format == GraphicsTextureFormat.Rgba16Float &&
        environmentResources.Texture.Descriptor.Size.Width == 2 &&
        environmentResources.Texture.Descriptor.Size.Height == 2 &&
        environmentResources.Texture.Descriptor.Size.DepthOrArrayLayers == 6 &&
        environmentResources.Texture.Descriptor.Usage ==
            (GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding),
        "HDR cube GPU texture descriptor",
        failures);
    Expect(
        environmentResources.View.Descriptor.Dimension == GraphicsTextureViewDimension.Cube &&
        environmentResources.View.Descriptor.ArrayLayerCount == 6,
        "HDR cube GPU view descriptor",
        failures);
    Expect(
        environmentResources.Sampler.Descriptor.MagFilter == GraphicsFilterMode.Linear &&
        environmentResources.Sampler.Descriptor.MinFilter == GraphicsFilterMode.Linear &&
        environmentResources.Sampler.Descriptor.MipmapFilter == GraphicsFilterMode.Linear,
        "HDR cube GPU linear sampler",
        failures);

    RecordedTextureWrite cubeWrite = graphicsDevice.TextureWrites[^1];
    Expect(
        ReferenceEquals(cubeWrite.Destination, environmentResources.Texture) &&
        cubeWrite.MipLevel == 0 && cubeWrite.Origin == new GraphicsOrigin3D() &&
        cubeWrite.Size == new GraphicsExtent3D(2, 2, 6) &&
        cubeWrite.BytesPerRow == 16 && cubeWrite.RowsPerImage == 2 &&
        cubeWrite.Data.Length == 192,
        "HDR cube GPU six-layer write layout",
        failures);
    ReadOnlySpan<Half> uploadedTexels = MemoryMarshal.Cast<byte, Half>(cubeWrite.Data);
    ExpectNear((float)uploadedTexels[0], -0.25f, 0.00001f, "HDR cube FP16 negative upload", failures);
    ExpectNear((float)uploadedTexels[1], 2f, 0.00001f, "HDR cube FP16 green upload", failures);
    ExpectNear((float)uploadedTexels[2], 4f, 0.00001f, "HDR cube FP16 blue upload", failures);
    ExpectNear((float)uploadedTexels[3], 1f, 0.00001f, "HDR cube FP16 alpha upload", failures);
}
int writesBeforeSpecularUpload = graphicsDevice.TextureWrites.Count;
using (EnvironmentCubeGpuResources specularResources =
       EnvironmentCubeGpuResources.Create(graphicsDevice, constantSpecularCube))
{
    Expect(
        specularResources.Texture.Descriptor.MipLevelCount == 3 &&
        specularResources.View.Descriptor.MipLevelCount == 3 &&
        graphicsDevice.TextureWrites.Count == writesBeforeSpecularUpload + 3,
        "specular cube uploads complete mip chain",
        failures);
    IReadOnlyList<RecordedTextureWrite> specularWrites = graphicsDevice.TextureWrites
        .Skip(writesBeforeSpecularUpload)
        .ToArray();
    Expect(
        specularWrites[0].MipLevel == 0 && specularWrites[0].Size == new GraphicsExtent3D(4, 4, 6) &&
        specularWrites[1].MipLevel == 1 && specularWrites[1].Size == new GraphicsExtent3D(2, 2, 6) &&
        specularWrites[2].MipLevel == 2 && specularWrites[2].Size == new GraphicsExtent3D(1, 1, 6),
        "specular cube mip write dimensions",
        failures);
}
HdrEnvironmentCube excessiveFp16Cube = HdrEnvironmentConverter.ConvertToCube(
    new EquirectangularHdrEnvironment(
        1,
        1,
        [new Vector3(100_000f)],
        StandardColorSpaces.LinearSrgb),
    1,
    StandardColorSpaces.LinearSrgb);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = EnvironmentCubeGpuResources.Create(graphicsDevice, excessiveFp16Cube),
    "HDR cube refuses FP16 infinity",
    failures);
using SceneRenderer renderer = new(graphicsDevice, GraphicsTextureFormat.Rgba16Float);
Expect(!renderer.AmbientOcclusionEnabled, "ambient occlusion is an explicit scene cost", failures);
Expect(!renderer.BloomEnabled, "HDR bloom is an explicit scene post-process", failures);
Expect(renderer.RenderLayer == SceneRenderLayer.Beauty, "default scene render layer", failures);
Expect(renderer.RenderOutput == RenderOutputIds.Beauty, "default stable render output", failures);
foreach (SceneRenderLayer layer in Enum.GetValues<SceneRenderLayer>())
{
    RenderOutputId output = RenderOutputIds.FromSceneRenderLayer(layer);
    Expect(output.IsValid, $"stable output identifier for {layer}", failures);
    Expect(
        RenderOutputIds.TryGetSceneRenderLayer(output, out SceneRenderLayer roundTripped) &&
        roundTripped == layer,
        $"stable output round trip for {layer}",
        failures);
}
Expect(
    !RenderOutputIds.TryGetSceneRenderLayer(new RenderOutputId("example.outline"), out _),
    "extension output stays outside built-in renderer mapping",
    failures);
ExpectThrows<ArgumentException>(
    () => renderer.RenderOutput = new RenderOutputId("example.outline"),
    "scene renderer rejects an unimplemented stable output",
    failures);
RenderOutputRegistry outputRegistry = RenderOutputRegistry.CreateDefault();
ulong builtInOutputRegistryRevision = outputRegistry.Revision;
Expect(outputRegistry.Contains(RenderOutputIds.Beauty), "default output registry contains Beauty", failures);
Expect(
    outputRegistry.Outputs.Count == Enum.GetValues<SceneRenderLayer>().Length,
    "default output registry enumerates every built-in output",
    failures);
IRenderPass explicitBeautyPass = outputRegistry.CreatePass(
    RenderOutputIds.Beauty,
    renderer,
    SceneRenderPassOptions.Default,
    "explicit Beauty");
Expect(
    explicitBeautyPass is SceneRenderOutputPass beautyPass &&
    beautyPass.Output == RenderOutputIds.Beauty,
    "default output registry creates an explicit scene-output pass",
    failures);
RenderOutputId extensionOutput = new("example.outline");
outputRegistry.Register(
    extensionOutput,
    static (sceneRenderer, options, name) => new SceneRenderPass(sceneRenderer, options, name));
Expect(
    outputRegistry.Revision != builtInOutputRegistryRevision &&
    outputRegistry.Contains(extensionOutput),
    "explicit extension output registration changes the registry revision",
    failures);
ExpectThrows<InvalidOperationException>(
    () => outputRegistry.Register(
        extensionOutput,
        static (sceneRenderer, options, name) => new SceneRenderPass(sceneRenderer, options, name)),
    "duplicate output registration requires explicit replacement",
    failures);
Expect(
    outputRegistry.Remove(extensionOutput) && !outputRegistry.Contains(extensionOutput),
    "extension output registration can be removed explicitly",
    failures);
ExpectThrows<ArgumentException>(
    () => outputRegistry.Contains(default),
    "output registry rejects an uninitialized identifier",
    failures);
ExpectNear(renderer.AmbientOcclusionRadius, 0.65f, 0.0001f, "default AO radius", failures);
ExpectNear(renderer.AmbientOcclusionStrength, 1f, 0.0001f, "default AO strength", failures);
ExpectNear(renderer.BloomThreshold, 1f, 0.0001f, "default bloom threshold", failures);
ExpectNear(renderer.BloomSoftKnee, 0.5f, 0.0001f, "default bloom soft knee", failures);
ExpectNear(renderer.BloomIntensity, 0.15f, 0.0001f, "default bloom intensity", failures);
ExpectNear(renderer.BloomRadiusPixels, 16f, 0.0001f, "default bloom pixel radius", failures);
Expect(
    renderer.BloomCompositeMode == BloomCompositeMode.EnergyPreserving,
    "default bloom composition preserves energy",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => renderer.AmbientOcclusionRadius = 0f,
    "AO rejects zero radius",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => renderer.AmbientOcclusionStrength = 1.01f,
    "AO rejects excessive strength",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => renderer.BloomThreshold = -0.01f,
    "bloom rejects negative threshold",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => renderer.BloomSoftKnee = float.NaN,
    "bloom rejects non-finite soft knee",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => renderer.BloomIntensity = -0.01f,
    "bloom rejects negative intensity",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => renderer.BloomRadiusPixels = 0f,
    "bloom rejects zero radius",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => renderer.BloomCompositeMode = (BloomCompositeMode)99,
    "bloom rejects unknown composition mode",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => renderer.RenderLayer = (SceneRenderLayer)99,
    "scene render layer validation",
    failures);
renderer.AmbientOcclusionEnabled = true;
using RecordingGraphicsTexture colorTarget = (RecordingGraphicsTexture)graphicsDevice.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(64, 64),
        GraphicsTextureFormat.Rgba16Float,
        GraphicsTextureUsage.RenderAttachment));
using GraphicsTexture depthTarget = graphicsDevice.CreateTexture(new GraphicsTextureDescriptor(
    new GraphicsExtent3D(64, 64),
    GraphicsTextureFormat.Depth32Float,
    GraphicsTextureUsage.RenderAttachment));
Scene renderScene = new("render scene");
UnlitMaterial firstMaterial = new(hdrBlue, "HDR blue");
PbrMaterial secondMaterial = new(
    new LinearRgba(0.8f, 0.2f, 0.1f, 1f, StandardColorSpaces.LinearSrgb),
    metallic: 0.7f,
    roughness: 0.25f,
    name: "PBR red")
{
    IsDoubleSided = true,
    EmissiveColor = new LinearRgba(
        0.2f,
        0.1f,
        0.05f,
        1f,
        StandardColorSpaces.LinearDisplayP3),
    EmissiveStrength = 4f,
    EmissiveTexture = emissiveImage,
    BaseColorTexture = materialImage,
    NormalTexture = normalDataImage,
    OcclusionRoughnessMetallicTexture = ormDataImage,
    NormalScale = 0.8f,
    ClearcoatFactor = 0.75f,
    ClearcoatRoughness = 0.2f,
    ClearcoatNormalScale = 0.6f,
    ClearcoatTexture = ormDataImage,
    ClearcoatRoughnessTexture = ormDataImage,
    ClearcoatNormalTexture = normalDataImage,
    SheenColor = new LinearRgba(0.4f, 0.2f, 0.1f, 1f, StandardColorSpaces.LinearSrgb),
    SheenRoughness = 0.5f,
    TextureSampling = new MaterialTextureSampling(
        MaterialTextureAddressMode.MirrorRepeat,
        MaterialTextureAddressMode.ClampToEdge,
        MaterialTextureFilter.Nearest),
    TextureCoordinateScale = new Vector2(2f),
    TextureCoordinateOffset = new Vector2(0.25f, 0f),
    AlphaMode = MaterialAlphaMode.Mask,
    AlphaCutoff = 0.5f,
};
secondMaterial.NormalTextureMapping = new MaterialTextureMapping(
    MaterialTextureSampling.RepeatingLinear,
    textureCoordinateSet: 1,
    scale: new Vector2(0.5f, 0.75f),
    offset: new Vector2(-0.1f, 0.2f),
    rotation: 0.5f);
secondMaterial.EmissiveTextureMapping = new MaterialTextureMapping(
    new MaterialTextureSampling(
        MaterialTextureAddressMode.ClampToEdge,
        MaterialTextureAddressMode.Repeat,
        MaterialTextureFilter.Linear),
    textureCoordinateSet: 1,
    scale: new Vector2(1.25f, 0.75f),
    offset: new Vector2(0.2f, -0.3f),
    rotation: 0.125f);
secondMaterial.OcclusionRoughnessMetallicTextureMapping = new MaterialTextureMapping(
    new MaterialTextureSampling(
        MaterialTextureAddressMode.ClampToEdge,
        MaterialTextureAddressMode.MirrorRepeat,
        MaterialTextureFilter.Linear));
Mesh firstMesh = new(geometry, firstMaterial, "first");
firstMesh.MorphWeights = [0.75f];
Mesh secondMesh = new(geometry, secondMaterial, "second");
secondMesh.Transform.Position = new Vector3(0.5f, 0, 0);
Mesh shadowOnlyMesh = new(geometry, firstMaterial, "shadow-only")
{
    ShadowCastingMode = MeshShadowCastingMode.ShadowsOnly,
};
shadowOnlyMesh.Transform.Position = new Vector3(-0.5f, 0, 0);
ExpectThrows<ArgumentOutOfRangeException>(
    () => shadowOnlyMesh.ShadowCastingMode = (MeshShadowCastingMode)99,
    "mesh shadow-casting mode range",
    failures);
renderScene.Add(firstMesh);
renderScene.Add(secondMesh);
renderScene.Add(shadowOnlyMesh);
DirectionalLight renderLight = new(
    new LinearRgba(1, 0.95f, 0.9f, 1, StandardColorSpaces.LinearDisplayP3),
    intensity: 3f,
    name: "key light");
renderLight.CastsShadows = true;
renderScene.Add(renderLight);
PerspectiveCamera renderCamera = new(aspectRatio: 1f);
renderCamera.Transform.Position = new Vector3(0, 0, 4);
renderer.Render(renderScene, renderCamera, colorTarget, depthTarget);
Expect(colorTarget.IndexedDrawCount == 2, "scene renderer indexed draw count", failures);
using RecordingGraphicsTexture explicitOutputTarget =
    (RecordingGraphicsTexture)graphicsDevice.CreateTexture(new GraphicsTextureDescriptor(
        new GraphicsExtent3D(64, 64),
        GraphicsTextureFormat.Rgba16Float,
        GraphicsTextureUsage.RenderAttachment));
using GraphicsTexture explicitOutputDepth = graphicsDevice.CreateTexture(new GraphicsTextureDescriptor(
    new GraphicsExtent3D(64, 64),
    GraphicsTextureFormat.Depth32Float,
    GraphicsTextureUsage.RenderAttachment));
renderer.RenderLayer = SceneRenderLayer.Emissive;
renderer.RenderOutputPass(
    renderScene,
    renderCamera,
    explicitOutputTarget,
    explicitOutputDepth,
    RenderOutputIds.SurfaceNormal,
    SceneRenderPassOptions.Default);
RecordingGraphicsBuffer frameUniform = graphicsDevice.Buffers.Single(buffer =>
    buffer.Label == "scene frame uniform");
Expect(
    MemoryMarshal.Cast<byte, float>(frameUniform.Data)[34] ==
        (float)SceneRenderLayer.SurfaceNormal,
    "explicit output pass uploads its immutable output",
    failures);
Expect(
    renderer.RenderLayer == SceneRenderLayer.Emissive,
    "explicit output pass preserves renderer compatibility state",
    failures);
renderer.RenderLayer = SceneRenderLayer.Beauty;
secondMesh.VisibilityMask = SceneVisibilityMask.FromLayer(1);
using RecordingGraphicsTexture layeredColorTarget = (RecordingGraphicsTexture)graphicsDevice.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(32, 32),
        GraphicsTextureFormat.Rgba16Float,
        GraphicsTextureUsage.RenderAttachment,
        label: "layered scene color"));
using RecordingGraphicsTexture layeredDepthTarget = (RecordingGraphicsTexture)graphicsDevice.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(32, 32),
        GraphicsTextureFormat.Depth32Float,
        GraphicsTextureUsage.RenderAttachment,
        label: "layered scene depth"));
SceneRenderPassOptions loadPass = new(
    GraphicsLoadOperation.Load,
    new LinearRgba(0, 0, 0, 1, StandardColorSpaces.LinearDisplayP3),
    GraphicsLoadOperation.Load,
    0.75f);
renderer.RenderPass(renderScene, renderCamera, layeredColorTarget, layeredDepthTarget, loadPass);
Expect(
    layeredColorTarget.IndexedDrawCount == 1,
    "scene renderer filters meshes by camera visibility mask",
    failures);
Expect(
    layeredColorTarget.LastColorLoadOperation == GraphicsLoadOperation.Load &&
    layeredDepthTarget.LastDepthLoadOperation == GraphicsLoadOperation.Load &&
    loadPass.SizeDependency == RenderPassSizeDependency.OutputExtent,
    "scene render pass preserves explicit color/depth load behavior",
    failures);
LinearRgba p3PassClear = new(0.2f, 0.4f, 1.5f, 1f, StandardColorSpaces.LinearDisplayP3);
renderer.RenderPass(
    renderScene,
    renderCamera,
    layeredColorTarget,
    layeredDepthTarget,
    new SceneRenderPassOptions(GraphicsLoadOperation.Clear, p3PassClear));
LinearRgba expectedPassClear = StandardLinearRgbConverter.Convert(
    p3PassClear,
    StandardColorSpaces.LinearSrgb);
Expect(
    layeredColorTarget.LastClearColor == new GraphicsClearColor(
        expectedPassClear.Red,
        expectedPassClear.Green,
        expectedPassClear.Blue,
        expectedPassClear.Alpha),
    "scene render pass converts tagged HDR clear into the working space",
    failures);
secondMesh.VisibilityMask = SceneVisibilityMask.Default;
ExpectThrows<ArgumentException>(
    () => new SceneRenderPassOptions(GraphicsLoadOperation.Clear, default),
    "scene render pass rejects untagged clear color",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => new SceneRenderPassOptions(
        GraphicsLoadOperation.Clear,
        new LinearRgba(0, 0, 0, 1, StandardColorSpaces.LinearSrgb),
        clearDepth: 1.1f),
    "scene render pass validates depth clear",
    failures);

using (RecordingGraphicsDevice orderedDevice = new())
using (SceneRenderer orderedRenderer = new(
    orderedDevice,
    GraphicsTextureFormat.Rgba16Float))
using (RecordingGraphicsTexture orderedColorTarget =
    (RecordingGraphicsTexture)orderedDevice.CreateTexture(new GraphicsTextureDescriptor(
        new GraphicsExtent3D(16, 16),
        GraphicsTextureFormat.Rgba16Float,
        GraphicsTextureUsage.RenderAttachment,
        label: "ordered color")))
using (RecordingGraphicsTexture orderedDepthTarget =
    (RecordingGraphicsTexture)orderedDevice.CreateTexture(new GraphicsTextureDescriptor(
        new GraphicsExtent3D(16, 16),
        GraphicsTextureFormat.Depth32Float,
        GraphicsTextureUsage.RenderAttachment,
        label: "ordered depth")))
{
    Scene orderedScene = new("ordered scene");
    PerspectiveCamera orderedCamera = new(aspectRatio: 1f);
    SceneRenderPass orderedScenePass = new(
        orderedRenderer,
        new SceneRenderPassOptions(
            GraphicsLoadOperation.Clear,
            p3PassClear,
            GraphicsLoadOperation.Clear,
            0.625f),
        "ordered scene clear");
    RenderPassPipeline scenePipeline = new([orderedScenePass]);
    RenderPassContext orderedContext = new(
        orderedScene,
        orderedCamera,
        orderedColorTarget,
        orderedDepthTarget,
        StandardColorSpaces.LinearSrgb);
    RenderPassExecutionResult scenePipelineResult = scenePipeline.Execute(orderedContext);
    Expect(
        orderedColorTarget.LastColorLoadOperation == GraphicsLoadOperation.Clear &&
        orderedDepthTarget.LastDepthLoadOperation == GraphicsLoadOperation.Clear &&
        scenePipelineResult.ColorTargetInitialized &&
        scenePipelineResult.DepthTargetInitialized &&
        orderedScenePass.Descriptor.Outputs ==
            (RenderPassOutputs.Color | RenderPassOutputs.Depth) &&
        orderedScenePass.Descriptor.SizeDependency == RenderPassSizeDependency.OutputExtent,
        "scene pass executes through ordered composition",
        failures);

    List<string> passOrder = [];
    List<(bool Color, bool Depth)> passInputs = [];
    RenderPassColorAttachmentPolicy clearColorPolicy = new(
        GraphicsLoadOperation.Clear,
        new LinearRgba(0, 0, 0, 1, StandardColorSpaces.LinearSrgb));
    RenderPassDepthAttachmentPolicy clearDepthPolicy = new(GraphicsLoadOperation.Clear);
    RecordingOrderedRenderPass firstOrderedPass = new(
        new RenderPassDescriptor(
            "first clear",
            StandardColorSpaces.LinearSrgb,
            clearColorPolicy,
            clearDepthPolicy),
        context =>
        {
            passOrder.Add("first");
            passInputs.Add((context.ColorTargetInitialized, context.DepthTargetInitialized));
        });
    RecordingOrderedRenderPass secondOrderedPass = new(
        new RenderPassDescriptor(
            "second load",
            StandardColorSpaces.LinearSrgb,
            new RenderPassColorAttachmentPolicy(
                GraphicsLoadOperation.Load,
                clearColorPolicy.ClearColor),
            new RenderPassDepthAttachmentPolicy(GraphicsLoadOperation.Load)),
        context =>
        {
            passOrder.Add("second");
            passInputs.Add((context.ColorTargetInitialized, context.DepthTargetInitialized));
        });
    RenderPassPipeline orderedPipeline = new([firstOrderedPass, secondOrderedPass]);
    RenderPassExecutionResult orderedResult = orderedPipeline.Execute(orderedContext);
    Expect(
        passOrder.SequenceEqual(["first", "second"]) &&
        passInputs.SequenceEqual([(false, false), (true, true)]) &&
        orderedResult.ColorTargetInitialized &&
        orderedResult.DepthTargetInitialized &&
        ReferenceEquals(orderedPipeline.Descriptors[1], secondOrderedPass.Descriptor),
        "ordered render passes retain order and attachment state",
        failures);

    RecordingOrderedRenderPass uninitializedLoadPass = new(
        new RenderPassDescriptor(
            "invalid initial load",
            StandardColorSpaces.LinearSrgb,
            new RenderPassColorAttachmentPolicy(
                GraphicsLoadOperation.Load,
                clearColorPolicy.ClearColor)),
        static _ => { });
    ExpectThrows<InvalidOperationException>(
        () => new RenderPassPipeline([uninitializedLoadPass]).Execute(orderedContext),
        "ordered render pipeline rejects initial color load",
        failures);
    Expect(
        uninitializedLoadPass.ExecutionCount == 0,
        "invalid ordered pass is rejected before execution",
        failures);

    RecordingOrderedRenderPass mismatchedColorSpacePass = new(
        new RenderPassDescriptor(
            "mismatched color space",
            StandardColorSpaces.LinearDisplayP3,
            new RenderPassColorAttachmentPolicy(GraphicsLoadOperation.Clear, p3PassClear)),
        static _ => { });
    ExpectThrows<InvalidOperationException>(
        () => new RenderPassPipeline([mismatchedColorSpacePass]).Execute(orderedContext),
        "ordered render pipeline rejects working-space mismatch",
        failures);
    ExpectThrows<InvalidOperationException>(
        () => new RenderPassPipeline([firstOrderedPass]).Execute(new RenderPassContext(
            orderedScene,
            orderedCamera,
            orderedColorTarget,
            depthTarget: null,
            StandardColorSpaces.LinearSrgb)),
        "ordered render pipeline rejects missing declared depth",
        failures);
}

secondMesh.Geometry = bentGeometry;
ExpectThrows<InvalidOperationException>(
    () => renderer.Render(renderScene, renderCamera, colorTarget, depthTarget),
    "material image requires UVs",
    failures);
MeshGeometry uv0OnlyGeometry = new(
    [new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(0, 1, 0)],
    [0, 1, 2],
    [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
    [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]);
secondMesh.Geometry = uv0OnlyGeometry;
ExpectThrows<InvalidOperationException>(
    () => renderer.Render(renderScene, renderCamera, colorTarget, depthTarget),
    "material texture mapping requires selected UV set",
    failures);
secondMesh.Geometry = geometry;
Expect(
    colorTarget.LastClearColor == new GraphicsClearColor(0, 0, 0, 1),
    "scene renderer default clear is exact linear black",
    failures);
Expect(
    colorTarget.CullModes.SequenceEqual([GraphicsCullMode.Back, GraphicsCullMode.None]),
    "material cull-mode selection",
    failures);
Expect(
    renderer.CachedGeometryCount == 1 && renderer.CachedMeshCount == 3 &&
    renderer.CachedMaterialTextureCount == 2 &&
    renderer.CachedMaterialDataTextureCount == 2,
    "scene renderer shares geometry and images while retaining the shadow-only mesh",
    failures);
Expect(
    graphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.FragmentEntryPoint == "fs_pbr" &&
        descriptor.FragmentShader is not null &&
        descriptor.Layout?.Descriptor.BindGroupLayouts.Count == 4 &&
        descriptor.Layout.Descriptor.BindGroupLayouts[1].Descriptor.Entries.Count == 23 &&
        descriptor.Layout.Descriptor.BindGroupLayouts[3].Descriptor.Entries.Count == 5 &&
        descriptor.FragmentShader.Descriptor.Code.Contains("texture_cube<f32>", StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains("textureSampleLevel", StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains("environment_brdf", StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains("textureSampleCompare", StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains("textureLoad(shadow_map", StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "screen_space_ambient_occlusion",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "index < 32u",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "rotation_angle = noise",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "ambient_occlusion * draw.material_parameters.z",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "render_layer == 5u",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "specular_ambient_visibility",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "receiver_depth_gradient",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "dot(receiver_depth_gradient, sample_offset)",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "geometric_normal: vec3f",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "let sampled_visibility = visibility / 32.0",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "clamp(frame.shadow_parameters.x, 0.0, 1.0)",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "input.world_bent_normal",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "resolve_material_alpha",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "material_base_color",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "render_layer == 8u",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "apply_normal_map",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "material_orm",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "map_texture_coordinate",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "render_layer == 10u",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "render_layer == 11u",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "render_layer == 15u",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "render_layer == 16u",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "effective_diffuse_transmission",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "opposite_environment_radiance",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "dielectric_f0 * specular_factor",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "iridescent_environment_f90 * integrated_brdf.y",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "scene_background",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "refract(",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "transmission_parameters",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "skin_position",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "var<storage, read> skin_joints",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "var<storage, read> morph_deltas",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "morph_position",
            StringComparison.Ordinal) &&
        !descriptor.FragmentShader.Descriptor.Code.Contains(
            "for (var target ",
            StringComparison.Ordinal) &&
        descriptor.VertexBuffers.Count == 1 &&
        descriptor.FragmentShader.Descriptor.Code.Contains("authored_tangent", StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains("input.world_tangent", StringComparison.Ordinal) &&
        descriptor.VertexBuffers[0].ArrayStride == 100 &&
        descriptor.VertexBuffers[0].Attributes.Count == 8),
    "PBR pipeline geometry, skin, material-image, and environment inputs",
    failures);
RecordingGraphicsBuffer morphBuffer = graphicsDevice.Buffers.First(buffer =>
    buffer.Label == "scene geometry FP32 morph deltas");
ReadOnlySpan<float> morphValues = MemoryMarshal.Cast<byte, float>(morphBuffer.Data);
ExpectNear(morphValues[0], 0.25f, 0f, "morph position delta GPU upload", failures);
ExpectNear(morphValues[29], 0.1f, 0f, "morph normal delta GPU upload", failures);
ExpectNear(morphValues[20], 0.2f, 0f, "morph tangent delta GPU upload", failures);
RecordingGraphicsBuffer morphDrawUniform = graphicsDevice.Buffers.First(buffer =>
    buffer.Label == "scene mesh draw uniform");
ReadOnlySpan<float> morphDrawValues = MemoryMarshal.Cast<byte, float>(morphDrawUniform.Data);
ExpectNear(morphDrawValues[48], 0.75f, 0f, "morph weight draw upload", failures);
ExpectNear(morphDrawValues[56], 1f, 0f, "morph target count draw upload", failures);
RecordingGraphicsBuffer pbrDrawUniform = graphicsDevice.Buffers
    .Where(buffer => buffer.Label == "scene mesh draw uniform")
    .ElementAt(1);
ReadOnlySpan<float> pbrDrawValues = MemoryMarshal.Cast<byte, float>(pbrDrawUniform.Data);
LinearRgba expectedEmissive = StandardLinearRgbConverter.Convert(
    secondMaterial.EmissiveColor,
    StandardColorSpaces.LinearSrgb);
ExpectNear(pbrDrawValues[60], expectedEmissive.Red, 0.00001f, "emissive red draw upload", failures);
ExpectNear(pbrDrawValues[61], expectedEmissive.Green, 0.00001f, "emissive green draw upload", failures);
ExpectNear(pbrDrawValues[62], expectedEmissive.Blue, 0.00001f, "emissive blue draw upload", failures);
ExpectNear(pbrDrawValues[63], 4f, 0f, "HDR emissive strength draw upload", failures);
ExpectNear(pbrDrawValues[64], 2f, 0.00001f, "base-color UV matrix X scale", failures);
ExpectNear(pbrDrawValues[70], 0f, 0f, "base-color UV set upload", failures);
ExpectNear(pbrDrawValues[78], 1f, 0f, "normal UV set upload", failures);
ExpectNear(pbrDrawValues[73], MathF.Sin(0.5f) * 0.75f, 0.00001f, "normal UV rotation row zero", failures);
ExpectNear(pbrDrawValues[74], -MathF.Sin(0.5f) * 0.5f, 0.00001f, "normal UV rotation row one", failures);
ExpectNear(pbrDrawValues[89], MathF.Sin(0.125f) * 0.75f, 0.00001f, "emissive UV rotation row zero", failures);
ExpectNear(pbrDrawValues[94], 1f, 0f, "emissive UV set upload", failures);
ExpectNear(pbrDrawValues[96], 0.75f, 0f, "clearcoat factor draw upload", failures);
ExpectNear(pbrDrawValues[97], 0.2f, 0f, "clearcoat roughness draw upload", failures);
ExpectNear(pbrDrawValues[98], 0.6f, 0f, "clearcoat normal scale draw upload", failures);
RecordedTextureWrite materialImageWrite = graphicsDevice.TextureWrites.Single(write =>
    write.Destination.Descriptor.Label == "test P3 material image" && write.MipLevel == 0);
ReadOnlySpan<Half> materialImageTexels = MemoryMarshal.Cast<byte, Half>(materialImageWrite.Data);
LinearRgba expectedMaterialPixel = StandardLinearRgbConverter.Convert(
    new LinearRgba(1.2f, 0.1f, 0.2f, 1f, StandardColorSpaces.LinearDisplayP3),
    StandardColorSpaces.LinearSrgb);
ExpectNear(
    (float)materialImageTexels[0],
    expectedMaterialPixel.Red,
    0.001f,
    "material image converts P3 red to renderer working space",
    failures);
Expect(
    materialImageWrite.BytesPerRow == 16 && materialImageWrite.RowsPerImage == 1,
    "material image FP16 upload layout",
    failures);
RecordedTextureWrite normalDataWrite = graphicsDevice.TextureWrites.Single(write =>
    write.Destination.Descriptor.Label == "test normal data" && write.MipLevel == 0);
RecordedTextureWrite ormDataWrite = graphicsDevice.TextureWrites.Single(write =>
    write.Destination.Descriptor.Label == "test ORM data" && write.MipLevel == 0);
Expect(
    normalDataWrite.Destination.Descriptor.Format == GraphicsTextureFormat.Rgba8Unorm &&
    normalDataWrite.Data[0] == 128 && normalDataWrite.Data[2] == 255 &&
    ormDataWrite.Data[0] == 255 && ormDataWrite.Data[1] == 64 && ormDataWrite.Data[2] == 0,
    "normal and ORM data upload without color conversion",
    failures);
RecordedTextureWrite emissiveImageWrite = graphicsDevice.TextureWrites.Single(write =>
    write.Destination.Descriptor.Label == "test P3 emissive image" && write.MipLevel == 0);
ReadOnlySpan<Half> emissiveImageTexels = MemoryMarshal.Cast<byte, Half>(emissiveImageWrite.Data);
LinearRgba expectedEmissivePixel = StandardLinearRgbConverter.Convert(
    new LinearRgba(0.25f, 0.5f, 1.25f, 1f, StandardColorSpaces.LinearDisplayP3),
    StandardColorSpaces.LinearSrgb);
ExpectNear(
    (float)emissiveImageTexels[2],
    expectedEmissivePixel.Blue,
    0.001f,
    "emissive image converts to renderer working space",
    failures);
RecordedTextureWrite materialMip = graphicsDevice.TextureWrites.Single(write =>
    write.Destination.Descriptor.Label == "test P3 material image" && write.MipLevel == 1);
RecordedTextureWrite normalMip = graphicsDevice.TextureWrites.Single(write =>
    write.Destination.Descriptor.Label == "test normal data" && write.MipLevel == 1);
RecordedTextureWrite ormMip = graphicsDevice.TextureWrites.Single(write =>
    write.Destination.Descriptor.Label == "test ORM data" && write.MipLevel == 1);
ReadOnlySpan<Half> materialMipTexels = MemoryMarshal.Cast<byte, Half>(materialMip.Data);
LinearRgba expectedMaterialPixel2 = StandardLinearRgbConverter.Convert(
    new LinearRgba(0.1f, 0.3f, 1.5f, 1f, StandardColorSpaces.LinearDisplayP3),
    StandardColorSpaces.LinearSrgb);
Vector3 decodedNormalMip = new(
    normalMip.Data[0] / 255f * 2f - 1f,
    normalMip.Data[1] / 255f * 2f - 1f,
    normalMip.Data[2] / 255f * 2f - 1f);
Expect(
    materialMip.Destination.Descriptor.MipLevelCount == 2 &&
    materialMip.Size == new GraphicsExtent3D(1, 1) &&
    MathF.Abs((float)materialMipTexels[0] -
        ((expectedMaterialPixel.Red + expectedMaterialPixel2.Red) * 0.5f)) < 0.001f &&
    normalMip.Destination.Descriptor.MipLevelCount == 2 &&
    MathF.Abs(decodedNormalMip.Length() - 1f) < 0.01f &&
    ormMip.Data[0] == 191 && ormMip.Data[1] == 128 && ormMip.Data[2] == 128,
    "deterministic color, normalized-normal, and linear-channel mip chains",
    failures);
Expect(
    graphicsDevice.SamplerDescriptors.Any(static descriptor =>
        descriptor.Label == "material texture sampler" &&
        descriptor.AddressModeU == GraphicsAddressMode.MirrorRepeat &&
        descriptor.AddressModeV == GraphicsAddressMode.ClampToEdge &&
        descriptor.MinFilter == GraphicsFilterMode.Nearest &&
        descriptor.MagFilter == GraphicsFilterMode.Nearest &&
        descriptor.MipmapFilter == GraphicsFilterMode.Nearest),
    "explicit material sampler mapping",
    failures);
Expect(
    graphicsDevice.SamplerDescriptors.Count(static descriptor =>
        descriptor.Label == "material texture sampler") >= 4 &&
    graphicsDevice.SamplerDescriptors.Any(static descriptor =>
        descriptor.Label == "material texture sampler" &&
        descriptor.AddressModeU == GraphicsAddressMode.Repeat &&
        descriptor.AddressModeV == GraphicsAddressMode.Repeat &&
        descriptor.MinFilter == GraphicsFilterMode.Linear) &&
    graphicsDevice.SamplerDescriptors.Any(static descriptor =>
        descriptor.Label == "material texture sampler" &&
        descriptor.AddressModeU == GraphicsAddressMode.ClampToEdge &&
        descriptor.AddressModeV == GraphicsAddressMode.MirrorRepeat),
    "independent per-slot GPU sampler creation",
    failures);
Expect(
    graphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.Blend == GraphicsBlendState.PremultipliedAlpha &&
        descriptor.DepthStencil?.DepthWriteEnabled == false),
    "premultiplied transparent pipeline disables depth writes",
    failures);
string sceneShaderCode = graphicsDevice.PipelineDescriptors
    .First(static descriptor => descriptor.FragmentEntryPoint == "fs_pbr")
    .FragmentShader!
    .Descriptor
    .Code;
int environmentSpecularDeclaration = sceneShaderCode.IndexOf(
    "let environment_specular_lighting =",
    StringComparison.Ordinal);
int environmentSpecularLayerUse = sceneShaderCode.IndexOf(
    "diffuse_transmission_environment_lighting +",
    StringComparison.Ordinal);
Expect(
    environmentSpecularDeclaration >= 0 &&
    environmentSpecularLayerUse > environmentSpecularDeclaration &&
    sceneShaderCode.Contains(
        "draw.emissive_color.rgb * draw.emissive_color.a",
        StringComparison.Ordinal) &&
    sceneShaderCode.Contains("material_emissive", StringComparison.Ordinal) &&
    sceneShaderCode.Contains("material_clearcoat_normal", StringComparison.Ordinal) &&
    sceneShaderCode.Contains("clearcoat_layer_attenuation", StringComparison.Ordinal) &&
    sceneShaderCode.Contains("clearcoat_environment_specular", StringComparison.Ordinal) &&
    sceneShaderCode.Contains("distribution_ggx_anisotropic", StringComparison.Ordinal) &&
    sceneShaderCode.Contains("visibility_ggx_anisotropic", StringComparison.Ordinal) &&
    sceneShaderCode.Contains("distribution_charlie", StringComparison.Ordinal) &&
    sceneShaderCode.Contains("visibility_sheen", StringComparison.Ordinal) &&
    sceneShaderCode.Contains("render_layer == 12u", StringComparison.Ordinal) &&
    sceneShaderCode.Contains(
        "return material_fragment(sampled_emissive, output_alpha)",
        StringComparison.Ordinal),
    "IBL diagnostics and Beauty emissive contribution use declared shader values",
    failures);
Expect(
    graphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.FragmentShader is null &&
        descriptor.ColorFormat is null &&
        descriptor.DepthStencil?.Format == GraphicsTextureFormat.Depth32Float &&
        descriptor.CullMode == GraphicsCullMode.Back),
    "single-sided directional depth-only shadow pipeline",
    failures);
Expect(
    graphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.VertexEntryPoint == "vs_shadow" &&
        descriptor.FragmentShader is null &&
        descriptor.ColorFormat is null &&
        descriptor.DepthStencil?.Format == GraphicsTextureFormat.Depth32Float &&
        descriptor.CullMode == GraphicsCullMode.None),
    "double-sided directional depth-only shadow pipeline",
    failures);
Expect(
    graphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.VertexEntryPoint == "vs_depth_prepass" &&
        descriptor.FragmentShader is null &&
        descriptor.ColorFormat is null &&
        descriptor.DepthStencil?.Format == GraphicsTextureFormat.Depth32Float),
    "ambient-occlusion camera depth prepass pipeline",
    failures);
Expect(
    graphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.VertexEntryPoint == "vs_shadow" &&
        descriptor.FragmentEntryPoint == "fs_alpha_test_depth" &&
        descriptor.FragmentShader is not null &&
        descriptor.ColorFormat is null &&
        descriptor.DepthStencil?.Format == GraphicsTextureFormat.Depth32Float &&
        descriptor.CullMode == GraphicsCullMode.Back),
    "single-sided alpha-tested directional shadow pipeline",
    failures);
Expect(
    graphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.VertexEntryPoint == "vs_shadow" &&
        descriptor.FragmentEntryPoint == "fs_alpha_test_depth" &&
        descriptor.FragmentShader is not null &&
        descriptor.ColorFormat is null &&
        descriptor.DepthStencil?.Format == GraphicsTextureFormat.Depth32Float &&
        descriptor.CullMode == GraphicsCullMode.None),
    "double-sided alpha-tested directional shadow pipeline",
    failures);
Expect(
    graphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.VertexEntryPoint == "vs_depth_prepass" &&
        descriptor.FragmentEntryPoint == "fs_alpha_test_depth" &&
        descriptor.FragmentShader is not null &&
        descriptor.ColorFormat is null &&
        descriptor.DepthStencil?.Format == GraphicsTextureFormat.Depth32Float),
    "alpha-tested ambient-occlusion depth prepass pipeline",
    failures);
Expect(
    graphicsDevice.SubmittedPipelineLabels.Contains("scene directional shadow pipeline") &&
    graphicsDevice.SubmittedPipelineLabels.Contains(
        "scene double-sided alpha-tested directional shadow pipeline") &&
    graphicsDevice.SubmittedPipelineLabels.Contains("scene alpha-tested ambient-occlusion depth prepass pipeline"),
    "material sidedness and textured mask select matching shadow and AO depth passes",
    failures);
Expect(
    sceneShaderCode.Contains("@fragment fn fs_alpha_test_depth", StringComparison.Ordinal) &&
    sceneShaderCode.Contains("textureSample(", StringComparison.Ordinal) &&
    sceneShaderCode.Contains("material_base_color,", StringComparison.Ordinal) &&
    sceneShaderCode.Contains("discard;", StringComparison.Ordinal),
    "alpha-tested depth fragment samples texture alpha and discards",
    failures);
renderer.BloomEnabled = true;
renderer.BloomThreshold = 2f;
renderer.BloomSoftKnee = 0.75f;
renderer.BloomIntensity = 0.25f;
renderer.BloomRadiusPixels = 24f;
ExpectThrows<NotSupportedException>(
    () => renderer.RenderPass(renderScene, renderCamera, colorTarget, depthTarget, loadPass),
    "HDR bloom rejects loaded external color attachment",
    failures);
int submittedBeforeBloom = graphicsDevice.SubmittedPipelineLabels.Count;
renderer.Render(renderScene, renderCamera, colorTarget, depthTarget);
IReadOnlyList<string?> bloomSubmission = graphicsDevice.SubmittedPipelineLabels
    .Skip(submittedBeforeBloom)
    .ToArray();
Expect(
    bloomSubmission.Contains("scene bloom highlight extraction pipeline") &&
    bloomSubmission.Contains("scene bloom vertical blur pipeline") &&
    bloomSubmission.Contains("scene bloom HDR composite pipeline") &&
    graphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.Label == "scene bloom HDR composite pipeline" &&
        descriptor.FragmentShader?.Descriptor.Code.Contains(
            "scene.rgb - extracted + bloom",
            StringComparison.Ordinal) == true &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "textureLoad(",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "source_origin + vec2u(x, y)",
            StringComparison.Ordinal) &&
        descriptor.FragmentShader.Descriptor.Code.Contains(
            "scene.rgb + bloom * settings.intensity",
            StringComparison.Ordinal)),
    "Beauty HDR bloom extraction, separable blur, and additive composition",
    failures);
RecordingGraphicsBuffer bloomSettings = graphicsDevice.Buffers.Single(buffer =>
    buffer.Label == "scene HDR bloom settings");
ReadOnlySpan<float> bloomSettingValues = MemoryMarshal.Cast<byte, float>(bloomSettings.Data);
ExpectNear(bloomSettingValues[0], 2f, 0f, "bloom threshold upload", failures);
ExpectNear(bloomSettingValues[1], 0.75f, 0f, "bloom soft-knee upload", failures);
ExpectNear(bloomSettingValues[2], 0.25f, 0f, "bloom intensity upload", failures);
ExpectNear(bloomSettingValues[3], 12f, 0f, "bloom radius converts to selected pyramid texels", failures);
Expect(
    MemoryMarshal.Cast<byte, uint>(bloomSettings.Data)[4] ==
        (uint)BloomCompositeMode.EnergyPreserving,
    "energy-preserving bloom mode upload",
    failures);
Expect(
    MemoryMarshal.Cast<byte, uint>(bloomSettings.Data)[5] == 2u,
    "energy-preserving bloom downsample footprint upload",
    failures);
renderer.RenderLayer = SceneRenderLayer.Emissive;
int submittedBeforeDiagnostic = graphicsDevice.SubmittedPipelineLabels.Count;
renderer.Render(renderScene, renderCamera, colorTarget, depthTarget);
Expect(
    !graphicsDevice.SubmittedPipelineLabels
        .Skip(submittedBeforeDiagnostic)
        .Any(static label => label?.StartsWith("scene bloom", StringComparison.Ordinal) == true),
    "diagnostic render layers bypass bloom",
    failures);
renderer.RenderLayer = SceneRenderLayer.Beauty;
renderer.BloomEnabled = false;
renderScene.Remove(renderLight);
ExpectThrows<InvalidOperationException>(
    () => renderer.Render(renderScene, renderCamera, colorTarget, depthTarget),
    "PBR scene requires a light",
    failures);
renderScene.Add(imageBasedLight);
renderer.Render(renderScene, renderCamera, colorTarget, depthTarget);
Expect(
    colorTarget.IndexedDrawCount == 6 && renderer.CachedEnvironmentCount == 1,
    "PBR scene renders with diffuse IBL only",
    failures);
int writesAfterFirstEnvironmentRender = graphicsDevice.TextureWrites.Count;
renderer.Render(renderScene, renderCamera, colorTarget, depthTarget);
Expect(
    colorTarget.IndexedDrawCount == 8 &&
    graphicsDevice.TextureWrites.Count == writesAfterFirstEnvironmentRender,
    "diffuse IBL GPU resources are cached",
    failures);
ImageBasedLight secondImageBasedLight = new(extendedEnvironment, name: "second environment");
renderScene.Add(secondImageBasedLight);
ExpectThrows<NotSupportedException>(
    () => renderer.Render(renderScene, renderCamera, colorTarget, depthTarget),
    "multiple IBL rejection",
    failures);
renderScene.Remove(secondImageBasedLight);
renderScene.Remove(imageBasedLight);
renderScene.Add(renderLight);
renderScene.Remove(secondMesh);
renderer.TrimCaches(renderScene);
Expect(
    renderer.CachedGeometryCount == 1 && renderer.CachedMeshCount == 2 &&
    renderer.CachedEnvironmentCount == 0,
    "scene renderer trims removed mesh and environment but retains the shadow-only mesh",
    failures);

Mesh p3Mesh = new(
    geometry,
    new UnlitMaterial(new LinearRgba(1, 0, 0, 1, StandardColorSpaces.LinearDisplayP3)));
renderScene.Add(p3Mesh);
renderer.Render(renderScene, renderCamera, colorTarget, depthTarget);
Expect(colorTarget.IndexedDrawCount == 10, "renderer accepts standard wide-gamut material", failures);

using RecordingGraphicsDevice compressedGraphicsDevice = new();
using SceneRenderer compressedRenderer = new(
    compressedGraphicsDevice,
    GraphicsTextureFormat.Rgba16Float);
using RecordingGraphicsTexture compressedColorTarget =
    (RecordingGraphicsTexture)compressedGraphicsDevice.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(32, 32),
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment));
using GraphicsTexture compressedDepthTarget = compressedGraphicsDevice.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(32, 32),
        GraphicsTextureFormat.Depth32Float,
        GraphicsTextureUsage.RenderAttachment));
PbrMaterial compressedMaterial = new(
    new LinearRgba(1, 1, 1, 1, StandardColorSpaces.LinearSrgb))
{
    CompressedBaseColorTexture = compressedBaseTexture,
    CompressedEmissiveTexture = compressedBaseTexture,
    CompressedNormalTexture = compressedDataTexture,
    CompressedOcclusionRoughnessMetallicTexture = compressedDataTexture,
};
Scene compressedScene = new("compressed texture scene");
compressedScene.Add(new Mesh(geometry, compressedMaterial));
compressedScene.Add(new DirectionalLight(
    new LinearRgba(1, 1, 1, 1, StandardColorSpaces.LinearSrgb)));
compressedRenderer.Render(
    compressedScene,
    renderCamera,
    compressedColorTarget,
    compressedDepthTarget);
RecordedTextureWrite[] compressedBaseWrites = compressedGraphicsDevice.TextureWrites
    .Where(static write => write.Destination.Descriptor.Label == "compressed base color")
    .ToArray();
RecordedTextureWrite[] compressedDataWrites = compressedGraphicsDevice.TextureWrites
    .Where(static write => write.Destination.Descriptor.Label == "compressed material data")
    .ToArray();
Expect(
    compressedColorTarget.IndexedDrawCount == 1 &&
    compressedRenderer.CachedMaterialTextureCount == 1 &&
    compressedRenderer.CachedMaterialDataTextureCount == 2 &&
    compressedBaseWrites.Length == 3 && compressedDataWrites.Length == 6 &&
    compressedBaseWrites.All(static write =>
        write.Destination.Descriptor.Format == GraphicsTextureFormat.Bc7RgbaUnormSrgb) &&
    compressedDataWrites.All(static write =>
        write.Destination.Descriptor.Format == GraphicsTextureFormat.Astc4x4Unorm),
    "compressed PBR material upload, cache, and draw",
    failures);
Expect(
    compressedBaseWrites[0].BytesPerRow == 32 && compressedBaseWrites[0].RowsPerImage == 2 &&
    compressedBaseWrites[1].BytesPerRow == 16 && compressedBaseWrites[1].RowsPerImage == 1 &&
    compressedBaseWrites[2].Size == new GraphicsExtent3D(4, 4) &&
    compressedBaseWrites[2].BytesPerRow == 16 && compressedBaseWrites[2].RowsPerImage == 1 &&
    compressedDataWrites.Where(static write => write.MipLevel == 2).All(static write =>
        write.Size == new GraphicsExtent3D(4, 4) &&
        write.BytesPerRow == 16 && write.RowsPerImage == 1),
    "compressed material physical mip block layout",
    failures);
compressedScene.Remove(compressedScene.Root.Children.OfType<Mesh>().Single());
compressedRenderer.TrimCaches(compressedScene);
Expect(
    compressedRenderer.CachedMaterialTextureCount == 0 &&
    compressedRenderer.CachedMaterialDataTextureCount == 0,
    "compressed material cache trimming",
    failures);

using RecordingGraphicsDevice skinGraphicsDevice = new();
using SceneRenderer skinRenderer = new(skinGraphicsDevice, GraphicsTextureFormat.Rgba16Float);
using RecordingGraphicsTexture skinColorTarget = (RecordingGraphicsTexture)skinGraphicsDevice.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(32, 32),
        GraphicsTextureFormat.Rgba16Float,
        GraphicsTextureUsage.RenderAttachment));
using GraphicsTexture skinDepthTarget = skinGraphicsDevice.CreateTexture(new GraphicsTextureDescriptor(
    new GraphicsExtent3D(32, 32),
    GraphicsTextureFormat.Depth32Float,
    GraphicsTextureUsage.RenderAttachment));
Scene skinScene = new("skin scene");
animatedJoint.Transform.Position = Vector3.UnitX;
skinScene.Add(animatedJoint);
Mesh skinnedMesh = new(skinnedGeometry, new UnlitMaterial(hdrBlue), "skinned triangle");
skinScene.Add(skinnedMesh);
PerspectiveCamera skinCamera = new(aspectRatio: 1f);
skinCamera.Transform.Position = new Vector3(0, 0, 4);
ExpectThrows<InvalidOperationException>(
    () => skinRenderer.Render(skinScene, skinCamera, skinColorTarget, skinDepthTarget),
    "renderer requires a skin for weighted geometry",
    failures);
skinnedMesh.Skin = skin;
skinRenderer.Render(skinScene, skinCamera, skinColorTarget, skinDepthTarget);
RecordingGraphicsBuffer skinPalette = skinGraphicsDevice.Buffers.Single(buffer =>
    buffer.Label == "scene mesh FP32 skin palette");
ReadOnlySpan<Matrix4x4> skinMatrices = MemoryMarshal.Cast<byte, Matrix4x4>(skinPalette.Data);
Expect(
    skinColorTarget.IndexedDrawCount == 1 && skinMatrices.Length == 2,
    "GPU skin draw and position/normal palette",
    failures);
ExpectNear(skinMatrices[0].M41, 1f, 0.00001f, "skin palette joint translation", failures);
Expect(
    skinGraphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.VertexShader.Descriptor.Code.Contains(
            "skin_position(position",
            StringComparison.Ordinal) &&
        descriptor.VertexShader.Descriptor.Code.Contains(
            "vs_shadow",
            StringComparison.Ordinal) &&
        descriptor.VertexShader.Descriptor.Code.Contains(
            "vs_depth_prepass",
            StringComparison.Ordinal)),
    "skinning participates in visible, shadow, and AO vertex paths",
    failures);

using RecordingGraphicsDevice singularGraphicsDevice = new();
using SceneRenderer singularRenderer = new(
    singularGraphicsDevice,
    GraphicsTextureFormat.Rgba16Float);
using RecordingGraphicsTexture singularColorTarget =
    (RecordingGraphicsTexture)singularGraphicsDevice.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(32, 32),
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment));
using GraphicsTexture singularDepthTarget = singularGraphicsDevice.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(32, 32),
        GraphicsTextureFormat.Depth32Float,
        GraphicsTextureUsage.RenderAttachment));
Scene singularScene = new("singular animation scene");
Mesh singularMesh = new(
    geometry,
    new PbrMaterial(
        new LinearRgba(0.5f, 0.5f, 0.5f, 1f, StandardColorSpaces.LinearSrgb)),
    "zero-scale animated mesh");
singularMesh.Transform.Scale = Vector3.Zero;
singularScene.Add(singularMesh);
singularScene.Add(new DirectionalLight(
    new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb),
    intensity: 1f));
PerspectiveCamera singularCamera = new(aspectRatio: 1f);
singularCamera.Transform.Position = new Vector3(0, 0, 4);
singularRenderer.Render(
    singularScene,
    singularCamera,
    singularColorTarget,
    singularDepthTarget);
Expect(
    singularColorTarget.IndexedDrawCount == 0,
    "singular lit animation pose is omitted without failing the scene",
    failures);
singularMesh.Transform.Scale = Vector3.One;
singularRenderer.Render(
    singularScene,
    singularCamera,
    singularColorTarget,
    singularDepthTarget);
Expect(
    singularColorTarget.IndexedDrawCount == 1,
    "lit mesh resumes after animation leaves singular pose",
    failures);

using RecordingGraphicsDevice sheenGraphicsDevice = new();
using SceneRenderer sheenRenderer = new(sheenGraphicsDevice, GraphicsTextureFormat.Rgba16Float);
using RecordingGraphicsTexture sheenColorTarget =
    (RecordingGraphicsTexture)sheenGraphicsDevice.CreateTexture(new GraphicsTextureDescriptor(
        new GraphicsExtent3D(32, 32),
        GraphicsTextureFormat.Rgba16Float,
        GraphicsTextureUsage.RenderAttachment));
using GraphicsTexture sheenDepthTarget = sheenGraphicsDevice.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(32, 32),
        GraphicsTextureFormat.Depth32Float,
        GraphicsTextureUsage.RenderAttachment));
PbrMaterial texturedSheenMaterial = new(
    new LinearRgba(0.5f, 0.5f, 0.5f, 1f, StandardColorSpaces.LinearSrgb))
{
    SheenColor = new LinearRgba(0.5f, 0.25f, 0.1f, 1f, StandardColorSpaces.LinearSrgb),
    SheenRoughness = 0.6f,
    SheenColorTexture = materialImage,
    SheenRoughnessTexture = ormDataImage,
};
Scene sheenScene = new("textured Sheen scene");
sheenScene.Add(new Mesh(geometry, texturedSheenMaterial));
sheenScene.Add(new DirectionalLight(
    new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb)));
PerspectiveCamera sheenCamera = new(aspectRatio: 1f);
sheenCamera.Transform.Position = new Vector3(0f, 0f, 4f);
MaterialVariantManifest sheenManifest = MaterialVariantManifest.FromMaterials(
    [texturedSheenMaterial, texturedSheenMaterial]);
int pipelinesBeforeSheenPrewarm = sheenGraphicsDevice.PipelineDescriptors.Count;
Expect(
    sheenRenderer.CachedMaterialShaderVariantCount == 0 &&
    !sheenGraphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.FragmentEntryPoint == "fs_pbr_compact"),
    "unused textured-Sheen variant is not generated during renderer construction",
    failures);
sheenRenderer.PrewarmMaterialVariants(sheenManifest);
Expect(
    sheenRenderer.CachedMaterialShaderVariantCount == 1 &&
    sheenRenderer.MaterialShaderVariantCacheMisses == 1 &&
    sheenRenderer.MaterialShaderVariantCacheHits == 0 &&
    sheenGraphicsDevice.PipelineDescriptors.Count == pipelinesBeforeSheenPrewarm + 1,
    "manifest prewarm generates exactly one used textured-Sheen pipeline",
    failures);
sheenRenderer.PrewarmMaterialVariants(sheenManifest);
Expect(
    sheenRenderer.CachedMaterialShaderVariantCount == 1 &&
    sheenRenderer.MaterialShaderVariantCacheMisses == 1 &&
    sheenRenderer.MaterialShaderVariantCacheHits == 1 &&
    sheenGraphicsDevice.PipelineDescriptors.Count == pipelinesBeforeSheenPrewarm + 1,
    "repeated manifest prewarm hits the variant cache",
    failures);
sheenRenderer.Render(sheenScene, sheenCamera, sheenColorTarget, sheenDepthTarget);
Expect(
    sheenColorTarget.IndexedDrawCount == 1,
    "textured Sheen renders one draw",
    failures);
Expect(
    sheenRenderer.ObservedMaterialShaderVariantCount == 1 &&
    sheenRenderer.MaximumObservedMaterialSampledTextureCount == 2,
    $"textured Sheen normalized key ({sheenRenderer.ObservedMaterialShaderVariantCount} variants, " +
        $"{sheenRenderer.MaximumObservedMaterialSampledTextureCount} textures)",
    failures);
MaterialShaderVariant sheenVariant = sheenManifest.Variants.Single();
Expect(
    sheenManifest.Variants.Count == 1 &&
    sheenManifest.MaximumSampledMaterialTextureCount == 2 &&
    sheenVariant.BaseModel == MaterialBaseModel.PbrMetallicRoughness &&
    sheenVariant.Extensions == PbrMaterialExtensions.Sheen &&
    sheenVariant.TextureBindings == (
        PbrMaterialTextureBindings.SheenColor | PbrMaterialTextureBindings.SheenRoughness),
    "material variant manifest de-duplicates exact used KHR feature signatures",
    failures);
texturedSheenMaterial.ShaderTemplate = texturedSheenMaterial.ShaderTemplate.WithoutTextureSlots(
    PbrMaterialTextureSlots.Sheen);
MaterialShaderVariant factorOnlySheenVariant = MaterialShaderVariant.FromMaterial(texturedSheenMaterial);
Expect(
    factorOnlySheenVariant.Extensions == PbrMaterialExtensions.Sheen &&
    factorOnlySheenVariant.TextureBindings == PbrMaterialTextureBindings.None,
    "material variant resolver applies template policy without inventing combinations",
    failures);
texturedSheenMaterial.ShaderTemplate = MaterialShaderTemplate.FullPbr;
Expect(
    sheenGraphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.FragmentEntryPoint == "fs_pbr_compact" &&
        descriptor.FragmentShader?.Descriptor.Code.Contains(
            "sheen_roughness_texture_coordinate",
            StringComparison.Ordinal) == true),
    "textured Sheen selects the portable generated variant",
    failures);
PbrMaterial texturedDiffuseTransmissionMaterial = new(
    new LinearRgba(0.4f, 0.6f, 0.2f, 1f, StandardColorSpaces.LinearSrgb))
{
    DiffuseTransmissionFactor = 0.5f,
    DiffuseTransmissionColor = new LinearRgba(
        0.9f, 0.25f, 0.15f, 1f, StandardColorSpaces.LinearSrgb),
    DiffuseTransmissionTexture = ormDataImage,
    DiffuseTransmissionColorTexture = materialImage,
};
MaterialShaderVariant diffuseTransmissionVariant = MaterialShaderVariant.FromMaterial(
    texturedDiffuseTransmissionMaterial);
Expect(
    diffuseTransmissionVariant.Extensions == PbrMaterialExtensions.DiffuseTransmission &&
    diffuseTransmissionVariant.TextureBindings == (
        PbrMaterialTextureBindings.DiffuseTransmissionFactor |
        PbrMaterialTextureBindings.DiffuseTransmissionColor) &&
    diffuseTransmissionVariant.SampledMaterialTextureCount == 2,
    "diffuse transmission generates only its two authored texture slots",
    failures);
sheenRenderer.PrewarmMaterialVariants(MaterialVariantManifest.FromMaterials(
    [texturedDiffuseTransmissionMaterial]));
Expect(
    sheenGraphicsDevice.PipelineDescriptors.Last().FragmentShader?.Descriptor.Code.Contains(
        "diffuse_transmission_texture_coordinate).a",
        StringComparison.Ordinal) == true &&
    sheenGraphicsDevice.PipelineDescriptors.Last().FragmentShader?.Descriptor.Code.Contains(
        "diffuse_transmission_color_texture_coordinate).rgb",
        StringComparison.Ordinal) == true,
    "diffuse transmission compact shader samples factor alpha and sRGB color independently",
    failures);
PbrMaterial layeredMaterial = new(
    new LinearRgba(0.5f, 0.5f, 0.5f, 1f, StandardColorSpaces.LinearSrgb))
{
    NormalTexture = normalDataImage,
    OcclusionRoughnessMetallicTexture = ormDataImage,
    SheenColor = new LinearRgba(0.4f, 0.2f, 0.1f, 1f, StandardColorSpaces.LinearSrgb),
    SheenRoughness = 0.5f,
    SheenColorTexture = materialImage,
    SheenRoughnessTexture = ormDataImage,
    TransmissionFactor = 0.7f,
    TransmissionTexture = ormDataImage,
    VolumeThicknessFactor = 0.2f,
    VolumeThicknessTexture = ormDataImage,
    SpecularFactor = 0.6f,
    SpecularColor = new LinearRgba(1.5f, 0.75f, 0.25f, 1f, StandardColorSpaces.LinearSrgb),
    SpecularTexture = ormDataImage,
    SpecularColorTexture = materialImage,
};
Scene layeredScene = new("compact layered material scene");
layeredScene.Add(new Mesh(geometry, layeredMaterial));
layeredScene.Add(new DirectionalLight(
    new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb)));
MaterialVariantManifest layeredManifest = MaterialVariantManifest.FromMaterials([layeredMaterial]);
MaterialShaderVariant layeredVariant = layeredManifest.Variants.Single();
int pipelinesBeforeLayeredPrewarm = sheenGraphicsDevice.PipelineDescriptors.Count;
sheenRenderer.PrewarmMaterialVariants(layeredManifest);
Expect(
    layeredVariant.Extensions.HasFlag(PbrMaterialExtensions.Sheen) &&
    layeredVariant.Extensions.HasFlag(PbrMaterialExtensions.Transmission) &&
    layeredVariant.Extensions.HasFlag(PbrMaterialExtensions.Volume) &&
    layeredVariant.Extensions.HasFlag(PbrMaterialExtensions.Specular) &&
    layeredVariant.SampledMaterialTextureCount == 8 &&
    sheenGraphicsDevice.PipelineDescriptors.Count == pipelinesBeforeLayeredPrewarm + 1 &&
    sheenGraphicsDevice.PipelineDescriptors.Last().FragmentShader?.Descriptor.Code.Contains(
        "textureSample(material_emissive, emissive_sampler, transmission_texture_coordinate)",
        StringComparison.Ordinal) == true &&
    sheenGraphicsDevice.PipelineDescriptors.Last().FragmentShader?.Descriptor.Code.Contains(
        "material_clearcoat_roughness,",
        StringComparison.Ordinal) == true,
    "exact textured Sheen plus Transmission, Volume and Specular combination prewarms one compact variant",
    failures);
sheenRenderer.Render(layeredScene, sheenCamera, sheenColorTarget, sheenDepthTarget);
Expect(
    sheenColorTarget.IndexedDrawCount == 2 &&
    sheenGraphicsDevice.PipelineDescriptors.Any(static descriptor =>
        descriptor.FragmentEntryPoint == "fs_pbr_compact"),
    "compact layered material renders without exceeding portable sampled-texture limits",
    failures);
PbrMaterial maximumPortableMaterial = new(
    new LinearRgba(0.35f, 0.4f, 0.45f, 1f, StandardColorSpaces.LinearSrgb))
{
    BaseColorTexture = materialImage,
    NormalTexture = normalDataImage,
    OcclusionRoughnessMetallicTexture = ormDataImage,
    EmissiveColor = new LinearRgba(0.1f, 0.05f, 0.02f, 1f, StandardColorSpaces.LinearSrgb),
    ClearcoatFactor = 0.6f,
    ClearcoatTexture = ormDataImage,
    ClearcoatRoughnessTexture = ormDataImage,
    ClearcoatNormalTexture = normalDataImage,
    AnisotropyStrength = 0.4f,
    TransmissionFactor = 0.5f,
    TransmissionTexture = ormDataImage,
    VolumeThicknessFactor = 0.3f,
    VolumeThicknessTexture = ormDataImage,
    Dispersion = 0.2f,
    SheenColor = new LinearRgba(0.2f, 0.1f, 0.05f, 1f, StandardColorSpaces.LinearSrgb),
    SheenRoughness = 0.45f,
    SheenColorTexture = materialImage,
    SheenRoughnessTexture = ormDataImage,
    IridescenceFactor = 0.5f,
    SpecularFactor = 0.75f,
    DiffuseTransmissionFactor = 0.2f,
};
MaterialShaderVariant maximumPortableVariant = MaterialShaderVariant.FromMaterial(maximumPortableMaterial);
MaterialShaderTemplate dispersionTemplate = MaterialShaderTemplate.BasePbr.WithExtensions(
    PbrMaterialExtensions.Dispersion);
Expect(
    maximumPortableVariant.Extensions == PbrMaterialExtensions.All &&
    maximumPortableVariant.SampledMaterialTextureCount == 10 &&
    dispersionTemplate.Includes(PbrMaterialExtensions.Transmission | PbrMaterialExtensions.Volume |
        PbrMaterialExtensions.Dispersion) &&
    !dispersionTemplate.WithoutExtensions(PbrMaterialExtensions.Transmission).Includes(
        PbrMaterialExtensions.Transmission | PbrMaterialExtensions.Volume |
            PbrMaterialExtensions.Dispersion),
    "all M4 PBR extension blocks compose at the portable ten-material-texture boundary",
    failures);
int pipelinesBeforeMaximumPortablePrewarm = sheenGraphicsDevice.PipelineDescriptors.Count;
sheenRenderer.PrewarmMaterialVariants(MaterialVariantManifest.FromMaterials([maximumPortableMaterial]));
Expect(
    sheenGraphicsDevice.PipelineDescriptors.Count == pipelinesBeforeMaximumPortablePrewarm + 1 &&
    sheenGraphicsDevice.PipelineDescriptors.Last().FragmentEntryPoint == "fs_pbr_compact",
    "all-extension material generates one observed AOT-safe compact shader variant",
    failures);
Scene maximumPortableScene = new("maximum portable compound material scene");
maximumPortableScene.Add(new Mesh(geometry, maximumPortableMaterial));
maximumPortableScene.Add(new DirectionalLight(
    new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb)));
sheenRenderer.Render(maximumPortableScene, sheenCamera, sheenColorTarget, sheenDepthTarget);
Expect(
    sheenColorTarget.IndexedDrawCount == 3,
    "all-extension material renders at sixteen total sampled textures without binding overflow",
    failures);

Scene reusableSourceScene = new("reusable furniture");
SceneNode reusableJoint = new("hinge");
PbrMaterial reusableMaterial = new(
    new LinearRgba(0.3f, 0.5f, 0.8f, 1f, StandardColorSpaces.LinearSrgb),
    metallic: 0.25f,
    roughness: 0.4f,
    name: "paint")
{
    ClearcoatFactor = 0.7f,
    IsDoubleSided = true,
};
Mesh reusableMesh = new(geometry, reusableMaterial, "door")
{
    MorphWeights = [0.2f],
    Skin = new Skin([reusableJoint], [Matrix4x4.Identity], "door skin"),
    ShadowCastingMode = MeshShadowCastingMode.ShadowsOnly,
};
Mesh reusableSecondMesh = new(geometry, reusableMaterial, "handle")
{
    MorphWeights = [0.4f],
    ShadowCastingMode = MeshShadowCastingMode.Off,
};
DirectionalLight reusableLight = new(
    new LinearRgba(1f, 0.9f, 0.8f, 1f, StandardColorSpaces.LinearSrgb),
    intensity: 2f,
    name: "furniture light")
{
    CastsShadows = true,
    AngularDiameterRadians = 0.06f,
    ShadowOpacity = 0.62f,
};
reusableSourceScene.Add(reusableJoint);
reusableJoint.AddChild(reusableMesh);
reusableSourceScene.Add(reusableSecondMesh);
reusableSourceScene.Add(reusableLight);
AnimationClip reusableClip = new(
    [new Vector3AnimationTrack(
        reusableMesh,
        Vector3AnimationTarget.Position,
        [0f, 1f],
        [Vector3.Zero, Vector3.UnitX])],
    "open");
SceneAsset reusableAsset = new(reusableSourceScene, [reusableClip]);

reusableMesh.Transform.Position = new Vector3(99f);
reusableMaterial.BaseColor = new LinearRgba(
    1f,
    0f,
    0f,
    1f,
    StandardColorSpaces.LinearSrgb);
SceneInstance reusableFirst = reusableAsset.CreateInstance("first furniture");
SceneInstance reusableSecond = reusableAsset.CreateInstance("second furniture");
Mesh firstDoor = reusableFirst.Root.EnumerateDepthFirst().OfType<Mesh>()
    .Single(static mesh => mesh.Name == "door");
Mesh firstHandle = reusableFirst.Root.EnumerateDepthFirst().OfType<Mesh>()
    .Single(static mesh => mesh.Name == "handle");
Mesh secondDoor = reusableSecond.Root.EnumerateDepthFirst().OfType<Mesh>()
    .Single(static mesh => mesh.Name == "door");
DirectionalLight firstFurnitureLight = reusableFirst.Root.EnumerateDepthFirst()
    .OfType<DirectionalLight>()
    .Single(static light => light.Name == "furniture light");
SceneNode firstJoint = reusableFirst.Root.EnumerateDepthFirst()
    .Single(static node => node.Name == "hinge");
Expect(
    firstDoor.Transform.Position == Vector3.Zero &&
    ((PbrMaterial)firstDoor.Material).BaseColor.Red == 0.3f &&
    firstDoor.ShadowCastingMode == MeshShadowCastingMode.ShadowsOnly &&
    firstHandle.ShadowCastingMode == MeshShadowCastingMode.Off,
    "scene asset captures source state",
    failures);
Expect(
    firstFurnitureLight.CastsShadows &&
    MathF.Abs(firstFurnitureLight.AngularDiameterRadians - 0.06f) <= 0.000001f &&
    MathF.Abs(firstFurnitureLight.ShadowOpacity - 0.62f) <= 0.000001f,
    "scene asset captures directional shadow state",
    failures);
Expect(
    ReferenceEquals(firstDoor.Geometry, reusableMesh.Geometry) &&
    ReferenceEquals(firstDoor.Geometry, secondDoor.Geometry),
    "scene instances share immutable geometry",
    failures);
Expect(
    !ReferenceEquals(firstDoor, secondDoor) &&
    !ReferenceEquals(firstDoor.Material, secondDoor.Material) &&
    ReferenceEquals(firstDoor.Material, firstHandle.Material),
    "scene instances isolate mutable nodes and preserve internal material sharing",
    failures);
Expect(
    ReferenceEquals(firstDoor.Skin!.Joints[0], firstJoint) &&
    !ReferenceEquals(firstDoor.Skin.Joints[0], reusableJoint),
    "scene instance skins rebind cloned joints",
    failures);
Expect(
    ReferenceEquals(reusableFirst.Animations[0].Tracks[0].Target, firstDoor) &&
    !ReferenceEquals(reusableFirst.Animations[0].Tracks[0].Target, reusableMesh),
    "scene instance animations rebind cloned targets",
    failures);
reusableFirst.Animations[0].Apply(1f);
Expect(
    firstDoor.Transform.Position == Vector3.UnitX &&
    secondDoor.Transform.Position == Vector3.Zero,
    "scene instance animation state remains independent",
    failures);
Scene firstHostScene = new("first room");
Scene secondHostScene = new("second room");
reusableFirst.AttachTo(firstHostScene);
reusableFirst.AttachTo(secondHostScene);
Expect(
    !firstHostScene.Root.Children.Contains(reusableFirst.Root) &&
    secondHostScene.Root.Children.Contains(reusableFirst.Root),
    "scene instance reparents between scenes",
    failures);
reusableFirst.Detach();
Expect(reusableFirst.Root.Parent is null, "scene instance detach", failures);

Scene customAssetScene = new("custom material");
customAssetScene.Add(new Mesh(geometry, new TestMaterial("custom"), "custom mesh"));
ExpectThrows<NotSupportedException>(
    () => _ = new SceneAsset(customAssetScene),
    "scene asset custom material requires explicit factory",
    failures);
SceneAsset customMaterialAsset = new(
    customAssetScene,
    cloneOptions: new SceneAssetCloneOptions
    {
        CustomMaterialFactory = static source => new TestMaterial(source.Name),
    });
Expect(
    customMaterialAsset.CreateInstance().Root.EnumerateDepthFirst()
        .OfType<Mesh>().Single().Material is TestMaterial,
    "scene asset custom material factory",
    failures);

using (RecordingGraphicsDevice viewportDevice = new())
using (SceneRenderer viewportRenderer = new(viewportDevice, GraphicsTextureFormat.Rgba16Float))
using (RecordingGraphicsTexture viewportColor =
       (RecordingGraphicsTexture)viewportDevice.CreateTexture(new GraphicsTextureDescriptor(
           new GraphicsExtent3D(128, 64),
           GraphicsTextureFormat.Rgba16Float,
           GraphicsTextureUsage.RenderAttachment)))
using (GraphicsTexture viewportDepth = viewportDevice.CreateTexture(new GraphicsTextureDescriptor(
           new GraphicsExtent3D(128, 64),
           GraphicsTextureFormat.Depth32Float,
           GraphicsTextureUsage.RenderAttachment)))
{
    Scene leftScene = new("left viewport scene");
    Scene rightScene = new("right viewport scene");
    leftScene.Add(new Mesh(geometry, firstMaterial, "left viewport mesh"));
    rightScene.Add(new Mesh(geometry, firstMaterial, "right viewport mesh"));
    PerspectiveCamera leftCamera = new(aspectRatio: 1f);
    PerspectiveCamera rightCamera = new(aspectRatio: 1f);
    leftCamera.Transform.Position = new Vector3(0f, 0f, 4f);
    rightCamera.Transform.Position = new Vector3(0f, 0f, 4f);
    SceneRenderViewport leftViewport = new(leftScene, leftCamera, 0, 0, 32, 64);
    SceneRenderViewport rightViewport = new(rightScene, rightCamera, 32, 0, 96, 64);
    viewportRenderer.RenderViewports(
        [leftViewport, rightViewport],
        viewportColor,
        viewportDepth,
        new LinearRgba(0f, 0f, 0f, 1f, StandardColorSpaces.LinearSrgb));
    Expect(
        viewportColor.IndexedDrawCount == 2 &&
        leftCamera.AspectRatio == 0.5f &&
        rightCamera.AspectRatio == 1.5f,
        "shared scene viewport drawing and camera aspects",
        failures);
    Expect(
        viewportRenderer.CachedGeometryCount == 1 && viewportRenderer.CachedMeshCount == 2,
        "shared scene viewport union cache",
        failures);

    viewportRenderer.RenderViewports(
        [leftViewport],
        viewportColor,
        viewportDepth,
        new LinearRgba(0f, 0f, 0f, 1f, StandardColorSpaces.LinearSrgb),
        [rightScene]);
    Expect(
        viewportRenderer.CachedMeshCount == 2,
        "shared scene viewport warm cache retention",
        failures);
    viewportRenderer.RenderViewports(
        [leftViewport],
        viewportColor,
        viewportDepth,
        new LinearRgba(0f, 0f, 0f, 1f, StandardColorSpaces.LinearSrgb));
    Expect(
        viewportRenderer.CachedMeshCount == 1,
        "shared scene viewport cold cache trimming",
        failures);
    ExpectThrows<ArgumentException>(
        () => viewportRenderer.RenderViewports(
            [leftViewport, new SceneRenderViewport(rightScene, rightCamera, 16, 0, 96, 64)],
            viewportColor,
            viewportDepth,
            new LinearRgba(0f, 0f, 0f, 1f, StandardColorSpaces.LinearSrgb)),
        "shared scene viewport overlap rejection",
        failures);
}

SceneRendererCacheChecks.Run(failures);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine(
    "Validated Mu3D scene hierarchy, glTF/GLB import, FP32 animation/skinning, explicit linear color, renderer cache, and draw contracts.");
return 0;

static void Expect(bool condition, string name, ICollection<string> failures)
{
    if (!condition)
    {
        failures.Add($"FAILED: {name}");
    }
}

static bool IsByteNormalized(float value) =>
    MathF.Abs((value * 255f) - MathF.Round(value * 255f)) < 0.0001f;

static void ExpectNear(
    float actual,
    float expected,
    float tolerance,
    string name,
    ICollection<string> failures)
{
    if (!float.IsFinite(actual) || MathF.Abs(actual - expected) > tolerance)
    {
        failures.Add($"FAILED: {name}; expected {expected}, actual {actual}");
    }
}

static void ExpectThrows<TException>(Action action, string name, ICollection<string> failures)
    where TException : Exception
{
    try
    {
        action();
        failures.Add($"FAILED: {name} did not throw {typeof(TException).Name}");
    }
    catch (TException)
    {
    }
}

static async Task ExpectThrowsAsync<TException>(
    Func<Task> action,
    string name,
    ICollection<string> failures)
    where TException : Exception
{
    try
    {
        await action();
        failures.Add($"FAILED: {name} did not throw {typeof(TException).Name}");
    }
    catch (TException)
    {
    }
}

static EquirectangularHdrEnvironment CreateDirectionalEnvironment(uint width, uint height)
{
    Vector3[] pixels = new Vector3[checked((int)((ulong)width * height))];
    for (uint y = 0; y < height; y++)
    {
        float theta = ((y + 0.5f) / height) * MathF.PI;
        float sinTheta = MathF.Sin(theta);
        for (uint x = 0; x < width; x++)
        {
            float phi = (((x + 0.5f) / width) - 0.5f) * 2f * MathF.PI;
            pixels[checked((int)((ulong)y * width + x))] = new Vector3(
                sinTheta * MathF.Cos(phi),
                MathF.Cos(theta),
                sinTheta * MathF.Sin(phi));
        }
    }

    return new EquirectangularHdrEnvironment(
        width,
        height,
        pixels,
        StandardColorSpaces.LinearSrgb,
        "direction environment");
}

static Stream CreateRadianceRleTestStream()
{
    MemoryStream stream = new();
    byte[] header = Encoding.ASCII.GetBytes(
        "#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 8\n");
    stream.Write(header);
    stream.Write([2, 2, 0, 8]);
    stream.Write([136, 128]);
    stream.Write([136, 64]);
    stream.Write([136, 32]);
    stream.Write([136, 129]);
    stream.Position = 0;
    return stream;
}

static byte[] CreateGlb(byte[] json, byte[] binary)
{
    int paddedJsonLength = (json.Length + 3) & ~3;
    int paddedBinaryLength = (binary.Length + 3) & ~3;
    byte[] result = new byte[12 + 8 + paddedJsonLength + 8 + paddedBinaryLength];
    BinaryPrimitives.WriteUInt32LittleEndian(result, 0x46546C67);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), checked((uint)result.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), checked((uint)paddedJsonLength));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), 0x4E4F534A);
    result.AsSpan(20, paddedJsonLength).Fill(0x20);
    json.CopyTo(result.AsSpan(20));
    int binaryHeader = 20 + paddedJsonLength;
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(binaryHeader), checked((uint)paddedBinaryLength));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(binaryHeader + 4), 0x004E4942);
    binary.CopyTo(result.AsSpan(binaryHeader + 8));
    return result;
}

sealed class TestMaterial(string? name) : Material(name);

sealed class CancellingReadStream(
    byte[] source,
    CancellationTokenSource cancellation) : MemoryStream(source)
{
    private bool hasCancelled;

    public bool IsDisposed { get; private set; }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int read = await base.ReadAsync(buffer, cancellationToken);
        if (!hasCancelled)
        {
            hasCancelled = true;
            cancellation.Cancel();
        }
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}

sealed class TrackingMemoryStream(byte[] source) : MemoryStream(source)
{
    public bool IsDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}

sealed class TestEncodedImageDecoder : IEncodedImageDecoder
{
    public NormalizedRgbaDataImage? DataImage { get; init; }

    public int ColorDecodeCount { get; private set; }

    public int DataDecodeCount { get; private set; }

    public string? LastColorMimeType { get; private set; }

    public string? LastDataMimeType { get; private set; }

    public LinearRgbaImage DecodeColor(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        _ = source;
        ColorDecodeCount++;
        LastColorMimeType = mimeType;
        return new LinearRgbaImage(
            1,
            1,
            [new Vector4(0.25f, 0.5f, 1f, 0.5f)],
            StandardColorSpaces.LinearSrgb,
            name);
    }

    public NormalizedRgbaDataImage DecodeData(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        _ = source;
        DataDecodeCount++;
        LastDataMimeType = mimeType;
        return DataImage ?? new NormalizedRgbaDataImage(
            1,
            1,
            [new Vector4(0.5f, 0.5f, 1f, 1f)],
            name);
    }
}

sealed class TestEncodedTextureTranscoder : IEncodedTextureTranscoder
{
    public bool ReturnNull { get; init; }

    public bool ReturnWrongContent { get; init; }

    public int ColorTranscodeCount { get; private set; }

    public int DataTranscodeCount { get; private set; }

    public CompressedMaterialTexture? TryTranscode(
        ReadOnlyMemory<byte> source,
        string mimeType,
        CompressedMaterialTextureContent content,
        GraphicsCapabilities capabilities,
        string? name = null)
    {
        _ = source;
        if (mimeType != "image/ktx2")
        {
            throw new InvalidOperationException("The test transcoder accepts only KTX2.");
        }
        if (content == CompressedMaterialTextureContent.Color)
        {
            ColorTranscodeCount++;
        }
        else
        {
            DataTranscodeCount++;
        }
        if (ReturnNull)
        {
            return null;
        }
        CompressedMaterialTextureContent resultContent = ReturnWrongContent
            ? content == CompressedMaterialTextureContent.Color
                ? CompressedMaterialTextureContent.Data
                : CompressedMaterialTextureContent.Color
            : content;
        if (resultContent == CompressedMaterialTextureContent.Color)
        {
            return new CompressedMaterialTexture(
                capabilities.SupportsBcTextureCompression
                    ? GraphicsTextureFormat.Bc7RgbaUnormSrgb
                    : GraphicsTextureFormat.Astc4x4UnormSrgb,
                resultContent,
                [new CompressedTextureMipLevel(4, 4, new byte[16])],
                StandardColorSpaces.LinearSrgb,
                name);
        }
        return new CompressedMaterialTexture(
            GraphicsTextureFormat.Astc4x4Unorm,
            resultContent,
            [new CompressedTextureMipLevel(4, 4, new byte[16])],
            name: name);
    }
}
