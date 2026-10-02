---
title: XAML component catalog
description: Author and bind every public Mu3D MAUI component from compiled XAML.
---

# XAML component catalog

`Mu3D.Maui` and `Mu3D.Maui.Toolkit` expose their MAUI-facing configuration through
`BindableProperty`. Values can therefore come from literals, compiled bindings, styles, dynamic
resources or control references. Collection properties such as `Scene3D.Children`,
`Mu3DSceneView.Features` and `ViewportTools.Items` are XAML content collections rather than scalar
bindable properties.

The UI-independent `Mu3D.Core` and `Mu3D.Toolkit` objects remain ordinary .NET objects. Bind them to
the MAUI wrappers when the application creates them in a view model; do not move renderer, cache,
selection-ranking or undo policy into XAML merely to make it declarative.

## Namespaces and compiled binding

```xaml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:mu3d="clr-namespace:Mu3D.Maui.Controls;assembly=Mu3D.Maui"
    xmlns:toolkit="clr-namespace:Mu3D.Maui.Toolkit.Controls;assembly=Mu3D.Maui.Toolkit"
    xmlns:diagnostics="clr-namespace:Mu3D.Maui.Toolkit.Diagnostics;assembly=Mu3D.Maui.Toolkit"
    xmlns:overlays="clr-namespace:Mu3D.Maui.Toolkit.Overlays;assembly=Mu3D.Maui.Toolkit"
    xmlns:viewModels="clr-namespace:MyApp.ViewModels"
    x:DataType="viewModels:SceneViewModel">
    <mu3d:Mu3DSceneView
        ClearColor="{Binding ClearColor}"
        IsRenderingEnabled="{Binding IsRenderingEnabled}"
        RenderOutput="{Binding SelectedOutput}" />
</ContentPage>
```

Use `x:DataType` wherever a page or data template has a stable view-model type. The XAML compiler
then validates binding paths instead of deferring misspellings until runtime.

## Low-level and declarative scene controls

`Mu3DView` is the low-level draw callback. `Mu3DSceneView` is the normal high-level host. A
`Scene3D` can contain a `PerspectiveCamera3D`, `DirectionalLight3D`, `Sphere3D`, `Cone3D`,
`PbrMaterial3D` and `UnlitMaterial3D` directly in compiled XAML.

```xaml
<Grid>
    <mu3d:Mu3DView
        x:Name="ManualSurface"
        IsVisible="False"
        OutputSettings="{Binding OutputSettings}"
        SurfaceManagement="Automatic"
        Draw="OnDraw" />

    <mu3d:Mu3DSceneView
        x:Name="SceneView"
        AutomaticallyUpdateCameraAspectRatio="True"
        ClearColor="{Binding ClearColor}">
        <mu3d:Scene3D Name="Bound scene">
            <mu3d:Scene3D.Camera>
                <mu3d:PerspectiveCamera3D
                    x:Name="Camera"
                    FarClip="100"
                    FieldOfView="52"
                    NearClip="0.1"
                    Z="6" />
            </mu3d:Scene3D.Camera>
            <mu3d:Sphere3D Radius="0.9" X="-1">
                <mu3d:PbrMaterial3D
                    Color="{Binding PrimaryObjectColor}"
                    Metallic="0.2"
                    Roughness="0.3" />
            </mu3d:Sphere3D>
            <mu3d:Cone3D Height="1.8" Radius="0.8" X="1">
                <mu3d:UnlitMaterial3D Color="#FF8A38" />
            </mu3d:Cone3D>
            <mu3d:DirectionalLight3D
                CastsShadows="True"
                Intensity="4"
                RotationX="-35"
                RotationY="-30" />
        </mu3d:Scene3D>
    </mu3d:Mu3DSceneView>
</Grid>
```

`SceneNode3D`, `Camera3D`, `Primitive3D` and `Material3D` are abstract XAML authoring bases. Use one
of the concrete types above rather than instantiating the base type.

## Viewport tools and physical input

`ViewportTools` is the recommended feature collection. The following compact catalog shows every
high-level tool and every replaceable physical-input group. In a real page, install only the tools
needed by that viewport.

```xaml
<mu3d:Mu3DSceneView x:Name="ToolScene">
    <mu3d:Mu3DSceneView.Features>
        <toolkit:ViewportTools>
            <toolkit:OrbitTool
                Camera="{x:Reference Camera}"
                IsEnabled="{Binding IsOrbitEnabled}">
                <toolkit:OrbitTool.Input>
                    <toolkit:ViewportInput>
                        <toolkit:ViewportInput.Mouse>
                            <toolkit:MouseInput
                                LeftButtonDragAction="Rotate"
                                RightButtonDragAction="Pan" />
                        </toolkit:ViewportInput.Mouse>
                        <toolkit:ViewportInput.Trackpad>
                            <toolkit:TrackpadInput TwoFingerDragAction="Pan" />
                        </toolkit:ViewportInput.Trackpad>
                        <toolkit:ViewportInput.Touchscreen>
                            <toolkit:TouchscreenInput
                                OneFingerDragAction="Rotate"
                                TwoFingerDragAction="Pan" />
                        </toolkit:ViewportInput.Touchscreen>
                        <toolkit:ViewportInput.Keyboard>
                            <toolkit:KeyboardInput IsEnabled="{Binding IsKeyboardNavigationEnabled}" />
                        </toolkit:ViewportInput.Keyboard>
                    </toolkit:ViewportInput>
                </toolkit:OrbitTool.Input>
            </toolkit:OrbitTool>
            <toolkit:MapTool Camera="{x:Reference Camera}" IsEnabled="False" />
            <toolkit:FlyTool Camera="{x:Reference Camera}" IsEnabled="False" />
            <toolkit:SceneSelectionTool
                ClearSelectionOnMiss="True"
                SelectedNode="{Binding SelectedNode, Mode=TwoWay}"
                SelectionMask="3" />
            <toolkit:TransformGizmoTool Target="{Binding SelectedNode}" />
            <toolkit:ProgressTool Progress="{Binding Progress}">
                <toolkit:NodeTransformProgress
                    From="0"
                    Property="RotationY"
                    Target="{x:Reference AnimatedNode}"
                    To="360" />
            </toolkit:ProgressTool>
            <toolkit:PlaybackToolbar Playable="{Binding Player}" />
            <toolkit:AxesHelper Orbit="{Binding OrbitController}" />
            <toolkit:GridHelper Divisions="24" Plane="XZ" Size="12" />
            <toolkit:BoundsHelper Target="{Binding SelectedNode}" />
            <toolkit:OutlineHelper Target="{Binding SelectedNode}" />
            <toolkit:RenderOutputToolbar Output="{Binding SelectedOutput}">
                <toolkit:RenderOutputOption Label="Custom" Output="{Binding CustomOutput}" />
            </toolkit:RenderOutputToolbar>
            <toolkit:RenderOutputTool IsVisible="False" Output="{Binding SelectedOutput}" />
            <diagnostics:FrameStatisticsOverlay IsDetailed="{Binding ShowDetailedStatistics}" />
        </toolkit:ViewportTools>
    </mu3d:Mu3DSceneView.Features>
</mu3d:Mu3DSceneView>
```

Scene nodes can opt into the default selector without code-behind:

```xaml
<mu3d:Sphere3D
    toolkit:SceneSelection.IsSelectable="True"
    toolkit:SceneSelection.Mask="2" />
```

## Lower-level feature and behavior forms

The high-level tools above are preferred. The feature and behavior forms remain public for hosts
that compose controller ownership themselves. They are also bindable and XAML-constructible.

```xaml
<mu3d:Mu3DSceneView x:Name="AdvancedScene">
    <mu3d:Mu3DSceneView.Features>
        <toolkit:OrbitSceneViewFeature
            Controller="{Binding OrbitController}"
            IsEnabled="{Binding IsOrbitEnabled}" />
        <toolkit:TransformGizmoSceneViewFeature
            Gizmo="{Binding Gizmo}"
            HitTester="{Binding GizmoHitTester}"
            IsEnabled="{Binding IsGizmoEnabled}" />
    </mu3d:Mu3DSceneView.Features>
    <mu3d:Mu3DSceneView.Behaviors>
        <toolkit:ViewportNavigationBehavior
            Controller="{Binding OrbitController}"
            Input="{Binding ViewportInput}" />
        <toolkit:TransformGizmoPointerBehavior
            Gizmo="{Binding Gizmo}"
            HitTester="{Binding GizmoHitTester}" />
        <diagnostics:FrameStatisticsBehavior
            Collector="{Binding StatisticsCollector}"
            DrawCallCount="{Binding DrawCallCount}"
            PrimitiveCount="{Binding PrimitiveCount}" />
    </mu3d:Mu3DSceneView.Behaviors>
</mu3d:Mu3DSceneView>
```

## MAUI overlays and diagnostics

`ViewportOverlay` and `SceneNodeAnchor` use the shared viewport overlay manager. The older
`SceneNodeAnchorLayer` remains useful as a standalone layer. `FrameStatisticsView` can present a
standalone `FrameStatisticsBehavior` outside the viewport.

```xaml
<Grid>
    <mu3d:Mu3DSceneView x:Name="OverlayScene">
        <mu3d:Mu3DSceneView.Features>
            <toolkit:ViewportTools>
                <overlays:ViewportOverlay Placement="TopRight">
                    <overlays:ViewportOverlay.Anchor>
                        <overlays:SceneNodeAnchor
                            Node="{Binding SelectedCoreNode}"
                            Offset="8,-28" />
                    </overlays:ViewportOverlay.Anchor>
                    <Label Text="{Binding SelectedName}" />
                </overlays:ViewportOverlay>
            </toolkit:ViewportTools>
        </mu3d:Mu3DSceneView.Features>
    </mu3d:Mu3DSceneView>

    <overlays:SceneNodeAnchorLayer SceneView="{x:Reference OverlayScene}">
        <Label
            overlays:SceneNodeAnchorLayer.Node="{Binding SelectedCoreNode}"
            overlays:SceneNodeAnchorLayer.Offset="8,-28"
            Text="{Binding SelectedName}" />
    </overlays:SceneNodeAnchorLayer>

    <diagnostics:FrameStatisticsView
        HorizontalOptions="End"
        Source="{Binding StatisticsBehavior}"
        VerticalOptions="Start" />
</Grid>
```

## Virtualized and shared-device hosts

`VirtualizedSceneView`, `SceneViewProxyHost` and `SceneViewProxy` accept application-owned sources,
scenes and cameras through bindings. `SceneViewportHost` is XAML-configurable, but its changing
`SceneViewportSlot` snapshot is intentionally supplied by `SetViewports`; a slot contains borrowed
Core objects and rectangle state rather than UI state.

```xaml
<Grid>
    <toolkit:SceneViewProxyHost
        x:Name="ProxyHost"
        IsRenderingEnabled="{Binding IsRenderingEnabled}">
        <CollectionView ItemsSource="{Binding Products}">
            <CollectionView.ItemTemplate>
                <DataTemplate x:DataType="viewModels:ProductViewModel">
                    <toolkit:SceneViewProxy
                        Camera="{Binding Camera}"
                        RenderRevision="{Binding RenderRevision}"
                        Scene="{Binding Scene}">
                        <ActivityIndicator IsRunning="True" />
                    </toolkit:SceneViewProxy>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </toolkit:SceneViewProxyHost>

    <toolkit:VirtualizedSceneView
        Activity="{Binding Activity}"
        Budget="{Binding ViewportBudget}"
        IsActivationEnabled="{Binding IsActive}"
        Source="{Binding ViewportSource}">
        <Label Text="Loading 3D preview…" />
    </toolkit:VirtualizedSceneView>

    <toolkit:SceneViewportHost
        x:Name="SharedSurfaceHost"
        ClearColor="{Binding ClearColor}"
        IsRenderingEnabled="{Binding IsRenderingEnabled}" />
</Grid>
```

## OpenPBR rendering and compatibility preview

`OpenPbrSurface3D` preserves constant OpenPBR authoring values and optional immutable input graphs
through its bindable `Graph` property. `OpenPbrMaterial3D` creates the distinct
Core OpenPBR root and requires an explicitly installed `OpenPbrRenderPass` from the optional
`Mu3D.Rendering.OpenPbr` package. It supports interactive and progressive reference modes. The
separately named `OpenPbrPreviewMaterial3D` uses the existing metallic/roughness renderer and exposes
conversion limitations. MaterialX `.mtlx` interchange belongs to `Mu3D.Formats.MaterialX`.

```xaml
<mu3d:Sphere3D>
    <mu3d:OpenPbrMaterial3D NitsPerSceneUnit="100">
        <mu3d:OpenPbrSurface3D
            BaseColor="0.08,0.35,0.8;acescg"
            BaseMetalness="0.75"
            SpecularRoughness="0.22" />
    </mu3d:OpenPbrMaterial3D>
</mu3d:Sphere3D>
```

The compatibility preview instead uses the default renderer. Its default policy rejects unsupported
active inputs; selecting lossy conversion remains an explicit application choice:

```xaml
<mu3d:Sphere3D>
    <mu3d:OpenPbrPreviewMaterial3D>
        <mu3d:OpenPbrSurface3D BaseMetalness="0.75" SpecularRoughness="0.22" />
    </mu3d:OpenPbrPreviewMaterial3D>
</mu3d:Sphere3D>
```

See [OpenPBR and MaterialX](features/openpbr-materialx.md) for render-pass setup, preview policy and import,
or [HDR canvas](features/hdr-canvas.md) for binding a floating-point snapshot to
`UnlitMaterial3D.Texture` without an SDR image conversion.

For complete, runnable pages, open the matching Gallery feature and its **Docs** action. Every
feature article contains a XAML host excerpt in addition to the focused C# logic it teaches.

## Explicit display views

`ColorView3D` is an optional bindable final view, shared by the default scene renderer and custom
OpenPBR pipelines. The default is no display transform. Scene and accumulated radiance stay linear.

```xaml
<mu3d:Mu3DSceneView>
    <mu3d:Mu3DSceneView.DisplayTransform>
        <mu3d:ColorView3D Preset="AgXHdr1000" ExposureStops="0" ReferenceWhiteNits="100" />
    </mu3d:Mu3DSceneView.DisplayTransform>
    <!-- Scene3D content -->
</mu3d:Mu3DSceneView>
```

Choose `AgXSdr`, `AgXHdr1000`, `Aces2Sdr` or `Aces2Hdr1000`. HDR presets require an HDR
extended-linear sRGB surface and never silently become SDR. See [Color management](features/color-management.md).

## Explicit encoded wide-gamut OpenPBR colors

```xml
<mu3d:OpenPbrSurface3D BaseColor="0.8,0.2,0.1;display-p3" />
```

`LinearRgbaTypeConverter` accepts `srgb`, `display-p3`, `a98-rgb` (Adobe RGB 1998),
`prophoto-rgb` and `rec2020`, decoding their transfer functions before authoring/rendering.
The existing `lin_rec709`, `lin_displayp3`, `lin_adobergb`, `lin_prophoto`, `lin_rec2020`
and `acescg` tokens already represent linear values and are not decoded again.
Use invariant decimal points and `r,g,b[,a];space`. OpenPBR color parameters require alpha 1;
use `GeometryOpacity` for opacity. These tokens apply to typed Mu3D linear-color properties,
not arbitrary MAUI `Color` properties, which retain their existing sRGB semantics.
