using MauiColor = Microsoft.Maui.Graphics.Color;
using Mu3D.Color;

namespace Mu3D.Maui.Controls;

internal static class DeclarativeColors
{
    internal static LinearRgba ToLinearSrgb(MauiColor color, bool opaque = false)
    {
        ArgumentNullException.ThrowIfNull(color);
        return new LinearRgba(
            DecodeSrgb(color.Red),
            DecodeSrgb(color.Green),
            DecodeSrgb(color.Blue),
            opaque ? 1f : color.Alpha,
            StandardColorSpaces.LinearSrgb);
    }

    private static float DecodeSrgb(float encoded) => encoded <= 0.04045f
        ? encoded / 12.92f
        : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
}
