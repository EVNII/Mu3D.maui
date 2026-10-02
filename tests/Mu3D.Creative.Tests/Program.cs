using System.Numerics;
using Mu3D.Color;
using Mu3D.Creative;

int assertions = 0;
List<string> failures = [];
StandardRgbColorSpaceReference srgb = StandardColorSpaces.LinearSrgb;
HdrCanvasOptions full = new() { Precision = PixelPrecision.Float32, TileSize = 2 };
LinearRgba Color(float r, float g = 0f, float b = 0f, float a = 1f) => new(r, g, b, a, srgb);

HdrCanvas empty = new(1048576, 1048576, srgb);
Expect(empty.TileCount == 0 && empty.AllocatedStorageBytes == 0 && empty.Revision == 0,
    "large canvas starts sparse without allocating tiles");
Pixel(empty.GetPixel(1048575, 1048575), new Vector4(0), "unallocated edge is tagged transparent black");
Expect(empty.GetPixel(0, 0).ColorSpace == srgb && empty.GetDirtyRegions().Count == 0, "empty metadata");
Throws<InvalidOperationException>(() => empty.Snapshot(), "oversized full snapshot bounded before allocation");
Expect(empty.Snapshot(new CanvasRegion(1000000, 1000000, 2, 3)).Pixels.Count == 6, "sparse canvas crop");

HdrCanvas sourceOver = new(2, 2, srgb, full);
sourceOver.SetPixel(0, 0, Color(0, 0, 2, 0.5f));
sourceOver.Composite(new CanvasRegion(0, 0, 1, 1), Color(4, 0, 0, 0.5f));
Pixel(sourceOver.GetPixel(0, 0), new Vector4(8f / 3f, 0, 2f / 3f, 0.75f),
    "premultiplied source-over has correct straight-alpha boundary");
Expect(sourceOver.Revision == 2, "one revision per changed mutation");
ulong noOpRevision = sourceOver.Revision;
sourceOver.Composite(new CanvasRegion(0, 0, 2, 2), Color(500, 2, 1, 0));
sourceOver.Composite(new CanvasRegion(0, 0, 2, 2), Color(500, 2, 1), opacity: 0f);
Expect(sourceOver.Revision == noOpRevision, "zero alpha and zero opacity leave revision unchanged");

foreach ((CanvasBlendMode mode, float expected) in new[]
{
    (CanvasBlendMode.Normal, 0.75f), (CanvasBlendMode.Multiply, 0.1875f),
    (CanvasBlendMode.Screen, 0.8125f), (CanvasBlendMode.Add, 1f),
})
{
    HdrCanvas blend = new(1, 1, srgb, full);
    blend.SetPixel(0, 0, Color(0.25f));
    blend.Composite(new CanvasRegion(0, 0, 1, 1), Color(0.75f), mode);
    Near(blend.GetPixel(0, 0).Red, expected, $"opaque {mode} formula");

    blend.SetPixel(0, 0, Color(0.25f, a: 0.4f));
    blend.Composite(new CanvasRegion(0, 0, 1, 1), Color(0.75f, a: 0.5f), mode);
    float alpha = 0.5f + 0.4f * 0.5f;
    float red = (0.75f * 0.5f * 0.6f + 0.25f * 0.4f * 0.5f + expected * 0.5f * 0.4f) / alpha;
    Pixel(blend.GetPixel(0, 0), new Vector4(red, 0, 0, alpha), $"partial-alpha {mode} overlap");
}

HdrCanvas hdr = new(3, 3, srgb, full);
hdr.SetPixel(2, 2, Color(-2, 4, 70000, 0.5f));
Pixel(hdr.GetPixel(2, 2), new Vector4(-2, 4, 70000, 0.5f), "Float32 negative and high HDR retained");
Expect(hdr.TileCount == 1 && hdr.AllocatedStorageBytes == 16, "edge tile only allocates one actual pixel");
LinearRgbaImage snapshot = hdr.Snapshot(name: "HDR crop");
hdr.SetPixel(2, 2, Color(0));
Expect(snapshot.Name == "HDR crop" && snapshot.Width == 3 && snapshot.Height == 3 && snapshot.ColorSpace == srgb,
    "snapshot retains dimensions, name and color identity");
Expect(snapshot.Pixels[8] == new Vector4(-2, 4, 70000, 0.5f), "snapshot detached from later mutations and row-major");
HdrCanvas half = new(3, 3, srgb, new HdrCanvasOptions { TileSize = 2 });
half.SetPixel(2, 2, Color(-0.33333f, 2.2222f, 4, 1f));
Expect(half.Precision == PixelPrecision.Float16 && half.AllocatedStorageBytes == 8, "default is actual Half storage");
Near(half.GetPixel(2, 2).Red, (float)(Half)(-0.33333f), "Float16 rounding is observable", 0f);
Near(half.GetPixel(2, 2).Green, (float)(Half)2.2222f, "HDR half value remains above one", 0f);
half.SetPixel(0, 0, Color(1, 2, 3, float.Epsilon));
Pixel(half.GetPixel(0, 0), Vector4.Zero, "half alpha underflow canonicalizes transparent RGB");
Expect(half.TileCount == 1, "transparent underflow does not allocate a tile");

HdrCanvas spaces = new(1, 1, StandardColorSpaces.AcesCg,
    full with { CompositingSpace = StandardColorSpaces.LinearRec2020 });
LinearRgba p3Color = new(-0.2f, 2, 0.5f, 0.3f, StandardColorSpaces.LinearDisplayP3);
spaces.SetPixel(0, 0, p3Color);
LinearRgba expectedColor = StandardLinearRgbConverter.Convert(p3Color, StandardColorSpaces.AcesCg);
Pixel(spaces.GetPixel(0, 0), new Vector4(expectedColor.Red, expectedColor.Green, expectedColor.Blue, 0.3f),
    "working and compositing spaces remain distinct through convert-premultiply-read", 2e-5f);
Expect(spaces.WorkingSpace == StandardColorSpaces.AcesCg &&
    spaces.CompositingSpace == StandardColorSpaces.LinearRec2020 &&
    spaces.Snapshot().ColorSpace == StandardColorSpaces.AcesCg, "snapshot is explicitly working-space tagged");

HdrCanvas dirty = new(5, 3, srgb, full);
dirty.SetPixel(1, 1, Color(1));
dirty.SetPixel(0, 0, Color(1));
dirty.SetPixel(4, 2, Color(1));
Expect(dirty.GetDirtyRegions().SequenceEqual([new CanvasRegion(0, 0, 2, 2), new CanvasRegion(4, 2, 1, 1)]),
    "dirty changes merge per tile and sort in row-major order");
IReadOnlyList<CanvasRegion> oldDirty = dirty.GetDirtyRegions();
dirty.ClearDirtyRegions();
Expect(dirty.GetDirtyRegions().Count == 0 && oldDirty.Count == 2 && dirty.Revision == 3,
    "dirty acknowledgment is detached and does not edit document");
dirty.SetPixel(4, 2, Color(1));
Expect(dirty.GetDirtyRegions().Count == 0 && dirty.Revision == 3, "same stored value is not dirty");
dirty.SetPixel(4, 2, Color(99, 99, 99, 0));
Expect(dirty.TileCount == 1 && dirty.GetDirtyRegions().SequenceEqual([new CanvasRegion(4, 2, 1, 1)]),
    "erased edge tile releases storage but remains dirty");
dirty.Clear();
Expect(dirty.TileCount == 0 && dirty.AllocatedStorageBytes == 0 && dirty.Revision == 5 && dirty.GetDirtyRegions().Count == 2,
    "clear releases all storage and retains dirty tile bounds");
dirty.Clear();
Expect(dirty.Revision == 5, "clearing an empty canvas is a no-op");

HdrCanvas dabCanvas = new(5, 5, srgb, full);
dabCanvas.ApplyDab(new BrushDab(2.5f, 2.5f, 2f, Color(4, -1, 0, 0.5f), opacity: 0.5f, hardness: 0f));
Pixel(dabCanvas.GetPixel(2, 2), new Vector4(4, -1, 0, 0.25f), "dab center multiplies color alpha and opacity");
Pixel(dabCanvas.GetPixel(3, 2), new Vector4(4, -1, 0, 0.125f), "soft dab annulus coverage");
Pixel(dabCanvas.GetPixel(4, 2), Vector4.Zero, "dab outer radius has zero coverage");
ulong dabRevision = dabCanvas.Revision;
dabCanvas.ApplyDab(new BrushDab(-100, -100, 1, Color(1)));
Expect(dabCanvas.Revision == dabRevision, "off-canvas dab is a no-op");
dabCanvas.ApplyDab(new BrushDab(0.5f, 0.5f, 1, Color(2), hardness: 1));
Pixel(dabCanvas.GetPixel(0, 0), new Vector4(2, 0, 0, 1), "hard dab clipped at canvas boundary");
Expect(dabCanvas.GetDirtyRegions().All(r => r.X >= 0 && r.Y >= 0 && r.Right <= 5 && r.Bottom <= 5),
    "dab dirty bounds stay inside canvas");

LinearRgbaImage imported = new(2, 1, [new Vector4(3, -1, 1, 0.5f), new Vector4(2, 4, 1, 1)], srgb);
HdrCanvas images = new(3, 2, srgb, full);
images.CompositeImage(imported, 1, 1, opacity: 0.5f);
Pixel(images.GetPixel(1, 1), new Vector4(3, -1, 1, 0.25f), "image composition straight-alpha and placement");
Pixel(images.GetPixel(2, 1), new Vector4(2, 4, 1, 0.5f), "image row indexing");
Throws<ArgumentOutOfRangeException>(() => images.CompositeImage(imported, 2), "image must fit completely");

HdrCanvas atomic = new(4, 1, srgb, new HdrCanvasOptions { TileSize = 1 });
atomic.SetPixel(0, 0, Color(100));
atomic.SetPixel(3, 0, Color(60000));
atomic.ClearDirtyRegions();
LinearRgbaImage before = atomic.Snapshot();
ulong beforeRevision = atomic.Revision;
long beforeStorage = atomic.AllocatedStorageBytes;
Throws<ArgumentOutOfRangeException>(() => atomic.Composite(new CanvasRegion(0, 0, 4, 1), Color(10000), CanvasBlendMode.Add),
    "late half blend overflow rejected");
Unchanged(atomic, before, beforeRevision, beforeStorage, "late numeric overflow is transactional across tiles");
LinearRgbaImage badImage = new(4, 1, [new Vector4(1, 0, 0, 1), new Vector4(1), new Vector4(1), new Vector4(70000, 0, 0, 1)], srgb);
Throws<ArgumentOutOfRangeException>(() => atomic.CompositeImage(badImage), "late image storage overflow rejected");
Unchanged(atomic, before, beforeRevision, beforeStorage, "late image validation is transactional");

HdrCanvas floatOverflow = new(2, 1, srgb, full with { TileSize = 1 });
floatOverflow.SetPixel(0, 0, Color(1));
floatOverflow.SetPixel(1, 0, Color(float.MaxValue));
floatOverflow.ClearDirtyRegions();
LinearRgbaImage floatBefore = floatOverflow.Snapshot();
Throws<ArgumentOutOfRangeException>(() => floatOverflow.Composite(new CanvasRegion(0, 0, 2, 1), Color(2), CanvasBlendMode.Multiply),
    "Float32 arithmetic overflow fails explicitly");
Unchanged(floatOverflow, floatBefore, 2, 32, "late Float32 overflow is transactional");

Random random = new(2146);
foreach (CanvasBlendMode mode in Enum.GetValues<CanvasBlendMode>())
{
    HdrCanvas numerical = new(1, 1, srgb, full);
    for (int sample = 0; sample < 24; sample++)
    {
        // Independent straight-alpha, double-precision reference, including negative/HDR colors.
        float srcR = (float)(random.NextDouble() * 8 - 2), dstR = (float)(random.NextDouble() * 8 - 2);
        float srcA = (float)random.NextDouble(), dstA = (float)random.NextDouble();
        numerical.SetPixel(0, 0, Color(dstR, a: dstA));
        numerical.Composite(new CanvasRegion(0, 0, 1, 1), Color(srcR, a: srcA), mode);
        double overlap = mode switch
        {
            CanvasBlendMode.Normal => srcR,
            CanvasBlendMode.Multiply => (double)srcR * dstR,
            CanvasBlendMode.Screen => (double)srcR + dstR - (double)srcR * dstR,
            CanvasBlendMode.Add => (double)srcR + dstR,
            _ => throw new InvalidOperationException(),
        };
        double outputA = srcA + dstA * (1d - srcA);
        double outputR = ((1d - srcA) * dstR * dstA + (1d - dstA) * srcR * srcA + srcA * dstA * overlap) / outputA;
        Near(numerical.GetPixel(0, 0).Red, (float)outputR, $"{mode} HDR numerical reference {sample}", 1e-5f);
    }
}

HdrCanvas capped = new(4, 1, srgb, new HdrCanvasOptions
{
    TileSize = 1, MaxStorageBytes = 16, MaxOperationBytes = 32, MaxSnapshotBytes = 128,
});
capped.SetPixel(0, 0, Color(1));
capped.ClearDirtyRegions();
LinearRgbaImage cappedBefore = capped.Snapshot();
Throws<InvalidOperationException>(() => capped.Fill(new CanvasRegion(0, 0, 4, 1), Color(2)), "retained storage cap enforced");
Unchanged(capped, cappedBefore, 1, 8, "storage-cap failure is transactional");
HdrCanvas operationCapped = new(3, 3, srgb, new HdrCanvasOptions { TileSize = 2, MaxOperationBytes = 8 });
Throws<InvalidOperationException>(() => operationCapped.SetPixel(0, 0, Color(1)), "staging budget includes complete interior tile");
operationCapped.SetPixel(2, 2, Color(1));
Expect(operationCapped.AllocatedStorageBytes == 8, "staging cap accounts for smaller edge tile");
HdrCanvas snapshotCapped = new(2, 2, srgb, full with { MaxSnapshotBytes = 127 });
Throws<InvalidOperationException>(() => snapshotCapped.Snapshot(), "snapshot accounts for both FP32 arrays");

HdrCanvas tileCapped = new(4, 1, srgb, full with { TileSize = 1, MaxTileCount = 1 });
tileCapped.SetPixel(0, 0, Color(1));
tileCapped.SetPixel(0, 0, Color(0, a: 0));
Throws<InvalidOperationException>(() => tileCapped.SetPixel(1, 0, Color(1)), "dirty tile metadata count is bounded after clear");
Expect(tileCapped.TileCount == 0 && tileCapped.Revision == 2 && tileCapped.GetDirtyRegions().Count == 1,
    "dirty-budget rejection leaves all state unchanged");
tileCapped.ClearDirtyRegions();
tileCapped.SetPixel(1, 0, Color(1));
Throws<InvalidOperationException>(() => tileCapped.SetPixel(2, 0, Color(1)), "retained tile count bounded");
Throws<InvalidOperationException>(() => tileCapped.Fill(new CanvasRegion(0, 0, 2, 1), Color(1)), "intersected tile count bounded");

Throws<ArgumentOutOfRangeException>(() => new HdrCanvas(0, 1, srgb), "zero canvas width rejected");
Throws<ArgumentOutOfRangeException>(() => new HdrCanvas(1048577, 1, srgb), "unbounded dimension rejected");
Throws<ArgumentOutOfRangeException>(() => new HdrCanvas(1, 1, srgb, full with { Precision = (PixelPrecision)42 }), "invalid precision rejected");
Throws<ArgumentOutOfRangeException>(() => new HdrCanvas(1, 1, srgb, full with { TileSize = 0 }), "zero tile size rejected");
Throws<ArgumentOutOfRangeException>(() => new HdrCanvas(1, 1, srgb, full with { MaxStorageBytes = 0 }), "invalid resource budget rejected");
Throws<ArgumentOutOfRangeException>(() => new CanvasRegion(int.MaxValue, 0, 1, 1), "rectangle overflow rejected");
Throws<ArgumentOutOfRangeException>(() => atomic.Fill(default, Color(1)), "default region rejected");
Throws<ArgumentOutOfRangeException>(() => atomic.SetPixel(-1, 0, Color(1)), "negative pixel rejected");
Throws<ArgumentOutOfRangeException>(() => atomic.SetPixel(4, 0, Color(1)), "exclusive edge pixel rejected");
Throws<NotSupportedException>(() => atomic.SetPixel(0, 0, default), "default untagged color rejected");
Throws<ArgumentOutOfRangeException>(() => atomic.Composite(new CanvasRegion(0, 0, 1, 1), Color(1), (CanvasBlendMode)42), "unknown blend mode rejected");
Throws<ArgumentOutOfRangeException>(() => atomic.Composite(new CanvasRegion(0, 0, 1, 1), Color(1), opacity: float.NaN), "non-finite opacity rejected");
Throws<ArgumentOutOfRangeException>(() => atomic.ApplyDab(default), "default dab rejected");
Throws<ArgumentOutOfRangeException>(() => new BrushDab(float.NaN, 0, 1, Color(1)), "non-finite dab center rejected");
Throws<ArgumentOutOfRangeException>(() => new BrushDab(0, 0, 0, Color(1)), "zero dab radius rejected");
Throws<ArgumentOutOfRangeException>(() => new BrushDab(0, 0, 1, Color(1), hardness: 2), "invalid hardness rejected");
Unchanged(atomic, before, beforeRevision, beforeStorage, "invalid public inputs preserve previous valid state");

foreach (string failure in failures)
{
    Console.Error.WriteLine($"FAIL: {failure}");
}
Console.WriteLine($"Mu3D.Creative: {assertions - failures.Count}/{assertions} assertions passed.");
return failures.Count == 0 ? 0 : 1;

void Expect(bool condition, string message)
{
    assertions++;
    if (!condition)
    {
        failures.Add(message);
    }
}

void Near(float actual, float expected, string message, float tolerance = 2e-6f) =>
    Expect(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance, $"{message}: {actual} vs {expected}");

void Pixel(LinearRgba actual, Vector4 expected, string message, float tolerance = 2e-6f)
{
    Near(actual.Red, expected.X, message + " red", tolerance);
    Near(actual.Green, expected.Y, message + " green", tolerance);
    Near(actual.Blue, expected.Z, message + " blue", tolerance);
    Near(actual.Alpha, expected.W, message + " alpha", tolerance);
}

void Throws<T>(Action action, string message) where T : Exception
{
    try
    {
        action();
        Expect(false, message + " (no exception)");
    }
    catch (T)
    {
        Expect(true, message);
    }
    catch (Exception exception)
    {
        Expect(false, message + $" ({exception.GetType().Name} instead of {typeof(T).Name})");
    }
}

void Unchanged(HdrCanvas canvas, LinearRgbaImage prior, ulong revision, long bytes, string message) =>
    Expect(canvas.Snapshot().Pixels.SequenceEqual(prior.Pixels) && canvas.Revision == revision &&
        canvas.AllocatedStorageBytes == bytes && canvas.GetDirtyRegions().Count == 0, message);
