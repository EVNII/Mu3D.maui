using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mu3D.Web.Validation;

// Tests the .NET/Emscripten link and callback mechanism; this is not a WebGPU backend.
internal static partial class NativeProbe
{
    [LibraryImport("mu3d_web_probe", EntryPoint = "mu3d_web_pointer_size")]
    private static partial int PointerSize();

    [LibraryImport("mu3d_web_probe", EntryPoint = "mu3d_web_callback")]
    private static unsafe partial void Schedule(delegate* unmanaged[Cdecl]<int, nint, void> callback, nint state);

    internal static async Task VerifyAsync()
    {
        if (PointerSize() != 4) throw new InvalidOperationException("Native object is not wasm32.");
        TaskCompletionSource<int> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GCHandle handle = GCHandle.Alloc(completion);
        // Callback owns the handle after successful scheduling, including a timed-out caller.
        try
        {
            unsafe { Schedule(&Complete, GCHandle.ToIntPtr(handle)); }
        }
        catch
        {
            handle.Free();
            throw;
        }
        int result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (result != 42) throw new InvalidOperationException("Native callback payload changed.");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Complete(int value, nint state)
    {
        GCHandle handle = GCHandle.FromIntPtr(state);
        TaskCompletionSource<int> completion = (TaskCompletionSource<int>)handle.Target!;
        handle.Free();
        completion.TrySetResult(value);
    }
}
