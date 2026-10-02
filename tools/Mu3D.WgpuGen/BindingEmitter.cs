namespace Mu3D.WgpuGen;

internal static class BindingEmitter
{
    private static readonly string[] RequiredWebGpuTokens =
    [
        "typedef struct WGPUInstanceImpl* WGPUInstance",
        "typedef struct WGPUAdapterImpl* WGPUAdapter",
        "typedef struct WGPUDeviceImpl* WGPUDevice",
        "typedef struct WGPUSurfaceImpl* WGPUSurface",
        "typedef struct WGPURequestAdapterCallbackInfo",
        "typedef struct WGPURequestDeviceCallbackInfo",
        "WGPU_EXPORT WGPUInstance wgpuCreateInstance",
        "WGPU_EXPORT WGPUFuture wgpuInstanceRequestAdapter",
        "WGPU_EXPORT WGPUFuture wgpuAdapterRequestDevice",
        "WGPU_EXPORT WGPUSurface wgpuInstanceCreateSurface",
    ];

    public static IReadOnlyList<string> ValidateHeaders(string webGpuHeaderPath, string wgpuHeaderPath)
    {
        List<string> errors = [];
        string webGpuHeader = File.ReadAllText(webGpuHeaderPath);
        string wgpuHeader = File.ReadAllText(wgpuHeaderPath);

        foreach (string token in RequiredWebGpuTokens)
        {
            if (!webGpuHeader.Contains(token, StringComparison.Ordinal))
            {
                errors.Add($"webgpu.h does not contain required ABI declaration: {token}");
            }
        }

        if (!wgpuHeader.Contains("#include \"webgpu.h\"", StringComparison.Ordinal))
        {
            errors.Add("wgpu.h is not the expected native extension header for webgpu.h.");
        }

        if (!wgpuHeader.Contains("typedef enum WGPUNativeSType", StringComparison.Ordinal))
        {
            errors.Add("wgpu.h does not declare the wgpu-native extension type range.");
        }

        return errors;
    }

}
