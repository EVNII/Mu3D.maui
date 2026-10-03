using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;

namespace Mu3D.GalleryApp.Examples;

// Selector shapes, ranges and coordinate sections are this sample application's policy.
internal sealed class PaintingColorSpacesExample : IDisposable
{
    internal static readonly string[] Models = ["Lab · D50", "LCh · D50", "OKLab · D65", "OKLCh · D65",
        "YUV · BT.601", "YCbCr · BT.709", "HSL · sRGB", "HSV · sRGB"];
    internal readonly record struct Channel(string Name, float Minimum, float Maximum, float Step);
    private readonly float[] values = new float[3];
    private readonly int[] channelRevisions = new int[3];
    private readonly PaintingColorPickerRenderer renderer = new();
    internal PaintingColorSpacesExample() => Reset();
    internal int Model { get; private set; }
    internal LinearRgba Color { get; private set; }
    internal int Shape => Model is 1 or 3 ? 1 : Model == 6 ? 2 : 0;
    internal int PlaneX => Model is 1 or 3 ? 2 : 1;
    internal int PlaneY => Model is 1 or 3 ? 1 : 2;
    internal int StripChannel => 0;
    internal int PlaneRevision { get; private set; }
    internal int StripRevision { get; private set; }
    internal Vector3 Coordinates => new(values[0], values[1], values[2]);
    internal float GetValue(int channel) => values[channel];
    internal Channel GetChannel(int channel) => Definition(Model, channel);
    internal int ChannelRevision(int channel) { _ = GetChannel(channel); return channelRevisions[channel]; }
    internal float ChannelPosition(int channel)
    { var range = GetChannel(channel); return Fraction(values[channel], range); }
    internal bool IsPickable(LinearRgba color)
    {
        var rgb = StandardLinearRgbConverter.Convert(color, StandardColorSpaces.LinearSrgb);
        return PaintingColorPickerPolicy.IsNonnegative(rgb) &&
            (Model < 6 || rgb.Red <= 1 && rgb.Green <= 1 && rgb.Blue <= 1);
    }
    internal bool IsOutsideSelector
    {
        get
        {
            for (int i = 0; i < 3; i++)
            { var range = GetChannel(i); if (values[i] < range.Minimum || values[i] > range.Maximum) return true; }
            return false;
        }
    }
    internal float StripPosition => Fraction(values[StripChannel], GetChannel(StripChannel));
    internal Vector2 PlanePosition
    {
        get
        {
            if (Shape == 1)
            {
                float angle = values[2] * (MathF.PI / 180), radius = Fraction(values[1], GetChannel(1)) * 0.5f;
                return new(0.5f + MathF.Cos(angle) * radius, 0.5f - MathF.Sin(angle) * radius);
            }
            float x = Fraction(values[PlaneX], GetChannel(PlaneX)), y = Fraction(values[PlaneY], GetChannel(PlaneY));
            return new(Shape == 2 ? x * (1 - MathF.Abs(2 * y - 1)) : x, 1 - y);
        }
    }
    internal string Notes => Model switch
    {
        0 or 1 => "CIE Lab / LCh · D50；L* = 100 是参考白，允许 HDR 与超色域坐标。",
        2 or 3 => "OKLab / OKLCh · D65；L = 1 是参考白，允许 HDR 与超色域坐标。",
        4 or 5 => "Y′ 是编码 RGB 的 luma；色差通道零中心、全范围浮点，并非视频量化值。",
        _ => "HSL / HSV 基于编码 sRGB 0–1；不接收 HDR 或超色域颜色，不隐式裁切。",
    };
    internal string Readout
    {
        get
        {
            var c = Color; var e = StandardRgbEncodingConverter.Encode(c, StandardRgbEncoding.Srgb);
            bool negative = c.Red < 0 || c.Green < 0 || c.Blue < 0, hdr = c.Red > 1 || c.Green > 1 || c.Blue > 1;
            return FormattableString.Invariant($"Linear sRGB: {c.Red:0.0000}, {c.Green:0.0000}, {c.Blue:0.0000}\n") +
                FormattableString.Invariant($"Encoded sRGB: {e.Red:0.0000}, {e.Green:0.0000}, {e.Blue:0.0000}\n") +
                (negative ? "含负 RGB 分量；" : "") + (hdr ? "含 HDR 高光；" : "") +
                (IsOutsideSelector ? "坐标在选区范围之外；" : "") + "未做色域映射或色调映射。";
        }
    }
    internal void Reset() => SetColor(StandardRgbEncodingConverter.Decode(new(0.9f, 0.3f, 0.1f, 1, StandardRgbEncoding.Srgb)));
    internal void SelectModel(int model)
    {
        if ((uint)model >= Models.Length) throw new ArgumentOutOfRangeException(nameof(model));
        if (model == Model) return;
        Vector3 next = ToCoordinates(model, Color); // Validate before changing model or color.
        Model = model; Commit(next, Color, allChannels: true); PlaneRevision++; StripRevision++;
    }
    internal void SetColor(LinearRgba color)
    {
        LinearRgba nextColor = StandardLinearRgbConverter.Convert(color, StandardColorSpaces.LinearSrgb);
        if (nextColor == Color) return;
        Vector3 next = ToCoordinates(Model, nextColor);
        Commit(next, nextColor, allChannels: true); PlaneRevision++; StripRevision++;
    }
    internal void SetValue(int channel, float value)
    {
        if ((uint)channel > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == values[channel]) return;
        Vector3 next = Coordinates; next[channel] = value;
        LinearRgba color = Convert(Model, next, Color.Alpha);
        Commit(next, color);
        if (channel == StripChannel) PlaneRevision++; else StripRevision++;
    }
    internal void SetPlane(float u, float v)
    {
        Vector3 next = PlaneCoordinates(Model, Coordinates, Unit(u), Unit(v));
        if (next == Coordinates) return;
        LinearRgba color = Convert(Model, next, Color.Alpha);
        Commit(next, color); StripRevision++;
    }
    internal void SetStrip(float fraction) => SetValue(StripChannel, At(GetChannel(StripChannel), Unit(fraction)));
    internal bool PickValue(int channel, float value)
    {
        _ = GetChannel(channel);
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        Vector3 next = Coordinates; next[channel] = value; return PickCoordinates(next);
    }
    internal bool PickPlane(float u, float v) => PickCoordinates(PlaneCoordinates(Model, Coordinates, Unit(u), Unit(v)));
    internal bool PickColor(LinearRgba color)
    {
        if (!IsPickable(Color)) return false;
        var rgb = StandardLinearRgbConverter.Convert(color, StandardColorSpaces.LinearSrgb);
        if (Model >= 6 && !IsPickable(rgb)) return false;
        rgb = new(rgb.Red, rgb.Green, rgb.Blue, Color.Alpha, StandardColorSpaces.LinearSrgb);
        if (rgb == Color) return true;
        Vector3 next = ToCoordinates(Model, rgb);
        if (IsPickable(rgb))
        {
            // Keep the exact valid swatch; converting its coordinates back can introduce negative roundoff.
            CommitPick(next, rgb); return true;
        }
        return PickCoordinates(next);
    }
    private bool PickCoordinates(Vector3 requested)
    {
        bool reached = PaintingColorPickerPolicy.Project(this, requested, out Vector3 next, out LinearRgba color);
        CommitPick(next, color); return reached;
    }
    private void CommitPick(Vector3 next, LinearRgba color)
    {
        if (next != Coordinates || color != Color)
        {
            bool planeChanged = next.X != values[0], stripChanged = next.Y != values[1] || next.Z != values[2];
            Commit(next, color);
            if (planeChanged) PlaneRevision++;
            if (stripChanged) StripRevision++;
        }
    }
    internal LinearRgba ColorAt(Vector3 coordinates) => Convert(Model, coordinates, Color.Alpha);
    internal LinearRgba SamplePlane(float u, float v) => Convert(Model, PlaneCoordinates(Model, Coordinates, Unit(u), Unit(v)));
    internal LinearRgba SampleStrip(float fraction) => SampleChannel(StripChannel, fraction);
    internal LinearRgba SampleChannel(int channel, float fraction)
    { var range = GetChannel(channel); Vector3 next = Coordinates; next[channel] = At(range, Unit(fraction)); return Convert(Model, next); }
    internal LinearRgba SampleHarmony(int index, int count)
    {
        if (count < 1 || (uint)index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == 0) return Color;
        var current = PerceptualColorConverter.ToOklch(PerceptualColorConverter.ToOklab(Color));
        return PerceptualColorConverter.FromOklab(PerceptualColorConverter.FromOklch(
            new(current.Lightness, current.Chroma, current.HueDegrees + index * (360f / count), Color.Alpha)), StandardColorSpaces.LinearSrgb);
    }
    internal void FillPlane(Span<Vector2> coordinates, Span<LinearRgba> colors, int size)
    {
        if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
        int count = checked(size * size);
        if (coordinates.Length < count || colors.Length < count) throw new ArgumentException("Plane buffers are too small.");
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            coordinates[y * size + x] = new(x / (float)(size - 1), y / (float)(size - 1));
        ColorBatch.Transform<Vector2, LinearRgba, PlaneTransform>(coordinates[..count], colors[..count], new(Model, Coordinates));
    }
    internal void FillStrip(Span<float> fractions, Span<LinearRgba> colors) => FillChannel(StripChannel, fractions, colors);
    internal void FillChannel(int channel, Span<float> fractions, Span<LinearRgba> colors)
    {
        var range = GetChannel(channel);
        if (fractions.Length < 2) throw new ArgumentException("A strip needs at least two samples.", nameof(fractions));
        if (colors.Length < fractions.Length) throw new ArgumentException("Strip colors are too small.", nameof(colors));
        for (int i = 0; i < fractions.Length; i++) fractions[i] = i / (float)(fractions.Length - 1);
        ColorBatch.Transform<float, LinearRgba, ChannelTransform>(fractions, colors, new(Model, Coordinates, channel, range));
    }
    private readonly struct PlaneTransform(int model, Vector3 values) : IColorTransform<Vector2, LinearRgba>
    { public LinearRgba Transform(Vector2 source) => Convert(model, PlaneCoordinates(model, values, source.X, source.Y)); }
    private readonly struct ChannelTransform(int model, Vector3 values, int channel, Channel range) : IColorTransform<float, LinearRgba>
    { public LinearRgba Transform(float source) { Vector3 next = values; next[channel] = At(range, source); return Convert(model, next); } }
    private void Commit(Vector3 next, LinearRgba color, bool allChannels = false)
    {
        for (int track = 0; track < 3; track++)
        {
            bool changed = allChannels;
            for (int channel = 0; channel < 3; channel++) changed |= channel != track && values[channel] != next[channel];
            if (changed) channelRevisions[track]++;
        }
        values[0] = next.X; values[1] = next.Y; values[2] = next.Z; Color = color;
    }
    private static float Unit(float value)
    { if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); return Math.Clamp(value, 0, 1); }
    private static float At(Channel range, float fraction) => range.Minimum + (range.Maximum - range.Minimum) * fraction;
    private static float Fraction(float value, Channel range) => Math.Clamp((value - range.Minimum) / (range.Maximum - range.Minimum), 0, 1);
    private static Vector3 PlaneCoordinates(int model, Vector3 values, float u, float v)
    {
        if (model is 1 or 3)
        {
            float x = (u - 0.5f) * 2, y = (0.5f - v) * 2, radius = MathF.Sqrt(x * x + y * y);
            values.Y = At(Definition(model, 1), MathF.Min(radius, 1));
            if (radius > 0.000001f) values.Z = (MathF.Atan2(y, x) * (180 / MathF.PI) + 360) % 360;
        }
        else
        {
            float y = 1 - v, width = model == 6 ? 1 - MathF.Abs(2 * y - 1) : 1;
            values.Y = At(Definition(model, 1), width > 0.000001f ? Math.Clamp(u / width, 0, 1) : values.Y);
            values.Z = At(Definition(model, 2), y);
        }
        return values;
    }
    private static Channel Definition(int model, int channel)
    {
        if ((uint)channel > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        return model switch
        {
            0 => channel == 0 ? new("L*", 0, 150, 0.1f) : new(channel == 1 ? "a*" : "b*", -128, 128, 0.1f),
            1 => channel switch { 0 => new("L*", 0, 150, 0.1f), 1 => new("C*", 0, 200, 0.1f), _ => new("h°", 0, 360, 0.5f) },
            2 => channel == 0 ? new("L", 0, 1.4f, 0.001f) : new(channel == 1 ? "a" : "b", -0.4f, 0.4f, 0.001f),
            3 => channel switch { 0 => new("L", 0, 1.4f, 0.001f), 1 => new("C", 0, 0.5f, 0.001f), _ => new("h°", 0, 360, 0.5f) },
            4 => channel switch { 0 => new("Y′", 0, 1, 0.001f), 1 => new("U", -0.436f, 0.436f, 0.001f), _ => new("V", -0.615f, 0.615f, 0.001f) },
            5 => channel == 0 ? new("Y′", 0, 1, 0.001f) : new(channel == 1 ? "Cb" : "Cr", -0.5f, 0.5f, 0.001f),
            _ => channel == 0 ? new("H°", 0, 360, 0.5f) : new(channel == 1 ? "S" : model == 6 ? "L" : "V", 0, 1, 0.001f),
        };
    }
    private static Vector3 ToCoordinates(int model, LinearRgba color)
    {
        if (model == 0) { var v = PerceptualColorConverter.ToLab(color); return new(v.Lightness, v.A, v.B); }
        if (model == 1) { var v = PerceptualColorConverter.ToLch(PerceptualColorConverter.ToLab(color)); return new(v.Lightness, v.Chroma, v.HueDegrees); }
        if (model == 2) { var v = PerceptualColorConverter.ToOklab(color); return new(v.Lightness, v.A, v.B); }
        if (model == 3) { var v = PerceptualColorConverter.ToOklch(PerceptualColorConverter.ToOklab(color)); return new(v.Lightness, v.Chroma, v.HueDegrees); }
        var encoded = StandardRgbEncodingConverter.Encode(color, StandardRgbEncoding.Srgb);
        if (model is 4 or 5) { var v = LumaChromaConverter.FromRgb(encoded, model == 4 ? LumaChromaModel.YuvBt601 : LumaChromaModel.YCbCrBt709); return new(v.Luma, v.ChromaBlue, v.ChromaRed); }
        if (encoded.Red is < 0 or > 1 || encoded.Green is < 0 or > 1 || encoded.Blue is < 0 or > 1)
            throw new InvalidOperationException("当前颜色越出 SDR sRGB；HSL / HSV 不隐式裁切。请先重置测试色。");
        if (model == 6) { var v = CylindricalRgbConverter.ToHsl(encoded); return new(v.HueDegrees, v.Saturation, v.Lightness); }
        else { var v = CylindricalRgbConverter.ToHsv(encoded); return new(v.HueDegrees, v.Saturation, v.Value); }
    }
    private static LinearRgba Convert(int model, Vector3 v, float alpha = 1) => model switch
    {
        0 => PerceptualColorConverter.FromLab(new(v.X, v.Y, v.Z, alpha), StandardColorSpaces.LinearSrgb),
        1 => PerceptualColorConverter.FromLab(PerceptualColorConverter.FromLch(new(v.X, v.Y, v.Z, alpha)), StandardColorSpaces.LinearSrgb),
        2 => PerceptualColorConverter.FromOklab(new(v.X, v.Y, v.Z, alpha), StandardColorSpaces.LinearSrgb),
        3 => PerceptualColorConverter.FromOklab(PerceptualColorConverter.FromOklch(new(v.X, v.Y, v.Z, alpha)), StandardColorSpaces.LinearSrgb),
        4 or 5 => StandardRgbEncodingConverter.Decode(LumaChromaConverter.ToRgb(new(v.X, v.Y, v.Z, alpha,
            StandardRgbEncoding.Srgb, model == 4 ? LumaChromaModel.YuvBt601 : LumaChromaModel.YCbCrBt709))),
        6 => StandardRgbEncodingConverter.Decode(CylindricalRgbConverter.FromHsl(new(v.X, v.Y, v.Z, alpha, StandardRgbEncoding.Srgb))),
        _ => StandardRgbEncodingConverter.Decode(CylindricalRgbConverter.FromHsv(new(v.X, v.Y, v.Z, alpha, StandardRgbEncoding.Srgb))),
    };
    internal void Draw(GraphicsDevice device, GraphicsTexture target, ColorEncoding encoding = ColorEncoding.ExtendedSrgbLinear) =>
        renderer.Draw(this, device, target, encoding);
    public void Dispose() => renderer.Dispose();
}
