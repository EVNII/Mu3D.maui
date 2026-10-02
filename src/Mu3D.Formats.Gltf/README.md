# Mu3D.Formats.Gltf

Optional pure-managed glTF 2.0 and GLB import for Mu3D.

`GltfImporter` consumes caller-owned bytes and produces backend-independent Mu3D scene and
animation objects. It does not own file pickers, URI policy, GPU resources or platform I/O.

`GltfAssetLoader.LoadAsync` is the bounded stream orchestration path. The caller opens and closes
the stream, chooses `MaximumSourceByteCount`, and may cancel both asynchronous reading and the
cooperative import stages. Parsing runs away from the caller's synchronization context. The loader
does not retain the source by default, close the stream, create a hidden cache, or resolve a
file/network URI.

After import, call `GltfAsset.CreateSceneAsset` once when the same model needs more than one
placement. `GltfSceneAsset.CreateInstance` shares immutable geometry and decoded/compressed texture
sources while cloning mutable nodes, transforms, materials, skins, morph weights and animation
targets. Each `GltfSceneInstance` also owns its active `KHR_materials_variants` selection, so one
product card or room placement can change appearance without mutating its siblings. Definitions and
instances own no renderer, surface or native GPU resource and can be attached to different
application scenes or views.

```csharp
GltfSceneAsset product = asset.CreateSceneAsset("Product definition");
GltfSceneInstance left = product.CreateInstance("Left product");
GltfSceneInstance right = product.CreateInstance("Right product");
left.AttachTo(scene);
right.AttachTo(scene);
right.ApplyMaterialVariant("Blue");
```

For `.gltf` documents, an application may provide `ExternalResourceResolver` to open approved
external buffer and image URIs. Exact URI strings are read once, with independent per-resource and
aggregate limits. Ownership of each resolved stream transfers to the loader, which closes it after
the bounded read; the main source stream remains caller-owned. The application still owns URI
normalization, traversal protection, package/file/network policy and any persistent cache.

`GltfExternalResourceCache` is the explicit reusable-cache boundary. The application creates it,
scopes it to assets that share one URI identity, passes it into any number of loads, and decides when
to clear it. The loader stores owned byte copies after a successful bounded read, can load a fully
warm `.gltf` without a resolver, and never clears or disposes the cache. Cache hits still obey the
current load's per-resource and aggregate limits.

Exact encoded source retention is separate. Set `SourceRetention` to retain the main source, or the
main source plus asynchronously resolved external files, in `GltfAsset.SourceArchive`. The archive
owns independent copies and is guarded by `MaximumRetainedSourceByteCount`. Its default is `None`,
so temporary encoded buffers are released after import. Decoded PNG/JPEG images and transcoded KTX2
mip data referenced by imported materials are unaffected: they remain with the asset because they
are the CPU-side source used for GPU upload and device-resource recreation.

```csharp
await using Stream stream = await OpenAssetStreamAsync(cancellationToken);
GltfAsset asset = await GltfAssetLoader.LoadAsync(
    stream,
    new GltfAssetLoadOptions
    {
        MaximumSourceByteCount = 32 * 1024 * 1024,
        MaximumExternalResourceByteCount = 16 * 1024 * 1024,
        MaximumTotalExternalResourceByteCount = 64 * 1024 * 1024,
        ExternalResourceCache = productAssetCache,
        SourceRetention = GltfSourceRetentionMode.None,
        ExternalResourceResolver = async (uri, token) =>
            await OpenApprovedExternalStreamAsync(uri, token),
        ImportOptions = new GltfImportOptions { Name = "Product" },
    },
    cancellationToken);
```

Embedded glTF PNG assets use the package's strict built-in decoder. Other encoded image formats are
resolved through the caller-supplied `IEncodedImageDecoder`, allowing applications to reuse their
existing image stack. `KHR_texture_basisu` is recognized. Applications may use
`ImportWithOptions`/`ImportAssetWithOptions` to supply an `IEncodedTextureTranscoder` and explicit
device capabilities; validated BC, ETC2 or ASTC mip chains remain GPU compressed, while a null
transcoder result falls back to decoded RGBA. The package does not select or depend on
`Mu3D.Native.UltraHdr`, Basis Universal or another native codec.
