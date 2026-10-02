using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;

int assertions = 0;
List<string> failures = [];
ColorSpaceReference sourceSpace = StandardColorSpaces.AcesCg;
ColorSpaceReference destinationSpace = StandardColorSpaces.LinearRec2020;
LinearRgbLut3D table = MakeTable(ColorLutRangePolicy.Clamp);
using RecordingGraphicsDevice device = new();
LinearRgbLutGpuTransform transform = new(device, table, PixelPrecision.Float32);
Expect(transform.SourceSpace == sourceSpace && transform.DestinationSpace == destinationSpace &&
    ReferenceEquals(transform.Table, table) && transform.OutputPrecision == PixelPrecision.Float32 &&
    transform.OutputFormat == GraphicsTextureFormat.Rgba32Float, "explicit transform metadata retained");
RecordedTextureWrite upload = device.TextureWrites.Single();
Expect(upload.Destination.Descriptor.Format == GraphicsTextureFormat.Rgba32Float &&
    upload.Size == new GraphicsExtent3D(9, 3) && upload.BytesPerRow == 144 && upload.RowsPerImage == 3,
    "FP32 2D LUT upload uses R-fastest G-next B-row layout");
Expect(upload.Destination.Descriptor.Usage == (GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.CopyDestination),
    "LUT resource has only required usage");
Vector4[] uploaded = MemoryMarshal.Cast<byte, Vector4>(upload.Data).ToArray();
Expect(uploaded.Length == table.Samples.Count && uploaded.Select(v => new Vector3(v.X, v.Y, v.Z)).SequenceEqual(table.Samples),
    "upload preserves all FP32 HDR and negative sample values");
Expect(device.SamplerDescriptors.Count == 0, "manual texture loads require no filtering sampler");
GraphicsRenderPipelineDescriptor pipeline = device.PipelineDescriptors.Single();
Expect(pipeline.ColorFormat == GraphicsTextureFormat.Rgba32Float && pipeline.Blend is null && pipeline.DepthStencil is null,
    "float output performs no alpha blending or depth writes");
IReadOnlyList<GraphicsBindGroupLayoutEntry> entries = pipeline.Layout!.Descriptor.BindGroupLayouts.Single().Descriptor.Entries;
Expect(entries.Count == 3 && entries.Take(2).All(e =>
    e.TextureSampleType == GraphicsTextureSampleType.UnfilterableFloat && e.Visibility == GraphicsShaderStage.Fragment),
    "source and LUT use unfilterable FP32-compatible texture bindings");
Vector4[] uniforms = MemoryMarshal.Cast<byte, Vector4>(device.Buffers.Single().Data).ToArray();
Expect(uniforms.SequenceEqual([new Vector4(table.DomainMinimum, 3), new Vector4(table.DomainMaximum, 0),
    new Vector4(table.DomainMaximum - table.DomainMinimum, 0)]), "domain and edge upload preserves FP32 parameters");

using GraphicsTexture source = Texture(device, GraphicsTextureFormat.Rgba32Float,
    GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.RenderAttachment);
using GraphicsTexture target = Texture(device, GraphicsTextureFormat.Rgba32Float, GraphicsTextureUsage.RenderAttachment);
transform.Apply(source, sourceSpace, target, destinationSpace);
Expect(device.SubmittedPipelineLabels.SequenceEqual(["Mu3D color LUT transform"]), "one fullscreen pass submitted");
Expect(((RecordingGraphicsTexture)target).LastColorLoadOperation == GraphicsLoadOperation.Clear,
    "complete output is overwritten rather than blended with previous pixels");
Expect(device.Buffers.Count == 1 && device.Buffers.All(b => (b.Usage & GraphicsBufferUsage.MapRead) == 0),
    "normal transform creates no readback buffer");
transform.Apply(source, sourceSpace, target, destinationSpace);
Expect(device.TextureWrites.Count == 1 && device.PipelineDescriptors.Count == 1 && device.SubmittedPipelineLabels.Count == 2,
    "subsequent transforms reuse uploaded table and compiled pipeline");

int submitted = device.SubmittedPipelineLabels.Count;
Throws<ArgumentException>(() => transform.Apply(source, destinationSpace, target, destinationSpace), "source identity mismatch rejected");
Throws<ArgumentException>(() => transform.Apply(source, sourceSpace, target, sourceSpace), "destination identity mismatch rejected");
Throws<ArgumentException>(() => transform.Apply(source, sourceSpace, source, destinationSpace), "in-place texture alias rejected");
using GraphicsTexture wrongSize = device.CreateTexture(new GraphicsTextureDescriptor(new GraphicsExtent3D(1, 1),
    GraphicsTextureFormat.Rgba32Float, GraphicsTextureUsage.RenderAttachment));
Throws<ArgumentException>(() => transform.Apply(source, sourceSpace, wrongSize, destinationSpace), "different dimensions rejected");
using GraphicsTexture wrongOutput = Texture(device, GraphicsTextureFormat.Rgba16Float, GraphicsTextureUsage.RenderAttachment);
Throws<ArgumentException>(() => transform.Apply(source, sourceSpace, wrongOutput, destinationSpace), "destination precision mismatch rejected");
using GraphicsTexture wrongInput = Texture(device, GraphicsTextureFormat.Rgba8UnormSrgb, GraphicsTextureUsage.TextureBinding);
Throws<ArgumentException>(() => transform.Apply(wrongInput, sourceSpace, target, destinationSpace), "encoded input rejected");
using GraphicsTexture wrongUsage = Texture(device, GraphicsTextureFormat.Rgba32Float, GraphicsTextureUsage.CopySource);
Throws<ArgumentException>(() => transform.Apply(wrongUsage, sourceSpace, target, destinationSpace), "input without texture binding rejected");
Throws<ArgumentException>(() => transform.Apply(source, sourceSpace, wrongUsage, destinationSpace), "output without render attachment rejected");
using RecordingGraphicsDevice anotherDevice = new();
using GraphicsTexture foreign = Texture(anotherDevice, GraphicsTextureFormat.Rgba32Float, GraphicsTextureUsage.TextureBinding);
Throws<ArgumentException>(() => transform.Apply(foreign, sourceSpace, target, destinationSpace), "cross-device texture rejected");
Expect(device.SubmittedPipelineLabels.Count == submitted, "invalid apply calls submit no work");
Throws<NotSupportedException>(() => new LinearRgbLutGpuTransform(device, MakeTable(ColorLutRangePolicy.Reject), PixelPrecision.Float32),
    "GPU cannot silently replace explicit Reject policy with Clamp");
LinearRgbLut3D hugeTable = LinearRgbLut3D.Bake(2, Vector3.Zero, Vector3.One, sourceSpace, destinationSpace,
    c => new LinearRgba(70000, c.Green, c.Blue, 1, destinationSpace), ColorLutRangePolicy.Clamp);
Throws<ArgumentOutOfRangeException>(() => new LinearRgbLutGpuTransform(device, hugeTable, PixelPrecision.Float16),
    "half output rejects LUT values outside finite half storage before upload");
Expect(device.TextureWrites.Count == 1, "constructor validation errors allocate no LUT data");
Vector3[] domainTestCorners = Enumerable.Range(0, 8)
    .Select(i => new Vector3(i % 2, i / 2 % 2, i / 4)).ToArray();
LinearRgbLut3D subnormalWidth = new(2, domainTestCorners, Vector3.Zero, new Vector3(float.Epsilon),
    sourceSpace, destinationSpace, ColorLutRangePolicy.Clamp);
Throws<ArgumentOutOfRangeException>(() => new LinearRgbLutGpuTransform(device, subnormalWidth, PixelPrecision.Float32),
    "subnormal GPU domain width is rejected rather than producing NaN after shader flush-to-zero");
float halfMinimumNormal = BitConverter.Int32BitsToSingle(0x00400000);
LinearRgbLut3D subnormalEndpoints = new(2, domainTestCorners, new Vector3(-halfMinimumNormal), new Vector3(halfMinimumNormal),
    sourceSpace, destinationSpace, ColorLutRangePolicy.Clamp);
Throws<ArgumentOutOfRangeException>(() => new LinearRgbLutGpuTransform(device, subnormalEndpoints, PixelPrecision.Float32),
    "subnormal GPU endpoints rejected even when their combined domain width is normal");
LinearRgbLut3D narrowNormalDomain = new(2, domainTestCorners, new Vector3(1e-32f), new Vector3(float.BitIncrement(1e-32f)),
    sourceSpace, destinationSpace, ColorLutRangePolicy.Clamp);
Throws<ArgumentOutOfRangeException>(() => new LinearRgbLutGpuTransform(device, narrowNormalDomain, PixelPrecision.Float32),
    "normal endpoints with subnormal separation rejected before shader division");
Expect(device.TextureWrites.Count == 1, "subnormal domain failures allocate no GPU resources");
using (LinearRgbLutGpuTransform half = new(device, table, PixelPrecision.Float16))
{
    half.Apply(source, sourceSpace, wrongOutput, destinationSpace);
    Expect(half.OutputFormat == GraphicsTextureFormat.Rgba16Float, "explicit Float16 output works with Float32 input");
}

transform.Dispose();
transform.Dispose();
Expect(upload.Destination.IsDisposed && pipeline.VertexShader.IsDisposed && pipeline.Layout.IsDisposed &&
    pipeline.Layout.Descriptor.BindGroupLayouts.Single().IsDisposed && device.Buffers[0].IsDisposed,
    "disposing transform releases owned texture shader layouts and parameter buffer");
Expect(!source.IsDisposed && !target.IsDisposed && device.State == GraphicsDeviceState.Active,
    "caller textures and device survive transform disposal");
Throws<ObjectDisposedException>(() => transform.Apply(source, sourceSpace, target, destinationSpace), "disposed transform rejects work");

if (args.Contains("--native", StringComparer.Ordinal))
{
    try
    {
        using WgpuGraphicsDevice native = await WgpuGraphicsDevice.CreateAsync(TimeSpan.FromSeconds(15));
        foreach (PixelPrecision outputPrecision in Enum.GetValues<PixelPrecision>())
        foreach (GraphicsTextureFormat inputFormat in new[] { GraphicsTextureFormat.Rgba16Float, GraphicsTextureFormat.Rgba32Float })
        {
            await VerifyNative(native, inputFormat, outputPrecision);
        }
        Console.WriteLine("Headless native FP16/FP32 texture LUT comparisons completed; no surface or UI was created.");
    }
    catch (Exception exception)
    {
        Expect(false, $"Native comparison failed: {exception}");
    }
}

foreach (string failure in failures)
{
    Console.Error.WriteLine($"FAIL: {failure}");
}
Console.WriteLine($"Mu3D.Color.Gpu: {assertions - failures.Count}/{assertions} assertions passed.");
return failures.Count == 0 ? 0 : 1;

LinearRgbLut3D MakeTable(ColorLutRangePolicy policy) => LinearRgbLut3D.Bake(3,
    new Vector3(-1f), new Vector3(4f), sourceSpace, destinationSpace,
    c => new LinearRgba(c.Red * 1.2f + c.Green * 0.2f - 0.1f,
        c.Green * 0.9f + c.Blue * c.Blue * 0.1f,
        c.Blue * 0.8f - c.Red * 0.1f, c.Alpha, destinationSpace), policy);

static GraphicsTexture Texture(GraphicsDevice gpu, GraphicsTextureFormat format, GraphicsTextureUsage usage) =>
    gpu.CreateTexture(new GraphicsTextureDescriptor(new GraphicsExtent3D(4, 2), format, usage));

async Task VerifyNative(WgpuGraphicsDevice gpu, GraphicsTextureFormat inputFormat, PixelPrecision outputPrecision)
{
    Vector4[] pixels =
    [
        new(-1f, -1f, -1f, 0f), new(4f, 4f, 4f, 1f), new(0.13f, 1.82f, 3.4f, 0.333f), new(-0.5f, 2.6f, 0.7f, 0.5f),
        new(-8f, 9f, 2f, 0.25f), new(2.5f, 0f, -0.27f, 0.8f), new(0f, 0f, 0f, 0.4f), new(3f, 0.51f, 3.1f, 0.75f),
    ];
    using GraphicsTexture input = Texture(gpu, inputFormat, GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.CopyDestination);
    if (inputFormat == GraphicsTextureFormat.Rgba16Float)
    {
        Half[] halfPixels = MemoryMarshal.Cast<Vector4, float>(pixels).ToArray().Select(v => (Half)v).ToArray();
        gpu.Queue.WriteTexture(input, 0, default, input.Descriptor.Size, MemoryMarshal.AsBytes(halfPixels.AsSpan()), 32, 2);
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Vector4((float)halfPixels[i * 4], (float)halfPixels[i * 4 + 1],
                (float)halfPixels[i * 4 + 2], (float)halfPixels[i * 4 + 3]);
        }
    }
    else
    {
        gpu.Queue.WriteTexture(input, 0, default, input.Descriptor.Size, MemoryMarshal.AsBytes(pixels.AsSpan()), 64, 2);
    }
    using LinearRgbLutGpuTransform gpuTransform = new(gpu, table, outputPrecision);
    using GraphicsTexture output = Texture(gpu, gpuTransform.OutputFormat,
        GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource);
    gpuTransform.Apply(input, sourceSpace, output, destinationSpace);
    using GraphicsBuffer readback = gpu.CreateBuffer(new GraphicsBufferDescriptor(512,
        GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead));
    using GraphicsCommandEncoder encoder = gpu.CreateCommandEncoder("Color LUT numerical verification");
    encoder.CopyTextureToBuffer(output, 0, default, output.Descriptor.Size, readback, 0, 256, 2);
    using GraphicsCommandBuffer commands = encoder.Finish();
    gpu.Queue.Submit(commands);
    byte[] bytes = await readback.ReadAsync(0, 512).WaitAsync(TimeSpan.FromSeconds(15));
    for (int i = 0; i < pixels.Length; i++)
    {
        LinearRgba expected = table.Transform(new LinearRgba(pixels[i].X, pixels[i].Y, pixels[i].Z, pixels[i].W, sourceSpace));
        int offset = i / 4 * 256 + i % 4 * (outputPrecision == PixelPrecision.Float16 ? 8 : 16);
        Vector4 actual = outputPrecision == PixelPrecision.Float16
            ? new Vector4((float)BitConverter.ToHalf(bytes, offset), (float)BitConverter.ToHalf(bytes, offset + 2),
                (float)BitConverter.ToHalf(bytes, offset + 4), (float)BitConverter.ToHalf(bytes, offset + 6))
            : new Vector4(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4),
                BitConverter.ToSingle(bytes, offset + 8), BitConverter.ToSingle(bytes, offset + 12));
        Vector4 expectedValue = new(expected.Red, expected.Green, expected.Blue, expected.Alpha);
        float tolerance = outputPrecision == PixelPrecision.Float16 ? 0.004f : 1e-5f;
        for (int channel = 0; channel < 4; channel++)
        {
            Expect(float.IsFinite(actual[channel]) && MathF.Abs(actual[channel] - expectedValue[channel]) <= tolerance,
                $"native {inputFormat}->{outputPrecision} pixel {i} channel {channel}: {actual[channel]} vs {expectedValue[channel]}");
        }
    }
}

void Expect(bool condition, string message)
{
    assertions++;
    if (!condition) failures.Add(message);
}

void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); Expect(false, message + " (no exception)"); }
    catch (T) { Expect(true, message); }
    catch (Exception exception) { Expect(false, message + $" ({exception.GetType().Name} instead of {typeof(T).Name})"); }
}
