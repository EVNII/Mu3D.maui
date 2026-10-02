---
title: KTX2 decode
description: Decode bounded KTX2 sources with explicit cache and retention ownership.
feature_id: ktx2-decode
---

# KTX2 decode

<xref:Mu3D.Native.Ktx.Ktx2TextureLoader> loads either a caller-owned stream or an
application-keyed resolver result. <xref:Mu3D.Native.Ktx.Ktx2TextureLoadOptions> independently bounds
encoded source bytes and final decoded/compressed output bytes.

## XAML page

```xaml
<VerticalStackLayout Padding="24" Spacing="12">
    <Switch IsToggled="{Binding RetainEncodedSource, Mode=TwoWay}" />
    <Grid ColumnDefinitions="*,*" ColumnSpacing="8">
        <Button Command="{Binding DecodeCommand}" Text="Decode / Reload KTX2" />
        <Button Grid.Column="1" Command="{Binding CancelCommand}" Text="Cancel" />
    </Grid>
    <Label Text="{Binding Status}" />
    <Label FontSize="12" LineBreakMode="WordWrap" Text="{Binding Details}" />
</VerticalStackLayout>
```

```csharp
Ktx2TextureAsset asset = await Ktx2TextureLoader.LoadAsync(
    sourceKey,
    OpenSourceAsync,
    sourceCache,
    new Ktx2TextureLoadOptions
    {
        Content = CompressedMaterialTextureContent.Color,
        Preference = Ktx2TextureLoadPreference.DecodedRgba,
        MaximumSourceByteCount = 128 * 1024,
        MaximumOutputByteCount = 32 * 1024 * 1024,
        RetainEncodedSource = false,
    },
    cancellationToken);
```

<xref:Mu3D.Native.Ktx.Ktx2SourceCache> is application-owned, bounded and never implicitly cleared.
The returned <xref:Mu3D.Native.Ktx.Ktx2TextureAsset> always owns its decoded image or compressed mip
chain. Exact encoded KTX2 bytes are a separate optional retained copy.

A caller stream remains open. A resolver-returned stream transfers to the loader and closes on all
outcomes. Cancellation surrounds safe I/O/native boundaries; an already running native transcode
finishes safely.
