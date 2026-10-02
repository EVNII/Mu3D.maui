using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Mu3D.GalleryApp.Web.Infrastructure;

namespace Mu3D.Web.Validation;

internal static partial class RenderProbe
{
    private static ViewerBenchmark? benchmark;

    [JSExport]
    public static string GetBenchmarkBuild() =>
#if MU3D_WEB_AOT
        $"Mono AOT / .NET {Environment.Version}";
#else
        $"Mono interpreter / .NET {Environment.Version}";
#endif

    [JSExport]
    public static void BeginBenchmark(int frames)
    {
        if (drawing || stopping || benchmark is not null) throw new InvalidOperationException("Benchmark requires an idle session.");
        benchmark = new(frames);
    }

    [JSExport]
    public static string FinishBenchmark()
    {
        if (drawing || stopping || benchmark is null) throw new InvalidOperationException("No completed benchmark is available.");
        ViewerBenchmarkReport report = benchmark.Finish(GetBenchmarkBuild());
        benchmark = null;
        return JsonSerializer.Serialize(report, RenderJsonContext.Default.ViewerBenchmarkReport);
    }

    // Measures only synchronous string interop. It is not a substitute for the asynchronous frame path.
    [JSExport]
    public static string BenchmarkEcho(string value) => value;
}
