using System.Text.Json;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;

internal static class BsdfOracleChecks
{
    // No window, presentation surface or platform UI is created. Readback is test-only.
    internal static async Task<int> RunAsync(WgpuGraphicsDevice device)
    {
        string root = AppContext.BaseDirectory;
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "Reference", "oracle.json")));
        JsonElement metadata = oracle.RootElement;
        if (metadata.GetProperty("adobeCommit").GetString() != "c91aad1d1ce1693e803f039d7c92c2965c4eb013" ||
            metadata.GetProperty("openPbrVersion").GetString() != "1.1.1")
        {
            throw new InvalidOperationException("OpenPBR reference fixture version has changed.");
        }
        const uint rows = 30;
        byte[] lutBytes = File.ReadAllBytes(Path.Combine(root, "Shaders", "Lut.bin"));
        if (lutBytes.Length != 72736 * sizeof(float))
        {
            throw new InvalidOperationException("OpenPBR lookup buffer layout has changed.");
        }
        using GraphicsBuffer lut = device.CreateBuffer(new GraphicsBufferDescriptor((ulong)lutBytes.Length,
            GraphicsBufferUsage.Storage | GraphicsBufferUsage.CopyDestination));
        device.Queue.WriteBuffer(lut, 0, lutBytes);
        using GraphicsBindGroupLayout emptyLayout = device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor([]));
        using GraphicsBindGroupLayout lutLayout = device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
            [new GraphicsBindGroupLayoutEntry(0, GraphicsShaderStage.Fragment,
                GraphicsBufferBindingType.ReadOnlyStorage, (ulong)lutBytes.Length)]));
        using GraphicsBindGroup emptyGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(emptyLayout, []));
        using GraphicsBindGroup lutGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(lutLayout,
            [new GraphicsBindGroupEntry(0, lut, 0, (ulong)lutBytes.Length)]));
        using GraphicsPipelineLayout layout = device.CreatePipelineLayout(new GraphicsPipelineLayoutDescriptor([emptyLayout, lutLayout]));
        using GraphicsShaderModule vertex = device.CreateShaderModule(new GraphicsShaderModuleDescriptor(VertexSource));
        using GraphicsShaderModule fragment = device.CreateShaderModule(new GraphicsShaderModuleDescriptor(
            File.ReadAllText(Path.Combine(root, "Shaders", "reference.wgsl")), "Pinned OpenPBR numerical oracle"));
        using GraphicsRenderPipeline pipeline = device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
            vertex, "vs_main", fragment, "main", GraphicsTextureFormat.Rgba32Float, layout: layout));
        using GraphicsTexture output = device.CreateTexture(new GraphicsTextureDescriptor(new GraphicsExtent3D(8, rows),
            GraphicsTextureFormat.Rgba32Float, GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        using GraphicsBuffer readback = device.CreateBuffer(new GraphicsBufferDescriptor(256 * rows,
            GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead));
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("OpenPBR C++ / WGSL numerical comparisons");
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(output))))
        {
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, emptyGroup);
            pass.SetBindGroup(1, lutGroup);
            pass.Draw(3);
            pass.End();
        }
        encoder.CopyTextureToBuffer(output, 0, default, output.Descriptor.Size, readback, 0, 256, rows);
        using GraphicsCommandBuffer commands = encoder.Finish();
        device.Queue.Submit(commands);
        byte[] bytes = await readback.ReadAsync(0, 256 * (int)rows).WaitAsync(TimeSpan.FromSeconds(30));
        int count = 0;
        float absolute = metadata.GetProperty("absoluteTolerance").GetSingle();
        float relative = metadata.GetProperty("relativeTolerance").GetSingle();
        foreach (JsonElement item in metadata.GetProperty("cases").EnumerateArray())
        {
            int id = item.GetProperty("id").GetInt32();
            int component = 0;
            foreach (JsonElement scalar in item.GetProperty("values").EnumerateArray())
            {
                float expected = scalar.GetSingle();
                Compare(Read(id, component), expected, absolute + relative * MathF.Abs(expected),
                    $"case {id} ({item.GetProperty("name").GetString()}) component {component}");
                component++;
            }
        }
        foreach (JsonElement item in metadata.GetProperty("hemisphereQuadrature").GetProperty("cases").EnumerateArray())
        {
            int id = item.GetProperty("id").GetInt32();
            for (int channel = 0; channel < 3; channel++)
            {
                float sum = 0;
                for (int partition = 0; partition < 8; partition++) sum += Read(id + 3, partition * 4 + channel);
                // Each GPU partition accumulates in FP32, whereas C++ uses double for its sum.
                Compare(sum, item.GetProperty("integral")[channel].GetSingle(), 2e-4f, $"furnace {id} channel {channel}");
                Compare(sum, 1f, 0.02f, $"furnace {id} energy channel {channel}");
            }
        }
        foreach (JsonElement item in metadata.GetProperty("volumeTransmittance").EnumerateArray())
        {
            int id = item.GetProperty("id").GetInt32();
            for (int distance = 0; distance < 3; distance++)
            for (int channel = 0; channel < 3; channel++)
            {
                float expected = item.GetProperty("values")[distance][channel].GetSingle();
                Compare(Read(id + 16, distance * 4 + channel), expected, absolute + relative * MathF.Abs(expected),
                    $"volume {id} distance {distance} channel {channel}");
            }
        }
        Console.WriteLine($"OpenPBR pinned C++ / GPU: {count} checks passed (768 closure values, three hemisphere furnaces and volume transmittance).");
        return count;

        float Read(int row, int component) => BitConverter.ToSingle(bytes, row * 256 + component * sizeof(float));

        void Compare(float actual, float expected, float tolerance, string label)
        {
            if (!float.IsFinite(actual) || MathF.Abs(actual - expected) > tolerance)
            {
                throw new InvalidOperationException($"OpenPBR {label}: {actual:G9} versus {expected:G9}, tolerance {tolerance:G9}.");
            }
            count++;
        }
    }

    private const string VertexSource = """
        @vertex fn vs_main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4<f32> {
            let p = vec2<f32>(f32((index << 1u) & 2u), f32(index & 2u));
            return vec4<f32>(p * 2.0 - vec2<f32>(1.0), 0.0, 1.0);
        }
        """;
}
