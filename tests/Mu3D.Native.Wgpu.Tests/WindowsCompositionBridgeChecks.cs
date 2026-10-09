#if WINDOWS
using Mu3D.Maui.Handlers;
using Mu3D.Native.Wgpu;
using Mu3D.Graphics;
using Vortice.Direct3D11;
using Vortice.Direct3D12;
using Vortice.Direct3D11on12;

internal static class WindowsCompositionBridgeChecks
{
    internal static void Verify(WgpuGraphicsDevice device)
    {
        nint nativeDevice = device.NativeD3D12Device;
        nint nativeQueue = device.NativeD3D12CommandQueue;
        if (nativeDevice == 0 || nativeQueue == 0)
        {
            Console.WriteLine("SKIP: composition bridge requires a D3D12 adapter.");
            return;
        }
        var first = WindowsD3D11On12Bridge.Acquire(nativeDevice, nativeQueue);
        using var second = WindowsD3D11On12Bridge.Acquire(nativeDevice, nativeQueue);
        object secondGate = second.Gate;
        if (first.Device.NativePointer != second.Device.NativePointer ||
            first.Context.NativePointer != second.Context.NativePointer ||
            !ReferenceEquals(first.Gate, second.Gate))
            throw new InvalidOperationException("Compatible outputs must share bridge and synchronization.");
        first.Dispose();
        first.Dispose(); // A repeated detach must not release another sink's lease.
        using var target = device.CreateTexture(new(new(8, 8), GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        lock (second.Gate)
        {
            using ID3D12Resource resource = new(AddRef(WgpuGraphicsDevice.GetNativeD3D12Resource(target)));
            using var wrapped = second.On12.CreateWrappedResource<ID3D11Resource>(resource,
                new Vortice.Direct3D11on12.ResourceFlags { BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource },
                ResourceStates.RenderTarget, ResourceStates.RenderTarget);
            second.On12.AcquireWrappedResources([wrapped]);
            second.On12.ReleaseWrappedResources([wrapped]);
            second.Context.Flush();
        }
        Parallel.For(0, 32, _ =>
        {
            using var lease = WindowsD3D11On12Bridge.Acquire(nativeDevice, nativeQueue);
            lock (lease.Gate)
            {
                if (lease.Device.NativePointer != second.Device.NativePointer)
                    throw new InvalidOperationException("Concurrent compatible outputs created another bridge.");
                lease.Context.Flush();
            }
        });
        using (var queueDevice = new ID3D12Device(AddRef(nativeDevice)))
        using (var alternateQueue = queueDevice.CreateCommandQueue(
            new CommandQueueDescription(CommandListType.Direct)))
        using (var isolated = WindowsD3D11On12Bridge.Acquire(nativeDevice, alternateQueue.NativePointer))
        {
            if (ReferenceEquals(isolated.Gate, second.Gate))
                throw new InvalidOperationException("Different queues must not share bridge state.");
        }
        second.Dispose();
        using var recreated = WindowsD3D11On12Bridge.Acquire(nativeDevice, nativeQueue);
        if (ReferenceEquals(recreated.Gate, secondGate))
            throw new InvalidOperationException("Last detach must remove the idle bridge.");
        Console.WriteLine("PASS: native D3D11On12 sharing, wrapped-resource use, concurrency, queue isolation and last-lease release.");

        nint AddRef(nint pointer)
        {
            System.Runtime.InteropServices.Marshal.AddRef(pointer);
            return pointer;
        }
    }
}
#endif
