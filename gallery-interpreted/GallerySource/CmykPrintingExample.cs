using System.Numerics;
using Mu3D.Color;
using Mu3D.Color.Printing;
using Mu3D.GalleryApp.Pages;

namespace Mu3D.GalleryApp.Examples;

internal sealed record CmykProofResult(LinearRgba[] Preview, CmykImage Image,
    int Changed, int Clipped, float TotalDeltaE, float MaximumInk);

// Shared print math. Hosts supply file selection, chart drawing and scheduling.
internal static class CmykPrintingExample
{
    internal static PrintRgbTransform CreateTransform(int target, int mapping, float exposure) =>
        new(StandardColorSpaces.LinearAdobeRgb,
            target == 1 ? StandardColorSpaces.LinearProPhotoRgb : StandardColorSpaces.LinearAdobeRgb,
            mapping == 1 ? PrintToneMapping.SimpleLuminance : PrintToneMapping.AgXRec2020, exposure);

    internal static string GrayValues(PrintRgbTransform transform) => "中性灰：输入 → 映射后线性 Y（曝光已计入）  " +
        string.Join(" · ", CmykPrintingPattern.GrayLevels.Select(value =>
        {
            var result = transform.Transform(new(value, value, value, 1, StandardColorSpaces.LinearAdobeRgb));
            return $"{value:G3}→{StandardLinearRgbConverter.ToXyzD50(result).Y:F3}";
        }));

    internal static async Task<CmykProofResult?> SeparateAsync(LinearRgba[] mapped, CmykProfile profile,
        int intentIndex, bool paper, bool mark, Func<bool> isCurrent, bool yieldToHost = false)
    {
        var intent = intentIndex switch { 1 => IccRenderingIntent.Perceptual, 2 => IccRenderingIntent.Saturation,
            _ => IccRenderingIntent.MediaRelativeColorimetric };
        var print = new CmykTransform(profile, new() { Intent = intent, RangePolicy = PrintRangePolicy.Clip });
        var preview = new LinearRgba[mapped.Length]; var inks = new Vector4[mapped.Length];
        int changed = 0, clipped = 0; float total = 0, maxInk = 0;
        for (int index = 0; index < mapped.Length; index++)
        {
            if ((index & 63) == 0)
            {
                if (!isCurrent()) return null;
                // WASM uses the same transform, yielding between rows so the next slider input can supersede it.
                if (yieldToHost) await Task.Delay(1);
                if (!isCurrent()) return null;
            }
            var proof = print.Proof(mapped[index], StandardColorSpaces.LinearSrgb, paper);
            inks[index] = proof.Separation.Channels; total += proof.DeltaE76;
            maxInk = Math.Max(maxInk, proof.Separation.TotalInkPercent);
            if (proof.ExceedsTolerance) changed++;
            if (proof.OutsideDisplayGamut) clipped++;
            preview[index] = mark && proof.ExceedsTolerance
                ? new(1, 0, 1, 1, StandardColorSpaces.LinearSrgb) : proof.Preview;
        }
        if (!isCurrent()) return null;
        return new(preview, new(CmykPrintingPattern.Columns, CmykPrintingPattern.Rows, inks, profile),
            changed, clipped, total, maxInk);
    }
}
