using System.Diagnostics;
using System.Text;
using Mu3D.Maui.Toolkit.Controls;
using static Mu3D.GalleryApp.Pages.ProductFeedPolicy;

namespace Mu3D.GalleryApp.Pages;

public partial class ProductFeedPage
{
    private const double DiagnosticsLineHeight = 18d;
    private readonly Stopwatch animationClock = Stopwatch.StartNew();
    private readonly HashSet<int> realizedIndicesScratch = new(MaximumDemoProductCount);
    private readonly List<int> diagnosticsOrderScratch = new(MaximumDemoProductCount);
    private readonly StringBuilder diagnosticsBuilder = new(MaximumDemoProductCount * 80);

    private static double LogicalUnitsToCentimeters(double logicalUnits) =>
        logicalUnits / CentimetersToLogicalUnits(1d);

    private void OnProxyLoaded(object? sender, EventArgs e)
    {
        _ = e;
        TrackRealizedProxy(sender as SceneViewProxy);
    }

    private void OnProxyUnloaded(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is SceneViewProxy proxy)
        {
            UntrackRealizedProxy(proxy);
            QueueStatusUpdate();
        }
    }

    private void OnProxyBindingContextChanged(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is SceneViewProxy proxy && proxy.IsLoaded)
        {
            TrackRealizedProxy(proxy);
        }
    }

    private void TrackRealizedProxy(SceneViewProxy? proxy)
    {
        if (proxy is null)
        {
            return;
        }

        UntrackRealizedProxy(proxy);
        if (proxy.BindingContext is ProductPreview product)
        {
            realizedProxyIndices[proxy] = product.Index;
        }
        QueueStatusUpdate();
    }

    private void UntrackRealizedProxy(SceneViewProxy proxy)
    {
        if (!realizedProxyIndices.Remove(proxy, out int oldIndex))
        {
            return;
        }
        if (!realizedProxyIndices.ContainsValue(oldIndex))
        {
            presentedIndices.Remove(oldIndex);
        }
    }

    private void OnProxyFramePresented(object? sender, SceneViewProxyFramePresentedEventArgs e)
    {
        _ = e;
        if (sender is SceneViewProxy proxy && proxy.BindingContext is ProductPreview product)
        {
            realizedProxyIndices[proxy] = product.Index;
            presentedIndices.Add(product.Index);
            if (isFeedActive &&
                visibleIndices.Contains(product.Index) &&
                product.IsLive &&
                product.AdvanceAnimation(animationClock.Elapsed))
            {
                proxy.InvalidateScene();
            }
        }
        var renderer = ProxyHost.Renderer;
        cachedGeometryCount = renderer?.CachedGeometryCount ?? 0;
        cachedMeshCount = renderer?.CachedMeshCount ?? 0;
        QueueStatusUpdate();
    }

    private void QueueVisibleAnimatedFrames()
    {
        foreach ((SceneViewProxy proxy, int index) in realizedProxyIndices)
        {
            if (visibleIndices.Contains(index) &&
                Products[index].Model.AnimatesRotation &&
                Products[index].IsLive)
            {
                proxy.InvalidateScene();
            }
        }
    }

    private void UpdateDiagnostics()
    {
        realizedIndicesScratch.Clear();
        foreach (int index in realizedProxyIndices.Values)
        {
            realizedIndicesScratch.Add(index);
        }
        diagnosticsOrderScratch.Clear();
        for (int index = 0; index < Products.Count; index++)
        {
            diagnosticsOrderScratch.Add(index);
        }
        SortDiagnosticsByDistance();
        diagnosticsBuilder.Clear();
        diagnosticsBuilder.AppendLine(
            "卡片  距视口       MAUI  内容  首帧  策略状态（当前视口优先；距离为布局估算）");
        foreach (int index in diagnosticsOrderScratch)
        {
            ProductPreview product = Products[index];
            double signedDistance = index < signedCardDistances.Length
                ? signedCardDistances[index]
                : double.NaN;
            string policy = visibleIndices.Contains(index)
                ? "visible"
                : loadingIndices.Contains(index)
                    ? "loading"
                    : preloadIndices.Contains(index)
                        ? "preload"
                        : promotedIndices.Contains(index)
                            ? "promoted"
                            : warmIndices.Contains(index)
                                ? "warm"
                                : "cold";
            diagnosticsBuilder.Append('#').Append(index + 1).Append(' ');
            AppendDiagnosticDistance(signedDistance);
            diagnosticsBuilder
                .Append(realizedIndicesScratch.Contains(index) ? "已创建" : "未创建").Append(' ')
                .Append(product.Content is null ? "无" : "有").Append("    ")
                .Append(presentedIndices.Contains(index) ? "完成" : "未完成").Append("  ")
                .Append(policy).Append(" / ").AppendLine(product.StateText);
        }
        DiagnosticsLabel.Text = diagnosticsBuilder.ToString();
        // Keep a deterministic content height inside the bidirectional ScrollView. The Label uses
        // WordWrap so UIKit keeps unlimited lines and honors every explicit row newline.
        DiagnosticsLabel.HeightRequest =
            ((Products.Count + 1) * DiagnosticsLineHeight) + 12d;
    }

    private void AppendDiagnosticDistance(double signedDistance)
    {
        int startingLength = diagnosticsBuilder.Length;
        if (double.IsNaN(signedDistance))
        {
            diagnosticsBuilder.Append("未测量");
        }
        else if (signedDistance == 0d)
        {
            diagnosticsBuilder.Append("屏幕内");
        }
        else
        {
            diagnosticsBuilder
                .Append(signedDistance < 0d ? '上' : '下')
                .Append($"{LogicalUnitsToCentimeters(Math.Abs(signedDistance)):0.0}")
                .Append("cm");
        }

        int written = diagnosticsBuilder.Length - startingLength;
        diagnosticsBuilder.Append(' ', Math.Max(1, 10 - written));
    }

    private void SortDiagnosticsByDistance()
    {
        for (int index = 1; index < diagnosticsOrderScratch.Count; index++)
        {
            int candidate = diagnosticsOrderScratch[index];
            int position = index;
            while (position > 0 &&
                CompareDiagnosticIndices(candidate, diagnosticsOrderScratch[position - 1]) < 0)
            {
                diagnosticsOrderScratch[position] = diagnosticsOrderScratch[position - 1];
                position--;
            }
            diagnosticsOrderScratch[position] = candidate;
        }
    }

    private int CompareDiagnosticIndices(int left, int right)
    {
        int comparison = Math.Abs(signedCardDistances[left])
            .CompareTo(Math.Abs(signedCardDistances[right]));
        return comparison != 0 ? comparison : left.CompareTo(right);
    }
}
