---
title: HDR JPEG
description: Encode and decode Ultra HDR gain-map JPEG while preserving explicit color meaning.
feature_id: hdr-jpeg
---

# HDR JPEG

<xref:Mu3D.Native.UltraHdr.UltraHdrJpegEncoder> writes an SDR JPEG base plus gain-map metadata from
an explicitly tagged HDR linear image. <xref:Mu3D.Native.UltraHdr.UltraHdrJpegEncodeOptions>
controls the gain-map relationship and codec quality.

## XAML page

The codec has no visual control. Use normal bindable MAUI controls for the operation and its
result; perform encode/decode off the UI thread and publish only the final status.

```xaml
<VerticalStackLayout Padding="24" Spacing="16">
    <Button
        Command="{Binding RunRoundTripCommand}"
        Text="Run HDR JPEG round trip" />
    <ActivityIndicator IsRunning="{Binding IsRunning}" />
    <Label LineBreakMode="WordWrap" Text="{Binding Result}" />
</VerticalStackLayout>
```

<xref:Mu3D.Native.UltraHdr.JpegImageDecoder> implements Mu3D's application-replaceable encoded-image
decoder boundary. `DecodeDocumentColor` returns a
<xref:Mu3D.Native.UltraHdr.JpegDocumentDecodeResult> containing decoded linear pixels and document
color metadata; ordinary color/data decode entry points remain available for asset import.

## Measurement boundary

The Gallery creates a Display P3 HDR gradient and diagnostic patches, encodes a gain-map JPEG,
decodes it to FP32 and reports numerical error/PSNR plus CIEDE2000. Its relative-linear convention
maps 1.0 to the codec's 203-nit reference white.

This is a codec round-trip measurement. It does not prove that a display entered HDR mode or that a
compositor reproduced those values physically. Keep storage encoding, linear working space and
presentation space explicit.

The normal package is JPEG-only. Optional HEIF support is a separately built feature set and is not
selected dynamically by this example.

## Named standard RGB JPEG export

```csharp
byte[] jpeg = new JpegDocumentEncoder().Encode(image, StandardRgbEncoding.DisplayP3);
```

The overload also accepts sRGB, Adobe RGB (1998), ProPhoto RGB and Rec.2020. It converts
linear source pixels, encodes the selected transfer function and embeds its matching ICC v4
profile. This is ordinary lossy **8-bit JPEG**, including for ProPhoto/Rec.2020; it is not a
high-precision archival or HDR gain-map format. Alpha must already be composited to 1.
Out-of-range pixels are rejected unless the caller explicitly requests `JpegEncodingRangePolicy.Clip`.
Only media-relative colorimetric intent is supported by this named-standard overload. Use an
explicit supported ICC LUT profile with the existing overload for other rendering intents.
