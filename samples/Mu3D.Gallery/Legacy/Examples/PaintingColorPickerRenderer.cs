using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;

namespace Mu3D.GalleryApp.Examples;

// Sample-owned FP32 sampling and FP16 preview; no CSS/RGBA8 replacement for selected HDR colors.
internal sealed class PaintingColorPickerRenderer : IDisposable
{
    private const int Size = 64, UniformBytes = 20 * 16;
    private readonly Vector2[] coordinates = new Vector2[Size * Size];
    private readonly float[] fractions = new float[Size];
    private readonly LinearRgba[] colors = new LinearRgba[Size * Size];
    private readonly Half[] pixels = new Half[Size * Size * 4];
    private readonly Vector4[] uniforms = new Vector4[20];
    private GraphicsDevice? device;
    private GraphicsTexture? texture;
    private GraphicsTextureView? view;
    private GraphicsSampler? sampler;
    private GraphicsBuffer? buffer;
    private GraphicsBindGroupLayout? bindLayout;
    private GraphicsBindGroup? bindings;
    private GraphicsPipelineLayout? layout;
    private GraphicsShaderModule? shader;
    private GraphicsRenderPipeline? pipeline;
    private GraphicsTextureFormat format;
    private int planeRevision = -1;
    private readonly int[] channelRevisions = [-1, -1, -1];

    internal void Draw(PaintingColorSpacesExample example, GraphicsDevice currentDevice, GraphicsTexture target, ColorEncoding encoding)
    {
        bool hdr = target.Descriptor.Format == GraphicsTextureFormat.Rgba16Float && encoding == ColorEncoding.ExtendedSrgbLinear;
        bool encoded = target.Descriptor.Format is GraphicsTextureFormat.Bgra8Unorm or GraphicsTextureFormat.Rgba8Unorm;
        bool hardware = target.Descriptor.Format is GraphicsTextureFormat.Bgra8UnormSrgb or GraphicsTextureFormat.Rgba8UnormSrgb;
        if (!hdr && !(encoding == ColorEncoding.Srgb && (encoded || hardware))) throw new NotSupportedException("Unsupported color-picker output encoding.");
        EnsureResources(currentDevice, target.Descriptor.Format);
        if (planeRevision != example.PlaneRevision)
        {
            example.FillPlane(coordinates, colors, Size); Pack(Size * Size);
            device!.Queue.WriteTexture(texture!, 0, default, new(Size, Size),
                MemoryMarshal.AsBytes(pixels.AsSpan()), Size * 8, Size);
            planeRevision = example.PlaneRevision;
        }
        for (int channel = 0; channel < 3; channel++)
        {
            if (channelRevisions[channel] == example.ChannelRevision(channel)) continue;
            example.FillChannel(channel, fractions, colors.AsSpan(0, Size)); Pack(Size);
            device!.Queue.WriteTexture(texture!, 0, new(0, (uint)(Size + channel), 0), new(Size, 1),
                MemoryMarshal.AsBytes(pixels.AsSpan(0, Size * 4)), Size * 8, 1);
            channelRevisions[channel] = example.ChannelRevision(channel);
        }
        var extent = target.Descriptor.Size;
        PaintingColorPickerLayout regions = new(extent.Width, extent.Height);
        uniforms[0] = new(hdr ? 0 : 1, encoded ? 1 : 0, example.Shape, 12);
        uniforms[1] = Vector(example.Color);
        uniforms[2] = regions.Plane; uniforms[3] = regions.Strip;
        uniforms[4] = regions.Current; uniforms[5] = regions.Harmony;
        uniforms[6] = new(example.PlanePosition, example.ChannelPosition(0), example.ChannelPosition(1));
        uniforms[7] = new(extent.Width, extent.Height, example.ChannelPosition(2), 0);
        for (int i = 0; i < 12; i++)
        {
            var color = example.SampleHarmony(i, 12); var swatch = Vector(color);
            swatch.W = example.IsPickable(color) ? 1 : 0; uniforms[8 + i] = swatch;
        }
        device!.Queue.WriteBuffer(buffer!, 0, MemoryMarshal.AsBytes(uniforms.AsSpan()));
        using var encoder = device.CreateCommandEncoder("Painting color picker");
        using (var pass = encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(target))))
        { pass.SetPipeline(pipeline!); pass.SetBindGroup(0, bindings!); pass.Draw(3); }
        using var commands = encoder.Finish(); device.Queue.Submit(commands);
    }
    private void Pack(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector4 c = Vector(colors[i]);
            pixels[i * 4] = (Half)c.X; pixels[i * 4 + 1] = (Half)c.Y;
            pixels[i * 4 + 2] = (Half)c.Z; pixels[i * 4 + 3] = (Half)c.W;
        }
    }
    private static Vector4 Vector(LinearRgba c)
    {
        if (MathF.Max(MathF.Abs(c.Red), MathF.Max(MathF.Abs(c.Green), MathF.Abs(c.Blue))) > 65504)
            throw new InvalidOperationException("选色预览超出 FP16 有限范围；未做隐式裁切。");
        return new(c.Red, c.Green, c.Blue, c.Alpha);
    }
    private void EnsureResources(GraphicsDevice currentDevice, GraphicsTextureFormat targetFormat)
    {
        if (ReferenceEquals(device, currentDevice) && format == targetFormat) return;
        Dispose(); device = currentDevice; format = targetFormat;
        try
        {
            texture = device.CreateTexture(new(new(Size, Size + 3), GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.CopyDestination, label: "Painting picker atlas"));
            view = device.CreateTextureView(new(texture));
            sampler = device.CreateSampler(new(magFilter: GraphicsFilterMode.Linear, minFilter: GraphicsFilterMode.Linear));
            buffer = device.CreateBuffer(new(UniformBytes, GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination));
            bindLayout = device.CreateBindGroupLayout(new([
                new(0, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.Uniform, UniformBytes),
                new(1, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.Float, GraphicsTextureViewDimension.TwoD),
                new(2, GraphicsShaderStage.Fragment, GraphicsSamplerBindingType.Filtering)]));
            bindings = device.CreateBindGroup(new(bindLayout, [new(0, buffer, 0, UniformBytes), new(1, view), new(2, sampler)]));
            layout = device.CreatePipelineLayout(new([bindLayout]));
            shader = device.CreateShaderModule(new(Shader, "Painting color picker"));
            pipeline = device.CreateRenderPipeline(new(shader, "vs", shader, "fs", format, layout: layout));
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        pipeline?.Dispose(); pipeline = null; shader?.Dispose(); shader = null;
        layout?.Dispose(); layout = null; bindings?.Dispose(); bindings = null;
        bindLayout?.Dispose(); bindLayout = null; buffer?.Dispose(); buffer = null;
        sampler?.Dispose(); sampler = null; view?.Dispose(); view = null; texture?.Dispose(); texture = null;
        device = null; planeRevision = -1; Array.Fill(channelRevisions, -1);
    }
    internal const string Shader = """
        struct Parameters {
            settings: vec4f, color: vec4f, plane: vec4f, strip: vec4f, current: vec4f, harmony: vec4f,
            marker: vec4f, extent: vec4f, palette: array<vec4f,12>,
        };
        @group(0) @binding(0) var<uniform> p: Parameters;
        @group(0) @binding(1) var atlas: texture_2d<f32>;
        @group(0) @binding(2) var filtering: sampler;
        struct Vertex { @builtin(position) position: vec4f, @location(0) uv: vec2f, };
        @vertex fn vs(@builtin(vertex_index) i: u32) -> Vertex {
            let positions = array(vec2f(-1,-1),vec2f(3,-1),vec2f(-1,3));
            var v: Vertex; v.position = vec4f(positions[i],0,1);
            v.uv = (positions[i] * vec2f(1,-1) + vec2f(1)) * 0.5; return v;
        }
        fn local(uv: vec2f, r: vec4f) -> vec2f { return (uv - r.xy) / r.zw; }
        fn inside(q: vec2f) -> bool { return all(q >= vec2f(0)) && all(q <= vec2f(1)); }
        fn planeColor(q: vec2f) -> vec3f {
            return textureSampleLevel(atlas,filtering,(clamp(q,vec2f(0),vec2f(1))*63.0+vec2f(0.5))/vec2f(64,67),0).rgb;
        }
        fn channelColor(channel: u32, fraction: f32) -> vec3f {
            return textureSampleLevel(atlas,filtering,vec2f((clamp(fraction,0.0,1.0)*63.0+0.5)/64.0,(64.5+f32(channel))/67.0),0).rgb;
        }
        fn minimum(c: vec3f) -> f32 { return min(c.r,min(c.g,c.b)); }
        fn blocked(pixel: vec2f) -> vec3f {
            let tile = floor(pixel / 8.0);
            return select(vec3f(0.055),vec3f(0.085),((u32(tile.x)+u32(tile.y))%2u)==0u);
        }
        @fragment fn fs(v: Vertex) -> @location(0) vec4f {
            var rgb = vec3f(0.18);
            let q = local(v.uv, p.plane);
            let valid = (p.settings.z < 0.5 || p.settings.z > 1.5 || length(q - vec2f(0.5)) <= 0.5) &&
                (p.settings.z < 1.5 || q.x <= 1.0 - abs(2.0 * q.y - 1.0));
            if (inside(q) && valid) {
                rgb = planeColor(q); let allowed = minimum(rgb) >= 0.0;
                let pixel = vec2f(1) / (p.plane.zw * p.extent.xy);
                let left = minimum(planeColor(q-vec2f(pixel.x,0))) >= 0.0;
                let right = minimum(planeColor(q+vec2f(pixel.x,0))) >= 0.0;
                let up = minimum(planeColor(q-vec2f(0,pixel.y))) >= 0.0;
                let down = minimum(planeColor(q+vec2f(0,pixel.y))) >= 0.0;
                if (!allowed) { rgb = blocked(v.position.xy); }
                if (left != allowed || right != allowed || up != allowed || down != allowed) { rgb = vec3f(0.7); }
                let distance = length((q - p.marker.xy) * p.plane.zw * p.extent.xy);
                if (distance > 4.0 && distance < 7.0) { rgb = select(vec3f(0),vec3f(1),distance < 5.5); }
            } else if (inside(local(v.uv,p.current))) { rgb = p.color.rgb; }
            else {
                let markers = array<f32,3>(p.marker.z,p.marker.w,p.extent.z);
                for (var channel=0u;channel<3u;channel++) {
                    let rect = p.strip + vec4f(0,f32(channel)*0.105,0,0);
                    let bar = local(v.uv,rect);
                    if (!inside(bar)) { continue; }
                    rgb = channelColor(channel,bar.x); let allowed = minimum(rgb) >= 0.0;
                    let pixel = 1.0/(rect.z*p.extent.x);
                    let left = minimum(channelColor(channel,bar.x-pixel)) >= 0.0;
                    let right = minimum(channelColor(channel,bar.x+pixel)) >= 0.0;
                    if (!allowed) { rgb = blocked(v.position.xy); }
                    if (left != allowed || right != allowed) { rgb = vec3f(0.7); }
                    let distance = abs(bar.x-markers[channel])*rect.z*p.extent.x;
                    if (distance < 2.0) { rgb = select(vec3f(0),vec3f(1),distance < 1.0); }
                }
                let swatch = local(v.uv,p.harmony);
                if (inside(swatch) && fract(swatch.x*12.0) > 0.06 && fract(swatch.x*12.0) < 0.94) {
                    let color = p.palette[min(u32(swatch.x*12.0),11u)];
                    rgb = select(blocked(v.position.xy),color.rgb,color.a > 0.5);
                }
            }
            if (p.settings.x > 0.5) { rgb = clamp(rgb,vec3f(0),vec3f(1)); }
            if (p.settings.y > 0.5) {
                rgb = select(12.92 * rgb,1.055 * pow(max(rgb,vec3f(0)),vec3f(1.0/2.4))-0.055,rgb > vec3f(0.0031308));
            }
            return vec4f(rgb,1);
        }
        """;
}
