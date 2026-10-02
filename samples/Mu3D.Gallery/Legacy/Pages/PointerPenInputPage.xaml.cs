using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Selection;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates application-owned pointer and pen samples feeding scene raycasting.</summary>
public partial class PointerPenInputPage : ContentPage
{
    private static readonly long RaycastIntervalTicks = Stopwatch.Frequency / 60;
    private static readonly MauiColor[] TipColors = CreateTipColors(alpha: 1f);
    private static readonly MauiColor[] TipFillColors = CreateTipColors(alpha: 0.14f);
    private readonly SceneRaycaster raycaster = new();
    private readonly Predicate<Mesh> raycastMeshFilter;
    private bool contactActive;
    private bool hitMarkerVisible;
    private int lastTipColorIndex;
    private long lastRaycastTimestamp;
    private ApplicationPointerPhase? lastStatusPhase;
    private int sampleCount;

    /// <summary>Initializes the pointer and pen input example.</summary>
    public PointerPenInputPage()
    {
        InitializeComponent();
        pencilTipPreview = new ApplePencilTipShadowPreview((casters, state) =>
            SceneView.Features.Add(new PenTipDisplayNormalShadowFeature(casters, state)));
        InitializePencilTipPreview();
        raycastMeshFilter = IsRaycastTarget;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        lastRaycastTimestamp = 0;
        lastStatusPhase = null;
        SceneView.InvalidateScene();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        contactActive = false;
        lastStatusPhase = null;
        HidePencilTipPreview();
        SetDrawingCursorVisible(false);
        base.OnDisappearing();
    }

    private void OnPointerSampled(object? sender, ApplicationPointerSample e)
    {
        _ = sender;
        sampleCount++;
        ObservePencilHoverCapability(e);
        bool statusPhaseChanged = lastStatusPhase != e.Phase;
        lastStatusPhase = e.Phase;
        if (e.DeviceKind == ApplicationPointerDeviceKind.Pen &&
            e.Phase is not ApplicationPointerPhase.Canceled and
            not ApplicationPointerPhase.Exited)
        {
            UpdateDrawingCursor(e);
            UpdatePencilTipPreview(e);
        }
        else if (e.DeviceKind != ApplicationPointerDeviceKind.Pen)
        {
            HidePencilTipPreview();
            SetDrawingCursorVisible(false);
        }
        if (e.Phase == ApplicationPointerPhase.Hovered)
        {
            if (statusPhaseChanged)
            {
                UpdateStatus(e, null, "pen hover");
            }
            return;
        }
        if (e.Phase == ApplicationPointerPhase.Exited)
        {
            contactActive = false;
            HidePencilTipPreview();
            SetDrawingCursorVisible(false);
            UpdateStatus(e, null, "pointer left viewport");
            return;
        }
        if (e.Phase == ApplicationPointerPhase.Canceled)
        {
            contactActive = false;
            HidePencilTipPreview();
            SetDrawingCursorVisible(false);
            UpdateStatus(e, null, "contact canceled");
            return;
        }
        if (e.Phase == ApplicationPointerPhase.Pressed)
        {
            contactActive = true;
        }
        else if (!contactActive)
        {
            return;
        }

        long raycastTimestamp = Stopwatch.GetTimestamp();
        if (e.Phase == ApplicationPointerPhase.Moved &&
            raycastTimestamp - lastRaycastTimestamp < RaycastIntervalTicks)
        {
            return;
        }
        lastRaycastTimestamp = raycastTimestamp;

        try
        {
            Scene? scene = SceneView.Scene;
            if (scene is null || SceneView.Camera is not PerspectiveCamera camera ||
                SceneView.PixelWidth == 0 || SceneView.PixelHeight == 0)
            {
                UpdateStatus(e, null, "waiting for a renderable viewport");
                return;
            }
            bool hasHit = raycaster.TryHitClosest(
                scene,
                camera,
                SceneView.PixelWidth,
                SceneView.PixelHeight,
                e.PositionPixels,
                out SceneRaycastIntersection hit,
                raycastMeshFilter);
            if (!hasHit)
            {
                SetHitMarkerVisible(false);
                if (statusPhaseChanged || e.Phase != ApplicationPointerPhase.Moved)
                {
                    UpdateStatus(e, null, "no visible triangle hit");
                }
            }
            else
            {
                float size = 0.65f + (e.Pressure ?? 0.5f) * 1.75f;
                ApplyTipColor(e);
                HitMarker.CoreNode.Transform.Position = hit.WorldPosition;
                HitMarker.CoreNode.Transform.Scale = new Vector3(size);
                HitMarker.CoreNode.IsVisible = true;
                hitMarkerVisible = true;
                SceneView.InvalidateScene();
                if (statusPhaseChanged || e.Phase != ApplicationPointerPhase.Moved)
                {
                    UpdateStatus(e, hit, "hit");
                }
            }
        }
        catch (Exception exception)
        {
            StatusLabel.Text = $"Application raycast failed: {exception.Message}";
        }
        finally
        {
            if (e.Phase == ApplicationPointerPhase.Released)
            {
                contactActive = false;
            }
        }
    }

    private void UpdateDrawingCursor(ApplicationPointerSample sample)
    {
        float maximumX = Math.Max(0f, (float)(SceneView.Width - DrawingCursor.WidthRequest));
        float maximumY = Math.Max(0f, (float)(SceneView.Height - DrawingCursor.HeightRequest));
        float x = Math.Clamp(
            (float)(sample.LogicalPosition.X - DrawingCursor.WidthRequest / 2d),
            0f,
            maximumX);
        float y = Math.Clamp(
            (float)(sample.LogicalPosition.Y - DrawingCursor.HeightRequest / 2d),
            0f,
            maximumY);
        float rotation = GetTiltRotation(sample);
        bool platformHandled = false;
        UpdateDrawingCursorPlatform(x, y, rotation, ref platformHandled);
        if (!platformHandled)
        {
            DrawingCursor.TranslationX = x;
            DrawingCursor.TranslationY = y;
            DrawingCursorTiltLine.Rotation = rotation;
        }
        ApplyTipColor(sample);
        SetDrawingCursorVisible(true);
    }

#if WINDOWS
    private partial void UpdateDrawingCursorPlatform(
        float x,
        float y,
        float rotation,
        ref bool handled);
#else
    private static void UpdateDrawingCursorPlatform(
        float x,
        float y,
        float rotation,
        ref bool handled)
    {
        _ = x;
        _ = y;
        _ = rotation;
        _ = handled;
    }
#endif

    private void ApplyTipColor(ApplicationPointerSample sample)
    {
        int colorIndex = GetTipColorIndex(sample);
        if (lastTipColorIndex == colorIndex)
        {
            return;
        }
        lastTipColorIndex = colorIndex;
        MauiColor color = TipColors[colorIndex];
        if (DrawingCursorRing.Stroke is SolidColorBrush stroke)
        {
            stroke.Color = color;
        }
        if (DrawingCursorRing.Fill is SolidColorBrush fill)
        {
            fill.Color = TipFillColors[colorIndex];
        }
        DrawingCursorTiltLine.Color = color;
        HitMarkerMaterial.Color = color;
    }

    private void SetDrawingCursorVisible(bool visible)
    {
        if (DrawingCursor.IsVisible != visible)
        {
            DrawingCursor.IsVisible = visible;
        }
    }

    private void SetHitMarkerVisible(bool visible)
    {
        if (hitMarkerVisible == visible)
        {
            return;
        }
        HitMarker.CoreNode.IsVisible = visible;
        hitMarkerVisible = visible;
        SceneView.InvalidateScene();
    }

    private bool IsRaycastTarget(Mesh mesh) => !ReferenceEquals(mesh, HitMarker.Mesh);

    private static float GetTiltRotation(ApplicationPointerSample sample) =>
        Examples.PenTipPalette.Rotation(sample.TiltXDegrees, sample.TiltYDegrees);

    private static int GetTipColorIndex(ApplicationPointerSample sample) =>
        Examples.PenTipPalette.Index(sample.DeviceKind == ApplicationPointerDeviceKind.Pen,
            sample.TiltXDegrees, sample.TiltYDegrees);

    private static MauiColor[] CreateTipColors(float alpha) => Enumerable.Range(0, Examples.PenTipPalette.Count)
        .Select(index => { Vector3 rgb = Examples.PenTipPalette.EncodedColor(index); return new MauiColor(rgb.X, rgb.Y, rgb.Z, alpha); }).ToArray();

    private void UpdateStatus(
        ApplicationPointerSample sample,
        SceneRaycastIntersection? hit,
        string result)
    {
        string pressure = FormatOptional(sample.Pressure, "0.00");
        string tilt = sample.TiltXDegrees is float tiltX && sample.TiltYDegrees is float tiltY
            ? $"{tiltX:0.#}°/{tiltY:0.#}°"
            : "n/a";
        string flags = sample.IsEraser
            ? "eraser"
            : sample.IsBarrelButtonPressed ? "barrel" : "tip";
        string hoverDistance = sample.HoverDistanceNormalized is float distance
            ? $"  hover z {distance:0.00}"
            : string.Empty;
        string world = !hit.HasValue
            ? "—"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"({hit.Value.WorldPosition.X:0.00}, {hit.Value.WorldPosition.Y:0.00}, {hit.Value.WorldPosition.Z:0.00})");
        StatusLabel.Text =
            $"#{sampleCount} {sample.DeviceKind}/{sample.Phase}  " +
            $"px {sample.PositionPixels.X:0},{sample.PositionPixels.Y:0}  " +
            $"pressure {pressure}  tilt {tilt}{hoverDistance}  {flags}  {result} {world}";
    }

    private static string FormatOptional(float? value, string format) =>
        value is float number ? number.ToString(format, CultureInfo.InvariantCulture) : "n/a";

    private void OnClearClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        SetHitMarkerVisible(false);
        StatusLabel.Text = "Marker cleared; the application still owns input and raycast policy.";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }
}
