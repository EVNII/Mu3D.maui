using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mu3D.Web.Validation;

[assembly: DisableRuntimeMarshalling]

try
{
    await EmdawnProbe.RunAsync();
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

namespace Mu3D.Web.Validation
{
    internal static partial class EmdawnProbe
    {
        [LibraryImport("mu3d_emdawn_probe", EntryPoint = "mu3d_emdawn_create")]
        private static partial nint Create();
        [LibraryImport("mu3d_emdawn_probe", EntryPoint = "mu3d_emdawn_release")]
        private static partial void Release(nint instance);
        [LibraryImport("mu3d_emdawn_probe", EntryPoint = "mu3d_emdawn_has_gpu")]
        private static partial int HasGpu();
        [LibraryImport("mu3d_emdawn_probe", EntryPoint = "mu3d_emdawn_request")]
        private static partial void Request(nint instance, nint callback, nint state);

        internal static async Task RunAsync()
        {
            nint instance = Create();
            if (instance == 0) throw new InvalidOperationException("Emdawn instance creation failed.");
            try
            {
                bool hasGpu = HasGpu() != 0;
                TaskCompletionSource<(int, string)> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                GCHandle handle = GCHandle.Alloc(completion);
                try
                {
                    unsafe
                    {
                        Request(instance, (nint)(delegate* unmanaged[Cdecl]<int, byte*, nuint, nint, void>)&Complete,
                            GCHandle.ToIntPtr(handle));
                    }
                }
                catch { handle.Free(); throw; }
                (int status, string message) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Console.WriteLine($"Actual Emdawn RequestAdapter: navigator.gpu={hasGpu}, status={status}, message='{message}'");
                if ((!hasGpu && (status != 3 || !message.Contains("navigator.gpu", StringComparison.Ordinal))) ||
                    (hasGpu && status != 1))
                    throw new InvalidOperationException("Unexpected Emdawn adapter result.");
                Console.WriteLine(hasGpu
                    ? "Emdawn/.NET WASM adapter creation passed; drawing and HDR presentation not tested."
                    : "Emdawn/.NET WASM link and real GPU-unavailable callback passed; no GPU execution.");
            }
            finally { Release(instance); }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe void Complete(int status, byte* message, nuint length, nint state)
        {
            GCHandle handle = GCHandle.FromIntPtr(state);
            TaskCompletionSource<(int, string)> completion = (TaskCompletionSource<(int, string)>)handle.Target!;
            handle.Free();
            completion.TrySetResult((status, System.Text.Encoding.UTF8.GetString(message, checked((int)length))));
        }
    }
}
