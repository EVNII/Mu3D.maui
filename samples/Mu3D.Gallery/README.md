# Mu3D Gallery

This is the single native Gallery application. AdaptiveShell.Maui provides platform-native
navigation; example content uses MAUI/XAML and the real Mu3D controls and renderer.

Use **All Examples** to search the complete catalog, or select **Basics**, **Rendering**,
**Toolkit**, **Assets** or **About**. Each example has an **Examples** action to return to the
catalog and a **Docs** action for its public feature article. Desktop examples also expose a
collapsed source drawer. Pages are created when selected and pause when navigation hides them.

The catalog has 43 entries (42 without optional printing), including all original focused cases,
Advanced Material Conformance, Emissive Material, Translucent HDR Canvas, Procedural 3D Feed and
Licenses. OpenPBR, the white furnace, probes, shadows/AO, glTF and the real codecs are retained.
Scene hosts default to scene-linear HDR. Display views are explicit choices.

Build the appropriate target on its supported host, using the repository's pinned SDK/workloads:

```sh
dotnet build samples/Mu3D.Gallery/Mu3D.Gallery.csproj -c Release -f net10.0-maccatalyst
```

The original GalleryApp executable is retired. `Legacy` contains retained focused examples and
shared helpers, keeping their internal namespaces to preserve source contracts. Newer cases live
in `Pages`; navigation metadata and page creation are separate in `GalleryCatalog` and
`GalleryPageFactory`. `Resources/Raw` is the single original asset tree.

The [Web Gallery](Web/README.md) is the full Blazor/WASM host using the same portable operations
and assets. Native pages continue to render through native MAUI controls.
