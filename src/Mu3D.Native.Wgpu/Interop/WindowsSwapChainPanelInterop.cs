namespace Mu3D.Native.Wgpu.Interop;

internal static class WindowsSwapChainPanelInterop
{
    internal static unsafe int DetachSwapChain(nint panelNative)
    {
        ArgumentOutOfRangeException.ThrowIfZero(panelNative);
        nint* vtable = *(nint**)panelNative;
        if (vtable is null || vtable[3] == 0)
        {
            throw new InvalidOperationException(
                "ISwapChainPanelNative does not expose its SetSwapChain ABI entry.");
        }

        delegate* unmanaged[Stdcall]<nint, nint, int> setSwapChain =
            (delegate* unmanaged[Stdcall]<nint, nint, int>)vtable[3];
        return setSwapChain(panelNative, 0);
    }
}
