using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Native.Wgpu;

internal static class PresentationWhiteChecks
{
    internal static async Task<int> RunAsync(WgpuGraphicsDevice device)
    {
        int count = 0;
        var settings = OutputSettings.Default;
        Check(settings.WhiteMode == OutputWhiteMode.System, "default system policy");
        Check(PresentationWhite.Resolve(settings, ColorEncoding.ExtendedSrgbLinear, 80, 200, false) == 1,
            "Windows XAML switch disables matching");
        Check(PresentationWhite.Resolve(settings, ColorEncoding.ExtendedSrgbLinear, null, null, false) == 1,
            "Windows switch has no effect on compositor-managed platforms");
        Check(Resolve(settings, 80, 200) == 2.5f, "Windows 200-nit SDR white");
        Check(Resolve(settings, 80, 80) == 1, "Windows native white");
        Check(Resolve(settings, 80, 320) == 4, "live white change");
        Check(Resolve(settings, null, 200) == 1, "compositor-managed platform no double scaling");
        Check(Resolve(settings, null, null) == 1, "unknown mobile nits remain unknown");
        Check(Resolve(settings, 80, null) == 1, "unavailable query pass-through");
        Check(Resolve(settings, 80, float.NaN) == 1, "invalid query pass-through");
        Check(PresentationWhite.Resolve(settings, ColorEncoding.Srgb, 80, 200) == 1, "SDR compositor owns white");
        settings = settings with { WhiteMode = OutputWhiteMode.FixedAbsolute, ReferenceWhiteNits = 100 };
        Check(Resolve(settings, 80, 200) == 1.25f && Resolve(settings, 80, 320) == 1.25f, "fixed 100 nits ignores slider");
        Throws<NotSupportedException>(() => Resolve(settings, null, null));
        Throws<NotSupportedException>(() => PresentationWhite.Resolve(settings, ColorEncoding.Srgb, 80, 200));
        Check(Resolve(settings with { WhiteMode = OutputWhiteMode.PlatformNative }, 80, 320) == 1, "already calibrated opt-out");
        Throws<ArgumentOutOfRangeException>(() => _ = new OutputSettings { WhiteMode = (OutputWhiteMode)99 });
        Throws<ArgumentOutOfRangeException>(() => _ = new OutputSettings { ReferenceWhiteNits = float.NaN });
        Throws<ArgumentOutOfRangeException>(() => _ = new OutputSettings { ReferenceWhiteNits = 0 });

        foreach (var format in new[] { GraphicsTextureFormat.Rgba32Float, GraphicsTextureFormat.Rgba16Float })
        {
        using var pass = new PresentationWhitePass(device, format);
        foreach (uint size in new uint[] { 4, 8 })
        {
            using var target = device.CreateTexture(new(new(size, size), format,
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
            var input = pass.GetInput(target);
            Check(ReferenceEquals(input, pass.GetInput(target)), "stable intermediate reuse");
            foreach (var color in new[] { new Vector4(1, 1, 1, 1), new Vector4(0, 0, 0, 1),
                new Vector4(-.25f, .5f, 4, .5f), Vector4.Zero })
            {
                using (var encoder = device.CreateCommandEncoder())
                {
                    using (encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
                        new GraphicsRenderPassColorAttachment(input, GraphicsLoadOperation.Clear,
                            GraphicsStoreOperation.Store, new GraphicsClearColor(color.X, color.Y, color.Z, color.W))))) { }
                    using var commands = encoder.Finish();
                    device.Queue.Submit(commands);
                }
                foreach (float scale in new[] { 2.5f, 4, 1.25f, 1 })
                {
                    pass.Apply(target, scale);
                    var expected = new Vector4(color.X * scale, color.Y * scale, color.Z * scale, color.W);
                    Check((await ReadAsync(target)).All(p => Vector4.Distance(p, expected) < .00001f),
                        $"GPU scale {scale}, negative/HDR/black/alpha preservation, size {size}");
                }
            }
        }
        }
        foreach (var format in new[] { GraphicsTextureFormat.Rgba32Float, GraphicsTextureFormat.Rgba16Float })
        {
            using var target = device.CreateTexture(new(new(4, 2), format,
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
            using var exposure = new PresentationWhitePass(device, format);
            GraphicsTexture input = exposure.GetInput(target);
            using (var encoder = device.CreateCommandEncoder())
            {
                using (encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassColorAttachment(input, GraphicsLoadOperation.Clear,
                        GraphicsStoreOperation.Store, new GraphicsClearColor(.5f, 2f, 4f, .5f))))) { }
                using var commands = encoder.Finish(); device.Queue.Submit(commands);
            }
            foreach (var sample in new (float Stops, Vector4 Expected)[]
            {
                (-2, new(.125f, .5f, 1f, .5f)),
                (-1, new(.25f, 1f, 2f, .5f)),
                (0, new(.5f, 2f, 4f, .5f)),
                (1, new(1f, 4f, 8f, .5f)),
                (2, new(2f, 8f, 16f, .5f)),
            })
            {
                exposure.Apply(target, MathF.Pow(2f, sample.Stops));
                Check((await ReadAsync(target)).All(pixel => Vector4.Distance(pixel, sample.Expected) < .00001f),
                    $"scene-linear EV {sample.Stops}: HDR ratios and alpha preserved in {format}");
            }
        }
        using (var target = device.CreateTexture(new(new(6, 2), GraphicsTextureFormat.Rgba32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource)))
        using (var white = new PresentationWhitePass(device, GraphicsTextureFormat.Rgba32Float))
        using (var pattern = new Mu3D.Samples.SurfaceReferencePattern(device, GraphicsTextureFormat.Rgba32Float))
        {
            pattern.Draw(device, white.GetInput(target));
            foreach (bool enabled in new[] { false, true })
            {
                float scale = PresentationWhite.Resolve(OutputSettings.Default, ColorEncoding.ExtendedSrgbLinear, 80, 200, enabled);
                white.Apply(target, scale);
                var pixels = await ReadAsync(target);
                float[] bands = [0, .18f, .5f, 1, 2, 4];
                for (int x = 0; x < 6; x++)
                    Check(Vector4.Distance(pixels[6 + x], new Vector4(new Vector3(bands[x] * scale), 1)) < .00001f,
                        "Surface Probe band uses switch-controlled final white scale");
            }
        }
        return count;

        async Task<Vector4[]> ReadAsync(GraphicsTexture texture)
        {
            if (texture.Descriptor.Format == GraphicsTextureFormat.Rgba32Float)
                return await TransportChecks.ReadAsync(device, texture);
            uint width = texture.Descriptor.Size.Width, height = texture.Descriptor.Size.Height;
            using var buffer = device.CreateBuffer(new(256 * height, GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead));
            using var encoder = device.CreateCommandEncoder();
            encoder.CopyTextureToBuffer(texture, 0, default, texture.Descriptor.Size, buffer, 0, 256, height);
            using var commands = encoder.Finish(); device.Queue.Submit(commands);
            byte[] bytes = await buffer.ReadAsync(0, checked((int)height * 256)).WaitAsync(TimeSpan.FromSeconds(30));
            Vector4[] values = new Vector4[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = y * 256 + x * 8;
                values[y * width + x] = new((float)BitConverter.ToHalf(bytes, offset), (float)BitConverter.ToHalf(bytes, offset + 2),
                    (float)BitConverter.ToHalf(bytes, offset + 4), (float)BitConverter.ToHalf(bytes, offset + 6));
            }
            return values;
        }

        static float Resolve(OutputSettings s, float? unit, float? white) =>
            PresentationWhite.Resolve(s, ColorEncoding.ExtendedSrgbLinear, unit, white);
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            count++;
        }
        void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { count++; return; }
            throw new InvalidOperationException($"Expected {typeof(T).Name}");
        }
    }
}
