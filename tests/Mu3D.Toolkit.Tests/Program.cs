using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Assets;
using Mu3D.Toolkit.Animation;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Diagnostics;
using Mu3D.Toolkit.Gizmos;
using Mu3D.Toolkit.Helpers;
using Mu3D.Toolkit.Rendering;
using Mu3D.Toolkit.Selection;
using Mu3D.Toolkit.Viewports;

List<string> failures = [];

TestFrameRequester progressFrameRequester = new();
List<double> firstProgressValues = [];
List<double> secondProgressValues = [];
ViewportProgressController progressController = new(
    progressFrameRequester,
    [
        new DelegateViewportProgressMapping(firstProgressValues.Add),
        new DelegateViewportProgressMapping(secondProgressValues.Add),
    ]);
progressController.Reapply();
Expect(
    firstProgressValues.SequenceEqual([0d]) &&
    secondProgressValues.SequenceEqual([0d]) &&
    progressFrameRequester.RequestCount == 1,
    "progress controller applies the initial mapping snapshot once",
    failures);
Expect(
    progressController.SetProgress(0.25d) &&
    progressController.Progress == 0.25d &&
    firstProgressValues[^1] == 0.25d &&
    secondProgressValues[^1] == 0.25d &&
    progressFrameRequester.RequestCount == 2,
    "progress controller applies ordered mappings and one frame request",
    failures);
Expect(
    !progressController.SetProgress(0.25d) && progressFrameRequester.RequestCount == 2,
    "unchanged progress is coalesced",
    failures);
Expect(
    progressController.SetProgress(2d) && progressController.Progress == 1d &&
    firstProgressValues[^1] == 1d,
    "progress controller clamps upper overshoot",
    failures);
Expect(
    progressController.SetProgress(-2d) && progressController.Progress == 0d &&
    firstProgressValues[^1] == 0d,
    "progress controller clamps lower overscroll",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => progressController.SetProgress(double.NaN),
    "progress controller rejects non-finite input",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new ViewportProgressController(
        progressFrameRequester,
        [null!]),
    "progress controller rejects null mappings",
    failures);
ExpectThrows<ArgumentNullException>(
    () => _ = new DelegateViewportProgressMapping(null!),
    "delegate progress mapping requires an explicit target",
    failures);

PerspectiveCamera axesCamera = new(aspectRatio: 4f / 3f);
axesCamera.Transform.Position = new Vector3(0f, 0f, 5f);
TestFrameRequester axesFrameRequester = new();
using OrbitController axesOrbit = new(
    axesCamera,
    Vector3.Zero,
    axesFrameRequester)
{
    DampingEnabled = true,
};
AxesHelper axesHelper = new()
{
    Placement = ViewportOverlayPlacement.TopRight,
    ScreenSizePixels = 100f,
    MarginPixels = 10f,
    ShowDiagonalViews = true,
    ShowNegativeAxes = true,
};
AxesHelperHitTester axesHitTester = new(10f);
AxesHelperHit? positiveXHit = axesHitTester.HitTest(
    axesHelper,
    axesCamera,
    400,
    300,
    new Vector2(374f, 60f));
Expect(
    positiveXHit?.Preset == AxesViewPreset.PositiveX &&
    positiveXHit.Axis == AxesHelperAxis.X &&
    !positiveXHit.IsDiagonal,
    "axes helper accepts a projected positive-X endpoint",
    failures);
axesHelper.Placement = ViewportOverlayPlacement.TopCenter;
AxesHelperHit? topCenterPositiveXHit = axesHitTester.HitTest(
    axesHelper,
    axesCamera,
    400,
    300,
    new Vector2(234f, 60f));
Expect(
    topCenterPositiveXHit?.Preset == AxesViewPreset.PositiveX,
    "axes helper shares the nine-position overlay placement contract",
    failures);
axesHelper.Placement = ViewportOverlayPlacement.TopRight;
AxesHelperHit? diagonalHit = axesHitTester.HitTest(
    axesHelper,
    axesCamera,
    400,
    300,
    new Vector2(359f, 41f));
Expect(
    diagonalHit?.Preset == AxesViewPreset.PositiveXPositiveY &&
    diagonalHit.IsDiagonal,
    "axes helper exposes a two-axis 45-degree endpoint",
    failures);
Vector3 diagonalView = Vector3.Normalize(new Vector3(1f, 0f, 1f));
Expect(
    axesOrbit.SetViewDirection(diagonalView) &&
    axesFrameRequester.RequestCount == 1,
    "orbit controller accepts one axes-helper view command and requests one frame",
    failures);
ExpectVectorNear(
    Vector3.Transform(Vector3.Zero, axesCamera.WorldMatrix),
    diagonalView * 5f,
    0.0002f,
    "axes-helper view preserves orbit distance",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => axesOrbit.SetViewDirection(Vector3.Zero),
    "axes-helper view rejects a zero direction",
    failures);

PerspectiveCamera pickingCamera = new(aspectRatio: 1f);
pickingCamera.Transform.Position = new Vector3(0f, 0f, 5f);
UnlitMaterial pickingMaterial = new(new LinearRgba(
    1f,
    1f,
    1f,
    1f,
    StandardColorSpaces.LinearSrgb));
Mesh nearPickingMesh = new(
    MeshPrimitives.CreateUvSphere(0.75f, longitudeSegments: 12, latitudeSegments: 8),
    pickingMaterial,
    "near pick");
Mesh farPickingMesh = new(
    MeshPrimitives.CreateUvSphere(0.75f, longitudeSegments: 12, latitudeSegments: 8),
    pickingMaterial,
    "far pick");
farPickingMesh.Transform.Position = new Vector3(0f, 0f, -2f);
Scene pickingScene = new();
pickingScene.Add(nearPickingMesh);
pickingScene.Add(farPickingMesh);
SceneRaycaster sceneRaycaster = new();
IReadOnlyList<SceneRaycastHit> pickingHits = sceneRaycaster.HitTest(
    pickingScene,
    pickingCamera,
    200,
    200,
    new Vector2(100f, 100f));
Expect(
    pickingHits.Count == 2 &&
    ReferenceEquals(pickingHits[0].Mesh, nearPickingMesh) &&
    ReferenceEquals(pickingHits[1].Mesh, farPickingMesh) &&
    pickingHits[0].Distance < pickingHits[1].Distance,
    "scene raycaster ranks visible meshes by camera distance",
    failures);
Expect(
    sceneRaycaster.TryHitClosest(
        pickingScene,
        pickingCamera,
        200,
        200,
        new Vector2(100f, 100f),
        out SceneRaycastIntersection closestPickingHit) &&
    ReferenceEquals(closestPickingHit.Mesh, nearPickingMesh),
    "allocation-sensitive scene raycast returns the closest visible mesh",
    failures);
Predicate<Mesh> farPickingFilter = mesh => ReferenceEquals(mesh, farPickingMesh);
Expect(
    sceneRaycaster.TryHitClosest(
        pickingScene,
        pickingCamera,
        200,
        200,
        new Vector2(100f, 100f),
        out SceneRaycastIntersection filteredPickingHit,
        farPickingFilter) &&
    ReferenceEquals(filteredPickingHit.Mesh, farPickingMesh),
    "allocation-sensitive scene raycast applies the application mesh filter",
    failures);
_ = sceneRaycaster.TryHitClosest(
    pickingScene,
    pickingCamera,
    200,
    200,
    new Vector2(100f, 100f),
    out _);
long pickingAllocationStart = GC.GetAllocatedBytesForCurrentThread();
for (int sample = 0; sample < 64; sample++)
{
    _ = sceneRaycaster.TryHitClosest(
        pickingScene,
        pickingCamera,
        200,
        200,
        new Vector2(100f, 100f),
        out _);
}
long pickingAllocationBytes = GC.GetAllocatedBytesForCurrentThread() - pickingAllocationStart;
Expect(
    pickingAllocationBytes == 0,
    "closest scene raycast allocates no managed memory after warmup",
    failures);
nearPickingMesh.IsVisible = false;
pickingHits = sceneRaycaster.HitTest(
    pickingScene,
    pickingCamera,
    200,
    200,
    new Vector2(100f, 100f));
Expect(
    pickingHits.Count == 1 && ReferenceEquals(pickingHits[0].Mesh, farPickingMesh),
    "scene raycaster follows scene visibility traversal",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => sceneRaycaster.HitTest(
        pickingScene,
        pickingCamera,
        200,
        200,
        new Vector2(-1f, 100f)),
    "scene raycaster rejects positions outside the physical viewport",
    failures);

PerspectiveCamera camera = new(aspectRatio: 2f);
camera.Transform.Position = new Vector3(0f, 0f, 10f);
TestFrameRequester frameRequester = new();
using OrbitController controller = new(camera, Vector3.Zero, frameRequester);
int changedCount = 0;
controller.Changed += (_, _) => changedCount++;

ExpectNear(controller.Distance, 10f, 0.0001f, "initial orbit distance", failures);
ExpectNear(controller.AzimuthAngle, 0f, 0.0001f, "initial orbit azimuth", failures);
ExpectNear(controller.PolarAngle, MathF.PI / 2f, 0.0001f, "initial orbit polar angle", failures);
ExpectLooksAt(camera, Vector3.Zero, "initial camera look-at", failures);

Expect(
    controller.Rotate(new Vector2(MathF.PI / 2f, 0f)),
    "immediate orbit rotation accepted",
    failures);
ExpectVectorNear(
    GetWorldPosition(camera),
    new Vector3(10f, 0f, 0f),
    0.0002f,
    "orbit azimuth moves camera around world up",
    failures);
ExpectLooksAt(camera, controller.Target, "rotated camera look-at", failures);
Expect(
    changedCount == 1 && frameRequester.RequestCount == 1,
    "immediate orbit change event and frame request",
    failures);

Expect(controller.Dolly(MathF.Log(2f)), "immediate logarithmic dolly accepted", failures);
ExpectNear(controller.Distance, 5f, 0.0002f, "logarithmic dolly halves distance", failures);
ExpectVectorNear(
    GetWorldPosition(camera),
    new Vector3(5f, 0f, 0f),
    0.0003f,
    "dolly retains target and orbit direction",
    failures);

float verticalSpan = 2f * controller.Distance * MathF.Tan(camera.FieldOfViewRadians / 2f);
Vector3 expectedPan = new(
    0f,
    verticalSpan * 0.25f,
    -verticalSpan * camera.AspectRatio * 0.1f);
Expect(controller.Pan(new Vector2(0.1f, 0.25f)), "immediate viewport pan accepted", failures);
ExpectVectorNear(controller.Target, expectedPan, 0.0003f, "viewport-relative pan target", failures);
ExpectVectorNear(
    GetWorldPosition(camera),
    expectedPan + new Vector3(5f, 0f, 0f),
    0.0004f,
    "viewport-relative pan translates target and eye",
    failures);
ExpectLooksAt(camera, controller.Target, "panned camera look-at", failures);

controller.MinimumDistance = 4f;
controller.MaximumDistance = 6f;
controller.Dolly(100f);
ExpectNear(controller.Distance, 4f, 0f, "minimum orbit distance clamp", failures);
controller.Dolly(-100f);
ExpectNear(controller.Distance, 6f, 0f, "maximum orbit distance clamp", failures);
controller.MinimumPolarAngle = 0.2f;
controller.MaximumPolarAngle = MathF.PI - 0.2f;
controller.Rotate(new Vector2(0f, -100f));
ExpectNear(controller.PolarAngle, 0.2f, 0f, "minimum polar clamp", failures);
controller.Rotate(new Vector2(0f, 100f));
ExpectNear(controller.PolarAngle, MathF.PI - 0.2f, 0.000001f, "maximum polar clamp", failures);
ExpectLooksAt(camera, controller.Target, "polar-clamped camera look-at", failures);

int requestsBeforeDisabledCommand = frameRequester.RequestCount;
Vector3 positionBeforeDisabledCommand = GetWorldPosition(camera);
controller.IsEnabled = false;
Expect(
    !controller.Rotate(Vector2.One) &&
    GetWorldPosition(camera) == positionBeforeDisabledCommand &&
    frameRequester.RequestCount == requestsBeforeDisabledCommand,
    "disabled orbit ignores normalized commands",
    failures);
controller.IsEnabled = true;

PerspectiveCamera mapCamera = new(aspectRatio: 16f / 9f);
mapCamera.Transform.Position = new Vector3(6f, 8f, 6f);
TestFrameRequester mapRequester = new();
using MapController mapController = new(mapCamera, Vector3.Zero, mapRequester);
Expect(
    mapController.PanMode == OrbitPanMode.WorldUpPlane &&
    mapController.MaximumPolarAngle < MathF.PI / 2f,
    "map controller selects ground-plane panning and an above-horizon polar limit",
    failures);
Expect(
    mapController.Pan(new Vector2(0.15f, -0.2f)),
    "map controller accepts viewport-relative panning",
    failures);
ExpectNear(
    Vector3.Dot(mapController.Target, mapController.WorldUp),
    0f,
    0.0001f,
    "map panning keeps the target on its world-up plane",
    failures);
Vector3 mapTargetAfterPan = mapController.Target;
Expect(
    mapController.Rotate(new Vector2(0.2f, 0.1f)) &&
    mapController.Target == mapTargetAfterPan &&
    mapController.PolarAngle <= mapController.MaximumPolarAngle,
    "map rotation preserves the translated target and upper hemisphere",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => mapController.PanMode = (OrbitPanMode)int.MaxValue,
    "orbit controller rejects an unknown pan basis",
    failures);

PerspectiveCamera flyCamera = new();
flyCamera.Transform.Position = new Vector3(1f, 2f, 5f);
TestFrameRequester flyRequester = new();
using FlyController flyController = new(flyCamera, flyRequester);
ExpectVectorNear(
    flyController.ForwardDirection,
    -Vector3.UnitZ,
    0.0001f,
    "fly controller reads the initial camera forward direction",
    failures);
Expect(
    flyController.Move(new Vector3(0f, 0f, 2f)),
    "fly controller accepts forward movement",
    failures);
ExpectVectorNear(
    flyController.Position,
    new Vector3(1f, 2f, 3f),
    0.0002f,
    "fly forward movement follows camera direction in world units",
    failures);
Expect(
    flyController.Look(new Vector2(MathF.PI / 2f, 0f)),
    "fly controller accepts yaw look",
    failures);
ExpectVectorNear(
    flyController.ForwardDirection,
    Vector3.UnitX,
    0.0002f,
    "fly yaw rotates around world up",
    failures);
Expect(
    flyController.Move(new Vector3(1f, 1f, 1f)) && flyRequester.RequestCount == 3,
    "fly strafe, elevation and forward movement request one frame per command",
    failures);
flyController.MaximumPitchAngle = 0.25f;
flyController.Look(new Vector2(0f, 10f));
ExpectNear(
    flyController.PitchAngle,
    0.25f,
    0.00001f,
    "fly controller clamps pitch away from the pole",
    failures);
Vector3 disabledFlyPosition = flyController.Position;
flyController.IsEnabled = false;
Expect(
    !flyController.Move(Vector3.One) && flyController.Position == disabledFlyPosition,
    "disabled fly controller ignores commands",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => flyController.Look(new Vector2(float.NaN, 0f)),
    "fly controller rejects non-finite look input",
    failures);
flyController.IsEnabled = true;
Expect(
    flyController.Look(new Vector2(float.MaxValue, float.MaxValue)) &&
    float.IsFinite(flyController.ForwardDirection.X) &&
    float.IsFinite(flyController.ForwardDirection.Y) &&
    float.IsFinite(flyController.ForwardDirection.Z),
    "fly controller accumulates extreme finite look input without FP32 overflow",
    failures);

PerspectiveCamera compositeFlyCamera = new();
compositeFlyCamera.Transform.Position = new Vector3(0f, 0f, 5f);
TestFrameRequester compositeFlyRequester = new();
using FlyController compositeFly = new(compositeFlyCamera, compositeFlyRequester);
int compositeFlyChanged = 0;
compositeFly.Changed += (_, _) => compositeFlyChanged++;
Expect(
    compositeFly.Navigate(
        new Vector3(0f, 0f, 1f),
        new Vector2(MathF.PI / 2f, 0f)) &&
    compositeFlyRequester.RequestCount == 1 &&
    compositeFlyChanged == 1,
    "atomic fly navigation emits one change and frame request",
    failures);
ExpectVectorNear(
    compositeFly.Position,
    new Vector3(1f, 0f, 5f),
    0.0002f,
    "atomic fly movement follows the orientation updated in the same command",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => compositeFly.Navigate(
        new Vector3(float.NaN, 0f, 0f),
        Vector2.Zero),
    "atomic fly navigation validates movement before mutation",
    failures);

PerspectiveCamera sharedInputFlyCamera = new();
sharedInputFlyCamera.Transform.Position = new Vector3(0f, 0f, 5f);
using FlyController sharedInputFly = new(sharedInputFlyCamera);
IViewportNavigationController sharedFlyNavigation = sharedInputFly;
sharedInputFly.RotationSensitivity = 2f;
Expect(
    sharedFlyNavigation.Rotate(new Vector2(0.05f, 0.1f)),
    "shared Rotate intent is accepted by a fly controller",
    failures);
ExpectNear(
    sharedInputFly.YawAngle,
    0.1f,
    0.00001f,
    "fly applies Rotation sensitivity to horizontal look",
    failures);
ExpectNear(
    sharedInputFly.PitchAngle,
    0.2f,
    0.00001f,
    "fly applies Rotation sensitivity to vertical look",
    failures);
sharedInputFly.DollySensitivity = 3f;
sharedInputFly.PanSensitivity = 2f;
Vector3 sharedPanStart = sharedInputFly.Position;
Vector3 expectedSharedPan = sharedPanStart + sharedInputFly.RightDirection +
    sharedInputFly.WorldUp * 0.5f;
Expect(
    sharedFlyNavigation.Pan(new Vector2(0.5f, 0.25f)),
    "shared Pan intent is accepted by a fly controller",
    failures);
ExpectVectorNear(
    sharedInputFly.Position,
    expectedSharedPan,
    0.0002f,
    "fly applies Pan sensitivity to strafe and lift",
    failures);
Vector3 sharedDollyStart = sharedInputFly.Position;
Vector3 expectedSharedDolly = sharedDollyStart + sharedInputFly.ForwardDirection * 3f;
Expect(
    sharedFlyNavigation.Dolly(1f),
    "shared Dolly intent is accepted by a fly controller",
    failures);
ExpectVectorNear(
    sharedInputFly.Position,
    expectedSharedDolly,
    0.0002f,
    "fly applies Dolly sensitivity to forward movement",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => sharedInputFly.PanSensitivity = -0.1f,
    "fly rejects a negative Pan sensitivity",
    failures);

SceneNode flyCameraRig = new("fly camera rig");
flyCameraRig.Transform.Position = new Vector3(2f, 1f, -3f);
flyCameraRig.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);
PerspectiveCamera parentedFlyCamera = new();
parentedFlyCamera.Transform.Position = new Vector3(0f, 1f, 6f);
flyCameraRig.AddChild(parentedFlyCamera);
using FlyController parentedFly = new(parentedFlyCamera);
Vector3 parentedFlyStart = parentedFly.Position;
Vector3 parentedFlyForward = parentedFly.ForwardDirection;
Expect(
    parentedFly.Move(new Vector3(0f, 0f, 1.25f)),
    "parented fly camera accepts forward movement",
    failures);
ExpectVectorNear(
    parentedFly.Position,
    parentedFlyStart + parentedFlyForward * 1.25f,
    0.0005f,
    "parented fly camera preserves world-space movement",
    failures);

PerspectiveCamera dampedFlyCamera = new();
dampedFlyCamera.Transform.Position = new Vector3(0f, 0f, 10f);
TestFrameRequester dampedFlyRequester = new();
using FlyController dampedFly = new(dampedFlyCamera, dampedFlyRequester)
{
    DampingEnabled = true,
    DampingTime = 0.1f,
};
int dampedFlyChanged = 0;
dampedFly.Changed += (_, _) => dampedFlyChanged++;
Expect(
    dampedFly.Move(new Vector3(0f, 0f, 2f)) &&
    dampedFly.HasPendingMotion &&
    dampedFlyRequester.RequestCount == 1,
    "damped fly queues movement and requests its first frame",
    failures);
ExpectVectorNear(
    dampedFly.Position,
    new Vector3(0f, 0f, 10f),
    0.0001f,
    "queued fly damping does not mutate the camera before a frame update",
    failures);
Expect(
    dampedFly.Update(0.05f) && dampedFly.HasPendingMotion && dampedFlyChanged == 1,
    "damped fly consumes a partial movement step",
    failures);
for (int index = 0; index < 256 && dampedFly.HasPendingMotion; index++)
{
    dampedFly.Update(1f / 60f);
}
Expect(
    !dampedFly.HasPendingMotion,
    "damped fly converges without a controller-owned timer",
    failures);
ExpectVectorNear(
    dampedFly.Position,
    new Vector3(0f, 0f, 8f),
    0.0002f,
    "damped fly preserves the complete movement delta",
    failures);
dampedFly.Look(new Vector2(MathF.PI / 2f, 0f));
Expect(dampedFly.HasPendingMotion, "damped fly look queues motion", failures);
dampedFly.DampingEnabled = false;
Expect(
    !dampedFly.HasPendingMotion,
    "disabling fly damping flushes pending look motion",
    failures);
ExpectVectorNear(
    dampedFly.ForwardDirection,
    Vector3.UnitX,
    0.0002f,
    "flushed fly damping preserves the complete look delta",
    failures);

ExpectThrows<ArgumentOutOfRangeException>(
    () => dampedFly.DampingTime = 0f,
    "zero fly damping time rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => dampedFly.Update(-0.01f),
    "negative fly frame duration rejection",
    failures);

PerspectiveCamera dampedCamera = new();
dampedCamera.Transform.Position = new Vector3(0f, 0f, 10f);
TestFrameRequester dampedRequester = new();
using OrbitController damped = new(dampedCamera, Vector3.Zero, dampedRequester)
{
    DampingEnabled = true,
    DampingTime = 0.12f,
};
int dampedChangedCount = 0;
damped.Changed += (_, _) => dampedChangedCount++;
Expect(
    damped.Rotate(new Vector2(MathF.PI / 2f, 0f)) &&
    damped.HasPendingMotion &&
    dampedRequester.RequestCount == 1,
    "damped orbit queues motion and requests a frame",
    failures);
ExpectVectorNear(
    GetWorldPosition(dampedCamera),
    new Vector3(0f, 0f, 10f),
    0.0001f,
    "queued damping does not move before frame update",
    failures);
Expect(
    damped.Update(0.06f) && damped.HasPendingMotion && dampedChangedCount == 1,
    "damped orbit consumes a partial frame step",
    failures);
for (int index = 0; index < 256 && damped.HasPendingMotion; index++)
{
    damped.Update(1f / 60f);
}
Expect(
    !damped.HasPendingMotion,
    "damped orbit converges without a controller-owned timer",
    failures);
ExpectVectorNear(
    GetWorldPosition(dampedCamera),
    new Vector3(10f, 0f, 0f),
    0.0002f,
    "damped orbit preserves complete rotation delta",
    failures);
int requestsAfterDamping = dampedRequester.RequestCount;
Expect(
    !damped.Update(1f / 60f) && dampedRequester.RequestCount == requestsAfterDamping,
    "settled damping stops requesting frames",
    failures);

damped.Pan(new Vector2(0.1f, 0f));
Expect(damped.HasPendingMotion, "damped pan queues motion", failures);
damped.DampingEnabled = false;
Expect(
    !damped.HasPendingMotion && damped.Target != Vector3.Zero,
    "disabling damping flushes pending motion",
    failures);

PerspectiveCamera synchronizedCamera = new();
synchronizedCamera.Transform.Position = new Vector3(0f, 0f, 5f);
TestFrameRequester synchronizedRequester = new();
using OrbitController synchronized = new(
    synchronizedCamera,
    Vector3.Zero,
    synchronizedRequester);
synchronizedCamera.Transform.Position = new Vector3(0f, 0f, 20f);
synchronizedCamera.Transform.Rotation = Quaternion.Identity;
synchronized.SynchronizeFromCamera();
ExpectNear(synchronized.Distance, 20f, 0.0001f, "external camera resynchronization", failures);
ExpectLooksAt(synchronizedCamera, Vector3.Zero, "resynchronized camera look-at", failures);
synchronized.Target = new Vector3(1f, 2f, 3f);
ExpectLooksAt(synchronizedCamera, synchronized.Target, "changed orbit target look-at", failures);
Expect(
    synchronizedRequester.RequestCount == 2,
    "resynchronization and target change request frames",
    failures);

SceneNode cameraRig = new("camera rig");
cameraRig.Transform.Position = new Vector3(3f, 2f, -1f);
cameraRig.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.35f);
PerspectiveCamera parentedCamera = new();
parentedCamera.Transform.Position = new Vector3(0f, 0f, 8f);
cameraRig.AddChild(parentedCamera);
using OrbitController parented = new(parentedCamera, Vector3.Zero);
Expect(
    parented.Rotate(new Vector2(0.4f, -0.2f)),
    "parented camera orbit accepted",
    failures);
ExpectNear(
    Vector3.Distance(GetWorldPosition(parentedCamera), parented.Target),
    parented.Distance,
    0.0005f,
    "parented camera retains world orbit distance",
    failures);
ExpectLooksAt(parentedCamera, parented.Target, "parented camera world look-at", failures);

PerspectiveCamera validationCamera = new();
validationCamera.Transform.Position = new Vector3(0f, 0f, 1f);
using OrbitController validation = new(validationCamera, Vector3.Zero);
ExpectThrows<ArgumentOutOfRangeException>(
    () => validation.RotationSensitivity = -1f,
    "negative rotation sensitivity rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => validation.DampingTime = 0f,
    "zero damping time rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => validation.Rotate(new Vector2(float.NaN, 0f)),
    "non-finite rotation rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => validation.Update(-0.01f),
    "negative frame duration rejection",
    failures);
validation.MaximumDistance = 1f;
ExpectThrows<ArgumentOutOfRangeException>(
    () => validation.MinimumDistance = 2f,
    "inverted distance constraints rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => validation.MinimumPolarAngle = MathF.PI,
    "invalid polar constraint rejection",
    failures);
ExpectThrows<ArgumentException>(
    () => _ = new OrbitController(validationCamera, GetWorldPosition(validationCamera)),
    "camera-at-target rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new OrbitController(validationCamera, Vector3.Zero, worldUp: Vector3.Zero),
    "zero world-up rejection",
    failures);

PerspectiveCamera disposedCamera = new();
disposedCamera.Transform.Position = new Vector3(0f, 0f, 2f);
TestFrameRequester disposedRequester = new();
OrbitController disposedController = new(disposedCamera, Vector3.Zero, disposedRequester);
disposedController.DampingEnabled = true;
disposedController.Rotate(Vector2.One);
disposedController.Dispose();
disposedController.Dispose();
Expect(
    !disposedController.HasPendingMotion,
    "orbit disposal clears pending motion",
    failures);
ExpectThrows<ObjectDisposedException>(
    () => disposedController.Rotate(Vector2.One),
    "disposed orbit controller rejection",
    failures);

ValidateTransformGizmo(failures);
ValidateTransformGizmoHitTesting(failures);
ValidateTransformGizmoRendering(failures);
TransformGizmoAllocationChecks.Validate(failures);
ValidateAxesHelperRendering(failures);
AxesHelperAllocationChecks.Validate(failures);
ValidateGridHelperRendering(failures);
GridHelperAllocationChecks.Validate(failures);
ValidateBoundsHelperRendering(failures);
BoundsHelperEligibilityChecks.Validate(failures);
ValidateOutlineHelperRendering(failures);
OutlineHelperAllocationChecks.Validate(failures);
HelperVisibilityChecks.Validate(failures);
SceneRaycasterMorphChecks.Validate(failures);
SceneNodeAnchorSourceChecks.Validate((condition, message) => Expect(condition, message, failures));
await SceneNodeAnchorSourceChecks.ValidateBorrowedReferences((condition, message) => Expect(condition, message, failures));
ValidateControlArbitration(failures);
ValidateFrameStatistics(failures);
await ValidateSceneViewportBudgetAsync(failures);
await ValidateAsyncAssetCacheAsync(failures);

if (failures.Count != 0)
{
    Console.Error.WriteLine("Mu3D.Toolkit validation failed:");
    foreach (string failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }
    return 1;
}

Console.WriteLine(
    "Validated UI-independent progress mapping, orbit, axes/grid/bounds/outline-helper interaction/rendering, scene raycasting, node-anchor sources, transform-gizmo interaction/rendering, control-arbitration, bounded asset-cache, viewport-budget and frame-statistics contracts.");
return 0;

static async Task ValidateAsyncAssetCacheAsync(ICollection<string> failures)
{
    using AsyncAssetCache<string, TestAsset> cache = new(2, StringComparer.Ordinal);
    TaskCompletionSource firstLoadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseFirstLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int loaderCallCount = 0;
    async ValueTask<TestAsset> LoadAsync(string key, CancellationToken cancellationToken)
    {
        loaderCallCount++;
        if (key == "first")
        {
            firstLoadStarted.TrySetResult();
            await releaseFirstLoad.Task.WaitAsync(cancellationToken);
        }
        return new TestAsset(key);
    }

    Task<TestAsset> firstWaiter = cache.GetOrLoadAsync("first", LoadAsync).AsTask();
    await firstLoadStarted.Task;
    Task<TestAsset> secondWaiter = cache.GetOrLoadAsync("first", LoadAsync).AsTask();
    Expect(
        loaderCallCount == 1 && cache.Count == 1 && cache.InFlightCount == 1,
        "async asset cache single-flight joins an exact key",
        failures);
    releaseFirstLoad.TrySetResult();
    TestAsset firstAsset = await firstWaiter;
    TestAsset joinedAsset = await secondWaiter;
    Expect(
        ReferenceEquals(firstAsset, joinedAsset) && cache.InFlightCount == 0,
        "async asset cache shares the completed definition",
        failures);

    _ = await cache.GetOrLoadAsync("second", LoadAsync);
    _ = cache.TryGet("first", out _);
    _ = await cache.GetOrLoadAsync("third", LoadAsync);
    Expect(
        cache.Count == 2 && cache.TryGet("first", out _) && !cache.TryGet("second", out _),
        "async asset cache evicts the least-recently-used completed entry",
        failures);

    using CancellationTokenSource cancellation = new();
    TaskCompletionSource canceledLoadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    async ValueTask<TestAsset> LoadCanceledAsync(string key, CancellationToken token)
    {
        canceledLoadStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return new TestAsset(key);
    }
    Task<TestAsset> canceledWaiter = cache
        .GetOrLoadAsync("canceled", LoadCanceledAsync, cancellation.Token)
        .AsTask();
    await canceledLoadStarted.Task;
    cancellation.Cancel();
    try
    {
        _ = await canceledWaiter;
        failures.Add("async asset cache caller cancellation");
    }
    catch (OperationCanceledException)
    {
    }
    Expect(
        !cache.TryGet("canceled", out _),
        "async asset cache cancels an unfinished entry after its last waiter leaves",
        failures);
}

static async Task ValidateSceneViewportBudgetAsync(ICollection<string> failures)
{
    SceneViewportBudget budget = new()
    {
        MaximumLiveViewports = 1,
        MaximumConcurrentLoads = 1,
    };
    using SceneViewportBudgetLease firstViewport = await budget.AcquireViewportAsync();
    using CancellationTokenSource waitingCancellation = new();
    Task<SceneViewportBudgetLease> waitingViewport = budget
        .AcquireViewportAsync(waitingCancellation.Token)
        .AsTask();
    Expect(
        !waitingViewport.IsCompleted &&
        budget.Snapshot.ActiveViewportCount == 1 &&
        budget.Snapshot.PendingViewportCount == 1,
        "scene viewport budget enforces live capacity",
        failures);
    waitingCancellation.Cancel();
    try
    {
        _ = await waitingViewport;
        failures.Add("scene viewport budget waiting cancellation");
    }
    catch (OperationCanceledException)
    {
    }
    Expect(
        budget.Snapshot.ActiveViewportCount == 1 &&
        budget.Snapshot.PendingViewportCount == 0,
        "scene viewport budget releases canceled pending request",
        failures);

    using SceneViewportBudgetLease firstLoad = await budget.AcquireLoadAsync();
    Task<SceneViewportBudgetLease> waitingLoad = budget.AcquireLoadAsync().AsTask();
    Expect(
        !waitingLoad.IsCompleted &&
        budget.Snapshot.ActiveLoadCount == 1 &&
        budget.Snapshot.PendingLoadCount == 1,
        "scene viewport budget enforces loading capacity",
        failures);
    firstLoad.Dispose();
    using SceneViewportBudgetLease secondLoad = await waitingLoad;
    Expect(
        budget.Snapshot.ActiveLoadCount == 1 &&
        budget.Snapshot.PendingLoadCount == 0,
        "scene viewport budget promotes one waiting load",
        failures);
    secondLoad.Dispose();
    Expect(
        budget.Snapshot.ActiveLoadCount == 0,
        "scene viewport budget lease disposal is idempotent",
        failures);
    ExpectThrows<InvalidOperationException>(
        () => budget.MaximumLiveViewports = 2,
        "scene viewport budget freezes capacity after first request",
        failures);
}

static Vector3 GetWorldPosition(Camera camera) =>
    Vector3.Transform(Vector3.Zero, camera.WorldMatrix);

static void ValidateTransformGizmoRendering(ICollection<string> failures)
{
    Scene scene = new("gizmo render scene");
    SceneNode target = new("gizmo render target");
    scene.Add(target);
    PerspectiveCamera camera = new(aspectRatio: 1f);
    camera.Transform.Position = new Vector3(0f, 0f, 10f);
    using TransformGizmo gizmo = new(target)
    {
        ScreenSizePixels = 100f,
    };
    using RecordingGraphicsDevice device = new();
    using GraphicsTexture color = device.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(1000, 1000),
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "gizmo color"));
    using GraphicsTexture depth = device.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(1000, 1000),
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "gizmo depth"));
    RenderPassContext context = new(
        scene,
        camera,
        color,
        depth,
        StandardColorSpaces.LinearSrgb,
        colorTargetInitialized: true,
        depthTargetInitialized: true);

    using TransformGizmoRenderPass pass = new(gizmo);
    Expect(
        pass.Descriptor.ColorAttachment?.LoadOperation == GraphicsLoadOperation.Load &&
        pass.Descriptor.DepthAttachment is null &&
        pass.Descriptor.WorkingColorSpace == StandardColorSpaces.LinearSrgb &&
        pass.Descriptor.SizeDependency == RenderPassSizeDependency.OutputExtent,
        "default gizmo overlay declares only its HDR-linear color load dependency",
        failures);

    pass.Execute(context);
    GraphicsRenderPipelineDescriptor pipeline = device.PipelineDescriptors.Single();
    Expect(
        pipeline.ColorFormat == GraphicsTextureFormat.Rgba16Float &&
        pipeline.Topology == GraphicsPrimitiveTopology.TriangleList &&
        pipeline.CullMode == GraphicsCullMode.None &&
        pipeline.Blend == GraphicsBlendState.PremultipliedAlpha &&
        pipeline.DepthStencil is null &&
        pipeline.VertexBuffers.Count == 1 &&
        pipeline.VertexBuffers[0].ArrayStride == 28,
        "gizmo overlay pipeline uses premultiplied HDR color without scene depth",
        failures);
    Expect(
        device.SubmittedPipelineLabels.SequenceEqual(["Transform Gizmo pipeline"]) &&
        device.Buffers.Count == 1 &&
        device.Buffers[0].Data.Any(static value => value != 0) &&
        BitConverter.ToSingle(device.Buffers[0].Data, 12) > 1f &&
        ((RecordingGraphicsTexture)color).LastColorLoadOperation == GraphicsLoadOperation.Load &&
        ((RecordingGraphicsTexture)depth).LastDepthLoadOperation is null,
        "gizmo overlay records extended-linear triangle colors without attaching scene depth",
        failures);

    TransformGizmoRenderStyle defaultStyle = TransformGizmoRenderStyle.Default;
    TransformGizmoRenderStyle depthTestedStyle = new(
        defaultStyle.XAxisColor,
        defaultStyle.YAxisColor,
        defaultStyle.ZAxisColor,
        defaultStyle.UniformColor,
        defaultStyle.ActiveColor,
        TransformGizmoDepthMode.SceneDepthTested,
        defaultStyle.LineWidthPixels,
        defaultStyle.MarkerSizePixels);
    using (TransformGizmoRenderPass depthTestedPass = new(
        gizmo,
        depthTestedStyle,
        StandardColorSpaces.LinearSrgb,
        "Depth-tested gizmo"))
    {
        Expect(
            depthTestedPass.Descriptor.DepthAttachment?.LoadOperation ==
                GraphicsLoadOperation.Load,
            "opt-in scene-depth-tested gizmo declares a depth load dependency",
            failures);
        depthTestedPass.Execute(context);
        GraphicsRenderPipelineDescriptor depthTestedPipeline = device.PipelineDescriptors[^1];
        Expect(
            depthTestedPipeline.DepthStencil?.Format == GraphicsTextureFormat.Depth32Float &&
            depthTestedPipeline.DepthStencil?.DepthWriteEnabled == false &&
            depthTestedPipeline.DepthStencil?.DepthCompare == GraphicsCompareFunction.LessEqual &&
            ((RecordingGraphicsTexture)depth).LastDepthLoadOperation == GraphicsLoadOperation.Load,
            "opt-in gizmo pipeline retains read-only scene depth testing",
            failures);
    }

    int submissions = device.SubmittedPipelineLabels.Count;
    gizmo.Target = null;
    pass.Execute(context);
    Expect(
        device.SubmittedPipelineLabels.Count == submissions,
        "targetless gizmo render pass is a no-op",
        failures);
    gizmo.Target = target;
    target.Transform.Position = new Vector3(0f, 0f, 20f);
    pass.Execute(context);
    Expect(
        device.SubmittedPipelineLabels.Count == submissions,
        "behind-camera gizmo render pass is a no-op",
        failures);
    target.Transform.Position = Vector3.Zero;

    gizmo.Mode = TransformGizmoMode.Translate;
    gizmo.IsRotateEnabled = true;
    gizmo.IsScaleEnabled = true;
    int mixedSubmissions = device.SubmittedPipelineLabels.Count;
    pass.Execute(context);
    Expect(
        device.SubmittedPipelineLabels.Count == mixedSubmissions + 1,
        "mixed translate/rotate/scale handles render in one overlay pass",
        failures);
    submissions = device.SubmittedPipelineLabels.Count;

    gizmo.Mode = TransformGizmoMode.Rotate;
    gizmo.Space = TransformGizmoSpace.Local;
    target.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(0.2f, 0.3f, 0.1f);
    pass.Execute(context);
    Expect(
        device.SubmittedPipelineLabels.Count == submissions + 1,
        "local rotation rings render",
        failures);

    gizmo.Mode = TransformGizmoMode.Scale;
    gizmo.Space = TransformGizmoSpace.World;
    pass.Execute(context);
    byte[] worldRequestedScaleGeometry = device.Buffers[^1].Data.ToArray();
    gizmo.Space = TransformGizmoSpace.Local;
    pass.Execute(context);
    Expect(
        device.SubmittedPipelineLabels.Count == submissions + 3 &&
        device.Buffers[^1].Data.SequenceEqual(worldRequestedScaleGeometry),
        "scale render geometry follows target-local axes regardless of move/rotate space",
        failures);

    gizmo.BeginInteraction(TransformGizmoAxis.Uniform);
    pass.Execute(context);
    Expect(
        device.SubmittedPipelineLabels.Count == submissions + 4 &&
        MathF.Abs(BitConverter.ToSingle(device.Buffers[^1].Data, 12) - 2f) <= 0.0001f,
        "active uniform scale handle renders with active HDR color",
        failures);
    gizmo.CancelInteraction();

    ExpectThrows<InvalidOperationException>(
        () => pass.Execute(new RenderPassContext(
            scene,
            camera,
            color,
            depth,
            StandardColorSpaces.LinearSrgb)),
        "gizmo render rejects uninitialized load attachments",
        failures);
    using TransformGizmoRenderPass disposed = new(gizmo);
    disposed.Dispose();
    disposed.Dispose();
    ExpectThrows<ObjectDisposedException>(
        () => disposed.Execute(context),
        "disposed gizmo render pass rejection",
        failures);
    Expect(
        gizmo.Target == target,
        "gizmo render pass does not own gizmo",
        failures);

    ExpectThrows<ArgumentOutOfRangeException>(
        () => _ = new TransformGizmoRenderStyle(
            TransformGizmoRenderStyle.Default.XAxisColor,
            TransformGizmoRenderStyle.Default.YAxisColor,
            TransformGizmoRenderStyle.Default.ZAxisColor,
            TransformGizmoRenderStyle.Default.UniformColor,
            TransformGizmoRenderStyle.Default.ActiveColor,
            lineWidthPixels: 0f),
        "zero gizmo line width rejection",
        failures);
}

static void ValidateAxesHelperRendering(ICollection<string> failures)
{
    Scene scene = new("axes render scene");
    PerspectiveCamera camera = new(aspectRatio: 1f);
    camera.Transform.Position = new Vector3(0f, 0f, 10f);
    AxesHelper helper = new()
    {
        ScreenSizePixels = 120f,
        ShowDiagonalViews = true,
        ShowNegativeAxes = true,
    };
    using RecordingGraphicsDevice device = new();
    using GraphicsTexture color = device.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(800, 800),
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "axes color"));
    using GraphicsTexture depth = device.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(800, 800),
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "axes depth"));
    RenderPassContext context = new(
        scene,
        camera,
        color,
        depth,
        StandardColorSpaces.LinearSrgb,
        colorTargetInitialized: true,
        depthTargetInitialized: true);
    using AxesHelperRenderPass pass = new(helper);
    Expect(
        pass.Descriptor.ColorAttachment?.LoadOperation == GraphicsLoadOperation.Load &&
        pass.Descriptor.DepthAttachment is null,
        "axes helper declares a true HDR-linear overlay",
        failures);
    pass.Execute(context);
    GraphicsRenderPipelineDescriptor pipeline = device.PipelineDescriptors.Single();
    byte[] vertices = device.Buffers.Single().Data;
    bool containsHdrAxis = Enumerable.Range(0, vertices.Length / 28)
        .Any(index => BitConverter.ToSingle(vertices, (index * 28) + 12) > 1f);
    int lightSignAndCenterVertexCount = Enumerable.Range(0, vertices.Length / 28)
        .Count(index =>
            MathF.Abs(BitConverter.ToSingle(vertices, (index * 28) + 12) - 1.35f) < 0.0001f &&
            MathF.Abs(BitConverter.ToSingle(vertices, (index * 28) + 16) - 1.35f) < 0.0001f &&
            MathF.Abs(BitConverter.ToSingle(vertices, (index * 28) + 20) - 1.35f) < 0.0001f &&
            MathF.Abs(BitConverter.ToSingle(vertices, (index * 28) + 24) - 1f) < 0.0001f);
    int darkSignVertexCount = Enumerable.Range(0, vertices.Length / 28)
        .Count(index =>
            MathF.Abs(BitConverter.ToSingle(vertices, (index * 28) + 12) - 0.015f) < 0.0001f &&
            MathF.Abs(BitConverter.ToSingle(vertices, (index * 28) + 16) - 0.02f) < 0.0001f &&
            MathF.Abs(BitConverter.ToSingle(vertices, (index * 28) + 20) - 0.035f) < 0.0001f &&
            MathF.Abs(BitConverter.ToSingle(vertices, (index * 28) + 24) - 1f) < 0.0001f);
    Expect(
        pipeline.ColorFormat == GraphicsTextureFormat.Rgba16Float &&
        pipeline.Blend == GraphicsBlendState.PremultipliedAlpha &&
        pipeline.DepthStencil is null &&
        containsHdrAxis &&
        lightSignAndCenterVertexCount == 108 &&
        darkSignVertexCount == 18 &&
        ((RecordingGraphicsTexture)color).LastColorLoadOperation == GraphicsLoadOperation.Load &&
        ((RecordingGraphicsTexture)depth).LastDepthLoadOperation is null,
        "axes helper records extended-linear signed markers without scene depth",
        failures);
    using AxesHelperRenderPass disposed = new(helper);
    disposed.Dispose();
    disposed.Dispose();
    ExpectThrows<ObjectDisposedException>(
        () => disposed.Execute(context),
        "disposed axes-helper render pass rejection",
        failures);
}

static void ValidateGridHelperRendering(ICollection<string> failures)
{
    GridHelper helper = new()
    {
        Plane = GridHelperPlane.XY,
        Size = 8f,
        Divisions = 8,
        MajorLineEvery = 2,
        ShowCenterAxes = true,
        ShowMajorLines = true,
        ShowMinorLines = true,
    };
    ExpectThrows<ArgumentOutOfRangeException>(
        () => helper.Size = 0f,
        "grid helper rejects a zero world size",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => helper.Divisions = GridHelper.MaximumDivisions + 1,
        "grid helper bounds generated divisions",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => helper.Origin = new Vector3(float.NaN, 0f, 0f),
        "grid helper rejects a non-finite origin",
        failures);

    Scene scene = new("grid render scene");
    PerspectiveCamera camera = new(aspectRatio: 1f);
    camera.Transform.Position = new Vector3(0f, 0f, 10f);
    using RecordingGraphicsDevice device = new();
    using GraphicsTexture color = device.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(800, 800),
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "grid color"));
    using GraphicsTexture depth = device.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(800, 800),
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "grid depth"));
    RenderPassContext context = new(
        scene,
        camera,
        color,
        depth,
        StandardColorSpaces.LinearSrgb,
        colorTargetInitialized: true,
        depthTargetInitialized: true);
    using GridHelperRenderPass pass = new(helper);
    Expect(
        pass.Descriptor.ColorAttachment?.LoadOperation == GraphicsLoadOperation.Load &&
        pass.Descriptor.DepthAttachment?.LoadOperation == GraphicsLoadOperation.Load,
        "grid helper declares a scene-color and scene-depth continuation",
        failures);
    pass.Execute(context);
    GraphicsRenderPipelineDescriptor pipeline = device.PipelineDescriptors.Single();
    byte[] vertices = device.Buffers.Single().Data;
    int populatedVertexCount = Enumerable.Range(0, vertices.Length / 28)
        .Count(index => BitConverter.ToSingle(vertices, (index * 28) + 24) > 0f);
    bool containsHdrAxis = Enumerable.Range(0, vertices.Length / 28)
        .Any(index => BitConverter.ToSingle(vertices, (index * 28) + 12) > 1f);
    Expect(
        pipeline.ColorFormat == GraphicsTextureFormat.Rgba16Float &&
        pipeline.Blend == GraphicsBlendState.PremultipliedAlpha &&
        pipeline.DepthStencil?.Format == GraphicsTextureFormat.Depth32Float &&
        pipeline.DepthStencil?.DepthWriteEnabled == false &&
        pipeline.DepthStencil?.DepthCompare == GraphicsCompareFunction.LessEqual &&
        populatedVertexCount == 108 &&
        containsHdrAxis &&
        ((RecordingGraphicsTexture)color).LastColorLoadOperation == GraphicsLoadOperation.Load &&
        ((RecordingGraphicsTexture)depth).LastDepthLoadOperation == GraphicsLoadOperation.Load,
        "grid helper records bounded HDR lines against loaded scene depth",
        failures);

    RenderPassContext missingDepthContext = new(
        scene,
        camera,
        color,
        depth,
        StandardColorSpaces.LinearSrgb,
        colorTargetInitialized: true,
        depthTargetInitialized: false);
    ExpectThrows<InvalidOperationException>(
        () => pass.Execute(missingDepthContext),
        "grid helper rejects an uninitialized depth target",
        failures);
    helper.Plane = GridHelperPlane.XZ;
    helper.ShowMinorLines = false;
    helper.ShowMajorLines = false;
    PerspectiveCamera clippingCamera = new(aspectRatio: 1f);
    clippingCamera.Transform.Position = new Vector3(0f, 1f, 0f);
    RenderPassContext clippingContext = new(
        scene,
        clippingCamera,
        color,
        depth,
        StandardColorSpaces.LinearSrgb,
        colorTargetInitialized: true,
        depthTargetInitialized: true);
    pass.Execute(clippingContext);
    bool clippedCenterAxisVisible = Enumerable.Range(0, 6)
        .Any(index => BitConverter.ToSingle(vertices, (index * 28) + 20) > 1f);
    Expect(
        clippedCenterAxisVisible,
        "grid helper clips a center axis crossing the camera near plane instead of dropping it",
        failures);
    using GridHelperRenderPass disposed = new(helper);
    disposed.Dispose();
    disposed.Dispose();
    ExpectThrows<ObjectDisposedException>(
        () => disposed.Execute(context),
        "disposed grid-helper render pass rejection",
        failures);
}

static void ValidateBoundsHelperRendering(ICollection<string> failures)
{
    UnlitMaterial material = new(
        new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb));
    MeshGeometry geometry = new(
        [new Vector3(-1f, -2f, -3f), new Vector3(1f, 2f, 3f), Vector3.Zero],
        [0u, 1u, 2u]);
    SceneNode root = new("bounds root");
    root.Transform.Position = new Vector3(1f, 2f, 3f);
    Mesh child = new(geometry, material, "bounded child");
    child.Transform.Position = new Vector3(4f, 5f, 6f);
    root.AddChild(child);
    Mesh hidden = new(geometry, material, "hidden child") { IsVisible = false };
    hidden.Transform.Position = new Vector3(100f, 0f, 0f);
    root.AddChild(hidden);

    BoundsHelper helper = new()
    {
        Target = root,
        Padding = 0.5f,
    };
    Expect(
        helper.TryGetWorldBounds(out Bounds3D bounds) &&
        bounds.Minimum == new Vector3(3.5f, 4.5f, 5.5f) &&
        bounds.Maximum == new Vector3(6.5f, 9.5f, 12.5f),
        "bounds helper combines visible descendant geometry and world transforms",
        failures);
    helper.IncludeInvisible = true;
    Expect(
        helper.TryGetWorldBounds(out Bounds3D includingHidden) && includingHidden.Maximum.X == 102.5f,
        "bounds helper optionally includes invisible subtrees",
        failures);
    helper.IncludeDescendants = false;
    Expect(
        !helper.TryGetWorldBounds(out _),
        "bounds helper can restrict calculation to the target node",
        failures);
    helper.Target = child;
    Expect(
        helper.TryGetWorldBounds(out Bounds3D directBounds) && directBounds.Center == new Vector3(5f, 7f, 9f),
        "bounds helper accepts a direct mesh target",
        failures);
    helper.Padding = 0f;
    child.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.25f);
    helper.Precise = true;
    _ = helper.TryGetWorldBounds(out Bounds3D preciseRotatedBounds);
    helper.Precise = false;
    _ = helper.TryGetWorldBounds(out Bounds3D conservativeRotatedBounds);
    Expect(
        preciseRotatedBounds.Size.X < conservativeRotatedBounds.Size.X,
        "precise bounds transform real vertices instead of a local box's empty corners",
        failures);
    helper.Precise = true;
    helper.Padding = 0.5f;
    child.Transform.Rotation = Quaternion.Identity;
    ExpectThrows<ArgumentOutOfRangeException>(
        () => helper.Padding = -1f,
        "bounds helper rejects negative padding",
        failures);
    ExpectThrows<ArgumentException>(
        () => _ = new Bounds3D(Vector3.One, Vector3.Zero),
        "bounds value rejects inverted extrema",
        failures);

    Scene scene = new("bounds render scene");
    scene.Add(child);
    PerspectiveCamera camera = new(aspectRatio: 1f);
    camera.Transform.Position = new Vector3(5f, 7f, 19f);
    using RecordingGraphicsDevice device = new();
    using GraphicsTexture color = device.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(800, 800),
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "bounds color"));
    using GraphicsTexture depth = device.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(800, 800),
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "bounds depth"));
    RenderPassContext context = new(
        scene,
        camera,
        color,
        depth,
        StandardColorSpaces.LinearSrgb,
        colorTargetInitialized: true,
        depthTargetInitialized: true);
    using BoundsHelperRenderPass depthPass = new(helper);
    depthPass.Execute(context);
    GraphicsRenderPipelineDescriptor depthPipeline = device.PipelineDescriptors.Single();
    byte[] vertices = device.Buffers.Single().Data;
    int populatedVertexCount = Enumerable.Range(0, vertices.Length / 28)
        .Count(index => BitConverter.ToSingle(vertices, (index * 28) + 24) > 0f);
    Expect(
        depthPass.Descriptor.DepthAttachment?.LoadOperation == GraphicsLoadOperation.Load &&
        depthPipeline.ColorFormat == GraphicsTextureFormat.Rgba16Float &&
        depthPipeline.Blend == GraphicsBlendState.PremultipliedAlpha &&
        depthPipeline.DepthStencil?.DepthWriteEnabled == false &&
        depthPipeline.DepthStencil?.DepthCompare == GraphicsCompareFunction.LessEqual &&
        populatedVertexCount == 72,
        "bounds helper records twelve HDR physical-pixel edges against scene depth",
        failures);
    RenderPassContext missingDepthContext = new(
        scene,
        camera,
        color,
        depth,
        StandardColorSpaces.LinearSrgb,
        colorTargetInitialized: true,
        depthTargetInitialized: false);
    ExpectThrows<InvalidOperationException>(
        () => depthPass.Execute(missingDepthContext),
        "depth-tested bounds helper rejects uninitialized depth",
        failures);

    BoundsHelperRenderStyle overlayStyle = new(
        BoundsHelperRenderStyle.Default.Color,
        3f,
        BoundsHelperDepthMode.Overlay);
    using BoundsHelperRenderPass overlayPass = new(
        helper,
        overlayStyle,
        StandardColorSpaces.LinearSrgb);
    overlayPass.Execute(context);
    Expect(
        overlayPass.Descriptor.DepthAttachment is null &&
        device.PipelineDescriptors[^1].DepthStencil is null,
        "overlay bounds helper omits scene depth",
        failures);
    using BoundsHelperRenderPass disposed = new(helper);
    disposed.Dispose();
    disposed.Dispose();
    ExpectThrows<ObjectDisposedException>(
        () => disposed.Execute(context),
        "disposed bounds-helper render pass rejection",
        failures);
}

static void ValidateOutlineHelperRendering(ICollection<string> failures)
{
    UnlitMaterial material = new(
        new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb));
    MeshGeometry geometry = new(
        [
            new Vector3(-1f, -1f, 0f),
            new Vector3(1f, -1f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, 0.5f),
        ],
        [0u, 1u, 2u, 0u, 2u, 3u]);
    SceneNode root = new("outline root");
    Mesh target = new(geometry, material, "outlined child");
    root.AddChild(target);
    Mesh hidden = new(geometry, material, "hidden outline child") { IsVisible = false };
    root.AddChild(hidden);
    OutlineHelper helper = new()
    {
        Target = root,
        TriangleLimit = 4,
    };
    PerspectiveCamera camera = new(aspectRatio: 1f);
    camera.Transform.Position = new Vector3(0f, 0f, 5f);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => helper.TriangleLimit = 0,
        "outline helper rejects a zero triangle limit",
        failures);
    helper.TriangleLimit = 4;

    Scene scene = new("outline render scene");
    scene.Add(root);
    using RecordingGraphicsDevice overlayDevice = new();
    using GraphicsTexture overlayColor = overlayDevice.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(640, 480),
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "outline overlay color"));
    RenderPassContext overlayContext = new(
        scene,
        camera,
        overlayColor,
        null,
        StandardColorSpaces.LinearSrgb,
        colorTargetInitialized: true,
        depthTargetInitialized: false);
    OutlineHelperRenderStyle overlayStyle = new(
        OutlineHelperRenderStyle.Default.Color,
        4f,
        OutlineHelperDepthMode.Overlay);
    using OutlineHelperRenderPass overlayPass = new(
        helper,
        overlayStyle,
        StandardColorSpaces.LinearSrgb);
    overlayPass.Execute(overlayContext);
    GraphicsRenderPipelineDescriptor overlayMaskPipeline = overlayDevice.PipelineDescriptors[0];
    GraphicsRenderPipelineDescriptor overlayCompositePipeline = overlayDevice.PipelineDescriptors[1];
    Expect(
        overlayPass.Descriptor.DepthAttachment is null &&
        overlayMaskPipeline.ColorFormat == GraphicsTextureFormat.Rgba8Unorm &&
        overlayMaskPipeline.DepthStencil?.DepthWriteEnabled == true &&
        overlayCompositePipeline.ColorFormat == GraphicsTextureFormat.Rgba16Float &&
        overlayCompositePipeline.DepthStencil is null &&
        overlayCompositePipeline.Blend == GraphicsBlendState.PremultipliedAlpha &&
        overlayDevice.SubmittedPipelineLabels.Count == 2,
        "overlay outline rasterizes a private mask then composites an HDR outer edge",
        failures);
    RecordingGraphicsBuffer outlineVertices = overlayDevice.Buffers.Single();
    Expect(
        outlineVertices.LastWriteLength == 6 * 16,
        "outline helper includes visible descendant triangles",
        failures);
    helper.IncludeInvisible = true;
    overlayPass.Execute(overlayContext);
    Expect(
        outlineVertices.LastWriteLength == 12 * 16,
        "outline helper optionally includes invisible subtrees",
        failures);
    helper.IncludeInvisible = false;
    helper.IncludeDescendants = false;
    int submissionsBeforeEmptyTarget = overlayDevice.SubmittedPipelineLabels.Count;
    overlayPass.Execute(overlayContext);
    Expect(
        overlayDevice.SubmittedPipelineLabels.Count == submissionsBeforeEmptyTarget,
        "outline helper can restrict traversal to its target node",
        failures);
    helper.IncludeDescendants = true;
    helper.TriangleLimit = 1;
    ExpectThrows<InvalidOperationException>(
        () => overlayPass.Execute(overlayContext),
        "outline helper enforces its source-triangle safety limit",
        failures);
    helper.TriangleLimit = 4;

    using RecordingGraphicsDevice depthDevice = new();
    using GraphicsTexture depthColor = depthDevice.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(640, 480),
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "outline depth-tested color"));
    using GraphicsTexture depth = depthDevice.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(640, 480),
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
            label: "outline sampled scene depth"));
    RenderPassContext depthContext = new(
        scene,
        camera,
        depthColor,
        depth,
        StandardColorSpaces.LinearSrgb,
        colorTargetInitialized: true,
        depthTargetInitialized: true);
    using OutlineHelperRenderPass depthPass = new(helper);
    depthPass.Execute(depthContext);
    GraphicsRenderPipelineDescriptor depthCompositePipeline = depthDevice.PipelineDescriptors[1];
    Expect(
        depthPass.Descriptor.DepthAttachment?.LoadOperation == GraphicsLoadOperation.Load &&
        ((RecordingGraphicsTexture)depthColor).LastColorLoadOperation == GraphicsLoadOperation.Load &&
        depthCompositePipeline.Layout?.Descriptor.BindGroupLayouts[0].Descriptor.Entries.Count == 3,
        "scene-depth-tested outline loads existing HDR color and samples target/shared scene depth",
        failures);

    using GraphicsTexture nonSampleableDepth = depthDevice.CreateTexture(
        new GraphicsTextureDescriptor(
            new GraphicsExtent3D(640, 480),
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "outline non-sampleable scene depth"));
    RenderPassContext nonSampleableContext = new(
        scene,
        camera,
        depthColor,
        nonSampleableDepth,
        StandardColorSpaces.LinearSrgb,
        colorTargetInitialized: true,
        depthTargetInitialized: true);
    ExpectThrows<InvalidOperationException>(
        () => depthPass.Execute(nonSampleableContext),
        "scene-depth-tested outline rejects non-sampleable depth",
        failures);
    using OutlineHelperRenderPass disposed = new(helper);
    disposed.Dispose();
    disposed.Dispose();
    ExpectThrows<ObjectDisposedException>(
        () => disposed.Execute(depthContext),
        "disposed outline-helper render pass rejection",
        failures);
}

static void ValidateTransformGizmo(ICollection<string> failures)
{
    SceneNode target = new("world translation target");
    target.Transform.Position = new Vector3(1f, 2f, 3f);
    TestFrameRequester requester = new();
    using TransformGizmo translation = new(target, requester)
    {
        TranslationSnap = 0.5f,
    };
    int startedCount = 0;
    int changedCount = 0;
    int completedCount = 0;
    TransformGizmoInteractionEventArgs? completed = null;
    translation.InteractionStarted += (_, _) => startedCount++;
    translation.InteractionChanged += (_, _) => changedCount++;
    translation.InteractionCompleted += (_, args) =>
    {
        completedCount++;
        completed = args;
    };
    translation.BeginInteraction(TransformGizmoAxis.X);
    Expect(
        translation.UpdateTranslation(0.74f),
        "world translation update accepted",
        failures);
    Expect(
        !translation.UpdateTranslation(0.74f),
        "repeated cumulative translation does not drift",
        failures);
    translation.CompleteInteraction();
    ExpectVectorNear(
        target.Transform.Position,
        new Vector3(1.5f, 2f, 3f),
        0.0001f,
        "world translation snapping",
        failures);
    Expect(
        startedCount == 1 && changedCount == 1 && completedCount == 1 &&
        requester.RequestCount == 3,
        "transform interaction events and frame requests",
        failures);
    Expect(
        completed is not null &&
        ReferenceEquals(completed.Target, target) &&
        completed.Mode == TransformGizmoMode.Translate &&
        completed.Space == TransformGizmoSpace.World &&
        completed.Axis == TransformGizmoAxis.X &&
        completed.InitialPose.Position == new Vector3(1f, 2f, 3f) &&
        completed.CurrentPose.Position == new Vector3(1.5f, 2f, 3f),
        "transform event carries immutable undo snapshots",
        failures);

    SceneNode parent = new("translated parent");
    parent.Transform.Position = new Vector3(3f, -2f, 4f);
    parent.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
    SceneNode localTarget = new("local translation target");
    localTarget.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f);
    parent.AddChild(localTarget);
    Matrix4x4 initialLocalWorld = localTarget.WorldMatrix;
    Vector3 initialLocalWorldPosition = Vector3.Transform(Vector3.Zero, initialLocalWorld);
    Vector3 localXAxis = Vector3.Normalize(
        Vector3.TransformNormal(Vector3.UnitX, initialLocalWorld));
    using TransformGizmo localTranslation = new(localTarget)
    {
        Space = TransformGizmoSpace.Local,
    };
    localTranslation.BeginInteraction(TransformGizmoAxis.X);
    localTranslation.UpdateTranslation(2f);
    localTranslation.CompleteInteraction();
    ExpectVectorNear(
        Vector3.Transform(Vector3.Zero, localTarget.WorldMatrix),
        initialLocalWorldPosition + localXAxis * 2f,
        0.0005f,
        "parented local-axis translation uses captured world direction",
        failures);

    SceneNode localRotationTarget = new("local rotation target");
    Quaternion initialLocalRotation =
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);
    localRotationTarget.Transform.Rotation = initialLocalRotation;
    using TransformGizmo localRotation = new(localRotationTarget)
    {
        Mode = TransformGizmoMode.Rotate,
        Space = TransformGizmoSpace.Local,
        RotationSnapRadians = MathF.PI / 4f,
    };
    localRotation.BeginInteraction(TransformGizmoAxis.X);
    localRotation.UpdateRotation(0.6f);
    localRotation.CompleteInteraction();
    Matrix4x4 expectedLocalRotation =
        Matrix4x4.CreateRotationX(MathF.PI / 4f) *
        Matrix4x4.CreateFromQuaternion(initialLocalRotation);
    ExpectMatrixNear(
        localRotationTarget.Transform.LocalMatrix,
        expectedLocalRotation,
        0.0005f,
        "local rotation applies around the captured local axis",
        failures);

    SceneNode worldRotationTarget = new("world rotation target");
    Quaternion initialWorldRotation =
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);
    worldRotationTarget.Transform.Rotation = initialWorldRotation;
    using TransformGizmo worldRotation = new(worldRotationTarget)
    {
        Mode = TransformGizmoMode.Rotate,
    };
    worldRotation.BeginInteraction(TransformGizmoAxis.X);
    worldRotation.UpdateRotation(0.3f);
    worldRotation.CompleteInteraction();
    Matrix4x4 expectedWorldRotation =
        Matrix4x4.CreateFromQuaternion(initialWorldRotation) *
        Matrix4x4.CreateRotationX(0.3f);
    ExpectMatrixNear(
        worldRotationTarget.WorldMatrix,
        expectedWorldRotation,
        0.0005f,
        "world rotation applies around the fixed world axis",
        failures);

    SceneNode scaleTarget = new("scale target");
    scaleTarget.Transform.Scale = new Vector3(2f, 3f, 4f);
    using TransformGizmo scale = new(scaleTarget)
    {
        Mode = TransformGizmoMode.Scale,
        Space = TransformGizmoSpace.Local,
        ScaleSnap = 0.25f,
    };
    int canceledCount = 0;
    scale.InteractionCanceled += (_, _) => canceledCount++;
    scale.BeginInteraction(TransformGizmoAxis.X);
    scale.UpdateScale(1.2f);
    ExpectVectorNear(
        scaleTarget.Transform.Scale,
        new Vector3(2.5f, 3f, 4f),
        0.0001f,
        "local axis scale snapping around one",
        failures);
    scale.CancelInteraction();
    ExpectVectorNear(
        scaleTarget.Transform.Scale,
        new Vector3(2f, 3f, 4f),
        0f,
        "scale cancellation restores initial pose",
        failures);
    Expect(canceledCount == 1, "transform cancellation event", failures);
    scale.BeginInteraction(TransformGizmoAxis.Uniform);
    scale.UpdateScale(2f);
    scale.CompleteInteraction();
    ExpectVectorNear(
        scaleTarget.Transform.Scale,
        new Vector3(4f, 6f, 8f),
        0.0001f,
        "uniform local scaling",
        failures);

    SceneNode rotatedScaleTarget = new("rotated local-axis scale target");
    Quaternion rotatedScaleRotation =
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f);
    rotatedScaleTarget.Transform.Rotation = rotatedScaleRotation;
    using TransformGizmo rotatedScale = new(rotatedScaleTarget)
    {
        Mode = TransformGizmoMode.Scale,
    };
    TransformGizmoInteractionEventArgs? rotatedScaleStarted = null;
    rotatedScale.InteractionStarted += (_, args) => rotatedScaleStarted = args;
    rotatedScale.BeginInteraction(TransformGizmoAxis.X);
    Expect(
        rotatedScaleStarted?.Space == TransformGizmoSpace.Local &&
        rotatedScale.UpdateScale(2f),
        "scale interaction uses the effective target-local space",
        failures);
    rotatedScale.CompleteInteraction();
    ExpectVectorNear(
        rotatedScaleTarget.Transform.Scale,
        new Vector3(2f, 1f, 1f),
        0.0001f,
        "rotated scale X handle changes only the matching local X component",
        failures);
    ExpectNear(
        MathF.Abs(Quaternion.Dot(
            rotatedScaleTarget.Transform.Rotation,
            rotatedScaleRotation)),
        1f,
        0.0001f,
        "local-axis scale preserves target rotation",
        failures);

    SceneNode sizedTarget = new("screen-sized target");
    PerspectiveCamera sizedCamera = new();
    sizedCamera.Transform.Position = new Vector3(0f, 0f, 10f);
    using TransformGizmo sized = new(sizedTarget)
    {
        ScreenSizePixels = 100f,
    };
    float expectedWorldSize =
        2f * 10f * MathF.Tan(sizedCamera.FieldOfViewRadians / 2f) * 0.1f;
    ExpectNear(
        sized.CalculateWorldSize(sizedCamera, 1000),
        expectedWorldSize,
        0.0001f,
        "fixed screen-relative gizmo world size",
        failures);
    sizedTarget.Transform.Position = new Vector3(0f, 0f, 20f);
    ExpectNear(
        sized.CalculateWorldSize(sizedCamera, 1000),
        0f,
        0f,
        "gizmo size is zero behind the camera",
        failures);

    SceneNode externallyChangedTarget = new("externally changed target");
    TransformGizmo externallyChanged = new(externallyChangedTarget);
    externallyChanged.BeginInteraction(TransformGizmoAxis.X);
    externallyChanged.UpdateTranslation(1f);
    externallyChangedTarget.Transform.Position = new Vector3(5f, 0f, 0f);
    ExpectThrows<InvalidOperationException>(
        externallyChanged.CompleteInteraction,
        "external transform mutation is detected",
        failures);
    externallyChanged.Dispose();
    ExpectVectorNear(
        externallyChangedTarget.Transform.Position,
        new Vector3(5f, 0f, 0f),
        0f,
        "gizmo disposal does not overwrite an external mutation",
        failures);

    SceneNode firstParent = new("first parent");
    SceneNode secondParent = new("second parent");
    SceneNode reparentedTarget = new("reparented target");
    firstParent.AddChild(reparentedTarget);
    using TransformGizmo reparented = new(reparentedTarget);
    reparented.BeginInteraction(TransformGizmoAxis.X);
    secondParent.AddChild(reparentedTarget);
    ExpectThrows<InvalidOperationException>(
        () => reparented.UpdateTranslation(1f),
        "hierarchy mutation is detected",
        failures);

    using TransformGizmo validation = new();
    ExpectThrows<InvalidOperationException>(
        () => validation.BeginInteraction(TransformGizmoAxis.X),
        "missing explicit gizmo target rejection",
        failures);
    validation.Target = new SceneNode("validation target");
    validation.Mode = TransformGizmoMode.Rotate;
    ExpectThrows<ArgumentException>(
        () => validation.BeginInteraction(TransformGizmoAxis.Uniform),
        "uniform non-scale handle rejection",
        failures);
    validation.BeginInteraction(TransformGizmoAxis.X);
    ExpectThrows<InvalidOperationException>(
        () => validation.Mode = TransformGizmoMode.Scale,
        "active gizmo mode mutation rejection",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => validation.UpdateRotation(float.NaN),
        "non-finite gizmo update rejection",
        failures);
    validation.CancelInteraction();

    using TransformGizmo mixed = new(new SceneNode("mixed gizmo target"));
    Expect(
        mixed.IsTranslateEnabled && !mixed.IsRotateEnabled && !mixed.IsScaleEnabled,
        "transform gizmo defaults to translation-only compatibility mode",
        failures);
    mixed.IsRotateEnabled = true;
    mixed.IsScaleEnabled = true;
    Expect(
        mixed.IsTranslateEnabled && mixed.IsRotateEnabled && mixed.IsScaleEnabled &&
        mixed.IsModeEnabled(TransformGizmoMode.Translate) &&
        mixed.IsModeEnabled(TransformGizmoMode.Rotate) &&
        mixed.IsModeEnabled(TransformGizmoMode.Scale),
        "independent mode booleans compose a mixed transform gizmo",
        failures);
    mixed.BeginInteraction(TransformGizmoMode.Rotate, TransformGizmoAxis.Y);
    Expect(
        mixed.ActiveMode == TransformGizmoMode.Rotate &&
        mixed.ActiveAxis == TransformGizmoAxis.Y,
        "mixed gizmo begins the explicitly hit operation",
        failures);
    ExpectThrows<InvalidOperationException>(
        () => mixed.IsScaleEnabled = false,
        "mixed gizmo mode flags cannot change during interaction",
        failures);
    mixed.CancelInteraction();
    mixed.Mode = TransformGizmoMode.Scale;
    Expect(
        !mixed.IsTranslateEnabled && !mixed.IsRotateEnabled && mixed.IsScaleEnabled,
        "compatibility Mode assignment restores an exclusive handle set",
        failures);
    mixed.IsScaleEnabled = false;
    ExpectThrows<InvalidOperationException>(
        () => mixed.BeginInteraction(TransformGizmoMode.Scale, TransformGizmoAxis.X),
        "disabled mixed gizmo operation cannot begin",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => mixed.IsModeEnabled((TransformGizmoMode)int.MaxValue),
        "invalid mixed gizmo operation query rejection",
        failures);

    TransformGizmo disposed = new(new SceneNode("disposed gizmo target"));
    disposed.Dispose();
    disposed.Dispose();
    ExpectThrows<ObjectDisposedException>(
        () => disposed.BeginInteraction(TransformGizmoAxis.X),
        "disposed transform gizmo rejection",
        failures);
}

static void ValidateTransformGizmoHitTesting(ICollection<string> failures)
{
    const uint viewportWidth = 1000;
    const uint viewportHeight = 1000;
    PerspectiveCamera camera = new(aspectRatio: 1f);
    camera.Transform.Position = new Vector3(0f, 0f, 10f);
    SceneNode target = new("hit-test target");
    using TransformGizmo gizmo = new(target)
    {
        ScreenSizePixels = 100f,
    };
    TransformGizmoHitTester tester = new(hitTolerancePixels: 8f);

    TransformGizmoHit? xHit = tester.HitTest(
        gizmo,
        camera,
        viewportWidth,
        viewportHeight,
        new Vector2(550f, 500f));
    Expect(
        xHit is not null &&
        xHit.Mode == TransformGizmoMode.Translate &&
        xHit.Axis == TransformGizmoAxis.X &&
        xHit.ScreenDistancePixels <= 0.001f &&
        xHit.CameraDistance > 0f,
        "projected world X translation handle hit",
        failures);
    TransformGizmoHit? yHit = tester.HitTest(
        gizmo,
        camera,
        viewportWidth,
        viewportHeight,
        new Vector2(500f, 450f));
    Expect(
        yHit?.Axis == TransformGizmoAxis.Y,
        "projected world Y translation handle hit",
        failures);
    Expect(
        tester.HitTest(
            gizmo,
            camera,
            viewportWidth,
            viewportHeight,
            new Vector2(800f, 800f)) is null,
        "pointer outside transform handles misses",
        failures);

    gizmo.Mode = TransformGizmoMode.Scale;
    TransformGizmoHit? uniformHit = tester.HitTest(
        gizmo,
        camera,
        viewportWidth,
        viewportHeight,
        new Vector2(500f, 500f));
    Expect(
        uniformHit is not null &&
        uniformHit.Mode == TransformGizmoMode.Scale &&
        uniformHit.Axis == TransformGizmoAxis.Uniform,
        "uniform scale center takes center hit priority",
        failures);

    gizmo.Mode = TransformGizmoMode.Rotate;
    TransformGizmoHit? ringHit = tester.HitTest(
        gizmo,
        camera,
        viewportWidth,
        viewportHeight,
        new Vector2(553f, 447f));
    Expect(
        ringHit is not null &&
        ringHit.Mode == TransformGizmoMode.Rotate &&
        ringHit.Axis == TransformGizmoAxis.Z &&
        ringHit.ScreenDistancePixels < 1f,
        "projected Z rotation ring hit",
        failures);

    gizmo.Mode = TransformGizmoMode.Translate;
    gizmo.IsRotateEnabled = true;
    gizmo.IsScaleEnabled = true;
    TransformGizmoHit? mixedTranslateHit = tester.HitTest(
        gizmo,
        camera,
        viewportWidth,
        viewportHeight,
        new Vector2(600f, 500f));
    TransformGizmoHit? mixedScaleHit = tester.HitTest(
        gizmo,
        camera,
        viewportWidth,
        viewportHeight,
        new Vector2(572f, 500f));
    TransformGizmoHit? mixedRotateHit = tester.HitTest(
        gizmo,
        camera,
        viewportWidth,
        viewportHeight,
        new Vector2(553f, 447f));
    Expect(
        mixedTranslateHit?.Mode == TransformGizmoMode.Translate &&
        mixedTranslateHit.Axis == TransformGizmoAxis.X &&
        mixedScaleHit?.Mode == TransformGizmoMode.Scale &&
        mixedScaleHit.Axis == TransformGizmoAxis.X &&
        mixedRotateHit?.Mode == TransformGizmoMode.Rotate &&
        mixedRotateHit.Axis == TransformGizmoAxis.Z,
        "mixed gizmo hit testing distinguishes arrow, scale marker and rotation ring",
        failures);
    gizmo.IsTranslateEnabled = false;
    gizmo.IsRotateEnabled = false;
    gizmo.IsScaleEnabled = false;
    Expect(
        tester.HitTest(
            gizmo,
            camera,
            viewportWidth,
            viewportHeight,
            new Vector2(600f, 500f)) is null,
        "gizmo with every operation disabled has no hit targets",
        failures);
    gizmo.Mode = TransformGizmoMode.Translate;

    SceneNode localTarget = new("local hit-test target");
    localTarget.Transform.Rotation =
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
    using TransformGizmo local = new(localTarget)
    {
        Space = TransformGizmoSpace.Local,
        ScreenSizePixels = 100f,
    };
    TransformGizmoHit? localXHit = tester.HitTest(
        local,
        camera,
        viewportWidth,
        viewportHeight,
        new Vector2(500f, 450f));
    Expect(
        localXHit?.Axis == TransformGizmoAxis.X,
        "local target rotation reorients hit-test axes",
        failures);

    SceneNode rotatedScaleHitTarget = new("rotated scale hit-test target");
    rotatedScaleHitTarget.Transform.Rotation =
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
    using TransformGizmo rotatedScaleHitGizmo = new(rotatedScaleHitTarget)
    {
        Mode = TransformGizmoMode.Scale,
        Space = TransformGizmoSpace.World,
        ScreenSizePixels = 100f,
    };
    TransformGizmoHit? rotatedScaleXHit = tester.HitTest(
        rotatedScaleHitGizmo,
        camera,
        viewportWidth,
        viewportHeight,
        new Vector2(500f, 428f));
    Expect(
        rotatedScaleXHit?.Mode == TransformGizmoMode.Scale &&
        rotatedScaleXHit.Axis == TransformGizmoAxis.X,
        "scale hit geometry follows the rotated target-local X axis",
        failures);

    SceneNode distantTarget = new("distant hit-test target");
    distantTarget.Transform.Position = new Vector3(0f, 0f, -10f);
    using TransformGizmo distant = new(distantTarget)
    {
        ScreenSizePixels = 100f,
    };
    Expect(
        tester.HitTest(
            distant,
            camera,
            viewportWidth,
            viewportHeight,
            new Vector2(550f, 500f))?.Axis == TransformGizmoAxis.X,
        "fixed screen-size hit geometry is depth invariant",
        failures);

    SceneNode behindTarget = new("behind-camera hit-test target");
    behindTarget.Transform.Position = new Vector3(0f, 0f, 20f);
    using TransformGizmo behind = new(behindTarget);
    Expect(
        tester.HitTest(
            behind,
            camera,
            viewportWidth,
            viewportHeight,
            new Vector2(500f, 500f)) is null,
        "behind-camera gizmo cannot be hit",
        failures);

    using TransformGizmo missingTarget = new();
    Expect(
        tester.HitTest(
            missingTarget,
            camera,
            viewportWidth,
            viewportHeight,
            new Vector2(500f, 500f)) is null,
        "targetless gizmo cannot be hit",
        failures);
    gizmo.BeginInteraction(TransformGizmoAxis.X);
    ExpectThrows<InvalidOperationException>(
        () => tester.HitTest(
            gizmo,
            camera,
            viewportWidth,
            viewportHeight,
            new Vector2(550f, 500f)),
        "active gizmo hit-test rejection",
        failures);
    gizmo.CancelInteraction();

    ExpectThrows<ArgumentOutOfRangeException>(
        () => _ = new TransformGizmoHitTester(0f),
        "zero gizmo hit tolerance rejection",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => tester.HitTolerancePixels = float.NaN,
        "non-finite gizmo hit tolerance rejection",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => tester.HitTest(
            gizmo,
            camera,
            viewportWidth,
            viewportHeight,
            new Vector2(-1f, 0f)),
        "out-of-viewport gizmo pointer rejection",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => tester.HitTest(
            gizmo,
            camera,
            0,
            viewportHeight,
            Vector2.Zero),
        "zero gizmo viewport width rejection",
        failures);
}

static void ValidateControlArbitration(ICollection<string> failures)
{
    ViewportControlArbiter arbiter = new();
    ViewportControlLease camera = arbiter.TryAcquire(
        "orbit camera",
        ViewportControlPriorities.Camera)!;
    Expect(
        camera.IsActive &&
        camera.OwnerName == "orbit camera" &&
        camera.Priority == ViewportControlPriorities.Camera &&
        ReferenceEquals(arbiter.CurrentLease, camera),
        "camera control lease acquisition",
        failures);
    Expect(
        arbiter.TryAcquire("peer camera", ViewportControlPriorities.Camera) is null &&
        arbiter.TryAcquire("lower priority", -1) is null,
        "equal and lower control claims are denied",
        failures);

    bool cameraRevoked = false;
    bool inactiveDuringRevocation = false;
    bool reentrantAcquireRejected = false;
    camera.Revoked += (_, _) =>
    {
        cameraRevoked = true;
        inactiveDuringRevocation = !camera.IsActive;
        try
        {
            arbiter.TryAcquire("reentrant claim", int.MaxValue);
        }
        catch (InvalidOperationException)
        {
            reentrantAcquireRejected = true;
        }
    };
    ViewportControlLease gizmo = arbiter.TryAcquire(
        "transform gizmo",
        ViewportControlPriorities.Gizmo)!;
    Expect(
        cameraRevoked && inactiveDuringRevocation && reentrantAcquireRejected &&
        !camera.IsActive && gizmo.IsActive &&
        ReferenceEquals(arbiter.CurrentLease, gizmo),
        "higher-priority gizmo lease revokes camera ownership",
        failures);
    camera.Dispose();
    Expect(
        ReferenceEquals(arbiter.CurrentLease, gizmo),
        "stale lease disposal cannot release current ownership",
        failures);

    bool gizmoRevoked = false;
    gizmo.Revoked += (_, _) => gizmoRevoked = true;
    ViewportControlLease overlay = arbiter.TryAcquire(
        "viewport overlay",
        ViewportControlPriorities.OverlayUi)!;
    Expect(
        gizmoRevoked && !gizmo.IsActive && overlay.IsActive &&
        ReferenceEquals(arbiter.CurrentLease, overlay),
        "viewport overlay input has priority over scene manipulation",
        failures);
    gizmo.Dispose();
    Expect(
        ReferenceEquals(arbiter.CurrentLease, overlay),
        "revoked gizmo disposal cannot release overlay ownership",
        failures);
    overlay.Dispose();
    Expect(
        !overlay.IsActive && arbiter.CurrentLease is null,
        "voluntary overlay lease release clears ownership",
        failures);

    ViewportControlLease finalLease = arbiter.TryAcquire("final camera", 1)!;
    bool finalRevoked = false;
    finalLease.Revoked += (_, _) => finalRevoked = true;
    arbiter.Dispose();
    arbiter.Dispose();
    Expect(
        finalRevoked && !finalLease.IsActive && arbiter.CurrentLease is null,
        "arbiter disposal revokes its active lease",
        failures);
    ExpectThrows<ObjectDisposedException>(
        () => arbiter.TryAcquire("late claim", 1),
        "disposed control arbiter rejection",
        failures);
}

static void ValidateFrameStatistics(ICollection<string> failures)
{
    FrameStatisticsCollector collector = new(maximumSampleCount: 2);
    RenderResourceCounts resources = new(
        meshCount: 4,
        vertexCount: 120,
        materialCount: 3,
        textureCount: 5,
        bufferCount: null,
        pipelineCount: 2,
        estimatedCpuBytes: 4096,
        estimatedGpuBytes: 8192);
    collector.UpdateResourceCounts(resources);
    FrameStatisticsSnapshot resourceOnly = collector.CaptureSnapshot();
    Expect(
        resourceOnly.SampleCount == 0 &&
        resourceOnly.FramesPerSecond == 0d &&
        resourceOnly.AverageAcquireMilliseconds is null &&
        resourceOnly.ResourceCounts == resources,
        "resource-only statistics snapshot",
        failures);

    collector.RecordFrame(new FrameStatisticsSample(
        TimeSpan.FromMilliseconds(10),
        new PresentationSurfaceFrameTimings(1d, 2d, 3d, 6d),
        drawCallCount: 1));
    collector.RecordFrame(new FrameStatisticsSample(
        TimeSpan.FromMilliseconds(20),
        new PresentationSurfaceFrameTimings(2d, 4d, 6d, 12d),
        default(SceneRendererFrameTimings),
        drawCallCount: 3,
        primitiveCount: 90));
    collector.RecordFrame(new FrameStatisticsSample(
        TimeSpan.FromMilliseconds(40),
        rendererTimings: default(SceneRendererFrameTimings),
        primitiveCount: 110));

    FrameStatisticsSnapshot snapshot = collector.CaptureSnapshot();
    Expect(
        collector.MaximumSampleCount == 2 &&
        collector.SampleCount == 2 &&
        snapshot.SampleCount == 2,
        "fixed statistics window evicts the oldest frame",
        failures);
    ExpectDoubleNear(
        snapshot.WindowDurationMilliseconds,
        60d,
        0.000001d,
        "statistics window duration",
        failures);
    ExpectDoubleNear(
        snapshot.FramesPerSecond,
        1000d / 30d,
        0.000001d,
        "rolling FPS from host-observed intervals",
        failures);
    ExpectDoubleNear(
        snapshot.AverageFrameMilliseconds,
        30d,
        0.000001d,
        "average frame interval",
        failures);
    ExpectDoubleNear(
        snapshot.MinimumFrameMilliseconds,
        20d,
        0.000001d,
        "minimum frame interval",
        failures);
    ExpectDoubleNear(
        snapshot.MaximumFrameMilliseconds,
        40d,
        0.000001d,
        "maximum frame interval",
        failures);
    Expect(
        snapshot.PresentationSampleCount == 1 &&
        snapshot.AverageAcquireMilliseconds == 2d &&
        snapshot.AveragePresentationRenderMilliseconds == 4d &&
        snapshot.AveragePresentMilliseconds == 6d &&
        snapshot.AveragePresentationTotalMilliseconds == 12d,
        "optional presentation metrics use only available samples",
        failures);
    Expect(
        snapshot.RendererSampleCount == 2 &&
        snapshot.AverageRendererPrepareMilliseconds == 0d &&
        snapshot.AverageRendererEncodeMilliseconds == 0d &&
        snapshot.AverageRendererSubmitMilliseconds == 0d &&
        snapshot.AverageRendererCacheTrimMilliseconds == 0d &&
        snapshot.AverageRendererTotalMilliseconds == 0d,
        "optional renderer metric availability",
        failures);
    Expect(
        snapshot.DrawCallSampleCount == 1 && snapshot.AverageDrawCallCount == 3d &&
        snapshot.PrimitiveSampleCount == 2 && snapshot.AveragePrimitiveCount == 100d,
        "optional draw and primitive averages use independent sample counts",
        failures);
    Expect(
        snapshot.ResourceCounts is RenderResourceCounts capturedResources &&
        capturedResources.MeshCount == 4 &&
        capturedResources.VertexCount == 120 &&
        capturedResources.MaterialCount == 3 &&
        capturedResources.TextureCount == 5 &&
        capturedResources.BufferCount is null &&
        capturedResources.PipelineCount == 2 &&
        capturedResources.EstimatedCpuBytes == 4096 &&
        capturedResources.EstimatedGpuBytes == 8192,
        "latest immutable resource counts",
        failures);

    collector.RecordFrame(new FrameStatisticsSample(TimeSpan.FromMilliseconds(80)));
    collector.UpdateResourceCounts(null);
    collector.Reset();
    FrameStatisticsSnapshot reset = collector.CaptureSnapshot();
    Expect(
        reset.SampleCount == 0 &&
        reset.WindowDurationMilliseconds == 0d &&
        reset.FramesPerSecond == 0d &&
        reset.AverageFrameMilliseconds == 0d &&
        reset.MinimumFrameMilliseconds == 0d &&
        reset.MaximumFrameMilliseconds == 0d &&
        reset.PresentationSampleCount == 0 &&
        reset.RendererSampleCount == 0 &&
        reset.DrawCallSampleCount == 0 &&
        reset.PrimitiveSampleCount == 0 &&
        reset.ResourceCounts is null,
        "statistics reset preserves capacity and clears values",
        failures);
    Expect(
        snapshot.SampleCount == 2 &&
        snapshot.WindowDurationMilliseconds == 60d &&
        snapshot.ResourceCounts == resources,
        "captured statistics remain immutable after collector mutation",
        failures);

    ExpectThrows<ArgumentOutOfRangeException>(
        () => _ = new FrameStatisticsCollector(0),
        "zero statistics capacity rejection",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => _ = new FrameStatisticsSample(TimeSpan.Zero),
        "zero frame duration rejection",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => _ = new FrameStatisticsSample(
            TimeSpan.FromMilliseconds(1),
            new PresentationSurfaceFrameTimings(double.NaN, 0d, 0d, 0d)),
        "non-finite presentation timing rejection",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => _ = new FrameStatisticsSample(
            TimeSpan.FromMilliseconds(1),
            drawCallCount: -1),
        "negative draw-call rejection",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => collector.RecordFrame(default),
        "default invalid frame sample rejection",
        failures);
    ExpectThrows<ArgumentOutOfRangeException>(
        () => _ = new RenderResourceCounts(meshCount: -1),
        "negative resource-count rejection",
        failures);
}

static void ExpectLooksAt(
    Camera camera,
    Vector3 target,
    string name,
    ICollection<string> failures)
{
    Vector3 eye = GetWorldPosition(camera);
    Vector3 expectedForward = Vector3.Normalize(target - eye);
    Vector3 actualForward = Vector3.Normalize(
        Vector3.TransformNormal(-Vector3.UnitZ, camera.WorldMatrix));
    ExpectNear(Vector3.Dot(expectedForward, actualForward), 1f, 0.0001f, name, failures);
}

static void ExpectVectorNear(
    Vector3 actual,
    Vector3 expected,
    float tolerance,
    string name,
    ICollection<string> failures)
{
    if (Vector3.Distance(actual, expected) > tolerance)
    {
        failures.Add(name);
    }
}

static void ExpectMatrixNear(
    Matrix4x4 actual,
    Matrix4x4 expected,
    float tolerance,
    string name,
    ICollection<string> failures)
{
    float[] actualValues =
    [
        actual.M11, actual.M12, actual.M13, actual.M14,
        actual.M21, actual.M22, actual.M23, actual.M24,
        actual.M31, actual.M32, actual.M33, actual.M34,
        actual.M41, actual.M42, actual.M43, actual.M44,
    ];
    float[] expectedValues =
    [
        expected.M11, expected.M12, expected.M13, expected.M14,
        expected.M21, expected.M22, expected.M23, expected.M24,
        expected.M31, expected.M32, expected.M33, expected.M34,
        expected.M41, expected.M42, expected.M43, expected.M44,
    ];
    for (int index = 0; index < actualValues.Length; index++)
    {
        if (MathF.Abs(actualValues[index] - expectedValues[index]) > tolerance)
        {
            failures.Add(name);
            return;
        }
    }
}

static void ExpectNear(
    float actual,
    float expected,
    float tolerance,
    string name,
    ICollection<string> failures)
{
    if (MathF.Abs(actual - expected) > tolerance)
    {
        failures.Add(name);
    }
}

static void ExpectDoubleNear(
    double actual,
    double expected,
    double tolerance,
    string name,
    ICollection<string> failures)
{
    if (Math.Abs(actual - expected) > tolerance)
    {
        failures.Add(name);
    }
}

static void Expect(bool condition, string name, ICollection<string> failures)
{
    if (!condition)
    {
        failures.Add(name);
    }
}

static void ExpectThrows<TException>(
    Action action,
    string name,
    ICollection<string> failures)
    where TException : Exception
{
    try
    {
        action();
        failures.Add(name);
    }
    catch (TException)
    {
    }
}

sealed class TestFrameRequester : IViewportFrameRequester
{
    internal int RequestCount { get; private set; }

    public void RequestFrame() => RequestCount++;
}

sealed record TestAsset(string Key);
