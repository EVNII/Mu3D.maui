# Mu3D.Creative

Optional backend/UI-independent HDR creative primitives. This package depends only on
`Mu3D.Color`; it does not acquire input, define painting tools, or select a display transform.

```csharp
using Mu3D.Color;
using Mu3D.Creative;

var canvas = new HdrCanvas(512, 512, StandardColorSpaces.AcesCg);
canvas.ApplyDab(new BrushDab(256, 256, 80,
    new LinearRgba(4, 0.5f, -0.1f, 0.8f, StandardColorSpaces.AcesCg),
    hardness: 0.6f));
LinearRgbaImage image = canvas.Snapshot();
```

`HdrCanvas` uses sparse Float16 tiles by default, with an explicit Float32 document option.
Working space, linear compositing space and storage precision are separate settings.
Public colors and snapshots use straight alpha; internal pixels use premultiplied alpha.
Colors are converted before premultiplication. Negative and above-one RGB survive without
gamut or tone mapping. A zero stored alpha has zero RGB. Binary16 storage intentionally rounds
both premultiplied RGB and alpha; extremely small alpha can underflow to transparent black.
Finite values outside binary16 storage, non-finite results and colors that cannot be represented
at the public straight-alpha working-space boundary fail before the operation commits.

`Fill` replaces pixels; `Composite`, `CompositeImage` and `ApplyDab` use source-over alpha with
Normal, Multiply, Screen or Add RGB blending. Multiply and Screen use the algebraic blend
functions from [W3C Compositing and Blending](https://www.w3.org/TR/compositing-1/#blending),
extended to unrestricted linear RGB. Add uses `source + backdrop` in overlap, with ordinary
source-over alpha; it is not a Porter-Duff plus operator. HDR Screen may produce negative values,
as specified by its unclipped algebra. Brush coverage samples pixel centers; hardness selects a
fully covered inner radius followed by a linear falloff. There is no stroke interpolation or
implicit pressure mapping.

Explicit limits bound retained tile-pixel bytes, per-operation staging bytes, snapshot buffer
bytes and tile metadata counts. The staging check conservatively includes every complete tile
intersecting an operation's rectangle (or dab bounding rectangle). Managed object/collection
overhead is additional to pixel budgets. Snapshots account for both the temporary and immutable
FP32 pixel arrays. Right/bottom edge tiles allocate only their actual extent. Clearing the last
nontransparent pixel releases a tile. All rejected input, overflow and configured-budget errors
leave pixels, revision and dirty state unchanged. Concurrent calls and system out-of-memory
recovery are not supported.

Dirty rectangles are reported per tile in row-major order, include cleared tiles, and remain
pending until explicitly acknowledged with `ClearDirtyRegions`. Dirty metadata also consumes
the tile-count budget; applications should consume it regularly. A snapshot does not acknowledge
updates. `Snapshot(region)` can export a bounded crop of a very large sparse document. Applications
may use the immutable tagged image in a Mu3D image material and choose presentation separately.
