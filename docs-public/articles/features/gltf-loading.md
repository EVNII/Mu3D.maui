---
title: glTF loading
description: Load bounded glTF/GLB sources with explicit external caching and source retention.
feature_id: gltf-loading
---

# glTF loading

<xref:Mu3D.Formats.Gltf.GltfAssetLoader> is the bounded asynchronous entry point for a glTF or GLB
stream. <xref:Mu3D.Formats.Gltf.GltfAssetLoadOptions> separates main-source, per-external-resource,
aggregate-external and retained-source limits.

## XAML host

XAML owns only the MAUI presentation and controls. The asynchronous loader, cache and cancellation
token remain application services or view-model state.

```xaml
<Grid RowDefinitions="*,Auto">
    <mu3d:Mu3DView
        x:Name="SurfaceView"
        BackgroundColor="Black"
        Draw="OnSurfaceDraw" />
    <VerticalStackLayout Grid.Row="1" Padding="16" Spacing="8">
        <Label Text="{Binding LoadStatus}" />
        <Switch IsToggled="{Binding RetainEncodedSources, Mode=TwoWay}" />
        <Button Command="{Binding ReloadCommand}" Text="Reload" />
        <Button Command="{Binding CancelCommand}" Text="Cancel" />
    </VerticalStackLayout>
</Grid>
```

```csharp
GltfAsset asset = await GltfAssetLoader.LoadAsync(
    sourceStream,
    new GltfAssetLoadOptions
    {
        ExternalResourceResolver = OpenExternalAsync,
        ExternalResourceCache = cache,
        SourceRetention = GltfSourceRetentionMode.None,
    },
    cancellationToken);
```

A caller-supplied main stream remains caller-owned. Streams returned by the external resolver
transfer to the loader and close on success, failure or cancellation.
<xref:Mu3D.Formats.Gltf.GltfExternalResourceCache> is application-owned, bounded and keyed by exact
authored URI; scope it only across assets that share the same URI identity.

Exact encoded-source retention is independent. <xref:Mu3D.Formats.Gltf.GltfSourceRetentionMode>
selects none, main source, or main plus external resources. The returned
<xref:Mu3D.Formats.Gltf.GltfSourceArchive> owns those copies. Decoded/transcoded material sources
remain attached to materials for rendering/device recreation regardless of this switch.

Cancellation is cooperative around safe I/O/import boundaries. Do not forcibly dispose a stream or
native decoder from another thread while an operation is using it.

Keep explicit application cancellation separate from view lifecycle invalidation. A caller awaiting
`LoadAsync` should pass its real operation token and handle `OperationCanceledException` normally.
For a finite package-local Gallery load, page disappearance instead marks the page/session generation
stale, checks that state between I/O, import and environment stages, and discards the result. This
prevents an ordinary Shell navigation transition from being reported as an unhandled first-chance
cancellation while still preventing stale scene publication.
