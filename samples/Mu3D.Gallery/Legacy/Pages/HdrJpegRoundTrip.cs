using System.Diagnostics;
using System.Numerics;
using Mu3D.Color;
using Mu3D.Native.UltraHdr;

namespace Mu3D.GalleryApp.Pages;

// One actual Gallery codec operation shared by MAUI, Web and wasm32 ABI contracts.
internal static class HdrJpegRoundTrip
{
    internal static HdrJpegRoundTripResult Run()
    {
        const uint width = 128;
        const uint gradientHeight = 64;
        const uint height = 96;
        Vector3[] patches =
        [
            new(0.003f, 0.003f, 0.003f),
            new(0.18f, 0.18f, 0.18f),
            new(1f, 1f, 1f),
            new(2f, 2f, 2f),
            new(1f, 0f, 0f),
            new(0f, 1f, 0f),
            new(0f, 0f, 1f),
            new(1f, 1f, 0f),
        ];
        Vector4[] pixels = new Vector4[width * height];
        float inputMaximum = 0f;
        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                Vector4 pixel;
                if (y < gradientHeight)
                {
                    float vertical = y / (gradientHeight - 1f);
                    float horizontal = x / (width - 1f);
                    pixel = new Vector4(
                        0.05f + 1.45f * horizontal,
                        0.1f + 0.5f * vertical,
                        0.2f + 4f * horizontal * horizontal,
                        1f);
                }
                else
                {
                    int patch = Math.Min(
                        checked((int)(x * (uint)patches.Length / width)),
                        patches.Length - 1);
                    pixel = new Vector4(patches[patch], 1f);
                }
                pixels[checked((int)(y * width + x))] = pixel;
                inputMaximum = MathF.Max(inputMaximum, MathF.Max(pixel.X, MathF.Max(pixel.Y, pixel.Z)));
            }
        }

        LinearRgbaImage source = new(
            width,
            height,
            pixels,
            StandardColorSpaces.LinearDisplayP3,
            "Gallery HDR JPEG reference gradient");
        UltraHdrJpegEncoder encoder = new();
        Stopwatch stopwatch = Stopwatch.StartNew();
        byte[] encoded = encoder.Encode(
            source,
            new UltraHdrJpegEncodeOptions
            {
                BaseQuality = 95,
                GainMapQuality = 95,
                GainMapScaleFactor = 1,
                UseMultiChannelGainMap = true,
                TargetDisplayPeakBrightnessNits = 1000f,
            });
        TimeSpan encodeTime = stopwatch.Elapsed;

        JpegImageDecoder decoder = new();
        stopwatch.Restart();
        LinearRgbaImage decoded = decoder.DecodeColor(encoded, "image/jpeg", "round-trip result");
        TimeSpan decodeTime = stopwatch.Elapsed;
        if (decoded.Width != width || decoded.Height != height)
        {
            throw new InvalidDataException(
                $"Round-trip dimensions changed from {width}x{height} to {decoded.Width}x{decoded.Height}.");
        }

        LinearRgbImageComparison comparison = LinearRgbImageComparer.Compare(source, decoded);
        bool hdrHeadroomPreserved = comparison.ActualPeakMagnitude > 1;

        string report =
            $"Encoded bytes: {encoded.Length:N0}\n" +
            $"Encode time: {encodeTime.TotalMilliseconds:F1} ms\n" +
            $"Decode time: {decodeTime.TotalMilliseconds:F1} ms\n" +
            $"Decoded space: {decoded.ColorSpace.Name}\n" +
            $"Input maximum: {inputMaximum:F3}\n" +
            $"Decoded maximum magnitude: {comparison.ActualPeakMagnitude:F3}\n" +
            $"Mean absolute RGB error: {comparison.MeanAbsoluteRgbError:F5}\n" +
            $"RGB RMSE: {comparison.RootMeanSquareRgbError:F5}\n" +
            $"Maximum absolute RGB error: {comparison.MaximumAbsoluteRgbError:F5}\n" +
            $"Peak-relative RGB error: {comparison.PeakRelativeRgbError:P2}\n" +
            $"RGB PSNR: {comparison.PeakSignalToNoiseRatioDecibels:F2} dB\n" +
            $"Mean CIEDE2000 (gradient + patches): {comparison.MeanDeltaE2000:F3}\n" +
            $"Maximum CIEDE2000: {comparison.MaximumDeltaE2000:F3}\n" +
            $"HDR headroom preserved: {hdrHeadroomPreserved}";
        return new HdrJpegRoundTripResult(encoded, decoded, comparison, report);
    }
}

internal sealed record HdrJpegRoundTripResult(
    byte[] Encoded,
    LinearRgbaImage Decoded,
    LinearRgbImageComparison Comparison,
    string Report);
