using System.Numerics;
using Mu3D.Color;

namespace Mu3D.GalleryApp.Examples;

// The sample permits nonnegative linear sRGB, including HDR. Library conversions stay unclipped.
internal static class PaintingColorPickerPolicy
{
    private const int ScanSteps = 128, Refinements = 24;
    internal static bool IsNonnegative(LinearRgba color) => color.Red >= 0 && color.Green >= 0 && color.Blue >= 0;

    internal static bool Project(PaintingColorSpacesExample example, Vector3 requested,
        out Vector3 coordinates, out LinearRgba color)
    {
        Vector3 origin = example.Coordinates;
        coordinates = origin; color = example.Color;
        // A diagnostic raw color has no valid path origin. Only an explicit reset may replace it.
        if (!example.IsPickable(color)) return false;
        if (requested == origin) return true;
        // A valid requested color is directly selectable, even across an invalid coordinate interval.
        if (TryColor(example, requested, out LinearRgba endpoint))
        { coordinates = requested; color = endpoint; return true; }
        int hue = example.Model is 1 or 3 ? 2 : example.Model >= 6 ? 0 : -1;
        float hueOrigin = hue >= 0 ? NormalizeHue(origin[hue]) : 0;
        float hueDelta = hue >= 0 ? (NormalizeHue(requested[hue]) - hueOrigin + 540) % 360 - 180 : 0;
        float lastValid = 0;
        for (int step = 1; step <= ScanSteps; step++)
        {
            float fraction = step / (float)ScanSteps;
            Vector3 candidate = At(origin, requested, hue, hueOrigin, hueDelta, fraction);
            if (TryColor(example, candidate, out LinearRgba candidateColor))
            { lastValid = fraction; coordinates = candidate; color = candidateColor; continue; }
            // An invalid endpoint stops at the first detected invalid interval along the edit path.
            float low = lastValid, high = fraction;
            for (int i = 0; i < Refinements; i++)
            {
                float middle = (low + high) * 0.5f;
                if (middle == low || middle == high) break;
                candidate = At(origin, requested, hue, hueOrigin, hueDelta, middle);
                if (TryColor(example, candidate, out candidateColor))
                { low = middle; coordinates = candidate; color = candidateColor; }
                else high = middle;
            }
            return false;
        }
        return true;
    }

    private static Vector3 At(Vector3 origin, Vector3 requested, int hue, float hueOrigin, float hueDelta, float fraction)
    {
        if (fraction == 1) return requested;
        Vector3 coordinates = Vector3.Lerp(origin, requested, fraction);
        if (hue >= 0 && requested[hue] != origin[hue]) coordinates[hue] = hueOrigin + hueDelta * fraction;
        return coordinates;
    }
    private static bool TryColor(PaintingColorSpacesExample example, Vector3 coordinates, out LinearRgba color)
    {
        color = default;
        if (!float.IsFinite(coordinates.X) || !float.IsFinite(coordinates.Y) || !float.IsFinite(coordinates.Z)) return false;
        if ((example.Model is 1 or 3) && coordinates.Y < 0) return false;
        if (example.Model >= 6 && (coordinates.Y is < 0 or > 1 || coordinates.Z is < 0 or > 1)) return false;
        try { color = example.ColorAt(coordinates); }
        // Only genuine FP32 conversion overflow reaches this catch; ordinary domain checks branch above.
        catch (ArgumentOutOfRangeException) { return false; }
        return IsNonnegative(color);
    }
    private static float NormalizeHue(float hue)
    { float normalized = hue % 360; return normalized < 0 ? normalized + 360 : normalized; }
}
