using Mu3D.Color;
using Mu3D.Creative;

namespace Mu3D.GalleryApp.Examples;

// Same bounded ACEScg document and deterministic dabs on both native and browser hosts.
internal sealed class HdrCanvasExample
{
    private readonly HdrCanvas canvas = new(256, 256, StandardColorSpaces.AcesCg,
        new HdrCanvasOptions { MaxStorageBytes = 1024 * 1024, MaxOperationBytes = 1024 * 1024 });
    private int dab;

    internal void AddDab()
    {
        canvas.ApplyDab(new BrushDab(32 + dab * 37 % 192, 48 + dab * 53 % 160, 35,
            new LinearRgba(4, 0.6f, 0.15f, 0.75f, StandardColorSpaces.AcesCg), hardness: 0.2f));
        dab++;
    }
    internal void Reset()
    {
        canvas.Clear();
        canvas.Fill(new CanvasRegion(0, 0, 256, 256),
            new LinearRgba(0.025f, 0.08f, 0.2f, 1, StandardColorSpaces.AcesCg));
        dab = 0;
    }
    internal LinearRgbaImage Publish(out string status)
    {
        int changed = canvas.GetDirtyRegions().Count;
        var image = canvas.Snapshot(name: "HDR canvas snapshot");
        canvas.ClearDirtyRegions();
        status = $"{canvas.Precision} · ACEScg · {canvas.TileCount} tiles · {changed} changed regions · {canvas.AllocatedStorageBytes:N0} bytes. Dabs preserve values above 1.";
        return image;
    }
}
