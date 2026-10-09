#if WINDOWS
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D11on12;
using Vortice.Direct3D12;

namespace Mu3D.Maui.Handlers;

// Share only the expensive bridge, never a page, visual, drawing surface or source texture.
// The registry owns no idle entries. COM references protect native identities until last release.
internal static class WindowsD3D11On12Bridge
{
    private static readonly object registryGate = new();
    private static readonly Dictionary<(nint Device, nint Queue), Entry> entries = [];

    internal static Lease Acquire(nint device, nint queue)
    {
        ArgumentOutOfRangeException.ThrowIfZero(device);
        ArgumentOutOfRangeException.ThrowIfZero(queue);
        lock (registryGate)
        {
            var key = (device, queue);
            if (!entries.TryGetValue(key, out Entry? entry))
            {
                entry = new Entry(device, queue);
                entries.Add(key, entry);
            }
            entry.Users++;
            return new Lease(key, entry);
        }
    }

    internal sealed class Lease : IDisposable
    {
        private readonly (nint Device, nint Queue) key;
        private Entry? entry;

        internal Lease((nint Device, nint Queue) key, Entry entry)
        {
            this.key = key;
            this.entry = entry;
        }

        private Entry Value => entry ?? throw new ObjectDisposedException(nameof(Lease));
        internal object Gate => Value.Gate;
        internal ID3D11Device Device => Value.Device;
        internal ID3D11DeviceContext Context => Value.Context;
        internal ID3D11On12Device On12 => Value.On12;

        public void Dispose()
        {
            Entry? released = Interlocked.Exchange(ref entry, null);
            if (released is null) return;
            bool dispose;
            lock (registryGate)
            {
                dispose = --released.Users == 0;
                if (dispose) entries.Remove(key);
            }
            // Never hold the registry lock while flushing or releasing native resources.
            if (dispose) released.Dispose();
        }
    }

    internal sealed class Entry : IDisposable
    {
        private ID3D12Device? device;
        private ID3D12CommandQueue? queue;
        internal object Gate { get; } = new();
        internal int Users { get; set; }
        internal ID3D11Device Device { get; private set; } = null!;
        internal ID3D11DeviceContext Context { get; private set; } = null!;
        internal ID3D11On12Device On12 { get; private set; } = null!;

        internal Entry(nint devicePointer, nint queuePointer)
        {
            try
            {
                _ = Marshal.AddRef(devicePointer);
                device = new ID3D12Device(devicePointer);
                _ = Marshal.AddRef(queuePointer);
                queue = new ID3D12CommandQueue(queuePointer);
                Vortice.Direct3D11on12.Apis.D3D11On12CreateDevice(device,
                    DeviceCreationFlags.BgraSupport,
                    [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], [queue], 0,
                    out ID3D11Device createdDevice, out ID3D11DeviceContext createdContext,
                    out _).CheckError();
                Device = createdDevice;
                Context = createdContext;
                On12 = Device.QueryInterface<ID3D11On12Device>();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                Context?.ClearState();
                Context?.Flush();
                On12?.Dispose();
                Context?.Dispose();
                Device?.Dispose();
                queue?.Dispose();
                device?.Dispose();
            }
        }
    }
}
#endif
