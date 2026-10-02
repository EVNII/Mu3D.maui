---
title: Shared-device HDR Surface product feeds
description: Let MAUI position native HDR Surfaces while Mu3D shares one device and renderer.
feature_id: gltf-product-feed
---

# Shared-device HDR Surface product feeds

A catalog should not initialize a complete graphics stack in every recycled item, but its 3D output
also should not be converted into an SDR bitmap. Put the `CollectionView` inside one
<xref:Mu3D.Maui.Toolkit.Controls.SceneViewProxyHost> and place a
<xref:Mu3D.Maui.Toolkit.Controls.SceneViewProxy> in each preview region.

Every proxy contains a GPU-native presentation carrier, making its last presented buffer part of
the MAUI card's visual hierarchy. MAUI moves the Surface during native scrolling; application code
does not reconstruct item rectangles or synchronize a sibling render layer. The host shares one
graphics Device, Queue, renderer, pipelines and uploaded resources across those compatible Surfaces.
No GPU readback, PNG encoding or image decode is involved.

For uniform cards, set `ItemSizingStrategy="MeasureFirstItem"`, use compiled bindings, and use the
scrolling contract only to choose bounded loading and warm ranges. See the
[.NET MAUI CollectionView layout guidance](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/layout)
and [scrolling contract](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/scrolling).

## XAML

The default host policy is HDR-first. The following transparent background is a platform alpha
diagnostic, not a portable promise that sibling MAUI content will appear through the model Surface:

```xaml
<toolkit:SceneViewProxyHost x:Name="SurfaceHost">
  <CollectionView
      ItemSizingStrategy="MeasureFirstItem"
      ItemsSource="{Binding Products}">
    <CollectionView.ItemTemplate>
      <DataTemplate x:DataType="local:ProductPreview">
        <Grid RowDefinitions="190,Auto">
          <toolkit:SceneViewProxy
              Host="{x:Reference SurfaceHost}"
              Scene="{Binding Scene}"
              Camera="{Binding Camera}"
              IsRenderingEnabled="{Binding IsLive}"
              SceneBackgroundColor="{Binding SceneBackgroundColor}">
            <toolkit:SceneViewProxy.Underlay>
              <!-- Ordinary MAUI content kept behind the native presentation carrier. -->
              <Grid>
                <!-- Use an explicit child visual for the sampled backdrop on Windows. -->
                <BoxView Color="#FFF4E6" />
                <Label Text="ordinary MAUI underlay" />
              </Grid>
            </toolkit:SceneViewProxy.Underlay>
            <toolkit:SceneViewProxy.Placeholder>
              <Grid>
                <!-- ordinary MAUI placeholder shown until first presentation -->
              </Grid>
            </toolkit:SceneViewProxy.Placeholder>
          </toolkit:SceneViewProxy>
          <Label Grid.Row="1" Text="{Binding Name}" />
        </Grid>
      </DataTemplate>
    </CollectionView.ItemTemplate>
  </CollectionView>
</toolkit:SceneViewProxyHost>
```

The diagnostic accepts only an alpha-0 transparent or alpha-1 opaque `SceneBackgroundColor`.
Fractional background composition remains deliberately deferred. Mu3D
transfer-decodes opaque UI sRGB colors to linear light. Scene highlights remain in the HDR-first
FP16 Surface and are not clipped to an image format. The host selects the concrete alpha mode that
the target compositor advertises instead of assuming one alpha representation is portable.

If mutable camera, material, animation or scene state changes without replacing the object
reference, increment `RenderRevision` or call `InvalidateScene`. A successful frame sets
`HasPresentedFrame` and hides the optional placeholder.

## Ownership and loading policy

`SceneViewProxyHost` owns a hidden one-pixel automatic `Mu3DView` solely to establish the normal
MAUI Handler/window device lifecycle. Each proxy's manual `Mu3DView` supplies a native Surface or
platform GPU-native presentation target, and its compatible session borrows the host Device. The host
serializes carrier creation and one-frame presentation so a newly realized range does not initialize
in one blocking batch. CPU/GPU renderer preparation, uploads, encoding and submit run on the host's
single worker frame; MAUI retains carrier attachment, lifecycle changes and final state notification.

Bound these independent working sets:

1. Fetch, parse and instantiate glTF with bounded background concurrency. A fixed-capacity,
   reusable priority queue ranks visible items before preloads, then nominal distance and stable
   index; create no more real load tasks than the concurrency limit instead of parking a task and
   linked cancellation source for every nearby item.
2. Keep reusable <xref:Mu3D.Formats.Gltf.GltfSceneAsset> definitions in a bounded
   <xref:Mu3D.Toolkit.Assets.AsyncAssetCache`2>.
3. Start loading before a card enters the viewport. Treat the native CollectionView's reported
   first/last visible item indexes as viewport truth; use the known uniform row extent only to
   estimate signed distance beyond that anchored range. The Gallery uses a nominal 3 cm band,
   calculated from the platform's logical-unit standard; it is an interaction threshold, not
   calibrated physical-display metrology.
4. Keep a recently departed Proxy live briefly to avoid churn. The Gallery suspends at 6 cm or 1.5
   seconds and releases managed scene content at 12 cm or 5 seconds. `IsRenderingEnabled="False"`
   unconfigures that Proxy's compatible session and restores its XAML placeholder; recycled or
   unloaded proxies still leave the host completely. Crossing the release policy also cancels that
   item's unfinished linked asset request.
5. Pass the bounded nearby scene set to `SetRetainedScenes` for shared renderer GPU-cache warmth.
6. For a bounded catalog, materialize its lightweight product descriptors before the native list
   binds, then virtualize only the expensive scene content and HDR Surfaces. The Gallery fixes its
   60 descriptors at startup so native content extent, scrollbar thumb and item count do not change
   while scrolling. A genuinely unbounded consumer that must append should leave the native scroll
   callback first and publish each appended range atomically; UIKit validates section counts during
   smooth-scroll transactions.

Do not run the whole retention policy on every native scroll notification. The Gallery records the
latest native visible range, coalesces changed rows to at most one update per display interval and
reuses fixed-capacity sets, distance arrays, ordering buffers and diagnostic builders. Asset loads
and published UI strings still allocate by design; steady-state range calculation does not need to.
Keep the native item extent and its preview extent exact. During active momentum scrolling the
Gallery continues visible promotion/loading but defers Surface suspension, managed-content release
and diagnostic text publication until 200 ms of scroll inactivity. This keeps placeholder
visibility and native-session teardown out of UICollectionView's self-sizing transaction; the
idle reconciliation then applies the same distance/time retention policy once.
On Apple, confirm the native handler really honors that exact extent. The .NET 10 optimized
CollectionView handler uses a compositional layout with estimated dimensions; resolving preferred
sizes for cells realized above the current anchor can correct content offset and make upward scroll
or its indicator jump even when the XAML root has a height request. This Gallery scopes the legacy
flow-layout handler to its `ProductFeedCollectionView` only, where `MeasureFirstItem` supplies one
uniform exact item size; unrelated CollectionViews retain the optimized handler.
Android does not use that UIKit layout. For this fixed descriptor count and exact item extent, the
Gallery instead marks only the Product Feed's native RecyclerView as fixed-size and removes its
item animator, because Poster/Live state changes are non-structural and must not trigger parent-size
work or change animations. The .NET 10 Windows handler has no equivalent UIKit estimated-section
solver, so the shared exact XAML extent and stable item source remain the Windows policy; do not
replace handlers there without target-specific evidence.

The Gallery keeps diagnostics out of the reusable controls but shows a throttled row for every
product: nominal signed distance from the viewport, MAUI Proxy realization, managed-content
readiness, first-frame completion and current retention policy. This separates successful prefetch
from CollectionView cell/Handler realization and from native session/first-frame work. Do not
update a long diagnostic string on every scroll event in production UI.

For a passive catalog preview, set the Proxy `InputTransparent="True"` so the CollectionView remains
the wheel/pan target. The Gallery also keeps its nested diagnostic scroller transparent on Windows,
where the catalog's compositor experiment requires one wheel target; Mac Catalyst makes that
diagnostic scroller interactive. Do not apply input transparency to an interactive preview that
intentionally owns orbit, selection or gizmo input.

Page disappearance may set the host's `IsRenderingEnabled="False"` to unconfigure every compatible
Proxy Surface at once. Final Proxy Surfaces are released before the host's owning session/device
through normal Loaded/Unloaded, Handler and window teardown. Do not put a host, proxy, page, Handler or Surface in `AddSingleton`; DI is suitable
for bounded immutable asset definitions or a future window-scoped coordinator, not visual ownership.

## Choosing the rendering path

Use <xref:Mu3D.Maui.Toolkit.Controls.SceneViewProxy> for a virtualized collection that needs
GPU-native previews and shared device resources. An opaque background selects direct HDR
presentation; a transparent background selects the platform binary-mask carrier. Each active proxy
still consumes a native presentation output, so the realized/live range must remain bounded.

Use <xref:Mu3D.Maui.Toolkit.Controls.VirtualizedSceneView> for a few independent interactive views
that do not need this shared-device host. Use
<xref:Mu3D.Maui.Toolkit.Controls.SceneViewportHost> only when an application deliberately renders
multiple logical regions into one Mu3D Surface and accepts separate coordination of that layer.

Platform compositor details differ, so alpha transport uses platform-specific MAUI Handlers while
the XAML above remains portable. Android requests a translucent native Surface and wgpu's inherited
alpha mode. Apple marks its `UIView`/`CAMetalLayer` carrier non-opaque and requests Metal's
straight-alpha mode. Because the scene renderer produces premultiplied RGBA, Apple renders once to a
reusable FP16 intermediate and uses a full-screen texture-load pass to divide RGB by nonzero alpha
before presentation. This preserves extended-range color and per-pixel alpha without CPU readback,
image conversion or a second scene render. Opaque Windows output uses a direct `SwapChainPanel`.
The accepted synchronized Windows HDR-mask topology also keeps FP16 scene color in that direct
panel. Mu3D renders each frame once into one FP16 RGBA master texture. When the exact native Surface
advertises `CopyDestination`, a GPU texture copy sends its color to the direct HDR Surface. The
portable Surface baseline guarantees only `RenderAttachment`, so other devices use a full-screen
FP16 texture-load pass to transfer the same master frame without rerendering the scene. WinUI reads
the master texture's alpha, inverts it and replays the MAUI `Underlay` above the opaque HDR plane.
Both planes are children of the same XAML card, so
CollectionView scrolling, clipping and MAUI animation transforms remain synchronized and HDR color
never crosses a WinUI drawing-surface color path. No path uses a second scene submission, CPU
readback, image conversion or a codec.

Physical testing confirms that this synchronized Windows path preserves HDR, masking, scrolling and
frame-for-frame Alpha alignment during rotation. It remains guarded until Mu3D publishes the
required patched Windows runtime assets; the ordinary packaged transparent Windows path continues
to use the honest SDR compositor-owned mask carrier and respects `SdrFallbackMode`, while opaque
output remains direct HDR. Carrier selection is Handler-owned and does not require platform-specific
XAML.

Because the direct HDR plane is necessarily opaque, the underlay must paint the complete backdrop
that should appear outside the model silhouette. On Windows, make that backdrop an explicit child
visual such as a fill-sized `BoxView`; a layout's own MAUI background can live outside the sampled
child-visual content. An underlay containing only transparent pixels and text reveals the direct
plane's black clear color between those glyphs. An ancestor background is deliberately not sampled
or duplicated implicitly.

Physical testing accepts the combined HDR highlight, complete MAUI backdrop, model mask and
same-tree scrolling behavior. Repeating `Emissive row` cards now rotate continuously from their
frame-presented callback. That completion-driven loop changes the transform only between frames and
requests the next frame without a generic timer; it is the focused test that HDR color and mask
alpha stay on the same animated frame. `InputTransparent` remains an application choice: passive
catalog previews should let their CollectionView own input, while interactive previews may attach
normal camera, selection or gizmo tools.

For a like-for-like visual check, repeating `Emissive row` products alternate between transparent
FP16-mask and opaque direct-HDR presentation. Their card titles state the selected carrier, so the
same emissive glTF can be compared without inferring the path from its item number.
