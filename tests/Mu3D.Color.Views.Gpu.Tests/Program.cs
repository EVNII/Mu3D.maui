using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.GalleryApp.Pages;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;

int assertions = 0;
List<string> failures = [];
using (RecordingGraphicsDevice recording = new())
{
    ColorViewTransform view = new(ColorViewPreset.AgXHdr1000, StandardColorSpaces.LinearSrgb);
    using ColorViewGpuTransform gpu = new(recording, view, GraphicsTextureFormat.Rgba16Float);
    Expect(recording.Buffers.Count == 2 && recording.SamplerDescriptors.Count == 0, "official tables uploaded once without filter sampler");
    Expect(recording.Buffers[0].Data.Length == view.Program.Table.Length * 16, "complete FP32 table upload");
    using GraphicsTexture source = Texture(recording, GraphicsTextureFormat.Rgba32Float, GraphicsTextureUsage.TextureBinding, 8);
    using GraphicsTexture target = Texture(recording, GraphicsTextureFormat.Rgba16Float, GraphicsTextureUsage.RenderAttachment, 8);
    gpu.Apply(source, view.SourceSpace, target, view.DestinationSpace);
    gpu.UpdateTransform(new(view.Preset, view.SourceSpace, 2, 200));
    gpu.Apply(source, view.SourceSpace, target, view.DestinationSpace);
    Expect(recording.PipelineDescriptors.Count == 1 && recording.SubmittedPipelineLabels.Count == 2, "exposure changes reuse the view pipeline");
    Vector4 uniform = MemoryMarshal.Cast<byte, Vector4>(recording.Buffers[1].Data)[0];
    Expect(uniform.X == 4 && uniform.Y == 0.5f, "exposure and reference white are separate uniforms");
    ColorViewTransform retained = gpu.Transform;
    ColorViewTransform replacement = new(view.Preset, view.SourceSpace, -1, 100);
    recording.FailNextBufferWrite = true;
    Throws<InvalidOperationException>(() => gpu.UpdateTransform(replacement), "failed parameter upload reported");
    Expect(ReferenceEquals(retained, gpu.Transform), "failed upload retains matching CPU metadata for retry");
    gpu.UpdateTransform(replacement);
    Expect(ReferenceEquals(replacement, gpu.Transform), "parameter upload can recover after failure");
    Throws<ArgumentException>(() => gpu.Apply(source, StandardColorSpaces.AcesCg, target, view.DestinationSpace), "mismatched source rejected");
    Throws<ArgumentException>(() => gpu.Apply(source, view.SourceSpace, source, view.DestinationSpace), "in-place texture rejected");
    Throws<ArgumentException>(() => gpu.UpdateTransform(new(ColorViewPreset.Aces2Hdr1000, view.SourceSpace)), "program changes require new pipeline");
    Throws<NotSupportedException>(() => new ColorViewGpuTransform(recording, view, GraphicsTextureFormat.Rgba8UnormSrgb, ColorEncoding.Srgb), "HDR cannot silently clip to SDR");
    Throws<NotSupportedException>(() => new ColorViewGpuTransform(recording, view, GraphicsTextureFormat.Rgba16Float, ColorEncoding.Bt2100Pq), "numeric PQ utility does not enable unsupported compositor");
    gpu.Dispose();
    Expect(recording.Buffers.All(b => b.IsDisposed) && recording.PipelineDescriptors[0].VertexShader.IsDisposed, "owned resources released");
    Expect(!source.IsDisposed && !target.IsDisposed, "caller textures retained");
    Throws<ObjectDisposedException>(() => gpu.Apply(source, view.SourceSpace, target, view.DestinationSpace), "disposed transform rejected");
}

if (args.Contains("--native", StringComparer.Ordinal))
{
    using WgpuGraphicsDevice device = await WgpuGraphicsDevice.CreateAsync(TimeSpan.FromSeconds(30));
    foreach (ColorViewPreset preset in Enum.GetValues<ColorViewPreset>())
    foreach (ColorSpaceReference space in new[] { StandardColorSpaces.LinearSrgb, StandardColorSpaces.AcesCg, StandardColorSpaces.LinearDisplayP3, StandardColorSpaces.LinearRec2020, StandardColorSpaces.LinearAdobeRgb, StandardColorSpaces.LinearProPhotoRgb })
    {
        await Verify(device, new(preset, space), GraphicsTextureFormat.Rgba32Float, false, false);
        await Verify(device, new(preset, space, 1.5f, 200), GraphicsTextureFormat.Rgba16Float, true, true);
        await Verify(device, new(preset, space, -1, 100), GraphicsTextureFormat.Rgba32Float, true, false);
        if (preset is ColorViewPreset.AgXSdr or ColorViewPreset.Aces2Sdr or ColorViewPreset.FilmicSdr)
        {
            await Verify(device, new(preset, space), GraphicsTextureFormat.Rgba8UnormSrgb, false, false);
            await Verify(device, new(preset, space), GraphicsTextureFormat.Rgba8Unorm, false, false);
            await Verify(device, new(preset, space), GraphicsTextureFormat.Rgba8UnormSrgb, true, true);
            await Verify(device, new(preset, space), GraphicsTextureFormat.Rgba8Unorm, true, true);
            await Verify(device, new(preset, space), GraphicsTextureFormat.Rgba8UnormSrgb, true, false);
            await Verify(device, new(preset, space), GraphicsTextureFormat.Rgba8Unorm, true, false);
        }
    }
    Console.WriteLine("Headless native display-view comparisons completed; no surface or UI was created.");
}
if (args.Contains("--print-pattern", StringComparer.Ordinal))
{
    using var device = await WgpuGraphicsDevice.CreateAsync(TimeSpan.FromSeconds(30));
    await VerifyPrintPattern(device);
}
if (args.Contains("--color-comparison", StringComparer.Ordinal))
{
    using var device = await WgpuGraphicsDevice.CreateAsync(TimeSpan.FromSeconds(30));
    await VerifyComparisonPattern(device);
}
foreach (string failure in failures) Console.Error.WriteLine(failure);
Console.WriteLine($"Color view GPU: {assertions - failures.Count}/{assertions} checks passed.");
return failures.Count == 0 ? 0 : 1;

async Task Verify(WgpuGraphicsDevice device, ColorViewTransform view, GraphicsTextureFormat format, bool inputPremultiplied, bool outputPremultiplied)
{
    const uint count = 64;
    Vector4[] colors = new Vector4[count];
    colors[0] = Vector4.Zero; colors[1] = new(0.18f, 0.18f, 0.18f, 1); colors[2] = new(100, 100, 100, 1);
    colors[3] = new(-0.3f, 1.2f, 20, 0.5f); colors[4] = new(5, 0.04f, 0.2f, 0.25f);
    colors[5] = new(1e-6f, 0, 0.002f, 1); colors[6] = new(60000, 20000, 10000, 0.75f);
    colors[7] = new(0.8f, 5, 0.1f, 0);
    Random random = new(412);
    for (int i = 8; i < count; i++) colors[i] = new(
        (float)Math.Pow(2, random.NextDouble() * 18 - 8), (float)Math.Pow(2, random.NextDouble() * 18 - 8),
        (float)Math.Pow(2, random.NextDouble() * 18 - 8), (float)random.NextDouble());
    Vector4[] inputColors = colors.Select(v => inputPremultiplied ? new Vector4(new Vector3(v.X,v.Y,v.Z) * v.W, v.W) : v).ToArray();
    using GraphicsTexture source = Texture(device, GraphicsTextureFormat.Rgba32Float, GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.CopyDestination, count);
    device.Queue.WriteTexture(source, 0, default, source.Descriptor.Size, MemoryMarshal.AsBytes(inputColors.AsSpan()), count * 16, 1);
    bool encoded = format is GraphicsTextureFormat.Rgba8Unorm or GraphicsTextureFormat.Rgba8UnormSrgb;
    using ColorViewGpuTransform transform = new(device, view, format,
        encoded ? ColorEncoding.Srgb : ColorEncoding.ExtendedSrgbLinear, inputPremultiplied, outputPremultiplied);
    using GraphicsTexture target = Texture(device, format, GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource, count);
    transform.Apply(source, view.SourceSpace, target, view.DestinationSpace);
    uint stride = count * (encoded ? 4u : format == GraphicsTextureFormat.Rgba16Float ? 8u : 16u);
    using GraphicsBuffer readback = device.CreateBuffer(new GraphicsBufferDescriptor(stride, GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead));
    using GraphicsCommandEncoder encoder = device.CreateCommandEncoder();
    encoder.CopyTextureToBuffer(target, 0, default, target.Descriptor.Size, readback, 0, stride, 1);
    using GraphicsCommandBuffer command = encoder.Finish(); device.Queue.Submit(command);
    byte[] bytes = await readback.ReadAsync(0, (int)stride).WaitAsync(TimeSpan.FromSeconds(30));
    float maximumError = 0;
    for (int i = 0; i < count; i++)
    {
        Vector4 input = colors[i];
        LinearRgba expected = view.Transform(new(input.X, input.Y, input.Z, input.W, view.SourceSpace));
        Vector4 result = new(expected.Red, expected.Green, expected.Blue, expected.Alpha);
        if (inputPremultiplied && input.W == 0) result = Vector4.Zero;
        else if (outputPremultiplied) result = new(new Vector3(result.X,result.Y,result.Z) * result.W, result.W);
        for (int channel = 0; channel < 4; channel++)
        {
            float reference = result[channel];
            if (encoded && channel < 3) reference = reference <= 0.0031308f ? reference * 12.92f : 1.055f * MathF.Pow(reference, 1/2.4f) - 0.055f;
            if (encoded) reference = Math.Clamp(reference, 0, 1);
            float actual = encoded ? bytes[i*4+channel] / 255f : format == GraphicsTextureFormat.Rgba16Float
                ? (float)BitConverter.ToHalf(bytes, i*8+channel*2) : BitConverter.ToSingle(bytes, i*16+channel*4);
            float error = MathF.Abs(actual - reference); maximumError = MathF.Max(maximumError, error);
            float tolerance = encoded ? 1.1f/255 : (format == GraphicsTextureFormat.Rgba16Float ? 0.0015f : 0.0003f) * MathF.Max(1, MathF.Abs(reference));
            Expect(float.IsFinite(actual) && error <= tolerance,
                $"{view.Preset} {view.SourceSpace} {format} premul {inputPremultiplied}/{outputPremultiplied} p{i} c{channel}: {actual} vs {reference} (limit{tolerance})");
        }
    }
    Console.WriteLine($"{view.Preset} {view.SourceSpace} {format} premul {inputPremultiplied}/{outputPremultiplied}: max absolute error {maximumError:G4}");
}
GraphicsTexture Texture(GraphicsDevice device, GraphicsTextureFormat format, GraphicsTextureUsage usage, uint width) =>
    device.CreateTexture(new GraphicsTextureDescriptor(new(width,1),format,usage));
void Expect(bool condition, string message) { assertions++; if (!condition) failures.Add(message); }
void Throws<T>(Action action, string message) where T:Exception
{
    try { action(); Expect(false,message); } catch(T) { Expect(true,message); }
}

async Task VerifyPrintPattern(WgpuGraphicsDevice device)
{
    var pixels = CmykPrintingPattern.Create();
    Expect(pixels[24*64+16].Red == .18f && pixels[24*64+56].Red == 8, "middle gray and HDR reference patches");
    Expect(pixels[0].Red < .003 && pixels[23*64].Red > 11, "source covers shadows through HDR highlights");
    using var renderer = new CmykPrintingHdrRenderer(device);
    foreach(int zoom in new[] { 1, 2 })
    foreach(float exposure in new[] { 0f, -2f })
    {
        uint width=(uint)(64*zoom), height=(uint)(32*zoom), stride=width*8;
        using var target=device.CreateTexture(new(new(width,height),GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment|GraphicsTextureUsage.CopySource));
        renderer.Draw(target,pixels,exposure);
        using var readback=device.CreateBuffer(new(stride*height,GraphicsBufferUsage.CopyDestination|GraphicsBufferUsage.MapRead));
        using var encoder=device.CreateCommandEncoder();
        encoder.CopyTextureToBuffer(target,0,default,target.Descriptor.Size,readback,0,stride,height);
        using var command=encoder.Finish();device.Queue.Submit(command);
        var bytes=await readback.ReadAsync(0,(int)(stride*height)).WaitAsync(TimeSpan.FromSeconds(30));
        for(int y=0;y<32;y++)for(int x=0;x<64;x++)
        {
            var expected=StandardLinearRgbConverter.Convert(pixels[y*64+x],StandardColorSpaces.LinearSrgb);
            float[] reference=[expected.Red*MathF.Pow(2,exposure),expected.Green*MathF.Pow(2,exposure),expected.Blue*MathF.Pow(2,exposure),1];
            int offset=(int)((y*zoom+zoom/2)*stride+(x*zoom+zoom/2)*8);
            for(int channel=0;channel<4;channel++)
            {
                float actual=(float)BitConverter.ToHalf(bytes,offset+channel*2);
                Expect(Math.Abs(actual-reference[channel])<.001f*Math.Max(1,Math.Abs(reference[channel])),
                    $"HDR chart orientation/resize/exposure p{x},{y} c{channel} {actual} vs {reference[channel]}");
            }
        }
        float bright=(float)BitConverter.ToHalf(bytes,(int)((24*zoom)*stride+(56*zoom)*8));
        Expect(bright > 1,"native chart preserves HDR values above one");
    }
    Console.WriteLine("Print chart FP16 native readback, orientation, 2x resize and exposure passed.");
}

async Task VerifyComparisonPattern(WgpuGraphicsDevice device)
{
    using var renderer = new ColorComparisonRenderer(device);
    Vector3[] rows=[new(1,0,0),new(0,1,0),new(0,0,1),new(0,1,1),new(1,0,1),new(1,1,0),Vector3.One];
    foreach(var preset in new[] { ColorViewPreset.AgXHdr1000, ColorViewPreset.Aces2Hdr1000P3, ColorViewPreset.AgXSdr, ColorViewPreset.Aces2Sdr, ColorViewPreset.FilmicSdr })
    foreach(bool rainbow in new[] { false,true })
    foreach(float saturation in new[] { 1f,.6f,0f })
    {
        const uint width=64,height=7,stride=512;
        float exposure = saturation == .6f ? 2 : 0;
        using var target=device.CreateTexture(new(new(width,height),GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment|GraphicsTextureUsage.CopySource));
        renderer.Draw(target,preset,exposure,saturation,rainbow,ColorEncoding.ExtendedSrgbLinear);
        using var readback=device.CreateBuffer(new(stride*height,GraphicsBufferUsage.CopyDestination|GraphicsBufferUsage.MapRead));
        using var encoder=device.CreateCommandEncoder();
        encoder.CopyTextureToBuffer(target,0,default,target.Descriptor.Size,readback,0,stride,height);
        using var command=encoder.Finish();device.Queue.Submit(command);
        var bytes=await readback.ReadAsync(0,(int)(stride*height)).WaitAsync(TimeSpan.FromSeconds(30));
        var cpu=new ColorViewTransform(preset,StandardColorSpaces.LinearRec2020,exposure);
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            Vector3 primary=rows[y];
            if(rainbow)
            {
                float h=(y+.5f)/height*6;
                primary=new(Hue(h),Hue(h+4),Hue(h+2));
            }
            float peak=.18f*MathF.Pow(2,-6+18*(x+.5f)/width);
            var rgb=Vector3.Lerp(Vector3.One,primary,saturation)*peak;
            var expected=cpu.Transform(new(rgb.X,rgb.Y,rgb.Z,1,StandardColorSpaces.LinearRec2020));
            float[] reference=[expected.Red,expected.Green,expected.Blue,1];
            for(int c=0;c<4;c++)
            {
                float actual=(float)BitConverter.ToHalf(bytes,(int)(y*stride+x*8+c*2));
                Expect(float.IsFinite(actual) && Math.Abs(actual-reference[c])<.002f*Math.Max(1,Math.Abs(reference[c])),
                    $"comparison {preset} rainbow={rainbow} sat={saturation} pixel{x},{y} c{c}: {actual} vs {reference[c]}");
            }
        }
        float white=(float)BitConverter.ToHalf(bytes,(int)((height-1)*stride+(width-1)*8));
        if(!rainbow)Expect(cpu.IsHdr ? white>9 : white<=1.01f,"HDR/SDR highlight scale is explicit");
    }
    // Exercise reallocation and an explicitly encoded SDR target.
    using var resized=device.CreateTexture(new(new(128,14),GraphicsTextureFormat.Rgba8UnormSrgb,GraphicsTextureUsage.RenderAttachment));
    renderer.Draw(resized,ColorViewPreset.Aces2Sdr,0,1,false,ColorEncoding.Srgb);
    Console.WriteLine("Matched-gamut AgX/ACES 2 and SDR Filmic chart readback and SDR resize passed.");
    await VerifyRawComparison(device,renderer);
    static float Hue(float h) => Math.Clamp(Math.Abs(h%6-3)-1,0,1);
}

async Task VerifyRawComparison(WgpuGraphicsDevice device, ColorComparisonRenderer renderer)
{
    Vector3[] rows=[new(1,0,0),new(0,1,0),new(0,0,1),new(0,1,1),new(1,0,1),new(1,1,0),Vector3.One];
    foreach(var (format,hdr) in new[] {
        (GraphicsTextureFormat.Rgba16Float,true), (GraphicsTextureFormat.Rgba16Float,false),
        (GraphicsTextureFormat.Rgba8UnormSrgb,false), (GraphicsTextureFormat.Rgba8Unorm,false) })
    foreach(bool rainbow in new[] { false,true })
    foreach(float saturation in new[] { 1f,.6f,0f })
    foreach(float exposure in new[] { -6f,0f,6f })
    {
        uint width=exposure==6 ? 128u : 64u,height=exposure==6 ? 14u : 7u;
        bool floating=format==GraphicsTextureFormat.Rgba16Float;
        int pixelBytes=floating ? 8 : 4;
        uint stride=width*(uint)pixelBytes;
        var encoding=floating ? ColorEncoding.ExtendedSrgbLinear : ColorEncoding.Srgb;
        using var target=device.CreateTexture(new(new(width,height),format,
            GraphicsTextureUsage.RenderAttachment|GraphicsTextureUsage.CopySource));
        renderer.DrawStandard(target,exposure,saturation,rainbow,hdr,encoding);
        if(!floating)Throws<NotSupportedException>(() => renderer.DrawStandard(target,exposure,saturation,rainbow,true,encoding),
            "raw HDR cannot silently use an SDR surface");
        using var readback=device.CreateBuffer(new(stride*height,GraphicsBufferUsage.CopyDestination|GraphicsBufferUsage.MapRead));
        using var encoder=device.CreateCommandEncoder();
        encoder.CopyTextureToBuffer(target,0,default,target.Descriptor.Size,readback,0,stride,height);
        using var command=encoder.Finish();device.Queue.Submit(command);
        var bytes=await readback.ReadAsync(0,(int)(stride*height)).WaitAsync(TimeSpan.FromSeconds(30));
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            Vector3 primary=rows[(int)((y+.5f)/height*7)];
            if(rainbow)
            {
                float h=(y+.5f)/height*6;
                primary=new(Hue(h),Hue(h+4),Hue(h+2));
            }
            float peak=.18f*MathF.Pow(2,-6+18*(x+.5f)/width+exposure);
            var rgb=Vector3.Lerp(Vector3.One,primary,saturation)*peak;
            var expected=StandardLinearRgbConverter.Convert(new(rgb.X,rgb.Y,rgb.Z,1,StandardColorSpaces.LinearRec2020),StandardColorSpaces.LinearSrgb);
            float[] reference=[expected.Red,expected.Green,expected.Blue,1];
            for(int c=0;c<4;c++)
            {
                float value=hdr ? Math.Clamp(reference[c],-65504,65504) : Math.Clamp(reference[c],0,1);
                if(!floating && c<3)value=value<=.0031308f ? 12.92f*value : 1.055f*MathF.Pow(value,1/2.4f)-.055f;
                int offset=(int)(y*stride+x*pixelBytes);
                float actual=floating ? (float)BitConverter.ToHalf(bytes,offset+c*2) : bytes[offset+c]/255f;
                float tolerance=floating ? .002f*Math.Abs(value)+3e-7f : 2f/255;
                Expect(float.IsFinite(actual) && Math.Abs(actual-value)<=tolerance,
                    $"raw {format} HDR={hdr} rainbow={rainbow} sat={saturation} EV={exposure} p{x},{y} c{c}: {actual} vs {value}");
            }
        }
        if(hdr && !rainbow && saturation==1 && exposure==0)
        {
            float redGreen=(float)BitConverter.ToHalf(bytes,(int)((width-1)*8+2));
            float white=(float)BitConverter.ToHalf(bytes,(int)((height-1)*stride+(width-1)*8));
            Expect(redGreen<0,"raw transport preserves negative out-of-gamut channels");
            Expect(white>100,"raw highlights have no 1000-nit view compression");
        }
    }
    Console.WriteLine("Raw HDR/explicit SDR readback, saturation, exposure, finite FP16 extremes and resizing passed.");
    static float Hue(float h) => Math.Clamp(Math.Abs(h%6-3)-1,0,1);
}
