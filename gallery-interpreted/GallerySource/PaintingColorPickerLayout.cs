using System.Numerics;

namespace Mu3D.GalleryApp.Examples;

// One sample layout drives drawing and hit regions in physical pixels or logical host units.
internal readonly struct PaintingColorPickerLayout
{
    internal PaintingColorPickerLayout(float width, float height)
    {
        float aspect = Math.Max(width, 1) / Math.Max(height, 1);
        float side = Math.Min(.67f, .46f / aspect);
        Plane = new(.03f, .03f, side, side * aspect);
        Current = new(Plane.X + Plane.Z + .06f, Plane.Y, .88f - Plane.Z, Plane.W * .65f);
        Harmony = new(.03f, .54f, .94f, .055f);
    }
    internal Vector4 Plane { get; }
    internal Vector4 Strip => Track(0);
    internal Vector4 Current { get; }
    internal Vector4 Harmony { get; }
    internal static Vector4 Track(int channel)
    {
        if ((uint)channel > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        return new(.03f, .67f + channel * .105f, .94f, .045f);
    }
    internal int RegionAt(float u, float v)
    {
        if (Contains(Plane, u, v)) return 1;
        if (Contains(Harmony, u, v)) return 3;
        for (int i = 0; i < 3; i++) if (Contains(Track(i), u, v)) return 4 + i;
        return 0;
    }
    internal static Vector2 Map(Vector4 rect, float u, float v) => new((u - rect.X) / rect.Z, (v - rect.Y) / rect.W);
    internal bool Apply(PaintingColorSpacesExample example, int region, float u, float v)
    {
        if (!float.IsFinite(u) || !float.IsFinite(v)) return true;
        if (region == 1) { Vector2 p = Map(Plane, u, v); return example.PickPlane(p.X, p.Y); }
        if (region is >= 4 and <= 6)
        {
            int channel = region - 4; var range = example.GetChannel(channel);
            float fraction = Math.Clamp(Map(Track(channel), u, v).X, 0, 1);
            return example.PickValue(channel, range.Minimum + (range.Maximum - range.Minimum) * fraction);
        }
        if (region == 3)
        {
            int index = Math.Clamp((int)(Map(Harmony, u, v).X * 12), 0, 11);
            var color = example.SampleHarmony(index, 12);
            return example.IsPickable(color) && example.PickColor(color);
        }
        return true;
    }
    private static bool Contains(Vector4 rect, float u, float v) =>
        u >= rect.X && u <= rect.X + rect.Z && v >= rect.Y && v <= rect.Y + rect.W;
}
