using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mu3D.Web.Validation;
using Mu3D.GalleryApp.Web.Infrastructure;

[assembly: SupportedOSPlatform("browser")]

try
{
    await RenderProbe.InitializeAsync();
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

namespace Mu3D.Web.Validation
{
    // Export only host sizing/display inputs. Scene, drawing and GPU transfers stay in Mu3D/WASM.
    internal static partial class RenderProbe
    {
        private static RenderSession? session;
        private static bool drawing, stopping;

        internal static async Task InitializeAsync()
        {
            session = await RenderSession.OpenAsync();
            Console.WriteLine("Real browser GPU device ready; shared Mu3D SceneRenderer and WGPU adapter active.");
        }

        [JSExport]
        public static int GetMaximumDimension() => session?.MaximumDimension ?? 0;

        [JSExport]
        public static async Task<string> RenderFrame(int width, int height, double exposure, bool hdr, bool refreshSurface)
        {
            if (drawing || stopping) throw new InvalidOperationException("The browser host must serialize rendering.");
            RenderSession active = session ?? throw new InvalidOperationException("Renderer is not initialized.");
            drawing = true;
            try
            {
                RenderFrameReport result = await active.DrawAsync(width, height, (float)exposure, hdr, refreshSurface);
                return JsonSerializer.Serialize(result, RenderJsonContext.Default.RenderFrameReport);
            }
            finally
            {
                drawing = false;
                if (stopping) { session?.Dispose(); session = null; }
            }
        }

        [JSExport]
        public static async Task<string> RenderViewerFrame(int width, int height, double exposure,
            bool hdr, bool refreshSurface, string inputJson)
        {
            if (drawing || stopping) throw new InvalidOperationException("The browser host must serialize rendering.");
            RenderSession active = session ?? throw new InvalidOperationException("Renderer is not initialized.");
            ViewerBenchmark? measurement = benchmark;
            drawing = true;
            try
            {
                measurement?.BeginFrame();
                ViewerInput input = JsonSerializer.Deserialize(inputJson, RenderJsonContext.Default.ViewerInput)
                    ?? throw new ArgumentException("Viewer input is missing.", nameof(inputJson));
                measurement?.Mark(ViewerBenchmarkStage.InputJson);
                ViewerFrameReport result = await active.DrawViewerAsync(width, height, (float)exposure, hdr, refreshSurface, input, measurement);
                string json = JsonSerializer.Serialize(result, RenderJsonContext.Default.ViewerFrameReport);
                measurement?.Mark(ViewerBenchmarkStage.OutputJson);
                measurement?.CompleteFrame();
                return json;
            }
            finally
            {
                drawing = false;
                if (stopping) { session?.Dispose(); session = null; }
            }
        }

        [JSExport]
        public static bool BeginViewerDrag(double x, double y) =>
            !drawing && !stopping && (session?.BeginViewerDrag((float)x, (float)y) ?? false);

        [JSExport]
        public static void UpdateViewerDrag(double x, double y) { if (!stopping) session?.UpdateViewerDrag((float)x, (float)y); }

        [JSExport]
        public static void EndViewerDrag(bool cancel) => session?.EndViewerDrag(cancel);

        [JSExport]
        public static void ConfigureViewerGizmo(int mode, bool local, bool enabled, bool resetModel) =>
            session?.ConfigureViewerGizmo(mode, local, enabled, resetModel);

        [JSExport]
        public static string GetViewerNodeCatalog() => JsonSerializer.Serialize(
            (session ?? throw new InvalidOperationException("Renderer is not initialized.")).NodeCatalog,
            RenderJsonContext.Default.ViewerNodeCatalog);

        [JSExport]
        public static string ConfigureViewerNodeAnchor(string id, string target, double x, double y, double z) =>
            JsonSerializer.Serialize(
                (session ?? throw new InvalidOperationException("Renderer is not initialized."))
                    .ConfigureNodeAnchor(id, target, x, y, z),
                NodeAnchorJsonContext.Default.ViewerNodeAnchorRegistrationReport);

        [JSExport]
        public static double RemoveViewerNodeAnchor(string id) =>
            (session ?? throw new InvalidOperationException("Renderer is not initialized.")).RemoveNodeAnchor(id);

        [JSExport]
        public static string HitTestViewerSelection(double x, double y, double mask)
        {
            if (!double.IsFinite(mask) || mask < 0 || mask > uint.MaxValue || mask != Math.Truncate(mask))
                throw new ArgumentOutOfRangeException(nameof(mask));
            return JsonSerializer.Serialize(
                (session ?? throw new InvalidOperationException("Renderer is not initialized.")).HitTestSelection(x, y, (uint)mask),
                RenderJsonContext.Default.ViewerSelectionCandidates);
        }

        [JSExport]
        public static string SelectViewerNode(string token)
        {
            if (drawing || stopping) throw new InvalidOperationException("Selection changes must occur between viewer frames.");
            return JsonSerializer.Serialize(
                (session ?? throw new InvalidOperationException("Renderer is not initialized.")).SelectNode(token),
                RenderJsonContext.Default.ViewerSelectionReport);
        }

        [JSExport]
        public static void ConfigureViewerHighlight(bool enabled) => session?.ConfigureHighlight(enabled);

        [JSExport]
        public static void ConfigureViewerStatistics(bool enabled, double intervalMilliseconds,
            double drawCallCount, double primitiveCount) =>
            (session ?? throw new InvalidOperationException("Renderer is not initialized."))
                .ConfigureStatistics(new(enabled, intervalMilliseconds,
                    OptionalStatisticsCount(drawCallCount), OptionalStatisticsCount(primitiveCount)));

        private static long? OptionalStatisticsCount(double value)
        {
            if (value == -1) return null;
            if (!double.IsFinite(value) || value < 0 || value > 9_007_199_254_740_991 ||
                value != Math.Truncate(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            return (long)value;
        }

        [JSExport]
        public static string GetViewerStatisticsSnapshot() => JsonSerializer.Serialize(
            (session ?? throw new InvalidOperationException("Renderer is not initialized.")).StatisticsReport,
            StatisticsJsonContext.Default.ViewerStatisticsReport);

        [JSExport]
        public static string ResetViewerStatistics() => JsonSerializer.Serialize(
            (session ?? throw new InvalidOperationException("Renderer is not initialized.")).ResetStatistics(),
            StatisticsJsonContext.Default.ViewerStatisticsReport);

        [JSExport]
        public static string PublishViewerStatistics() => JsonSerializer.Serialize(
            (session ?? throw new InvalidOperationException("Renderer is not initialized.")).PublishStatistics(),
            StatisticsJsonContext.Default.ViewerStatisticsReport);

        [JSExport]
        public static void Shutdown()
        {
            stopping = true;
            if (!drawing) { session?.Dispose(); session = null; }
        }
    }

    [JsonSerializable(typeof(RenderFrameReport))]
    [JsonSerializable(typeof(ViewerInput))]
    [JsonSerializable(typeof(ViewerFrameReport))]
    [JsonSerializable(typeof(ViewerBenchmarkReport))]
    [JsonSerializable(typeof(ViewerNodeCatalog))]
    [JsonSerializable(typeof(ViewerSelectionCandidates))]
    [JsonSerializable(typeof(ViewerSelectionReport))]
    internal partial class RenderJsonContext : JsonSerializerContext;

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ViewerNodeAnchorRegistrationReport))]
    internal partial class NodeAnchorJsonContext : JsonSerializerContext;

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ViewerStatisticsReport))]
    internal partial class StatisticsJsonContext : JsonSerializerContext;

}
