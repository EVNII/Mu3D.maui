using System.Diagnostics;
using System.Numerics;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Maui.Toolkit.Diagnostics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Animation;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Diagnostics;
using Mu3D.Toolkit.Gizmos;

List<string> failures = [];

ValidateSurfaceFrameScheduling(failures);
ValidateRenderOutputToolState(failures);
ValidatePlaybackToolbarState(failures);

Expect(
    (int)ViewportDragAction.Rotate == 0 &&
    (int)ViewportDragAction.Pan == 1 &&
    (int)ViewportDragAction.None == 2,
    "drag actions preserve rotate/pan/none numeric ordering",
    failures);

Expect(
    (int)ViewportScalarAction.Dolly == 0 &&
    (int)ViewportScalarAction.None == 1,
    "scalar actions preserve dolly/none numeric ordering",
    failures);

Expect(
    ViewportKeyboardBindingState.Resolve(
        ViewportKey.A,
        ViewportKey.LeftArrow,
        ViewportKey.RightArrow,
        ViewportKey.UpArrow,
        ViewportKey.DownArrow,
        ViewportKey.A,
        ViewportKey.D,
        ViewportKey.E,
        ViewportKey.Q,
        ViewportKey.W,
        ViewportKey.S,
        ViewportKey.PageUp,
        ViewportKey.PageDown) == ViewportKeyboardAction.PanLeft &&
    ViewportKeyboardBindingState.Resolve(
        ViewportKey.W,
        ViewportKey.LeftArrow,
        ViewportKey.RightArrow,
        ViewportKey.UpArrow,
        ViewportKey.DownArrow,
        ViewportKey.A,
        ViewportKey.D,
        ViewportKey.E,
        ViewportKey.Q,
        ViewportKey.W,
        ViewportKey.S,
        ViewportKey.PageUp,
        ViewportKey.PageDown) == ViewportKeyboardAction.DollyIn,
    "portable keyboard bindings resolve customized letter keys",
    failures);
Expect(
    ViewportKeyboardBindingState.Resolve(
        ViewportKey.Z,
        ViewportKey.LeftArrow,
        ViewportKey.RightArrow,
        ViewportKey.UpArrow,
        ViewportKey.DownArrow,
        ViewportKey.A,
        ViewportKey.D,
        ViewportKey.E,
        ViewportKey.Q,
        ViewportKey.W,
        ViewportKey.S,
        ViewportKey.PageUp,
        ViewportKey.PageDown) is null,
    "unassigned portable keys remain application-owned",
    failures);

Vector2 rotation = ViewportGestureState.MapDragDelta(
    deltaX: 50d,
    deltaY: 25d,
    width: 200d,
    height: 100d,
    ViewportDragAction.Rotate,
    MathF.PI * 2f,
    panUnitsPerViewport: 1f,
    useIsotropicNormalization: false);
ExpectVectorNear(
    rotation,
    new Vector2(-MathF.PI / 2f, -MathF.PI / 2f),
    0.00001f,
    "viewport-normalized orbit rotation mapping with non-inverted vertical drag",
    failures);
Vector2 pan = ViewportGestureState.MapDragDelta(
    deltaX: 50d,
    deltaY: 25d,
    width: 200d,
    height: 100d,
    ViewportDragAction.Pan,
    MathF.PI * 2f,
    panUnitsPerViewport: 1f,
    useIsotropicNormalization: false);
ExpectVectorNear(
    pan,
    new Vector2(-0.25f, 0.25f),
    0.00001f,
    "viewport-normalized orbit pan mapping",
    failures);
Expect(
    ViewportGestureState.MapDragDelta(
        50d,
        25d,
        200d,
        100d,
        ViewportDragAction.None,
        MathF.PI * 2f,
        1f,
        false) == Vector2.Zero,
    "disabled primary drag maps to no camera command",
    failures);
Expect(
    ViewportGestureState.MapDragDelta(
        1d,
        1d,
        0d,
        100d,
        ViewportDragAction.Rotate,
        1f,
        1f,
        false) == Vector2.Zero,
    "unarranged viewport ignores pan delta",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => ViewportGestureState.MapDragDelta(
        double.NaN,
        0d,
        100d,
        100d,
        ViewportDragAction.Rotate,
        1f,
        1f,
        false),
    "non-finite gesture delta rejection",
    failures);
ViewportControlArbiter arbiter = new();
using ViewportGestureState gestures = new();
ExpectThrows<ArgumentOutOfRangeException>(
    () => gestures.BeginPan(double.NaN, 0d, arbiter),
    "non-finite initial pan total rejection",
    failures);
Expect(
    gestures.BeginPan(10d, 20d, arbiter) &&
    gestures.IsActive &&
    arbiter.CurrentLease?.Priority == ViewportControlPriorities.Camera,
    "pan gesture obtains camera-priority ownership",
    failures);
Expect(
    gestures.TryGetPanDelta(14d, 26d, out double deltaX, out double deltaY) &&
    deltaX == 4d && deltaY == 6d,
    "pan gesture converts cumulative totals to incremental deltas",
    failures);
gestures.EndPan();
Expect(
    !gestures.IsActive && arbiter.CurrentLease is null,
    "pan completion releases ownership",
    failures);

Expect(gestures.BeginPinch(arbiter), "pinch gesture acquisition", failures);
Expect(
    gestures.TryGetPinchRatio(2d, out double firstRatio) && firstRatio == 2d &&
    gestures.TryGetPinchRatio(3d, out double secondRatio) && secondRatio == 1.5d,
    "pinch cumulative scale becomes logarithmic-ready ratios",
    failures);
using ViewportControlLease gizmoLease = arbiter.TryAcquire(
    "test gizmo",
    ViewportControlPriorities.Gizmo)!;
Expect(
    !gestures.IsActive && ReferenceEquals(arbiter.CurrentLease, gizmoLease),
    "gizmo lease revokes active orbit gesture state",
    failures);
gizmoLease.Dispose();
using ViewportControlLease blockingLease = arbiter.TryAcquire(
    "blocking gizmo",
    ViewportControlPriorities.Gizmo)!;
Expect(
    !gestures.BeginPan(0d, 0d, arbiter) && !gestures.IsActive,
    "higher-priority ownership denies a new orbit gesture",
    failures);
blockingLease.Dispose();
ExpectThrows<ArgumentOutOfRangeException>(
    () =>
    {
        gestures.BeginPinch(arbiter);
        gestures.TryGetPinchRatio(double.NaN, out _);
    },
    "invalid pinch scale rejection",
    failures);
gestures.End();

using ViewportGestureState secondaryGesture = new();
Expect(
    secondaryGesture.BeginPan(300d, 200d, arbiter) &&
    !gestures.BeginPan(0d, 0d, arbiter),
    "an active secondary-pointer state prevents a second camera gesture from sharing totals",
    failures);
Expect(
    secondaryGesture.TryGetPanDelta(312d, 191d, out double secondaryDeltaX, out double secondaryDeltaY) &&
    secondaryDeltaX == 12d && secondaryDeltaY == -9d,
    "secondary-pointer absolute coordinates produce isolated incremental deltas",
    failures);
secondaryGesture.EndPan();
Expect(
    arbiter.CurrentLease is null,
    "secondary-pointer completion releases its camera lease",
    failures);

using ViewportMultiTouchGestureState multiTouchGesture = new();
Expect(
    multiTouchGesture.BeginPan(10d, 20d, arbiter) &&
    multiTouchGesture.BeginPinch(arbiter) &&
    multiTouchGesture.IsActive &&
    arbiter.CurrentLease?.Priority == ViewportControlPriorities.Camera,
    "two-pointer pan and pinch share one camera-priority gesture lease",
    failures);
Expect(
    multiTouchGesture.TryGetPanDelta(16d, 17d, out double multiDeltaX, out double multiDeltaY) &&
    multiDeltaX == 6d && multiDeltaY == -3d &&
    multiTouchGesture.TryGetPinchRatio(1.5d, out double multiPinchRatio) &&
    multiPinchRatio == 1.5d,
    "two-pointer translation and pinch retain independent cumulative state",
    failures);
multiTouchGesture.EndPan();
Expect(
    multiTouchGesture.IsActive && arbiter.CurrentLease is not null,
    "ending two-pointer translation preserves an active pinch lease",
    failures);
multiTouchGesture.EndPinch();
Expect(
    !multiTouchGesture.IsActive && arbiter.CurrentLease is null,
    "ending the final multi-touch component releases camera ownership",
    failures);
Expect(
    multiTouchGesture.BeginPinch(arbiter),
    "multi-touch pinch reacquires camera ownership",
    failures);
using (ViewportControlLease multiTouchGizmoLease = arbiter.TryAcquire(
    "multi-touch test gizmo",
    ViewportControlPriorities.Gizmo)!)
{
    Expect(
        !multiTouchGesture.IsActive && ReferenceEquals(arbiter.CurrentLease, multiTouchGizmoLease),
        "higher-priority ownership revokes every multi-touch component",
        failures);
}

PerspectiveCamera hardwareCamera = new(aspectRatio: 1f);
hardwareCamera.Transform.Position = new Vector3(0f, 0f, 10f);
using OrbitController hardwareController = new(hardwareCamera, Vector3.Zero);
ExpectNear(
    ViewportNavigationInputState.NormalizeWheelDelta(240f, 120f),
    2f,
    0f,
    "platform wheel units normalize to detents",
    failures);
ExpectNear(
    ViewportNavigationInputState.NormalizeWheelDelta(80f, 40f, invert: true),
    -2f,
    0f,
    "inverted continuous-scroll units normalize to detents",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => ViewportNavigationInputState.NormalizeWheelDelta(1f, 0f),
    "zero wheel unit scale rejection",
    failures);
Expect(
    ViewportNavigationInputState.TryApplyKeyboard(
        hardwareController,
        arbiter,
        ViewportKeyboardAction.RotateLeft,
        rotationStepRadians: 0.1f,
        panStep: 0.05f,
        dollyStep: 0.2f),
    "hardware arrow command accepted",
    failures);
ExpectNear(
    hardwareController.AzimuthAngle,
    -0.1f,
    0.00001f,
    "hardware arrow command mapping",
    failures);
Expect(
    arbiter.CurrentLease is null,
    "momentary hardware command releases camera ownership",
    failures);
PerspectiveCamera flyNavigationCamera = new();
flyNavigationCamera.Transform.Position = new Vector3(0f, 0f, 5f);
using FlyController flyNavigationController = new(flyNavigationCamera);
using (ViewportControlLease? flyBlockingLease = arbiter.TryAcquire(
    "test gizmo",
    ViewportControlPriorities.Gizmo))
{
    Expect(
        !FlyNavigationState.TryApply(
            flyNavigationController,
            arbiter,
            FlyNavigationAction.MoveForward,
            0.5f,
            0.1f) &&
        flyNavigationController.Position == new Vector3(0f, 0f, 5f),
        "fly navigation respects a higher-priority viewport owner",
        failures);
}
Expect(
    FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        FlyNavigationAction.MoveForward,
        0.5f,
        0.1f) &&
    flyNavigationController.Position == new Vector3(0f, 0f, 4.5f) &&
    arbiter.CurrentLease is null,
    "fly navigation maps movement and releases momentary ownership",
    failures);
Expect(
    FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        FlyNavigationAction.MoveBackward,
        0.5f,
        0.1f) &&
    FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        FlyNavigationAction.MoveLeft,
        0.5f,
        0.1f) &&
    FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        FlyNavigationAction.MoveRight,
        0.5f,
        0.1f) &&
    FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        FlyNavigationAction.MoveUp,
        0.5f,
        0.1f) &&
    FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        FlyNavigationAction.MoveDown,
        0.5f,
        0.1f) &&
    flyNavigationController.Position == new Vector3(0f, 0f, 5f),
    "fly navigation maps opposite movement actions symmetrically",
    failures);
Expect(
    FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        FlyNavigationAction.LookRight,
        0.5f,
        0.1f) &&
    flyNavigationController.ForwardDirection.X > 0f &&
    FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        FlyNavigationAction.LookLeft,
        0.5f,
        0.1f) &&
    FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        FlyNavigationAction.LookUp,
        0.5f,
        0.1f) &&
    flyNavigationController.ForwardDirection.Y > 0f &&
    FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        FlyNavigationAction.LookDown,
        0.5f,
        0.1f) &&
    Vector3.Distance(flyNavigationController.ForwardDirection, -Vector3.UnitZ) < 0.0001f &&
    arbiter.CurrentLease is null,
    "fly navigation maps opposite look actions symmetrically",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        (FlyNavigationAction)int.MaxValue,
        0.5f,
        0.1f),
    "fly navigation rejects an unknown action",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => FlyNavigationState.TryApply(
        flyNavigationController,
        arbiter,
        FlyNavigationAction.LookLeft,
        0.5f,
        float.NaN),
    "fly navigation rejects a non-finite look step",
    failures);
Vector2 mappedFlyRotate = ViewportGestureState.MapDragDelta(
    deltaX: 100d,
    deltaY: -50d,
    width: 200d,
    height: 100d,
    ViewportDragAction.Rotate,
    rotationRadiansPerViewport: MathF.PI,
    panUnitsPerViewport: 4f,
    useIsotropicNormalization: true);
ExpectNear(
    mappedFlyRotate.X,
    -MathF.PI,
    0.00001f,
    "shared Rotate route uses the short viewport side for isotropic horizontal speed",
    failures);
ExpectNear(
    mappedFlyRotate.Y,
    MathF.PI / 2f,
    0.00001f,
    "shared Rotate route maps upward drag consistently",
    failures);
Expect(
    ViewportGestureState.MapDragDelta(
        0d,
        0d,
        0d,
        100d,
        ViewportDragAction.Rotate,
        MathF.PI,
        4f,
        true) == Vector2.Zero,
    "unarranged viewport ignores a shared Rotate route",
    failures);
Vector2 mappedFlyTranslation = ViewportGestureState.MapDragDelta(
    deltaX: 50d,
    deltaY: -25d,
    width: 200d,
    height: 100d,
    ViewportDragAction.Pan,
    rotationRadiansPerViewport: MathF.PI,
    panUnitsPerViewport: 4f,
    useIsotropicNormalization: true);
ExpectVectorNear(
    mappedFlyTranslation,
    new Vector2(-2f, -1f),
    0.00001f,
    "shared Pan route uses one isotropic short-side scale",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => ViewportGestureState.MapDragDelta(
        0d,
        0d,
        100d,
        100d,
        ViewportDragAction.Pan,
        MathF.PI,
        0f,
        true),
    "shared Pan route rejects a non-positive scale",
    failures);
PerspectiveCamera compositeFlyNavigationCamera = new();
compositeFlyNavigationCamera.Transform.Position = new Vector3(0f, 0f, 5f);
using FlyController compositeFlyNavigation = new(compositeFlyNavigationCamera);
Expect(
    FlyNavigationState.TryApplyDelta(
        compositeFlyNavigation,
        arbiter,
        new Vector3(0.2f, 0f, 0.3f),
        new Vector2(0.1f, 0.08f)) &&
    compositeFlyNavigation.Position != new Vector3(0f, 0f, 5f) &&
    compositeFlyNavigation.YawAngle != 0f &&
    compositeFlyNavigation.PitchAngle != 0f &&
    arbiter.CurrentLease is null,
    "composite fly navigation applies movement and look under one lease",
    failures);
using (ViewportControlLease compositeFlyBlock = arbiter.TryAcquire(
    "blocking composite fly navigation",
    ViewportControlPriorities.Gizmo)!)
{
    Vector3 blockedFlyPosition = compositeFlyNavigation.Position;
    Expect(
        !FlyNavigationState.TryApplyDelta(
            compositeFlyNavigation,
            arbiter,
            Vector3.One,
            Vector2.One) &&
        compositeFlyNavigation.Position == blockedFlyPosition,
        "higher-priority ownership denies complete composite fly navigation",
        failures);
}
Vector3 targetBeforeNavigation = hardwareController.Target;
Expect(
    ViewportNavigationInputState.TryApplyNavigation(
        hardwareController,
        arbiter,
        OrbitNavigationAction.PanLeft,
        rotationStepRadians: 0.1f,
        panStep: 0.05f,
        dollyStep: 0.2f) &&
    hardwareController.Target != targetBeforeNavigation &&
    arbiter.CurrentLease is null,
    "MAUI navigation command pans and releases momentary ownership",
    failures);
PerspectiveCamera compositeCamera = new(aspectRatio: 1f);
compositeCamera.Transform.Position = new Vector3(0f, 0f, 10f);
using OrbitController compositeController = new(compositeCamera, Vector3.Zero);
float compositeAzimuthBefore = compositeController.AzimuthAngle;
float compositePolarBefore = compositeController.PolarAngle;
Vector3 compositeTargetBefore = compositeController.Target;
float compositeDistanceBefore = compositeController.Distance;
Expect(
    ViewportNavigationInputState.TryApplyNavigationDelta(
        compositeController,
        arbiter,
        new Vector2(0.1f, -0.08f),
        new Vector2(0.04f, 0.03f),
        0.12f) &&
    compositeController.AzimuthAngle != compositeAzimuthBefore &&
    compositeController.PolarAngle != compositePolarBefore &&
    compositeController.Target != compositeTargetBefore &&
    compositeController.Distance != compositeDistanceBefore &&
    arbiter.CurrentLease is null,
    "composite navigation applies rotate, pan and dolly under one momentary lease",
    failures);
using (ViewportControlLease compositeBlock = arbiter.TryAcquire(
    "blocking composite navigation",
    ViewportControlPriorities.Gizmo)!)
{
    Vector3 blockedPosition = compositeCamera.Transform.Position;
    Expect(
        !ViewportNavigationInputState.TryApplyNavigationDelta(
            compositeController,
            arbiter,
            new Vector2(0.1f),
            new Vector2(0.1f),
            0.1f) &&
        compositeCamera.Transform.Position == blockedPosition,
        "higher-priority ownership denies the complete composite delta",
        failures);
}
ExpectThrows<ArgumentOutOfRangeException>(
    () => ViewportNavigationInputState.TryApplyNavigationDelta(
        compositeController,
        arbiter,
        new Vector2(float.NaN, 0f),
        Vector2.Zero,
        0f),
    "composite navigation rejects a non-finite rotation delta",
    failures);
float distanceBeforeWheel = hardwareController.Distance;
Expect(
    ViewportNavigationInputState.TryApplyWheel(
        hardwareController,
        arbiter,
        detents: 2f,
        dollyStep: 0.1f),
    "hardware wheel command accepted",
    failures);
ExpectNear(
    hardwareController.Distance,
    distanceBeforeWheel * MathF.Exp(-0.2f),
    0.0001f,
    "wheel detents map to logarithmic dolly",
    failures);
using (ViewportControlLease hardwareBlock = arbiter.TryAcquire(
    "blocking hardware gizmo",
    ViewportControlPriorities.Gizmo)!)
{
    float blockedAzimuth = hardwareController.AzimuthAngle;
    Expect(
        !ViewportNavigationInputState.TryApplyKeyboard(
            hardwareController,
            arbiter,
            ViewportKeyboardAction.RotateRight,
            rotationStepRadians: 0.1f,
            panStep: 0.05f,
            dollyStep: 0.2f) &&
        hardwareController.AzimuthAngle == blockedAzimuth,
        "higher-priority ownership denies hardware command",
        failures);
}
Expect(
    !ViewportNavigationInputState.TryApplyWheel(
        hardwareController,
        arbiter,
        detents: 0f,
        dollyStep: 0.1f),
    "zero wheel delta is ignored",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => ViewportNavigationInputState.TryApplyWheel(
        hardwareController,
        arbiter,
        float.NaN,
        0.1f),
    "non-finite wheel delta rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => ViewportNavigationInputState.TryApplyKeyboard(
        hardwareController,
        arbiter,
        (ViewportKeyboardAction)int.MaxValue,
        0.1f,
        0.05f,
        0.1f),
    "unknown hardware key action rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => ViewportNavigationInputState.TryApplyNavigation(
        hardwareController,
        arbiter,
        (OrbitNavigationAction)int.MaxValue,
        0.1f,
        0.05f,
        0.1f),
    "unknown MAUI navigation action rejection",
    failures);

const uint gizmoViewportWidth = 1000;
const uint gizmoViewportHeight = 1000;
PerspectiveCamera gizmoCamera = new(aspectRatio: 1f);
gizmoCamera.Transform.Position = new Vector3(0f, 0f, 10f);
SceneNode gizmoTarget = new("MAUI pointer-state target");
using TransformGizmo pointerGizmo = new(gizmoTarget)
{
    ScreenSizePixels = 100f,
};
TransformGizmoHitTester gizmoHitTester = new();
TransformGizmoHit translationHit = gizmoHitTester.HitTest(
    pointerGizmo,
    gizmoCamera,
    gizmoViewportWidth,
    gizmoViewportHeight,
    new Vector2(550f, 500f))!;
using ViewportControlArbiter pointerArbiter = new();
using TransformGizmoPointerState pointerState = new();
Expect(
    pointerState.Begin(
        pointerGizmo,
        translationHit,
        gizmoCamera,
        gizmoViewportWidth,
        gizmoViewportHeight,
        new Vector2(550f, 500f),
        pointerArbiter,
        scaleFactorPerGizmoLength: 2f) &&
    pointerState.IsActive &&
    pointerGizmo.IsInteracting &&
    pointerArbiter.CurrentLease?.Priority == ViewportControlPriorities.Gizmo,
    "gizmo pointer press acquires ownership and begins the hit axis",
    failures);
float expectedHalfWorldSize =
    pointerGizmo.CalculateWorldSize(gizmoCamera, gizmoViewportHeight) * 0.5f;
Expect(
    pointerState.Update(new Vector2(600f, 500f)),
    "gizmo pointer translation update accepted",
    failures);
ExpectNear(
    gizmoTarget.Transform.Position.X,
    expectedHalfWorldSize,
    0.0001f,
    "gizmo pointer maps projected X pixels to cumulative world distance",
    failures);
pointerState.Complete();
Expect(
    !pointerState.IsActive &&
    !pointerGizmo.IsInteracting &&
    pointerArbiter.CurrentLease is null,
    "gizmo pointer completion releases interaction and ownership",
    failures);

gizmoTarget.Transform.Position = Vector3.Zero;
pointerGizmo.Mode = TransformGizmoMode.Scale;
TransformGizmoHit uniformScaleHit = gizmoHitTester.HitTest(
    pointerGizmo,
    gizmoCamera,
    gizmoViewportWidth,
    gizmoViewportHeight,
    new Vector2(500f, 500f))!;
pointerState.Begin(
    pointerGizmo,
    uniformScaleHit,
    gizmoCamera,
    gizmoViewportWidth,
    gizmoViewportHeight,
    new Vector2(500f, 500f),
    pointerArbiter,
    scaleFactorPerGizmoLength: 2f);
Vector2 uniformDragDirection = Vector2.Normalize(new Vector2(1f, -1f));
pointerState.Update(new Vector2(500f, 500f) + uniformDragDirection * 100f);
ExpectVector3Near(
    gizmoTarget.Transform.Scale,
    new Vector3(2f),
    0.0001f,
    "uniform pointer drag doubles scale over one fixed-screen gizmo length",
    failures);
pointerState.Cancel();
ExpectVector3Near(
    gizmoTarget.Transform.Scale,
    Vector3.One,
    0.0001f,
    "gizmo pointer cancellation restores the initial pose",
    failures);

gizmoTarget.Transform.Rotation = Quaternion.CreateFromAxisAngle(
    Vector3.UnitZ,
    MathF.PI / 4f);
Vector2 rotatedScaleHandlePosition = new(551f, 449f);
TransformGizmoHit rotatedLocalScaleHit = gizmoHitTester.HitTest(
    pointerGizmo,
    gizmoCamera,
    gizmoViewportWidth,
    gizmoViewportHeight,
    rotatedScaleHandlePosition)!;
Vector2 rotatedScaleDragDirection = Vector2.Normalize(new Vector2(1f, -1f));
Expect(
    rotatedLocalScaleHit?.Mode == TransformGizmoMode.Scale &&
    rotatedLocalScaleHit.Axis == TransformGizmoAxis.X &&
    pointerState.Begin(
        pointerGizmo,
        rotatedLocalScaleHit,
        gizmoCamera,
        gizmoViewportWidth,
        gizmoViewportHeight,
        rotatedScaleHandlePosition,
        pointerArbiter,
        scaleFactorPerGizmoLength: 2f) &&
    pointerState.Update(rotatedScaleHandlePosition + rotatedScaleDragDirection * 100f),
    "pointer scale follows the visible rotated target-local X handle",
    failures);
pointerState.Complete();
ExpectVector3Near(
    gizmoTarget.Transform.Scale,
    new Vector3(2f, 1f, 1f),
    0.0002f,
    "rotated scale X drag changes only the target-local X component",
    failures);
gizmoTarget.Transform.Rotation = Quaternion.Identity;
gizmoTarget.Transform.Scale = Vector3.One;

pointerGizmo.Mode = TransformGizmoMode.Rotate;
TransformGizmoHit rotationHit = gizmoHitTester.HitTest(
    pointerGizmo,
    gizmoCamera,
    gizmoViewportWidth,
    gizmoViewportHeight,
    new Vector2(553f, 447f))!;
pointerState.Begin(
    pointerGizmo,
    rotationHit,
    gizmoCamera,
    gizmoViewportWidth,
    gizmoViewportHeight,
    new Vector2(553f, 447f),
    pointerArbiter,
    scaleFactorPerGizmoLength: 2f);
Expect(
    pointerState.Update(new Vector2(603f, 497f)) &&
    gizmoTarget.Transform.Rotation != Quaternion.Identity,
    "rotation-ring pointer drag maps to a signed cumulative angle",
    failures);
pointerState.Cancel();

pointerGizmo.Mode = TransformGizmoMode.Translate;
TransformGizmoHit revocableHit = gizmoHitTester.HitTest(
    pointerGizmo,
    gizmoCamera,
    gizmoViewportWidth,
    gizmoViewportHeight,
    new Vector2(550f, 500f))!;
pointerState.Begin(
    pointerGizmo,
    revocableHit,
    gizmoCamera,
    gizmoViewportWidth,
    gizmoViewportHeight,
    new Vector2(550f, 500f),
    pointerArbiter,
    scaleFactorPerGizmoLength: 2f);
pointerState.Update(new Vector2(600f, 500f));
using (ViewportControlLease preemptingLease = pointerArbiter.TryAcquire(
    "application modal tool",
    ViewportControlPriorities.Gizmo + 1)!)
{
    Expect(
        !pointerState.IsActive &&
        !pointerGizmo.IsInteracting &&
        gizmoTarget.Transform.Position == Vector3.Zero,
        "higher-priority revocation cancels and restores the gizmo interaction",
        failures);
}
using (ViewportControlLease blockingGizmoLease = pointerArbiter.TryAcquire(
    "blocking gizmo",
    ViewportControlPriorities.Gizmo)!)
{
    Expect(
        !pointerState.Begin(
            pointerGizmo,
            revocableHit,
            gizmoCamera,
            gizmoViewportWidth,
            gizmoViewportHeight,
            new Vector2(550f, 500f),
            pointerArbiter,
            scaleFactorPerGizmoLength: 2f) &&
        !pointerGizmo.IsInteracting,
        "equal-priority ownership denies a new gizmo pointer interaction",
        failures);
}

ViewportControlLease? reentrantLease = null;
void RevokeFromInteractionStarted(object? sender, TransformGizmoInteractionEventArgs args)
{
    _ = sender;
    _ = args;
    reentrantLease = pointerArbiter.TryAcquire(
        "reentrant modal tool",
        ViewportControlPriorities.Gizmo + 1);
}
pointerGizmo.InteractionStarted += RevokeFromInteractionStarted;
Expect(
    !pointerState.Begin(
        pointerGizmo,
        revocableHit,
        gizmoCamera,
        gizmoViewportWidth,
        gizmoViewportHeight,
        new Vector2(550f, 500f),
        pointerArbiter,
        scaleFactorPerGizmoLength: 2f) &&
    !pointerState.IsActive &&
    !pointerGizmo.IsInteracting &&
    reentrantLease?.IsActive == true,
    "interaction-start callback revocation cannot leave a gizmo active without ownership",
    failures);
pointerGizmo.InteractionStarted -= RevokeFromInteractionStarted;
reentrantLease?.Dispose();

ExpectVectorNear(
    TransformGizmoPointerState.MapViewportPosition(
        50d,
        25d,
        100d,
        50d,
        1000,
        500),
    new Vector2(500f, 250f),
    0f,
    "platform coordinates map into physical viewport pixels",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => TransformGizmoPointerState.MapViewportPosition(
        0d,
        0d,
        0d,
        50d,
        1000,
        500),
    "zero platform viewport size rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => pointerState.Begin(
        pointerGizmo,
        revocableHit,
        gizmoCamera,
        gizmoViewportWidth,
        gizmoViewportHeight,
        new Vector2(550f, 500f),
        pointerArbiter,
        scaleFactorPerGizmoLength: 1f),
    "non-growing gizmo pointer scale mapping rejection",
    failures);
TransformGizmoHit? degenerateZHit = gizmoHitTester.HitTest(
    pointerGizmo,
    gizmoCamera,
    gizmoViewportWidth,
    gizmoViewportHeight,
    new Vector2(500f, 500f));
Expect(
    degenerateZHit is null,
    "camera-aligned projected axis is omitted from pointer hit targets",
    failures);

FrameStatisticsCollector collector = new(maximumSampleCount: 4);
FrameStatisticsSamplingState sampling = new();
long firstTimestamp = Stopwatch.GetTimestamp();
long secondTimestamp = firstTimestamp + Stopwatch.Frequency / 50;
long thirdTimestamp = secondTimestamp + Stopwatch.Frequency / 100;
PresentationSurfaceFrameTimings timings = new(1d, 2d, 3d, 6d);
Expect(
    !sampling.TryRecord(
        firstTimestamp,
        TimeSpan.FromMilliseconds(500),
        collector,
        timings,
        default(SceneRendererFrameTimings),
        out _),
    "first presented frame establishes statistics baseline",
    failures);
Expect(
    sampling.TryRecord(
        secondTimestamp,
        TimeSpan.FromMilliseconds(500),
        collector,
        timings,
        default(SceneRendererFrameTimings),
        out FrameStatisticsSnapshot published) &&
    published.SampleCount == 1 &&
    published.PresentationSampleCount == 1 &&
    published.AveragePresentationTotalMilliseconds == 6d,
    "statistics adapter records and initially publishes",
    failures);
Expect(
    !sampling.TryRecord(
        thirdTimestamp,
        TimeSpan.FromMilliseconds(500),
        collector,
        timings,
        rendererTimings: null,
        out _) &&
    collector.SampleCount == 2,
    "statistics publication throttle does not throttle collection",
    failures);
sampling.ResetTimestamps();
Expect(
    !sampling.TryRecord(
        thirdTimestamp + Stopwatch.Frequency,
        TimeSpan.Zero,
        collector,
        timings,
        rendererTimings: null,
        out _),
    "statistics reset requires a new frame baseline",
    failures);

FrameStatisticsCollector countedCollector = new(maximumSampleCount: 2);
FrameStatisticsSamplingState countedSampling = new();
long countedStart = thirdTimestamp + (Stopwatch.Frequency * 2);
_ = countedSampling.TryRecord(
    countedStart,
    TimeSpan.Zero,
    countedCollector,
    timings,
    rendererTimings: null,
    drawCallCount: 3,
    primitiveCount: 12,
    out _);
Expect(
    countedSampling.TryRecord(
        countedStart + Stopwatch.Frequency / 60,
        TimeSpan.Zero,
        countedCollector,
        timings,
        rendererTimings: null,
        drawCallCount: 3,
        primitiveCount: 12,
        out FrameStatisticsSnapshot countedSnapshot) &&
    countedSnapshot.AverageDrawCallCount == 3d &&
    countedSnapshot.AveragePrimitiveCount == 12d,
    "statistics adapter records application-known draw and primitive counts",
    failures);

FrameStatisticsCollector textCollector = new(maximumSampleCount: 4);
textCollector.RecordFrame(new FrameStatisticsSample(
    TimeSpan.FromMilliseconds(20),
    new PresentationSurfaceFrameTimings(1d, 2d, 3d, 6d),
    default(SceneRendererFrameTimings),
    drawCallCount: 2,
    primitiveCount: 36));
textCollector.UpdateResourceCounts(new RenderResourceCounts(
    meshCount: 1,
    vertexCount: 28,
    materialCount: 1,
    textureCount: 0));
FrameStatisticsSnapshot textSnapshot = textCollector.CaptureSnapshot();
FrameStatisticsText compactStatistics = FrameStatisticsTextFormatter.Format(
    textSnapshot,
    isDetailed: false);
FrameStatisticsText detailedStatistics = FrameStatisticsTextFormatter.Format(
    textSnapshot,
    isDetailed: true);
Expect(
    compactStatistics.Headline == "50.0 FPS  ·  20.00 ms" &&
    compactStatistics.Details.Contains("Draws 2.0", StringComparison.Ordinal) &&
    !compactStatistics.Details.Contains("Resources", StringComparison.Ordinal),
    "compact statistics text reports headline and primary counts",
    failures);
Expect(
    detailedStatistics.Details.Contains("Presentation  acquire 1.00 ms", StringComparison.Ordinal) &&
    detailedStatistics.Details.Contains("Renderer  prepare 0.00 ms", StringComparison.Ordinal) &&
    detailedStatistics.Details.Contains("meshes 1", StringComparison.Ordinal) &&
    detailedStatistics.Details.Contains("vertices 28", StringComparison.Ordinal),
    "detailed statistics text reports timing breakdown and known resources",
    failures);
Expect(
    FrameStatisticsTextFormatter.Format(default, isDetailed: false).Headline ==
        "Waiting for frame samples…",
    "empty statistics text reports its waiting state",
    failures);

FrameStatisticsHistory statisticsHistory = new(capacity: 3);
statisticsHistory.Add(30d);
statisticsHistory.Add(60d);
statisticsHistory.Add(45d);
Expect(
    statisticsHistory.Count == 3 &&
    statisticsHistory.Minimum == 30d &&
    statisticsHistory.Maximum == 60d &&
    statisticsHistory[0] == 30d &&
    statisticsHistory[2] == 45d,
    "statistics graph history preserves its initial chronological range",
    failures);
statisticsHistory.Add(75d);
Expect(
    statisticsHistory.Count == 3 &&
    statisticsHistory.Minimum == 45d &&
    statisticsHistory.Maximum == 75d &&
    statisticsHistory[0] == 60d &&
    statisticsHistory[2] == 75d,
    "statistics graph history evicts the oldest value",
    failures);
statisticsHistory.Clear();
Expect(
    statisticsHistory.Count == 0 &&
    statisticsHistory.Minimum == 0d &&
    statisticsHistory.Maximum == 0d,
    "statistics graph history reset clears its range",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => statisticsHistory.Add(double.NaN),
    "statistics graph history rejects non-finite values",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => sampling.TryRecord(
        thirdTimestamp,
        TimeSpan.FromMilliseconds(-1),
        collector,
        timings,
        rendererTimings: null,
        out _),
    "negative statistics publication interval rejection",
    failures);

gestures.Dispose();
arbiter.Dispose();

if (failures.Count != 0)
{
    Console.Error.WriteLine("Mu3D.Maui.Toolkit adapter-state validation failed:");
    foreach (string failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }
    return 1;
}

Console.WriteLine(
    "Validated non-reentrant surface scheduling, render-output ownership, playback-toolbar state, MAUI gesture/hardware/gizmo-pointer mapping, arbitration, statistics cadence, display text and graph history.");
return 0;

static void ValidateSurfaceFrameScheduling(ICollection<string> failures)
{
    List<Action> dispatched = [];
    bool Dispatch(Action action)
    {
        dispatched.Add(action);
        return true;
    }

    SurfaceFrameScheduler scheduler = new();
    int renderCount = 0;
    int renderDepth = 0;
    int maximumRenderDepth = 0;
    void Render()
    {
        renderCount++;
        renderDepth++;
        maximumRenderDepth = Math.Max(maximumRenderDepth, renderDepth);
        if (renderCount == 1)
        {
            scheduler.Request(Dispatch, Render);
            scheduler.Request(Dispatch, Render);
        }

        renderDepth--;
    }

    scheduler.Request(Dispatch, Render);
    scheduler.Request(Dispatch, Render);
    Expect(
        dispatched.Count == 1 && renderCount == 0,
        "surface requests are deferred and coalesced before rendering",
        failures);

    dispatched[0]();
    Expect(
        renderCount == 1 && maximumRenderDepth == 1 && dispatched.Count == 2,
        "surface invalidation during rendering queues one non-reentrant follow-up",
        failures);

    dispatched[1]();
    Expect(
        renderCount == 2 && maximumRenderDepth == 1 && dispatched.Count == 2,
        "follow-up surface frame completes without an extra scheduling loop",
        failures);
    dispatched[1]();
    Expect(
        renderCount == 2 && maximumRenderDepth == 1,
        "a repeated platform callback cannot render the same surface frame twice",
        failures);

    SurfaceFrameScheduler rejected = new();
    List<Action> rejectedDispatches = [];
    int rejectedRenderCount = 0;
    rejected.Request(
        action =>
        {
            rejectedDispatches.Add(action);
            return false;
        },
        () => rejectedRenderCount++);
    rejected.Request(
        action =>
        {
            rejectedDispatches.Add(action);
            return true;
        },
        () => rejectedRenderCount++);
    Expect(
        rejectedDispatches.Count == 2,
        "rejected surface dispatch releases scheduler state for a later request",
        failures);
    rejectedDispatches[0]();
    rejectedDispatches[1]();
    Expect(
        rejectedRenderCount == 1,
        "a callback delivered after dispatch rejection cannot race the accepted frame",
        failures);

    List<Action> cancellationDispatches = [];
    SurfaceFrameScheduler canceled = new();
    int canceledRenderCount = 0;
    canceled.Request(
        action =>
        {
            cancellationDispatches.Add(action);
            return true;
        },
        () => canceledRenderCount++);
    canceled.CancelPending();
    canceled.Request(
        action =>
        {
            cancellationDispatches.Add(action);
            return true;
        },
        () => canceledRenderCount++);
    cancellationDispatches[0]();
    Expect(
        canceledRenderCount == 0 && cancellationDispatches.Count == 2,
        "surface cancellation invalidates stale dispatch and preserves a later request",
        failures);
    cancellationDispatches[1]();
    Expect(
        canceledRenderCount == 1 && cancellationDispatches.Count == 2,
        "surface scheduling resumes after cancellation with no stale frame",
        failures);
}

static void ValidateRenderOutputToolState(ICollection<string> failures)
{
    RenderOutputId current = RenderOutputIds.Beauty;
    int writes = 0;
    RenderOutputToolState state = new(
        () => current,
        value =>
        {
            current = value;
            writes++;
        });

    state.Update(true, RenderOutputIds.AmbientOcclusion);
    Expect(
        current == RenderOutputIds.AmbientOcclusion && writes == 1,
        "render-output tool applies its enabled output",
        failures);

    state.Update(true, RenderOutputIds.SurfaceNormal);
    Expect(
        current == RenderOutputIds.SurfaceNormal && writes == 2,
        "render-output tool updates an owned output",
        failures);

    state.Release();
    Expect(
        current == RenderOutputIds.Beauty && writes == 3,
        "render-output tool restores the pre-attachment output",
        failures);

    state.Update(false, RenderOutputIds.ViewDepth);
    Expect(
        current == RenderOutputIds.Beauty && writes == 3,
        "disabled render-output tool leaves the view unchanged",
        failures);

    current = RenderOutputIds.DirectLighting;
    state.Update(true, RenderOutputIds.ViewDepth);
    current = RenderOutputIds.Emissive;
    state.Release();
    Expect(
        current == RenderOutputIds.Emissive && writes == 4,
        "render-output tool does not overwrite a later application assignment",
        failures);

    ExpectThrows<ArgumentException>(
        () => state.Update(true, default),
        "render-output tool rejects an uninitialized output identifier",
        failures);
}

static void ValidatePlaybackToolbarState(ICollection<string> failures)
{
    PlaybackToolbarSnapshot empty = PlaybackToolbarState.Read(null);
    Expect(
        empty.Progress == 0d && !empty.CanPlayPause && !empty.CanSeek &&
        empty.TimeText == "--:-- / --:--",
        "playback toolbar disables an absent source",
        failures);

    TestPlayable playable = new()
    {
        State = PlaybackState.Playing,
        Position = TimeSpan.FromSeconds(75d),
        Duration = TimeSpan.FromSeconds(150d),
        CanSeek = true,
        IsLooping = true,
    };
    PlaybackToolbarSnapshot active = PlaybackToolbarState.Read(playable);
    Expect(
        active.Progress == 0.5d && active.TimeText == "1:15 / 2:30" &&
        active.CanPlayPause && active.CanSeek && active.IsPlaying && active.IsLooping,
        "playback toolbar reads normalized time and active transport state",
        failures);

    playable.State = PlaybackState.Buffering;
    playable.Position = TimeSpan.FromHours(1d) + TimeSpan.FromMinutes(2d) + TimeSpan.FromSeconds(3d);
    playable.Duration = TimeSpan.FromHours(2d);
    PlaybackToolbarSnapshot buffering = PlaybackToolbarState.Read(playable);
    Expect(
        buffering.IsBuffering && !buffering.CanPlayPause &&
        buffering.TimeText == "1:02:03 / 2:00:00",
        "playback toolbar represents buffering and hour-scale time",
        failures);

    playable.Position = TimeSpan.FromSeconds(12d);
    playable.Duration = TimeSpan.Zero;
    PlaybackToolbarSnapshot unknownDuration = PlaybackToolbarState.Read(playable);
    Expect(
        unknownDuration.Progress == 0d && !unknownDuration.CanSeek &&
        unknownDuration.TimeText == "0:12 / --:--",
        "playback toolbar handles an unknown duration without invalid seeking",
        failures);

    Expect(
        PlaybackToolbarState.PositionFromProgress(TimeSpan.FromSeconds(8d), 0.25d) ==
            TimeSpan.FromSeconds(2d) &&
        PlaybackToolbarState.PositionFromProgress(TimeSpan.FromSeconds(8d), 2d) ==
            TimeSpan.FromSeconds(8d) &&
        PlaybackToolbarState.PositionFromProgress(TimeSpan.Zero, 0.5d) == TimeSpan.Zero,
        "playback toolbar converts and clamps normalized seek positions",
        failures);
}

static void ExpectVectorNear(
    Vector2 actual,
    Vector2 expected,
    float tolerance,
    string name,
    ICollection<string> failures)
{
    if (Vector2.Distance(actual, expected) > tolerance)
    {
        failures.Add(name);
    }
}

static void ExpectVector3Near(
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

sealed class TestPlayable : IPlayable
{
    public event EventHandler? PlaybackChanged
    {
        add { }
        remove { }
    }

    public PlaybackState State { get; set; }

    public TimeSpan Position { get; set; }

    public TimeSpan Duration { get; set; }

    public bool CanSeek { get; set; }

    public bool IsLooping { get; set; }

    public void Play() => State = PlaybackState.Playing;

    public void Pause() => State = PlaybackState.Paused;

    public void Stop() => State = PlaybackState.Stopped;

    public void Seek(TimeSpan position) => Position = position;
}
