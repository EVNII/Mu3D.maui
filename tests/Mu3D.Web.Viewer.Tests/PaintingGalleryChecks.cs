using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.GalleryApp.Examples;
using Mu3D.Graphics;

internal static class PaintingGalleryChecks
{
    internal static void Validate(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        ValidateAtlas(Verify);
        ValidateCoordinates(Verify);
        ValidatePickPolicy(Verify);
        ValidateDisconnectedValidColors(Verify);
        ValidateOutputAndLifetime(Verify);
        Console.WriteLine($"Painting picker recording checks passed: {count} coordinate, atlas, cache, output and lifetime checks.");
    }

    private static void ValidateAtlas(Action<bool, string> check)
    {
        using PaintingColorSpacesExample example = new();
        using RecordingGraphicsDevice device = new();
        using GraphicsTexture target = Target(device, GraphicsTextureFormat.Rgba16Float);
        bool hasNegative = false, hasHdr = false;
        for (int model = 0; model < PaintingColorSpacesExample.Models.Length; model++)
        {
            example.Reset(); LinearRgba original = example.Color; example.SelectModel(model);
            check(example.Color == original, "Changing painting coordinate model preserves the tagged selected color.");
            int start = device.TextureWrites.Count;
            example.Draw(device, target);
            ReadOnlySpan<Vector4> uniforms = Uniforms(device);
            PaintingColorPickerLayout regions = new(64, 64);
            bool paletteFlags = true;
            for (int i = 0; i < 12; i++)
            {
                LinearRgba swatch = example.SampleHarmony(i, 12);
                paletteFlags &= uniforms[8 + i].W == (Allowed(swatch, model) ? 1 : 0);
            }
            check(device.Buffers.Single().LastWriteLength == 320 && uniforms[0] == new Vector4(0, 0, example.Shape, 12) &&
                uniforms[1] == Rgba(original) && uniforms[2] == regions.Plane && uniforms[3] == regions.Strip &&
                uniforms[4] == regions.Current && uniforms[5] == regions.Harmony &&
                uniforms[6] == new Vector4(example.PlanePosition, example.ChannelPosition(0), example.ChannelPosition(1)) &&
                uniforms[7] == new Vector4(64, 64, example.ChannelPosition(2), 0) && paletteFlags,
                "The actual picker draw uploads FP32 color, shared layout, three markers, extent and explicit palette pickability flags.");
            RecordedTextureWrite plane = device.TextureWrites[start];
            bool atlas = device.TextureWrites.Count == start + 4 && IsPlane(plane) &&
                plane.Destination.Descriptor.Size == new GraphicsExtent3D(64, 67) &&
                plane.Destination.Descriptor.Format == GraphicsTextureFormat.Rgba16Float;
            bool finite = FiniteOpaque(plane.Data, ref hasNegative, ref hasHdr);
            for (int channel = 0; channel < 3; channel++)
            {
                RecordedTextureWrite track = device.TextureWrites[start + 1 + channel];
                atlas &= IsTrack(track, channel) && ReferenceEquals(plane.Destination, track.Destination);
                finite &= FiniteOpaque(track.Data, ref hasNegative, ref hasHdr);
            }
            check(atlas, "Each model writes a 64×64 plane and three distinct 64×1 channel rows into one FP16 atlas.");
            check(finite && Vector4.Distance(Pixel(plane.Data, 32 * 64), Pixel(plane.Data, 32 * 64 + 63)) > .00001f &&
                Vector4.Distance(Pixel(plane.Data, 32), Pixel(plane.Data, 63 * 64 + 32)) > .00001f,
                "Every model uploads finite opaque FP16 colors that vary along both selector axes.");
            example.Draw(device, target);
            check(device.TextureWrites.Count == start + 4 && device.Buffers.Count == 1 && device.PipelineDescriptors.Count == 1,
                "An unchanged picker redraw reuses resources and uploads none of the cached atlas regions.");

            int planeRevision = example.PlaneRevision;
            Vector3 before = example.Coordinates;
            int[] revisions = [example.ChannelRevision(0), example.ChannelRevision(1), example.ChannelRevision(2)];
            var range = example.GetChannel(example.StripChannel);
            start = device.TextureWrites.Count;
            example.SetValue(example.StripChannel, (range.Minimum + range.Maximum) / 2);
            example.Draw(device, target);
            check(DependencyWrites(example, device, start, before, planeRevision, revisions) &&
                Uniforms(device)[1] == Rgba(example.Color),
                "Changing the third coordinate rebuilds the plane and the other two tracks while retaining its own track.");
            planeRevision = example.PlaneRevision; before = example.Coordinates;
            revisions = [example.ChannelRevision(0), example.ChannelRevision(1), example.ChannelRevision(2)];
            start = device.TextureWrites.Count;
            if (model == 0) example.SetValue(1, -5); // One fixed plane coordinate changes; its own track stays cached.
            else example.SetPlane(.2f, .7f);
            example.Draw(device, target);
            check(DependencyWrites(example, device, start, before, planeRevision, revisions) &&
                Uniforms(device)[1] == Rgba(example.Color),
                "Changing the plane updates exactly those tracks whose fixed coordinates changed while retaining the plane texture.");
        }
        check(hasNegative && hasHdr, "The FP16 atlas retains finite negative components and above-one HDR colors across the models.");
        check(device.SubmittedPipelineLabels.Count == 32, "Selections, unchanged redraws and dependent edits submit the actual picker pass.");
    }

    private static void ValidateCoordinates(Action<bool, string> check)
    {
        using PaintingColorSpacesExample example = new();
        example.SelectModel(3);
        bool cardinals = true;
        Vector2[] points = [new(1, .5f), new(.5f, 0), new(0, .5f), new(.5f, 1)];
        for (int i = 0; i < points.Length; i++)
        {
            example.SetPlane(points[i].X, points[i].Y);
            cardinals &= Near(example.GetValue(1), .5f) && AngleDistance(example.GetValue(2), i * 90) < .001f &&
                Vector2.Distance(example.PlanePosition, points[i]) < .00001f;
        }
        check(cardinals, "Circular selectors map right/up/left/down to 0/90/180/270 degrees at maximum chroma.");
        example.SetValue(2, 123); example.SetPlane(.5f, .5f);
        check(example.GetValue(1) == 0 && example.GetValue(2) == 123,
            "Picking the neutral circle center preserves the user's hue coordinate.");
        example.SelectModel(1); example.SetValue(1, 0); example.SetValue(2, 120);
        PaintingColorPickerLayout neutralLayout = new(1200, 500);
        Vector4 neutralPalette = neutralLayout.Harmony;
        AssertAtomic(example, () => neutralLayout.Apply(example, 3,
            neutralPalette.X + neutralPalette.Z / 24, neutralPalette.Y + neutralPalette.W / 2), null, check);

        example.Reset(); example.SelectModel(6); example.SetValue(1, .5f); example.SetValue(2, .75f);
        check(Vector2.Distance(example.PlanePosition, new(.25f, .25f)) < .00001f,
            "HSL saturation 0.5 and lightness 0.75 locate independently at triangle coordinates (0.25, 0.25).");
        example.SetValue(1, .8f); example.SetValue(2, .2f); example.SetPlane(.25f, .25f);
        check(Near(example.GetValue(1), .5f) && Near(example.GetValue(2), .75f),
            "Triangle hit coordinates recover saturation and lightness without treating it as a square.");
        example.Reset(); example.SelectModel(7); example.SetValue(0, 120); example.SetPlane(.25f, .2f);
        check(Near(example.GetValue(1), .25f) && Near(example.GetValue(2), .8f) &&
            Vector2.Distance(example.PlanePosition, new(.25f, .2f)) < .00001f,
            "HSV square coordinates use saturation horizontally and value increasing upwards.");

        example.SelectModel(2); example.SetValue(0, 1.6f);
        check(example.GetValue(0) == 1.6f && example.GetChannel(0).Maximum == 1.4f && example.IsOutsideSelector &&
            example.StripPosition == 1 && (example.Color.Red > 1 || example.Color.Green > 1 || example.Color.Blue > 1),
            "Numeric HDR coordinates remain outside the bounded selector while only the displayed marker is projected.");
        AssertAtomic(example, () => example.SetValue(0, float.NaN), typeof(ArgumentException), check);
        AssertAtomic(example, () => example.SetPlane(float.PositiveInfinity, .5f), typeof(ArgumentException), check);
        AssertAtomic(example, () => example.SelectModel(99), typeof(ArgumentException), check);
        AssertAtomic(example, () => example.SelectModel(6), typeof(InvalidOperationException), check);
        AssertAtomic(example, () => example.SelectModel(7), typeof(InvalidOperationException), check);

        example.SetColor(new(.7f, .2f, .1f, .37f, StandardColorSpaces.LinearSrgb));
        var basis = PerceptualColorConverter.ToOklch(PerceptualColorConverter.ToOklab(example.Color));
        bool harmony = true;
        for (int i = 0; i < 12; i++)
        {
            LinearRgba color = example.SampleHarmony(i, 12);
            var polar = PerceptualColorConverter.ToOklch(PerceptualColorConverter.ToOklab(color));
            harmony &= color.Alpha == .37f && Math.Abs(polar.Lightness - basis.Lightness) < .0001f &&
                Math.Abs(polar.Chroma - basis.Chroma) < .0001f && AngleDistance(polar.HueDegrees, basis.HueDegrees + i * 30) < .001f;
        }
        check(harmony, "Harmony swatches keep OKLCh lightness/chroma and alpha fixed while advancing hue by 30 degrees.");
        AssertAtomic(example, () => example.SetColor(example.SampleHarmony(0, 12)), null, check);

        bool square = true;
        foreach (Vector2 extent in new[] { new Vector2(1200, 500), new Vector2(240, 700) })
        {
            PaintingColorPickerLayout layout = new(extent.X, extent.Y);
            square &= Math.Abs(layout.Plane.Z * extent.X - layout.Plane.W * extent.Y) < .001f;
        }
        check(square, "The shared normalized layout keeps circular selectors square in physical pixels for wide and tall viewports.");
        PaintingColorPickerLayout regions = new(1200, 500);
        Vector2 plane = Center(regions.Plane), strip = Center(regions.Strip), palette = Center(regions.Harmony);
        Vector2 secondTrack = Center(PaintingColorPickerLayout.Track(1)), thirdTrack = Center(PaintingColorPickerLayout.Track(2));
        check(regions.RegionAt(plane.X, plane.Y) == 1 && regions.RegionAt(strip.X, strip.Y) == 4 &&
            regions.RegionAt(secondTrack.X, secondTrack.Y) == 5 && regions.RegionAt(thirdTrack.X, thirdTrack.Y) == 6 &&
            regions.RegionAt(palette.X, palette.Y) == 3 && regions.RegionAt(Center(regions.Current).X, Center(regions.Current).Y) == 0,
            "Drawing and input share the plane, three tracks and palette regions while the current swatch stays read-only.");
        example.Reset(); example.SelectModel(7); example.SetValue(0, 120);
        regions.Apply(example, 1, strip.X, strip.Y);
        check(example.GetValue(0) == 120 && example.GetValue(1) == 1,
            "Captured plane drags project outside coordinates without jumping into the strip under the pointer.");
        Vector2 planeValues = new(example.GetValue(1), example.GetValue(2));
        regions.Apply(example, 4, regions.Strip.X + regions.Strip.Z + .1f, plane.Y);
        check(example.GetValue(0) == 360 && planeValues == new Vector2(example.GetValue(1), example.GetValue(2)),
            "Captured channel-track drags reach the right positive endpoint without editing plane coordinates or switching regions.");
        AssertAtomic(example, () => regions.Apply(example, 1, float.NaN, .5f), null, check);
        AssertAtomic(example, () => example.SelectModel(example.Model), null, check);
        AssertAtomic(example, () => example.SetValue(0, example.GetValue(0)), null, check);
        AssertAtomic(example, () => example.SetPlane(example.PlanePosition.X, example.PlanePosition.Y), null, check);
    }

    private static void ValidatePickPolicy(Action<bool, string> check)
    {
        Random random = new(3128);
        PaintingColorPickerLayout layout = new(1200, 500);
        Vector2[] anchors = [new(0, 0), new(1, 0), new(0, 1), new(1, 1), new(.5f, .5f), new(.25f, .75f)];
        for (int model = 0; model < PaintingColorSpacesExample.Models.Length; model++)
        {
            using PaintingColorSpacesExample example = new(); example.SelectModel(model);
            bool valid = true;
            for (int sample = 0; sample < 12; sample++)
            {
                Vector2 point = sample < anchors.Length ? anchors[sample] : new(random.NextSingle(), random.NextSingle());
                example.PickPlane(point.X, point.Y); valid &= Allowed(example.Color, model);
                for (int channel = 0; channel < 3; channel++)
                {
                    float fraction = sample < 3 ? sample * .5f : random.NextSingle();
                    Vector4 track = PaintingColorPickerLayout.Track(channel);
                    layout.Apply(example, 4 + channel, track.X + track.Z * fraction, track.Y + track.W / 2);
                    valid &= Allowed(example.Color, model);
                }
            }
            check(valid, "Fixed endpoints and seeded plane/three-track drags preserve nonnegative RGB in every model, including bounded HSL/HSV.");

            bool independent = true;
            for (int channel = 0; channel < 3; channel++)
            {
                example.Reset(); var before = Snapshot(example);
                LinearRgba sampled = example.SampleChannel(channel, .25f);
                independent &= Snapshot(example) == before;
                var range = example.GetChannel(channel);
                example.SetValue(channel, range.Minimum + (range.Maximum - range.Minimum) * .25f);
                independent &= Vector4.Distance(Rgba(sampled), Rgba(example.Color)) < .00001f;
                for (int other = 0; other < 3; other++)
                    if (other != channel) independent &= example.GetValue(other) == before.Item3[other];
            }
            check(independent, "Each channel preview scans that coordinate alone, matches its raw numeric endpoint and leaves sampling state untouched.");
        }

        using PaintingColorSpacesExample boundary = new();
        boundary.SetColor(new(.25f, .25f, .25f, .37f, StandardColorSpaces.LinearSrgb));
        check(boundary.PickValue(1, -5) && boundary.PickValue(2, -5) &&
            boundary.GetValue(1) == -5 && boundary.GetValue(2) == -5 && Nonnegative(boundary.Color),
            "Negative Lab a/b coordinates remain selectable when their RGB color is nonnegative.");
        boundary.SelectModel(2); boundary.SetColor(new(.25f, .25f, .25f, .37f, StandardColorSpaces.LinearSrgb));
        Vector3 requested = boundary.Coordinates; requested.Y = -.4f;
        LinearRgba raw = boundary.ColorAt(requested);
        bool complete = boundary.PickValue(1, -.4f);
        LinearRgba represented = PerceptualColorConverter.FromOklab(new(boundary.GetValue(0), boundary.GetValue(1), boundary.GetValue(2), .37f), StandardColorSpaces.LinearSrgb);
        Vector4 clipped = new(Math.Max(raw.Red, 0), Math.Max(raw.Green, 0), Math.Max(raw.Blue, 0), raw.Alpha);
        check(!Nonnegative(raw) && !complete && Nonnegative(boundary.Color) && boundary.GetValue(1) > -.4f &&
            Vector4.Distance(Rgba(boundary.Color), Rgba(represented)) < .00001f && Vector4.Distance(Rgba(boundary.Color), clipped) > .01f,
            "Invalid coordinates stop at a valid coordinate boundary while RGB remains the actual conversion, rather than component clipping.");
        check(boundary.PickValue(0, 1.6f) && boundary.GetValue(0) == 1.6f && boundary.IsOutsideSelector &&
            Nonnegative(boundary.Color) && (boundary.Color.Red > 1 || boundary.Color.Green > 1 || boundary.Color.Blue > 1),
            "The shared numeric/slider policy permits above-one HDR and out-of-selector coordinates without clipping highlights.");
        AssertAtomic(boundary, () => boundary.PickValue(0, boundary.GetValue(0)), null, check);
        AssertAtomic(boundary, () => boundary.PickValue(0, float.NaN), typeof(ArgumentException), check);
        boundary.SetColor(new(.25f, .25f, .25f, .37f, StandardColorSpaces.LinearSrgb));
        check(!boundary.PickColor(new(-.1f, .4f, .2f, .37f, StandardColorSpaces.LinearSrgb)) && Nonnegative(boundary.Color),
            "Application color picks also stop at the valid boundary instead of installing a negative RGB target.");

        foreach (int model in new[] { 2, 6, 7 })
        {
            using PaintingColorSpacesExample palette = new(); palette.SelectModel(model);
            palette.SetColor(new(1, 0, 0, .37f, StandardColorSpaces.LinearSrgb));
            bool found = false, ignored = false;
            for (int index = 1; index < 12; index++)
            {
                if (Allowed(palette.SampleHarmony(index, 12), model)) continue;
                found = true; var before = Snapshot(palette);
                ignored = !layout.Apply(palette, 3, layout.Harmony.X + layout.Harmony.Z * ((index + .5f) / 12),
                    layout.Harmony.Y + layout.Harmony.W / 2) && Snapshot(palette) == before;
                break;
            }
            check(found && ignored, "A disabled harmony swatch cannot change color, coordinates or caches, including HSL/HSV range limits.");
        }
        boundary.SetColor(new(-.25f, .5f, 2.5f, .37f, StandardColorSpaces.LinearSrgb));
        var diagnostic = Snapshot(boundary);
        check(!boundary.PickValue(0, 1) && !boundary.PickColor(new(0, 1, 0, .9f, StandardColorSpaces.LinearSrgb)) &&
            Snapshot(boundary) == diagnostic && boundary.Color.Red < 0 && boundary.Color.Blue > 1,
            "Raw FP32 negative/HDR diagnostics remain intact and cannot become a valid UI path origin implicitly.");
    }

    private static void ValidateDisconnectedValidColors(Action<bool, string> check)
    {
        using PaintingColorSpacesExample example = new(); example.SelectModel(3);
        void RestoreOrigin() { example.SetValue(0, .446f); example.SetValue(1, .1f); example.SetValue(2, 42); }
        RestoreOrigin();
        static LinearRgba Polar(float hue) => PerceptualColorConverter.FromOklab(
            PerceptualColorConverter.FromOklch(new(.446f, .1f, hue, 1)), StandardColorSpaces.LinearSrgb);
        LinearRgba target = Polar(120);
        check(Nonnegative(Polar(42)) && !Nonnegative(Polar(80)) && Nonnegative(target),
            "Independent OKLCh conversion identifies valid origin/green endpoints separated by a negative-blue middle hue.");
        PaintingColorPickerLayout layout = new(1200, 500);
        Vector4 hueTrack = PaintingColorPickerLayout.Track(2);
        bool reached = layout.Apply(example, 6, hueTrack.X + hueTrack.Z / 3, hueTrack.Y + hueTrack.W / 2);
        check(reached && Near(example.GetValue(0), .446f) && Near(example.GetValue(1), .1f) &&
            AngleDistance(example.GetValue(2), 120) < .001f && Nonnegative(example.Color) &&
            Vector4.Distance(Rgba(example.Color), Rgba(target)) < .00001f,
            "The hue track reaches valid green across an invalid intermediate hue instead of stopping at the earlier boundary.");
        RestoreOrigin();
        float angle = 120 * MathF.PI / 180;
        Vector2 green = new(.5f + .1f * MathF.Cos(angle), .5f - .1f * MathF.Sin(angle));
        reached = layout.Apply(example, 1, layout.Plane.X + layout.Plane.Z * green.X, layout.Plane.Y + layout.Plane.W * green.Y);
        check(reached && Near(example.GetValue(0), .446f) && Near(example.GetValue(1), .1f) &&
            AngleDistance(example.GetValue(2), 120) < .001f && Nonnegative(example.Color) &&
            Vector4.Distance(Rgba(example.Color), Rgba(target)) < .00001f,
            "The circular plane also reaches a valid disconnected green point directly.");
        RestoreOrigin();
        LinearRgba palette = example.SampleHarmony(3, 12);
        reached = layout.Apply(example, 3, layout.Harmony.X + layout.Harmony.Z * (3.5f / 12), layout.Harmony.Y + layout.Harmony.W / 2);
        check(Nonnegative(palette) && reached && Nonnegative(example.Color) &&
            Vector4.Distance(Rgba(example.Color), Rgba(palette)) < .00001f,
            "A valid harmony swatch remains selectable even when intermediate coordinate colors lie in the disabled region.");
        example.SetColor(new(.2f, .3f, .4f, .37f, StandardColorSpaces.LinearSrgb));
        bool primaries = true;
        foreach (Vector3 primary in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            Vector3 before = example.Coordinates;
            reached = example.PickColor(new(primary.X, primary.Y, primary.Z, .9f, StandardColorSpaces.LinearSrgb));
            LinearRgba exact = new(primary.X, primary.Y, primary.Z, .37f, StandardColorSpaces.LinearSrgb);
            var expected = PerceptualColorConverter.ToOklch(PerceptualColorConverter.ToOklab(exact));
            primaries &= reached && example.Color == exact && example.Coordinates != before &&
                Near(example.GetValue(0), expected.Lightness) && Near(example.GetValue(1), expected.Chroma) &&
                AngleDistance(example.GetValue(2), expected.HueDegrees) < .001f;
        }
        check(primaries, "Pure RGB picks preserve exact RGB and the current alpha while updating the displayed perceptual coordinates.");
    }

    private static void ValidateOutputAndLifetime(Action<bool, string> check)
    {
        using PaintingColorSpacesExample example = new(); example.SelectModel(2);
        example.SetColor(new(-.25f, .5f, 2.5f, .37f, StandardColorSpaces.LinearSrgb));
        using RecordingGraphicsDevice device = new();
        using GraphicsTexture hdr = Target(device, GraphicsTextureFormat.Rgba16Float);
        using GraphicsTexture encoded = Target(device, GraphicsTextureFormat.Bgra8Unorm);
        using GraphicsTexture hardware = Target(device, GraphicsTextureFormat.Bgra8UnormSrgb);
        GraphicsTexture[] targets = [hdr, encoded, hardware];
        for (int i = 0; i < targets.Length; i++)
        {
            GraphicsTexture? previousAtlas = device.TextureWrites.LastOrDefault().Destination;
            RecordingGraphicsBuffer? previousBuffer = device.Buffers.LastOrDefault();
            GraphicsRenderPipelineDescriptor? previous = device.PipelineDescriptors.LastOrDefault();
            example.Draw(device, targets[i], i == 0 ? ColorEncoding.ExtendedSrgbLinear : ColorEncoding.Srgb);
            Vector4 expected = new(i == 0 ? 0 : 1, i == 1 ? 1 : 0, example.Shape, 12);
            ReadOnlySpan<Vector4> uniforms = Uniforms(device);
            GraphicsRenderPipelineDescriptor descriptor = device.PipelineDescriptors.Last();
            check(device.Buffers.Last().LastWriteLength == 320 && uniforms[0] == expected && uniforms[1] == Rgba(example.Color) &&
                uniforms[1].X < 0 && uniforms[1].Z > 1 && uniforms[1].W == .37f && descriptor.ColorFormat == targets[i].Descriptor.Format,
                "Explicit HDR/SDR and shader/hardware sRGB output policies preserve the raw selected color and alpha in uploads.");
            if (i > 0)
                check(previousAtlas!.IsDisposed && previousBuffer!.IsDisposed && previous!.Value.VertexShader.IsDisposed &&
                    previous.Value.Layout!.IsDisposed && previous.Value.Layout.Descriptor.BindGroupLayouts.All(layout => layout.IsDisposed),
                    "Changing output format retires the recorded atlas, buffer, shader and pipeline-layout resources.");
        }
        // Recording validates the host's shader descriptor and uniforms, without executing WGSL.
        string source = device.PipelineDescriptors.Last().FragmentShader!.Descriptor.Code;
        check(source.Contains("if (p.settings.x > 0.5)", StringComparison.Ordinal) &&
            source.Contains("if (p.settings.y > 0.5)", StringComparison.Ordinal),
            "The recorded shader gates SDR clipping and transfer encoding separately.");
        int writes = device.TextureWrites.Count; RecordingGraphicsBuffer active = device.Buffers.Last();
        bool rejected = false;
        try { example.Draw(device, hdr, ColorEncoding.Srgb); } catch (NotSupportedException) { rejected = true; }
        check(rejected && writes == device.TextureWrites.Count && !active.IsDisposed,
            "Unsupported output encoding rejects before replacing the current picker resources.");
        GraphicsTexture retiredAtlas = device.TextureWrites.Last().Destination;
        example.Dispose(); example.Dispose();
        check(active.IsDisposed && retiredAtlas.IsDisposed && !hardware.IsDisposed,
            "Repeated picker disposal retires owned resources while preserving caller-owned targets.");
        using RecordingGraphicsDevice reconnect = new();
        using GraphicsTexture next = Target(reconnect, GraphicsTextureFormat.Rgba16Float);
        example.Draw(reconnect, next);
        check(reconnect.TextureWrites.Count == 4 && Uniforms(reconnect)[1] == Rgba(example.Color) &&
            reconnect.SubmittedPipelineLabels.Count == 1,
            "Device reconnection rebuilds all four atlas regions and uploads the retained negative/HDR color.");
    }

    private static void AssertAtomic(PaintingColorSpacesExample example, Action edit, Type? exception, Action<bool, string> check)
    {
        var before = Snapshot(example);
        bool expected = exception is null;
        try { edit(); }
        catch (Exception error)
        {
            if ((exception == typeof(ArgumentException) && error is ArgumentException) ||
                (exception == typeof(InvalidOperationException) && error is InvalidOperationException)) expected = true;
            else throw;
        }
        var after = Snapshot(example);
        bool equal = after == before;
        check(expected && equal,
            "Invalid or ignored input leaves the model, color, numeric coordinates and every cache revision unchanged.");
    }

    private static (int, LinearRgba, Vector3, int, int, int, int) Snapshot(PaintingColorSpacesExample example) =>
        (example.Model, example.Color, example.Coordinates, example.PlaneRevision,
            example.ChannelRevision(0), example.ChannelRevision(1), example.ChannelRevision(2));
    private static bool DependencyWrites(PaintingColorSpacesExample example, RecordingGraphicsDevice device,
        int start, Vector3 before, int planeRevision, int[] channelRevisions)
    {
        Vector3 after = example.Coordinates;
        bool changedPlane = after.X != before.X;
        bool correct = example.PlaneRevision == planeRevision + (changedPlane ? 1 : 0);
        int write = start;
        if (changedPlane)
        {
            correct &= write < device.TextureWrites.Count && IsPlane(device.TextureWrites[write]);
            write++;
        }
        for (int track = 0; track < 3; track++)
        {
            bool changed = false;
            for (int fixedCoordinate = 0; fixedCoordinate < 3; fixedCoordinate++)
                if (fixedCoordinate != track) changed |= before[fixedCoordinate] != after[fixedCoordinate];
            correct &= example.ChannelRevision(track) == channelRevisions[track] + (changed ? 1 : 0);
            if (!changed) continue;
            correct &= write < device.TextureWrites.Count && IsTrack(device.TextureWrites[write], track);
            write++;
        }
        return correct && write == device.TextureWrites.Count;
    }
    private static bool Nonnegative(LinearRgba color) => color.Red >= 0 && color.Green >= 0 && color.Blue >= 0;
    private static bool Allowed(LinearRgba color, int model) => Nonnegative(color) &&
        (model < 6 || color.Red <= 1 && color.Green <= 1 && color.Blue <= 1);
    private static bool IsPlane(RecordedTextureWrite write) => write.Origin == default && write.Size == new GraphicsExtent3D(64, 64) &&
        write.BytesPerRow == 512 && write.RowsPerImage == 64 && write.Data.Length == 32768;
    private static bool IsTrack(RecordedTextureWrite write, int channel) => write.Origin == new GraphicsOrigin3D(0, (uint)(64 + channel), 0) && write.Size == new GraphicsExtent3D(64, 1) &&
        write.BytesPerRow == 512 && write.RowsPerImage == 1 && write.Data.Length == 512;
    private static bool FiniteOpaque(byte[] bytes, ref bool negative, ref bool hdr)
    {
        ReadOnlySpan<Half> values = MemoryMarshal.Cast<byte, Half>(bytes);
        for (int i = 0; i < values.Length; i += 4)
        {
            if (!Half.IsFinite(values[i]) || !Half.IsFinite(values[i + 1]) || !Half.IsFinite(values[i + 2]) || values[i + 3] != (Half)1) return false;
            negative |= values[i] < (Half)0 || values[i + 1] < (Half)0 || values[i + 2] < (Half)0;
            hdr |= values[i] > (Half)1 || values[i + 1] > (Half)1 || values[i + 2] > (Half)1;
        }
        return true;
    }
    private static Vector4 Pixel(byte[] bytes, int index)
    { ReadOnlySpan<Half> values = MemoryMarshal.Cast<byte, Half>(bytes); int start = index * 4; return new((float)values[start], (float)values[start + 1], (float)values[start + 2], (float)values[start + 3]); }
    private static ReadOnlySpan<Vector4> Uniforms(RecordingGraphicsDevice device) => MemoryMarshal.Cast<byte, Vector4>(device.Buffers.Last().Data);
    private static GraphicsTexture Target(RecordingGraphicsDevice device, GraphicsTextureFormat format) =>
        device.CreateTexture(new(new GraphicsExtent3D(64, 64), format, GraphicsTextureUsage.RenderAttachment));
    private static Vector4 Rgba(LinearRgba color) => new(color.Red, color.Green, color.Blue, color.Alpha);
    private static Vector2 Center(Vector4 rect) => new(rect.X + rect.Z / 2, rect.Y + rect.W / 2);
    private static bool Near(float a, float b) => Math.Abs(a - b) < .00001f;
    private static float AngleDistance(float a, float b) => Math.Abs((a - b + 540) % 360 - 180);
}
