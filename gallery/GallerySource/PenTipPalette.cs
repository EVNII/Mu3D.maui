using System.Numerics;

namespace Mu3D.GalleryApp.Examples;

// The same 24-hue/four-magnitude application palette feeds MAUI and browser cursors/markers.
internal static class PenTipPalette
{
    internal const int HueSteps = 24, MagnitudeSteps = 4, Count = 1 + HueSteps * MagnitudeSteps;
    internal static float Rotation(float? tiltX, float? tiltY) => tiltX is float x && tiltY is float y ? MathF.Atan2(y, x) * 180 / MathF.PI + 90 : 0;
    internal static int Index(bool isPen, float? tiltX, float? tiltY)
    {
        if (!isPen || tiltX is not float x || tiltY is not float y) return 0;
        float magnitude = Math.Clamp(MathF.Sqrt(x * x + y * y) / 70, 0, 1);
        if (magnitude < .02f) return 0;
        float hue = MathF.Atan2(y, x) / (2 * MathF.PI); hue -= MathF.Floor(hue);
        return 1 + Math.Min(MagnitudeSteps - 1, (int)(magnitude * MagnitudeSteps)) * HueSteps + Math.Min(HueSteps - 1, (int)(hue * HueSteps));
    }
    internal static Vector3 EncodedColor(int index)
    {
        if (index == 0) return new(53 / 255f, 242 / 255f, 208 / 255f);
        int magnitudeIndex = (index - 1) / HueSteps, hueIndex = (index - 1) % HueSteps;
        float saturation = .62f + .38f * (magnitudeIndex + 1f) / MagnitudeSteps;
        float sector = hueIndex / (float)HueSteps * 6, fraction = sector - MathF.Floor(sector), minimum = 1 - saturation;
        float descending = 1 - fraction * saturation, ascending = 1 - (1 - fraction) * saturation;
        return ((int)MathF.Floor(sector) % 6) switch { 0 => new(1, ascending, minimum), 1 => new(descending, 1, minimum),
            2 => new(minimum, 1, ascending), 3 => new(minimum, descending, 1), 4 => new(ascending, minimum, 1), _ => new(1, minimum, descending) };
    }
}
