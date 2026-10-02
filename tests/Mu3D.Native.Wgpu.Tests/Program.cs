using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Native.Wgpu.Interop;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Gizmos;
using Mu3D.Toolkit.Helpers;
using Mu3D.Toolkit.Rendering;

namespace Mu3D.Native.Wgpu.Tests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["native-lifecycle", ..])
        {
            bool forceFallbackAdapter = args.Contains("--force-fallback", StringComparer.Ordinal);
            return await RunNativeLifecycleAsync(forceFallbackAdapter).ConfigureAwait(false);
        }
        if (args is ["native-graphics", ..])
        {
            bool forceFallbackAdapter = args.Contains("--force-fallback", StringComparer.Ordinal);
            return await RunNativeGraphicsAsync(forceFallbackAdapter).ConfigureAwait(false);
        }
        if (args is ["native-gltf", ..])
        {
            bool forceFallbackAdapter = args.Contains("--force-fallback", StringComparer.Ordinal);
            return await RunNativeGltfAsync(forceFallbackAdapter).ConfigureAwait(false);
        }
        if (args is ["native-gltf-surface", ..])
        {
            return await RunNativeGltfSurfaceAsync().ConfigureAwait(false);
        }
        if (args is ["native-d3d12-composition", ..])
        {
            return await RunNativeD3D12CompositionAsync().ConfigureAwait(false);
        }

        return await RunManagedAbiTestsAsync().ConfigureAwait(false);
    }

    private static async Task<int> RunManagedAbiTestsAsync()
    {
        List<string> failures = [];
        Expect(sizeof(WGPUInstanceImpl*) == IntPtr.Size, "WGPUInstance pointer size", failures);
        Expect(sizeof(WGPUAdapterImpl*) == IntPtr.Size, "WGPUAdapter pointer size", failures);
        Expect(sizeof(WGPUDeviceImpl*) == IntPtr.Size, "WGPUDevice pointer size", failures);
        Expect(sizeof(WGPUSurfaceImpl*) == IntPtr.Size, "WGPUSurface pointer size", failures);
        ExpectSize<WGPUFuture>(sizeof(ulong), failures);
        ExpectSize<WGPUStringView>(IntPtr.Size * 2, failures);

        int callbackInfoSize = (IntPtr.Size * 4) + (IntPtr.Size == 8 ? 8 : 4);
        ExpectSize<WGPURequestAdapterCallbackInfo>(callbackInfoSize, failures);
        ExpectSize<WGPURequestDeviceCallbackInfo>(callbackInfoSize, failures);
        ExpectSize<WGPUCallbackMode>(sizeof(uint), failures);
        ExpectSize<WGPURequestAdapterStatus>(sizeof(uint), failures);
        ExpectSize<WGPURequestDeviceStatus>(sizeof(uint), failures);

        Assembly assembly = typeof(WgpuNative).Assembly;
        Type[] interopTypes = assembly.GetTypes()
            .Where(type => type.Namespace == "Mu3D.Native.Wgpu.Interop")
            .ToArray();
        int enumCount = interopTypes.Count(type =>
            type.IsEnum && type.Name.StartsWith("WGPU", StringComparison.Ordinal));
        int structCount = interopTypes.Count(type =>
            type.IsValueType &&
            !type.IsEnum &&
            type.Name.StartsWith("WGPU", StringComparison.Ordinal));
        ExpectCount(enumCount, 70, "generated enum", failures);
        ExpectCount(structCount, 137, "generated struct", failures);
        Expect(
            typeof(WgpuNative)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Count(static method =>
                    method.Name.StartsWith("wgpu", StringComparison.Ordinal)) == 231,
            "generated native function count",
            failures);
        Expect(
            WgpuHeaderIdentity.WebGpuHeaderSha256 == "a483031c3fed05ea5dd1c74082a71676c46c5b2b820ccca10da515c033efc997",
            "webgpu.h identity",
            failures);
        Expect(
            WgpuHeaderIdentity.WgpuHeaderSha256 == "7bd23656d394f620a804b1f174444ea17082b6d330a2fca0c0e6b1121ec4b284",
            "wgpu.h identity",
            failures);
        ExpectInvalidOwnedHandles(failures);
        ExpectInvalidProbeTimeout(failures);
        await ExpectCooperativeOwnedHandleCancellationAsync(failures).ConfigureAwait(false);
        ExpectSurfaceOutputNegotiation(failures);
        ExpectNegotiatedAlphaInterfaceMapping(failures);
        ExpectSynchronizedSurfaceUsageSelection(failures);
        ExpectSurfaceFrameStatusMapping(failures);
        ExpectInvalidSurfaceInputs(failures);
        ExpectWindowsSwapChainPanelDetachAbi(failures);
        ExpectGraphicsMappings(failures);
        ExpectDeviceLostForwarding(failures);
        ExpectRuntimeVersionFormatting(failures);

        if (failures.Count == 0)
        {
            Console.WriteLine($"Validated generated raw ABI layouts for {IntPtr.Size * 8}-bit process.");
            return 0;
        }

        foreach (string failure in failures)
        {
            Console.Error.WriteLine($"error: {failure}");
        }

        return 1;
    }

    private static async Task ExpectCooperativeOwnedHandleCancellationAsync(
        List<string> failures)
    {
        TaskCompletionSource<RecordingOwnedHandle> request =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();

        Task<RecordingOwnedHandle?> wait = WgpuSurfaceProbe.TryWaitForOwnedHandleAsync(
            request.Task,
            TimeSpan.FromSeconds(1),
            cancellation.Token);
        cancellation.Cancel();
        RecordingOwnedHandle? result = await wait.ConfigureAwait(false);
        Expect(result is null, "cooperative native request cancellation result", failures);

        RecordingOwnedHandle lateHandle = new();
        request.SetResult(lateHandle);
        try
        {
            await lateHandle.Disposed.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            failures.Add("late native handle was not released after lifecycle cancellation.");
        }
    }

    private static async Task<int> RunNativeLifecycleAsync(bool forceFallbackAdapter)
    {
        try
        {
            using WgpuInstanceHandle instance = CreateInstance();
            using WgpuAdapterHandle adapter = await RequestAdapterAsync(
                instance,
                forceFallbackAdapter).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            using WgpuDeviceHandle device = await RequestDeviceAsync(adapter)
                .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            Console.WriteLine(
                forceFallbackAdapter
                    ? "Created and released a real wgpu instance, fallback adapter, and device."
                    : "Created and released a real wgpu instance, adapter, and device.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: Native wgpu lifecycle failed: {exception}");
            return 1;
        }
    }

    private static async Task<int> RunNativeGraphicsAsync(bool forceFallbackAdapter)
    {
        try
        {
            using WgpuGraphicsDevice device = await WgpuGraphicsDevice.CreateForTestingAsync(
                TimeSpan.FromSeconds(30),
                forceFallbackAdapter,
                forceFallbackAdapter ? WGPUBackendType.D3D12 : WGPUBackendType.Undefined)
                .ConfigureAwait(false);
            WgpuOffscreenTriangleProbeResult result = WgpuOffscreenTriangleProbe.Probe(device);
            if (!result.CommandSubmitted ||
                !result.QueueCompletionObserved ||
                result.DeviceState != GraphicsDeviceState.Active)
            {
                throw new InvalidOperationException($"Unexpected probe result: {result}");
            }
            RunIndexedDepthProbe(device);
            RunTransformGizmoProbe(device);
            RunOutlineHelperProbe(device);
            RunSampledTextureProbe(device);
            RunTextureLoadTransferProbe(device);
            await RunTextureReadbackProbeAsync(device).ConfigureAwait(false);

            Console.WriteLine(
                "Completed RGBA16Float triangle, indexed-depth, transform-gizmo, outline-helper, sampled-cube, texture-load transfer, and texture-copy/readback probes through wgpu-native.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: Native wgpu graphics test failed: {exception}");
            return 1;
        }
    }

    private static void RunIndexedDepthProbe(WgpuGraphicsDevice device)
    {
        const string shaderSource = """
            @group(0) @binding(0) var<uniform> transform: mat4x4f;
            struct VertexInput {
                @location(0) position: vec3f,
                @location(1) color: vec3f,
            };
            struct VertexOutput {
                @builtin(position) position: vec4f,
                @location(0) color: vec3f,
            };
            @vertex fn vs_main(input: VertexInput) -> VertexOutput {
                var output: VertexOutput;
                output.position = transform * vec4f(input.position, 1.0);
                output.color = input.color;
                return output;
            }
            @fragment fn fs_main(input: VertexOutput) -> @location(0) vec4f {
                return vec4f(input.color, 1.0);
            }
            """;
        using GraphicsShaderModule shader = device.CreateShaderModule(
            new GraphicsShaderModuleDescriptor(shaderSource, "indexed depth shader"));
        using GraphicsBindGroupLayout bindGroupLayout = device.CreateBindGroupLayout(
            new GraphicsBindGroupLayoutDescriptor(
                [new GraphicsBindGroupLayoutEntry(
                    0,
                    GraphicsShaderStage.Vertex,
                    GraphicsBufferBindingType.Uniform,
                    64)],
                "indexed probe bind-group layout"));
        using GraphicsPipelineLayout pipelineLayout = device.CreatePipelineLayout(
            new GraphicsPipelineLayoutDescriptor([bindGroupLayout], "indexed probe pipeline layout"));
        float[] transform =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f,
        ];
        using GraphicsBuffer transformBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
            64,
            GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination,
            "indexed probe transform"));
        device.Queue.WriteBuffer(transformBuffer, 0, MemoryMarshal.AsBytes(transform.AsSpan()));
        using GraphicsBindGroup bindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
            bindGroupLayout,
            [new GraphicsBindGroupEntry(0, transformBuffer, 0, 64)],
            "indexed probe bind group"));
        using GraphicsRenderPipeline pipeline = device.CreateRenderPipeline(
            new GraphicsRenderPipelineDescriptor(
                shader,
                "vs_main",
                shader,
                "fs_main",
                GraphicsTextureFormat.Rgba16Float,
                label: "indexed depth pipeline",
                vertexBuffers:
                [
                    new GraphicsVertexBufferLayout(
                        24,
                        [
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 0, 0),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 12, 1),
                        ]),
                ],
                depthStencil: new GraphicsDepthStencilState(GraphicsTextureFormat.Depth32Float),
                layout: pipelineLayout));
        float[] vertices =
        [
            -0.7f, -0.7f, 0.5f, 1f, 0f, 0f,
             0.7f, -0.7f, 0.5f, 0f, 1f, 0f,
             0.0f,  0.7f, 0.5f, 0f, 0f, 1f,
        ];
        ushort[] indices = [0, 1, 2, 0];
        using GraphicsBuffer vertexBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
            checked((ulong)(vertices.Length * sizeof(float))),
            GraphicsBufferUsage.Vertex | GraphicsBufferUsage.CopyDestination,
            "indexed probe vertices"));
        using GraphicsBuffer indexBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
            checked((ulong)(indices.Length * sizeof(ushort))),
            GraphicsBufferUsage.Index | GraphicsBufferUsage.CopyDestination,
            "indexed probe indices"));
        device.Queue.WriteBuffer(vertexBuffer, 0, MemoryMarshal.AsBytes(vertices.AsSpan()));
        device.Queue.WriteBuffer(indexBuffer, 0, MemoryMarshal.AsBytes(indices.AsSpan()));
        GraphicsExtent3D extent = new(16, 16);
        using GraphicsTexture color = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "indexed probe color"));
        using GraphicsTexture depth = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "indexed probe depth"));
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("indexed probe encoder");
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(color),
            "indexed probe pass",
            new GraphicsRenderPassDepthAttachment(depth))))
        {
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, bindGroup);
            pass.SetVertexBuffer(0, vertexBuffer);
            pass.SetIndexBuffer(indexBuffer, GraphicsIndexFormat.Uint16);
            pass.DrawIndexed(3);
        }
        using GraphicsCommandBuffer commands = encoder.Finish("indexed probe commands");
        device.Queue.Submit(commands);
        device.WaitForSubmittedWork("indexed depth probe");
    }

    private static void RunOutlineHelperProbe(WgpuGraphicsDevice device)
    {
        MeshGeometry geometry = new(
            [
                new Vector3(-0.6f, -0.6f, 0f),
                new Vector3(0.6f, -0.6f, 0f),
                new Vector3(0f, 0.6f, 0f),
            ],
            [0u, 1u, 2u]);
        UnlitMaterial material = new(
            new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb));
        Mesh mesh = new(geometry, material, "native outline target");
        Scene scene = new("native outline scene");
        scene.Add(mesh);
        PerspectiveCamera camera = new(aspectRatio: 1f);
        camera.Transform.Position = new Vector3(0f, 0f, 3f);
        GraphicsExtent3D extent = new(32, 32);
        using GraphicsTexture color = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "native outline color"));
        using GraphicsTexture depth = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
            label: "native outline scene depth"));
        ClearOutlineProbeTargets(device, color, depth);
        RenderPassContext context = new(
            scene,
            camera,
            color,
            depth,
            StandardColorSpaces.LinearSrgb,
            colorTargetInitialized: true,
            depthTargetInitialized: true);
        OutlineHelper helper = new() { Target = mesh };
        using (OutlineHelperRenderPass depthPass = new(helper))
        {
            depthPass.Execute(context);
        }
        using (OutlineHelperRenderPass overlayPass = new(
            helper,
            new OutlineHelperRenderStyle(
                OutlineHelperRenderStyle.Default.Color,
                2f,
                OutlineHelperDepthMode.Overlay),
            StandardColorSpaces.LinearSrgb,
            "Native overlay outline"))
        {
            overlayPass.Execute(context);
        }
        device.WaitForSubmittedWork("outline helper probe");
    }

    private static void ClearOutlineProbeTargets(
        WgpuGraphicsDevice device,
        GraphicsTexture color,
        GraphicsTexture depth)
    {
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("outline target clear encoder");
        using (encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(color),
            "outline target clear pass",
            new GraphicsRenderPassDepthAttachment(depth))))
        {
        }
        using GraphicsCommandBuffer commands = encoder.Finish("outline target clear commands");
        device.Queue.Submit(commands);
    }

    private static async Task<int> RunNativeD3D12CompositionAsync()
    {
        try
        {
            using WgpuGraphicsDevice device = await WgpuGraphicsDevice.CreateForTestingAsync(
                TimeSpan.FromSeconds(30),
                forceFallbackAdapter: false,
                WGPUBackendType.D3D12).ConfigureAwait(false);
            RecordingTexturePresentationSink sink = new();
            OutputSettings settings = OutputSettings.Default with
            {
                AlphaMode = SurfaceAlphaMode.Premultiplied,
            };
            using WgpuTexturePresentationSession session =
                WgpuTexturePresentationSession.CreateCompatible(
                    sink,
                    64,
                    48,
                    settings,
                    device);
            PresentationSurfaceFrameStatus status = session.RenderAndPresent(target =>
                ClearCompositionTarget(device, target));
            if (status != PresentationSurfaceFrameStatus.PresentedOptimal ||
                sink.Device == 0 ||
                sink.CommandQueue == 0 ||
                sink.Resource == 0 ||
                sink.AttachCount != 1 ||
                sink.PresentCount != 1 ||
                sink.Format != PresentationFormat.Rgba16Float ||
                sink.AlphaMode != SurfaceAlphaMode.Premultiplied)
            {
                throw new InvalidOperationException(
                    $"Unexpected composition bridge state: {sink}.");
            }

            session.Resize(80, 60);
            if (sink.AttachCount != 2 || sink.DetachCount != 1 ||
                sink.Width != 80 || sink.Height != 60)
            {
                throw new InvalidOperationException(
                    $"Unexpected composition resize state: {sink}.");
            }

            Console.WriteLine(
                "Validated borrowed D3D12 device, queue, and FP16 texture exports through the texture presentation session.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: Native D3D12 composition bridge failed: {exception}");
            return 1;
        }
    }

    private static void ClearCompositionTarget(
        GraphicsDevice device,
        GraphicsTexture target)
    {
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder(
            "D3D12 composition bridge clear");
        using (encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(
                target,
                clearColor: new GraphicsClearColor(0.25f, 0.5f, 1.0f, 0.5f)),
            "D3D12 composition bridge pass")))
        {
        }
        using GraphicsCommandBuffer commands = encoder.Finish(
            "D3D12 composition bridge commands");
        device.Queue.Submit(commands);
    }

    private static nint observedPanelNative;
    private static nint observedSwapChain;

    private static unsafe void ExpectWindowsSwapChainPanelDetachAbi(List<string> failures)
    {
        nint* vtable = (nint*)NativeMemory.Alloc((nuint)(4 * sizeof(nint)));
        nint* instance = (nint*)NativeMemory.Alloc((nuint)sizeof(nint));
        try
        {
            vtable[0] = 0;
            vtable[1] = 0;
            vtable[2] = 0;
            vtable[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, int>)
                &RecordSetSwapChain;
            instance[0] = (nint)vtable;
            observedPanelNative = 0;
            observedSwapChain = -1;

            int result = WindowsSwapChainPanelInterop.DetachSwapChain((nint)instance);
            Expect(result == 0, "SwapChainPanel detach HRESULT", failures);
            Expect(observedPanelNative == (nint)instance, "SwapChainPanel detach self ABI", failures);
            Expect(observedSwapChain == 0, "SwapChainPanel detach null swap chain", failures);
        }
        finally
        {
            NativeMemory.Free(instance);
            NativeMemory.Free(vtable);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int RecordSetSwapChain(nint panelNative, nint swapChain)
    {
        observedPanelNative = panelNative;
        observedSwapChain = swapChain;
        return 0;
    }

    private static async Task<int> RunNativeGltfAsync(bool forceFallbackAdapter)
    {
        try
        {
            using WgpuGraphicsDevice device = await WgpuGraphicsDevice.CreateForTestingAsync(
                TimeSpan.FromSeconds(30),
                forceFallbackAdapter,
                forceFallbackAdapter ? WGPUBackendType.D3D12 : WGPUBackendType.Undefined)
                .ConfigureAwait(false);
            string assetDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "TextureTransformTest");
            await using FileStream source = File.OpenRead(Path.Combine(
                assetDirectory,
                "TextureTransformTest.gltf"));
            GltfAsset asset = await GltfAssetLoader.LoadAsync(
                source,
                new GltfAssetLoadOptions
                {
                    MaximumSourceByteCount = 64 * 1024,
                    MaximumExternalResourceByteCount = 32 * 1024,
                    MaximumTotalExternalResourceByteCount = 64 * 1024,
                    ExternalResourceResolver = (uri, cancellationToken) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return ValueTask.FromResult<Stream>(File.OpenRead(Path.Combine(
                            assetDirectory,
                            uri)));
                    },
                    ImportOptions = new GltfImportOptions
                    {
                        Name = "native TextureTransformTest",
                        GraphicsCapabilities = device.Capabilities,
                    },
                }).ConfigureAwait(false);

            DirectionalLight light = new(
                new LinearRgba(1f, 0.95f, 0.9f, 1f, StandardColorSpaces.LinearSrgb),
                2f,
                "native glTF light");
            light.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(-0.55f, -0.6f, 0f);
            asset.Scene.Add(light);
            await using FileStream environmentStream = File.OpenRead(Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "studio_small_02_1k.hdr"));
            EquirectangularHdrEnvironment environment = RadianceHdrReader.Read(
                environmentStream,
                StandardColorSpaces.LinearSrgb,
                "native glTF studio environment");
            await SceneRenderer.PrepareImageBasedLightingAsync(environment).ConfigureAwait(false);
            asset.Scene.Add(new ImageBasedLight(environment, 0.7f, "native glTF IBL"));
            PerspectiveCamera camera = new(aspectRatio: 1f);
            camera.Transform.Position = new Vector3(0f, 0f, 4f);
            GraphicsTextureFormat[] colorFormats =
            [
                GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureFormat.Bgra8Unorm,
                GraphicsTextureFormat.Bgra8UnormSrgb,
                GraphicsTextureFormat.Rgb10A2Unorm,
            ];
            foreach (GraphicsTextureFormat colorFormat in colorFormats)
            {
                using SceneRenderer renderer = new(device, colorFormat);
                using GraphicsTexture color = device.CreateTexture(new GraphicsTextureDescriptor(
                    new GraphicsExtent3D(512, 512),
                    colorFormat,
                    GraphicsTextureUsage.RenderAttachment,
                    label: $"native external glTF {colorFormat} color"));
                using GraphicsTexture depth = device.CreateTexture(new GraphicsTextureDescriptor(
                    new GraphicsExtent3D(512, 512),
                    GraphicsTextureFormat.Depth32Float,
                    GraphicsTextureUsage.RenderAttachment,
                    label: $"native external glTF {colorFormat} depth"));
                renderer.Render(asset.Scene, camera, color, depth);
                device.WaitForSubmittedWork($"external glTF {colorFormat} rendering probe");
            }

            Console.WriteLine("Rendered TextureTransformTest.gltf with external BIN/PNG resources through wgpu-native in every presentation-capable color format.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: Native external glTF test failed: {exception}");
            return 1;
        }
    }

    private static async Task<int> RunNativeGltfSurfaceAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("error: Native glTF surface test requires Windows.");
            return 1;
        }

        try
        {
            using WindowsHiddenSurfaceWindow window = new(512, 512);
            using WgpuSurfaceSession session = await WgpuSurfaceSession.CreateAsync(
                window.Source,
                512,
                512,
                OutputSettings.Default,
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            string assetDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "TextureTransformTest");
            await using FileStream source = File.OpenRead(Path.Combine(
                assetDirectory,
                "TextureTransformTest.gltf"));
            GltfAsset asset = await GltfAssetLoader.LoadAsync(
                source,
                new GltfAssetLoadOptions
                {
                    MaximumSourceByteCount = 64 * 1024,
                    MaximumExternalResourceByteCount = 32 * 1024,
                    MaximumTotalExternalResourceByteCount = 64 * 1024,
                    ExternalResourceResolver = (uri, cancellationToken) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return ValueTask.FromResult<Stream>(File.OpenRead(Path.Combine(
                            assetDirectory,
                            uri)));
                    },
                    ImportOptions = new GltfImportOptions
                    {
                        Name = "native surface TextureTransformTest",
                        GraphicsCapabilities = session.Device.Capabilities,
                    },
                }).ConfigureAwait(false);
            DirectionalLight light = new(
                new LinearRgba(1f, 0.95f, 0.9f, 1f, StandardColorSpaces.LinearSrgb),
                2f,
                "native surface glTF light");
            light.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(-0.55f, -0.6f, 0f);
            asset.Scene.Add(light);
            await using FileStream environmentStream = File.OpenRead(Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "studio_small_02_1k.hdr"));
            EquirectangularHdrEnvironment environment = RadianceHdrReader.Read(
                environmentStream,
                StandardColorSpaces.LinearSrgb,
                "native surface glTF studio environment");
            await SceneRenderer.PrepareImageBasedLightingAsync(environment).ConfigureAwait(false);
            asset.Scene.Add(new ImageBasedLight(environment, 0.7f, "native surface glTF IBL"));
            PerspectiveCamera camera = new(aspectRatio: 1f);
            camera.Transform.Position = new Vector3(0f, 0f, 4f);
            SceneRenderer? renderer = null;
            GraphicsTexture? depth = null;
            bool reentrantAcquireRejected = false;
            bool dependentResourcesReleased = false;
            try
            {
                for (int frame = 0; frame < 2; frame++)
                {
                    PresentationSurfaceFrameStatus status = session.RenderAndPresent(target =>
                    {
                        if (frame == 0)
                        {
                            try
                            {
                                _ = session.RenderAndPresent(static _ => { });
                            }
                            catch (InvalidOperationException)
                            {
                                reentrantAcquireRejected = true;
                            }
                        }

                        renderer ??= new SceneRenderer(session.Device, target.Descriptor.Format);
                        depth ??= session.Device.CreateTexture(new GraphicsTextureDescriptor(
                            new GraphicsExtent3D(512, 512),
                            GraphicsTextureFormat.Depth32Float,
                            GraphicsTextureUsage.RenderAttachment,
                            label: "native external glTF surface depth"));
                        renderer.Render(asset.Scene, camera, target, depth);
                    });
                    if (status is not (PresentationSurfaceFrameStatus.PresentedOptimal or
                        PresentationSurfaceFrameStatus.PresentedSuboptimal))
                    {
                        throw new InvalidOperationException($"Unexpected surface frame status: {status}.");
                    }
                }
                if (!reentrantAcquireRejected)
                {
                    throw new InvalidOperationException(
                        "A re-entrant surface acquisition was not rejected before reaching wgpu-native.");
                }

                session.DrainAndReleaseDependentResources(() =>
                {
                    depth?.Dispose();
                    depth = null;
                    renderer?.Dispose();
                    renderer = null;
                    dependentResourcesReleased = true;
                });
                try
                {
                    _ = session.RenderAndPresent(static _ => { });
                    throw new InvalidOperationException(
                        "A presentation frame was accepted after dependent-resource release began.");
                }
                catch (ObjectDisposedException)
                {
                }
            }
            finally
            {
                depth?.Dispose();
                renderer?.Dispose();
            }

            if (!dependentResourcesReleased)
            {
                throw new InvalidOperationException(
                    "The dependent-resource release callback was not completed.");
            }

            Console.WriteLine(
                $"Presented TextureTransformTest.gltf twice through a hidden Windows {session.OutputPlan.Output.Format} surface and rejected a late frame after resource release.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: Native external glTF surface test failed: {exception}");
            return 1;
        }
    }

    private static void RunTransformGizmoProbe(WgpuGraphicsDevice device)
    {
        GraphicsTextureFormat[] colorFormats =
        [
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureFormat.Bgra8Unorm,
            GraphicsTextureFormat.Bgra8UnormSrgb,
            GraphicsTextureFormat.Rgba8Unorm,
            GraphicsTextureFormat.Rgba8UnormSrgb,
        ];
        foreach (GraphicsTextureFormat colorFormat in colorFormats)
        {
            RunTransformGizmoProbe(device, colorFormat);
        }
    }

    private static void RunTransformGizmoProbe(
        WgpuGraphicsDevice device,
        GraphicsTextureFormat colorFormat)
    {
        Scene scene = new("native gizmo scene");
        SceneNode target = new("native gizmo target");
        scene.Add(target);
        PerspectiveCamera camera = new(aspectRatio: 1f);
        camera.Transform.Position = new Vector3(0f, 0f, 10f);
        using TransformGizmo gizmo = new(target)
        {
            ScreenSizePixels = 100f,
            IsRotateEnabled = true,
            IsScaleEnabled = true,
        };
        using SceneRenderer renderer = new(device, colorFormat);
        SceneRenderPass scenePass = new(
            renderer,
            new SceneRenderPassOptions(
                GraphicsLoadOperation.Clear,
                new LinearRgba(0f, 0f, 0f, 1f, StandardColorSpaces.LinearSrgb)),
            "native gizmo scene pass");
        using TransformGizmoRenderPass gizmoPass = new(gizmo, "native gizmo handle pass");
        RenderPassPipeline pipeline = new([scenePass, gizmoPass]);
        GraphicsExtent3D extent = new(512, 512);
        using GraphicsTexture color = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            colorFormat,
            GraphicsTextureUsage.RenderAttachment,
            label: "native gizmo color"));
        using GraphicsTexture depth = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "native gizmo depth"));

        RenderPassExecutionResult result = pipeline.Execute(new RenderPassContext(
            scene,
            camera,
            color,
            depth,
            StandardColorSpaces.LinearSrgb));
        if (!result.ColorTargetInitialized || !result.DepthTargetInitialized)
        {
            throw new InvalidOperationException("The native transform-gizmo pipeline lost its attachments.");
        }
        device.WaitForSubmittedWork("transform gizmo probe");
    }

    private static void RunSampledTextureProbe(WgpuGraphicsDevice device)
    {
        using GraphicsTexture cubeTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            new GraphicsExtent3D(1, 1, 6),
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
            label: "sampled probe cube"));
        Half[] texels =
        [
            (Half)1f, (Half)0f, (Half)0f, (Half)1f,
            (Half)0f, (Half)1f, (Half)0f, (Half)1f,
            (Half)0f, (Half)0f, (Half)1f, (Half)1f,
            (Half)2f, (Half)1f, (Half)0f, (Half)1f,
            (Half)0f, (Half)2f, (Half)1f, (Half)1f,
            (Half)1f, (Half)0f, (Half)2f, (Half)1f,
        ];
        device.Queue.WriteTexture(
            cubeTexture,
            0,
            default,
            new GraphicsExtent3D(1, 1, 6),
            MemoryMarshal.AsBytes(texels.AsSpan()),
            bytesPerRow: 8,
            rowsPerImage: 1);
        using GraphicsTextureView cubeView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
            cubeTexture,
            GraphicsTextureViewDimension.Cube,
            arrayLayerCount: 6,
            label: "sampled probe cube view"));
        using GraphicsSampler sampler = device.CreateSampler(new GraphicsSamplerDescriptor(
            magFilter: GraphicsFilterMode.Linear,
            minFilter: GraphicsFilterMode.Linear,
            mipmapFilter: GraphicsFilterMode.Linear,
            label: "sampled probe sampler"));
        using GraphicsBindGroupLayout layout = device.CreateBindGroupLayout(
            new GraphicsBindGroupLayoutDescriptor(
                [
                    new GraphicsBindGroupLayoutEntry(
                        0,
                        GraphicsShaderStage.Fragment,
                        GraphicsSamplerBindingType.Filtering),
                    new GraphicsBindGroupLayoutEntry(
                        1,
                        GraphicsShaderStage.Fragment,
                        GraphicsTextureSampleType.Float,
                        GraphicsTextureViewDimension.Cube),
                ],
                "sampled probe layout"));
        using GraphicsBindGroup bindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
            layout,
            [new GraphicsBindGroupEntry(0, sampler), new GraphicsBindGroupEntry(1, cubeView)],
            "sampled probe bind group"));
        using GraphicsPipelineLayout pipelineLayout = device.CreatePipelineLayout(
            new GraphicsPipelineLayoutDescriptor([layout], "sampled probe pipeline layout"));
        const string shaderSource = """
            @group(0) @binding(0) var cube_sampler: sampler;
            @group(0) @binding(1) var cube_texture: texture_cube<f32>;

            @vertex fn vs_main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
                var positions = array(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
                return vec4f(positions[index], 0.0, 1.0);
            }

            @fragment fn fs_main() -> @location(0) vec4f {
                return textureSample(cube_texture, cube_sampler, normalize(vec3f(1.0, 0.4, 0.2)));
            }
            """;
        using GraphicsShaderModule shader = device.CreateShaderModule(
            new GraphicsShaderModuleDescriptor(shaderSource, "sampled cube shader"));
        using GraphicsRenderPipeline pipeline = device.CreateRenderPipeline(
            new GraphicsRenderPipelineDescriptor(
                shader,
                "vs_main",
                shader,
                "fs_main",
                GraphicsTextureFormat.Rgba16Float,
                layout: pipelineLayout,
                label: "sampled cube pipeline"));
        using GraphicsTexture target = device.CreateTexture(new GraphicsTextureDescriptor(
            new GraphicsExtent3D(4, 4),
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "sampled probe target"));
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("sampled probe encoder");
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
            new GraphicsRenderPassDescriptor(new GraphicsRenderPassColorAttachment(target))))
        {
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, bindGroup);
            pass.Draw(3);
        }
        using GraphicsCommandBuffer commands = encoder.Finish("sampled probe commands");
        device.Queue.Submit(commands);
        device.WaitForSubmittedWork("sampled cube probe");
    }

    private static void RunTextureLoadTransferProbe(WgpuGraphicsDevice device)
    {
        GraphicsExtent3D extent = new(2, 2);
        using GraphicsTexture source = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
            label: "texture-load transfer source"));
        Half[] texels =
        [
            (Half)4f, (Half)0f, (Half)0f, (Half)1f,
            (Half)0f, (Half)3f, (Half)0f, (Half)1f,
            (Half)0f, (Half)0f, (Half)2f, (Half)1f,
            (Half)1f, (Half)1f, (Half)1f, (Half)0f,
        ];
        device.Queue.WriteTexture(
            source,
            0,
            default,
            extent,
            MemoryMarshal.AsBytes(texels.AsSpan()),
            bytesPerRow: 16,
            rowsPerImage: 2);
        using GraphicsTextureView sourceView = device.CreateTextureView(
            new GraphicsTextureViewDescriptor(source, label: "texture-load transfer view"));
        using GraphicsBindGroupLayout layout = device.CreateBindGroupLayout(
            new GraphicsBindGroupLayoutDescriptor(
                [new GraphicsBindGroupLayoutEntry(
                    0,
                    GraphicsShaderStage.Fragment,
                    GraphicsTextureSampleType.UnfilterableFloat,
                    GraphicsTextureViewDimension.TwoD)],
                "texture-load transfer layout"));
        using GraphicsBindGroup bindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
            layout,
            [new GraphicsBindGroupEntry(0, sourceView)],
            "texture-load transfer bind group"));
        using GraphicsPipelineLayout pipelineLayout = device.CreatePipelineLayout(
            new GraphicsPipelineLayoutDescriptor([layout], "texture-load transfer pipeline layout"));
        const string shaderSource = """
            @group(0) @binding(0) var source_texture: texture_2d<f32>;

            @vertex fn vs_main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
                var positions = array(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
                return vec4f(positions[index], 0.0, 1.0);
            }

            @fragment fn fs_main(@builtin(position) position: vec4f) -> @location(0) vec4f {
                return textureLoad(source_texture, vec2i(position.xy), 0);
            }
            """;
        using GraphicsShaderModule shader = device.CreateShaderModule(
            new GraphicsShaderModuleDescriptor(shaderSource, "texture-load transfer shader"));
        using GraphicsRenderPipeline pipeline = device.CreateRenderPipeline(
            new GraphicsRenderPipelineDescriptor(
                shader,
                "vs_main",
                shader,
                "fs_main",
                GraphicsTextureFormat.Rgba16Float,
                layout: pipelineLayout,
                label: "texture-load transfer pipeline"));
        using GraphicsTexture target = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "texture-load transfer target"));
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder(
            "texture-load transfer encoder");
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
            new GraphicsRenderPassDescriptor(
                new GraphicsRenderPassColorAttachment(target),
                "texture-load transfer pass")))
        {
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, bindGroup);
            pass.Draw(3);
        }
        using GraphicsCommandBuffer commands = encoder.Finish("texture-load transfer commands");
        device.Queue.Submit(commands);
        device.WaitForSubmittedWork("texture-load transfer probe");
    }

    private static async Task RunTextureReadbackProbeAsync(WgpuGraphicsDevice device)
    {
        byte[] expected =
        [
            1, 2, 3, 4, 5, 6, 7, 8,
            9, 10, 11, 12, 13, 14, 15, 16,
        ];
        using GraphicsTexture texture = device.CreateTexture(new GraphicsTextureDescriptor(
            new GraphicsExtent3D(2, 2),
            GraphicsTextureFormat.Rgba8Unorm,
            GraphicsTextureUsage.CopySource | GraphicsTextureUsage.CopyDestination,
            label: "native readback texture"));
        device.Queue.WriteTexture(
            texture,
            0,
            default,
            new GraphicsExtent3D(2, 2),
            expected,
            bytesPerRow: 8,
            rowsPerImage: 2);
        using GraphicsTexture copiedTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            new GraphicsExtent3D(2, 2),
            GraphicsTextureFormat.Rgba8Unorm,
            GraphicsTextureUsage.CopySource | GraphicsTextureUsage.CopyDestination,
            label: "native copied texture"));
        using GraphicsBuffer readback = device.CreateBuffer(new GraphicsBufferDescriptor(
            512,
            GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead,
            "native readback buffer"));
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("native readback encoder");
        encoder.CopyTextureToTexture(
            texture,
            0,
            default,
            copiedTexture,
            0,
            default,
            new GraphicsExtent3D(2, 2));
        encoder.CopyTextureToBuffer(
            copiedTexture,
            0,
            default,
            new GraphicsExtent3D(2, 2),
            readback,
            0,
            bytesPerRow: 256,
            rowsPerImage: 2);
        using GraphicsCommandBuffer commands = encoder.Finish("native readback commands");
        device.Queue.Submit(commands);
        byte[] actual = await readback.ReadAsync(0, 512)
            .WaitAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);
        if (!actual.AsSpan(0, 8).SequenceEqual(expected.AsSpan(0, 8)) ||
            !actual.AsSpan(256, 8).SequenceEqual(expected.AsSpan(8, 8)))
        {
            throw new InvalidOperationException(
                "Texture copy/readback did not preserve row-padded RGBA8 bytes.");
        }
    }

    private static unsafe WgpuInstanceHandle CreateInstance() => WgpuBootstrap.CreateInstance();

    private static void ExpectGraphicsMappings(List<string> failures)
    {
        Expect(
            WgpuGraphicsMapper.MapBufferUsage(
                GraphicsBufferUsage.CopySource | GraphicsBufferUsage.Vertex) ==
                (WgpuNative.WGPUBufferUsage_CopySrc | WgpuNative.WGPUBufferUsage_Vertex),
            "graphics buffer usage mapping",
            failures);
        Expect(
            WgpuGraphicsMapper.MapBufferUsage(
                GraphicsBufferUsage.MapRead | GraphicsBufferUsage.CopyDestination) ==
                (WgpuNative.WGPUBufferUsage_MapRead | WgpuNative.WGPUBufferUsage_CopyDst),
            "graphics mapped-read usage mapping",
            failures);
        Expect(
            WgpuGraphicsMapper.MapTextureUsage(
                GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.RenderAttachment) ==
                (WgpuNative.WGPUTextureUsage_TextureBinding | WgpuNative.WGPUTextureUsage_RenderAttachment),
            "graphics texture usage mapping",
            failures);
        Expect(
            WgpuGraphicsMapper.MapTextureFormat(GraphicsTextureFormat.Rgba16Float) ==
                WGPUTextureFormat.RGBA16Float,
            "RGBA16Float texture mapping",
            failures);
        Expect(
            WgpuGraphicsMapper.MapTextureFormat(GraphicsTextureFormat.Bgra8UnormSrgb) ==
                WGPUTextureFormat.BGRA8UnormSrgb &&
            WgpuGraphicsMapper.MapTextureFormat(GraphicsTextureFormat.Rgb10A2Unorm) ==
                WGPUTextureFormat.RGB10A2Unorm,
            "surface fallback texture mappings",
            failures);
        Expect(
            WgpuGraphicsMapper.MapTextureFormat(GraphicsTextureFormat.Bc7RgbaUnormSrgb) ==
                WGPUTextureFormat.BC7RGBAUnormSrgb &&
            WgpuGraphicsMapper.MapTextureFormat(GraphicsTextureFormat.Etc2Rgba8Unorm) ==
                WGPUTextureFormat.ETC2RGBA8Unorm &&
            WgpuGraphicsMapper.MapTextureFormat(GraphicsTextureFormat.Astc6x6UnormSrgb) ==
                WGPUTextureFormat.ASTC6x6UnormSrgb,
            "compressed texture format mappings",
            failures);
        Expect(
            WgpuGraphicsMapper.MapAddressMode(GraphicsAddressMode.MirrorRepeat) ==
                WGPUAddressMode.MirrorRepeat,
            "sampler address mapping",
            failures);
        Expect(
            WgpuGraphicsMapper.MapFilterMode(GraphicsFilterMode.Linear) == WGPUFilterMode.Linear &&
            WgpuGraphicsMapper.MapMipmapFilterMode(GraphicsFilterMode.Linear) == WGPUMipmapFilterMode.Linear,
            "sampler filter mapping",
            failures);
        Expect(
            WgpuGraphicsMapper.MapPrimitiveTopology(GraphicsPrimitiveTopology.TriangleList) ==
                WGPUPrimitiveTopology.TriangleList &&
            WgpuGraphicsMapper.MapCullMode(GraphicsCullMode.Back) == WGPUCullMode.Back,
            "primitive topology mapping",
            failures);
        Expect(
            WgpuGraphicsMapper.MapBlendFactor(GraphicsBlendFactor.OneMinusSourceAlpha) ==
                WGPUBlendFactor.OneMinusSrcAlpha &&
            WgpuGraphicsMapper.MapBlendOperation(GraphicsBlendOperation.ReverseSubtract) ==
                WGPUBlendOperation.ReverseSubtract,
            "blend state mapping",
            failures);
        Expect(
            WgpuGraphicsMapper.MapVertexFormat(GraphicsVertexFormat.Float32x3) ==
                WGPUVertexFormat.Float32x3 &&
            WgpuGraphicsMapper.MapVertexStepMode(GraphicsVertexStepMode.Instance) ==
                WGPUVertexStepMode.Instance &&
            WgpuGraphicsMapper.MapIndexFormat(GraphicsIndexFormat.Uint16) ==
                WGPUIndexFormat.Uint16,
            "vertex and index mapping",
            failures);
        Expect(
            WgpuGraphicsMapper.MapCompareFunction(GraphicsCompareFunction.LessEqual) ==
                WGPUCompareFunction.LessEqual,
            "depth comparison mapping",
            failures);
        Expect(
            WgpuGraphicsMapper.MapShaderStages(
                GraphicsShaderStage.Vertex | GraphicsShaderStage.Fragment) ==
                (WgpuNative.WGPUShaderStage_Vertex | WgpuNative.WGPUShaderStage_Fragment) &&
            WgpuGraphicsMapper.MapBufferBindingType(GraphicsBufferBindingType.Uniform) ==
                WGPUBufferBindingType.Uniform &&
            WgpuGraphicsMapper.MapSamplerBindingType(GraphicsSamplerBindingType.Filtering) ==
                WGPUSamplerBindingType.Filtering &&
            WgpuGraphicsMapper.MapSamplerBindingType(GraphicsSamplerBindingType.Comparison) ==
                WGPUSamplerBindingType.Comparison &&
            WgpuGraphicsMapper.MapTextureSampleType(GraphicsTextureSampleType.Float) ==
                WGPUTextureSampleType.Float &&
            WgpuGraphicsMapper.MapTextureSampleType(GraphicsTextureSampleType.Depth) ==
                WGPUTextureSampleType.Depth &&
            WgpuGraphicsMapper.MapTextureViewDimension(GraphicsTextureViewDimension.Cube) ==
                WGPUTextureViewDimension.Cube,
            "bind-group layout mapping",
            failures);
        Expect(
            WgpuGraphicsMapper.MapLoadOperation(GraphicsLoadOperation.Clear) == WGPULoadOp.Clear &&
            WgpuGraphicsMapper.MapStoreOperation(GraphicsStoreOperation.Store) == WGPUStoreOp.Store,
            "render attachment operation mapping",
            failures);
    }

    private static void ExpectDeviceLostForwarding(List<string> failures)
    {
        WgpuDeviceLostSink sink = new();
        sink.Report("pending loss");
        List<string> reasons = [];
        sink.Handler = reasons.Add;
        sink.Report("live loss");
        Expect(
            reasons.SequenceEqual(["pending loss", "live loss"]),
            "device-loss callback forwarding",
            failures);
    }

    private static void ExpectRuntimeVersionFormatting(List<string> failures)
    {
        Expect(
            WgpuBackendInfo.FormatRuntimeVersion(0) == "unavailable (native returned 0)",
            "zero native runtime version is unavailable",
            failures);
        Expect(
            WgpuBackendInfo.FormatRuntimeVersion(0x1D000101) == "29.0.1.1",
            "four-component native runtime version formatting",
            failures);
    }


    private static unsafe Task<WgpuDeviceHandle> RequestDeviceAsync(WgpuAdapterHandle adapter) =>
        WgpuBootstrap.RequestDeviceAsync(adapter);

    private static unsafe Task<WgpuAdapterHandle> RequestAdapterAsync(
        WgpuInstanceHandle instance,
        bool forceFallbackAdapter)
    {
        WGPURequestAdapterOptions options = new()
        {
            featureLevel = WGPUFeatureLevel.Core,
            powerPreference = forceFallbackAdapter
                ? WGPUPowerPreference.LowPower
                : WGPUPowerPreference.HighPerformance,
            forceFallbackAdapter = forceFallbackAdapter ? 1u : 0u,
            backendType = forceFallbackAdapter ? WGPUBackendType.D3D12 : WGPUBackendType.Undefined,
        };
        return WgpuBootstrap.RequestAdapterAsync(instance, &options);
    }

    private static void ExpectSize<T>(int expected, List<string> failures)
    {
        int actual = Unsafe.SizeOf<T>();
        if (actual != expected)
        {
            failures.Add($"{typeof(T).Name}: expected size {expected}, got {actual}.");
        }
    }

    private static void Expect(bool condition, string scenario, List<string> failures)
    {
        if (!condition)
        {
            failures.Add($"Failed: {scenario}.");
        }
    }

    private static void ExpectCount(int actual, int expected, string scenario, List<string> failures)
    {
        if (actual != expected)
        {
            failures.Add($"{scenario}: expected {expected}, got {actual}.");
        }
    }

    private static unsafe void ExpectInvalidOwnedHandles(List<string> failures)
    {
        using WgpuInstanceHandle instance = new((WGPUInstanceImpl*)0);
        using WgpuAdapterHandle adapter = new((WGPUAdapterImpl*)0);
        using WgpuDeviceHandle device = new((WGPUDeviceImpl*)0);
        using WgpuSurfaceHandle surface = new((WGPUSurfaceImpl*)0);
        Expect(instance.IsInvalid, "null instance handle is invalid", failures);
        Expect(adapter.IsInvalid, "null adapter handle is invalid", failures);
        Expect(device.IsInvalid, "null device handle is invalid", failures);
        Expect(surface.IsInvalid, "null surface handle is invalid", failures);
    }

    private static void ExpectInvalidProbeTimeout(List<string> failures)
    {
        try
        {
            WgpuDeviceProbe.ProbeAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            failures.Add("device probe accepted a non-positive timeout.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static void ExpectSurfaceOutputNegotiation(List<string> failures)
    {
        SurfaceCapabilities hdr = new(
            [PresentationFormat.Bgra8UnormSrgb, PresentationFormat.Rgba16Float],
            [SurfacePresentMode.Fifo],
            [SurfaceAlphaMode.Opaque],
            SupportsRgba16Float: true,
            []);
        SurfaceOutputPlan hdrPlan = SurfaceOutputNegotiator.Negotiate(hdr, OutputSettings.Default);
        Expect(hdrPlan.Output.Format == PresentationFormat.Rgba16Float, "HDR format preference", failures);
        Expect(
            hdrPlan.Output.Encoding == ColorEncoding.ExtendedSrgbLinear,
            "legacy HDR encoding",
            failures);
        Expect(hdrPlan.Output.HdrHeadroom is null, "unknown HDR headroom stays nullable", failures);
        Expect(!hdrPlan.RequiresToneMapping, "HDR path does not tone map", failures);

        SurfaceCapabilities sdr = hdr with
        {
            Formats = [PresentationFormat.Bgra8UnormSrgb],
            SupportsRgba16Float = false,
        };
        SurfaceOutputPlan clampPlan = SurfaceOutputNegotiator.Negotiate(sdr, OutputSettings.Default);
        Expect(clampPlan.Output.DynamicRange == OutputDynamicRange.Sdr, "explicit SDR fallback state", failures);
        Expect(!clampPlan.RequiresToneMapping, "default fallback does not tone map", failures);

        try
        {
            _ = SurfaceOutputNegotiator.Negotiate(
                sdr,
                OutputSettings.Default with
                {
                    DynamicRange = OutputDynamicRange.Hdr,
                    SdrFallback = SdrFallbackMode.Fail,
                });
            failures.Add("required HDR accepted an SDR-only surface.");
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void ExpectNegotiatedAlphaInterfaceMapping(List<string> failures)
    {
        InterfaceMapping mapping = typeof(WgpuSurfaceSession).GetInterfaceMap(typeof(IPresentationSurfaceSession));
        int getter = Array.FindIndex(mapping.InterfaceMethods, static method => method.Name == "get_AlphaMode");
        Expect(getter >= 0 && mapping.TargetMethods[getter].DeclaringType == typeof(WgpuSurfaceSession) &&
            mapping.TargetMethods[getter].Name == "get_AlphaMode",
            "backend-independent session resolves the negotiated native alpha mode rather than the default Unknown", failures);
    }

    private static void ExpectSurfaceFrameStatusMapping(List<string> failures)
    {
        Expect(
            WgpuSurfaceSession.MapFrameStatus(
                WGPUSurfaceGetCurrentTextureStatus.SuccessOptimal) ==
            PresentationSurfaceFrameStatus.PresentedOptimal,
            "optimal surface frame status",
            failures);
        Expect(
            WgpuSurfaceSession.MapFrameStatus(
                WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal) ==
            PresentationSurfaceFrameStatus.PresentedSuboptimal,
            "suboptimal surface frame status",
            failures);
        Expect(
            WgpuSurfaceSession.MapFrameStatus(WGPUSurfaceGetCurrentTextureStatus.Timeout) ==
            PresentationSurfaceFrameStatus.Timeout,
            "surface frame timeout status",
            failures);
        Expect(
            WgpuSurfaceSession.MapFrameStatus(WGPUSurfaceGetCurrentTextureStatus.Outdated) ==
            PresentationSurfaceFrameStatus.Outdated,
            "outdated surface frame status",
            failures);
        Expect(
            WgpuSurfaceSession.MapFrameStatus(WGPUSurfaceGetCurrentTextureStatus.Lost) ==
            PresentationSurfaceFrameStatus.Lost,
            "lost surface frame status",
            failures);
        Expect(
            WgpuSurfaceSession.MapFrameStatus(WGPUSurfaceGetCurrentTextureStatus.Error) ==
            PresentationSurfaceFrameStatus.Error,
            "surface frame error status",
            failures);
    }

    private static void ExpectSynchronizedSurfaceUsageSelection(List<string> failures)
    {
        Expect(
            WgpuSurfaceSession.SelectCompatibleTextureUsage(
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopyDestination,
                GraphicsTextureUsage.CopyDestination,
                GraphicsTextureUsage.RenderAttachment) == GraphicsTextureUsage.CopyDestination,
            "synchronized Surface prefers copy destination when advertised",
            failures);
        Expect(
            WgpuSurfaceSession.SelectCompatibleTextureUsage(
                GraphicsTextureUsage.RenderAttachment,
                GraphicsTextureUsage.CopyDestination,
                GraphicsTextureUsage.RenderAttachment) == GraphicsTextureUsage.RenderAttachment,
            "synchronized Surface falls back to render attachment",
            failures);
        try
        {
            _ = WgpuSurfaceSession.SelectCompatibleTextureUsage(
                GraphicsTextureUsage.TextureBinding,
                GraphicsTextureUsage.CopyDestination,
                GraphicsTextureUsage.RenderAttachment);
            failures.Add("synchronized Surface accepted unsupported preferred and fallback usages.");
        }
        catch (NotSupportedException)
        {
        }
    }

    private static void ExpectInvalidSurfaceInputs(List<string> failures)
    {
        try
        {
            _ = NativeSurfaceSource.FromMetalLayer(0);
            failures.Add("native surface source accepted a zero handle.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        NativeSurfaceSource panel = NativeSurfaceSource.FromWindowsSwapChainPanel(1);
        Expect(
            panel.Kind == NativeSurfaceKind.WindowsSwapChainPanel && panel.Handle == 1,
            "WinUI SwapChainPanel native source",
            failures);

        try
        {
            _ = NativeSurfaceSource.FromWindowsSwapChainPanel(0);
            failures.Add("WinUI surface source accepted a zero interface pointer.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        try
        {
            WgpuSurfaceSession.CreateAsync(
                NativeSurfaceSource.FromMetalLayer(1),
                1,
                1,
                OutputSettings.Default,
                TimeSpan.Zero).GetAwaiter().GetResult();
            failures.Add("surface session accepted a non-positive timeout.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private sealed class RecordingOwnedHandle : IDisposable
    {
        private readonly TaskCompletionSource disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Disposed => disposed.Task;

        public void Dispose() => disposed.TrySetResult();
    }

    private sealed class RecordingTexturePresentationSink : IWgpuTexturePresentationSink
    {
        public SurfaceCapabilities Capabilities { get; } = new(
            [PresentationFormat.Rgba16Float, PresentationFormat.Bgra8Unorm],
            [SurfacePresentMode.Fifo],
            [SurfaceAlphaMode.Premultiplied, SurfaceAlphaMode.Opaque],
            SupportsRgba16Float: true,
            UnmappedBackendFormats: []);

        internal nint Device { get; private set; }

        internal nint CommandQueue { get; private set; }

        internal nint Resource { get; private set; }

        internal uint Width { get; private set; }

        internal uint Height { get; private set; }

        internal PresentationFormat Format { get; private set; }

        internal SurfaceAlphaMode AlphaMode { get; private set; }

        internal int AttachCount { get; private set; }

        internal int PresentCount { get; private set; }

        internal int DetachCount { get; private set; }

        public void Attach(
            nint d3d12Device,
            nint d3d12CommandQueue,
            nint d3d12Resource,
            uint width,
            uint height,
            PresentationFormat format,
            SurfaceAlphaMode alphaMode)
        {
            Device = d3d12Device;
            CommandQueue = d3d12CommandQueue;
            Resource = d3d12Resource;
            Width = width;
            Height = height;
            Format = format;
            AlphaMode = alphaMode;
            AttachCount++;
        }

        public void Present() => PresentCount++;

        public void Detach() => DetachCount++;

        public override string ToString() =>
            $"device=0x{Device:X}, queue=0x{CommandQueue:X}, resource=0x{Resource:X}, " +
            $"size={Width}x{Height}, format={Format}, alpha={AlphaMode}, " +
            $"attach={AttachCount}, present={PresentCount}, detach={DetachCount}";
    }
}
