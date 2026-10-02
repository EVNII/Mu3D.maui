using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls.Xaml;
using Mu3D.Color;
using Mu3D.Formats.MaterialX;
using Mu3D.Maui.Controls;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class OpenPbrMaterialAuthoringChecks
{
    internal static int Run()
    {
        int checks = 0;
        OpenPbrMaterial3D material = new();
        material.LoadFromXaml("""
            <local:OpenPbrMaterial3D
                xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                xmlns:local="clr-namespace:Mu3D.Maui.Controls;assembly=Mu3D.Maui.Authoring.Tests"
                NitsPerSceneUnit="100">
                <local:OpenPbrSurface3D Name="Authored surface" BaseColor="0.2,0.4,0.8;acescg"
                    BaseMetalness="{Binding Metallic}" SubsurfaceWeight="0.8" FuzzWeight="0.5"
                    CoatWeight="0.7" TransmissionWeight="0.4" ThinFilmWeight="0.6"
                    EmissionLuminance="900" GeometryThinWalled="True" GeometryCoatNormal="0,0,1" />
            </local:OpenPbrMaterial3D>
            """);
        OpenPbrMaterial core = material.OpenPbrMaterial;
        Check(ReferenceEquals(core, material.CoreMaterial) && core.BaseModel == MaterialBaseModel.OpenPbr,
            "XAML constructs the distinct OpenPBR material root.");
        Check(core.Surface.BaseColor.ColorSpace == StandardColorSpaces.AcesCg && core.Surface.BaseColor.Blue == 0.8f,
            "XAML preserves tagged authoring color.");
        Check(core.Surface.SubsurfaceWeight == 0.8f && core.Surface.FuzzWeight == 0.5f &&
            core.Surface.CoatWeight == 0.7f && core.Surface.TransmissionWeight == 0.4f && core.Surface.ThinFilmWeight == 0.6f,
            "Full-model lobes survive without preview conversion.");
        Check(core.NitsPerSceneUnit == 100 && core.Surface.EmissionLuminance == 900,
            "Physical emission and its explicit scale remain separate.");
        Check(core.Surface.GeometryCoatNormal == Vector3.UnitZ && core.IsDoubleSided && core.Name == "Authored surface",
            "Geometry and inherited metadata survive XAML.");
        material.BindingContext = new { Metallic = 0.35f };
        Check(core.Surface.BaseMetalness == 0.35f, "Nested binding inherits the wrapper context.");
        material.Surface.BindingContext = new { Metallic = 0.65f };
        material.BindingContext = new { Metallic = 0.95f };
        Check(core.Surface.BaseMetalness == 0.65f, "Explicit nested binding context wins.");
        int notifications = 0;
        material.Changed += (_, _) => notifications++;
        OpenPbrSurface priorSnapshot = core.Surface;
        material.Surface.CoatWeight = 0.25f;
        Check(notifications == 1 && ReferenceEquals(core, material.CoreMaterial) && core.Surface.CoatWeight == 0.25f,
            "Nested editing updates the stable scene-material identity and invalidates once.");
        Check(priorSnapshot.CoatWeight == 0.7f, "Completed Core authoring snapshots are independent.");
        material.Name = "Wrapper name";
        Check(core.Name == "Wrapper name", "Wrapper name takes precedence.");
        material.Name = null;
        Check(core.Name == "Authored surface", "Clearing wrapper name restores authoring name.");
        material.IsDoubleSided = true;
        material.IsDoubleSided = false;
        Check(core.IsDoubleSided, "Changing metadata cannot erase thin-wall sidedness.");
        material.Surface.GeometryThinWalled = false;
        Check(!core.IsDoubleSided, "Removing thin wall respects false wrapper sidedness.");
        OpenPbrSurface3D previous = material.Surface;
        OpenPbrSurface3D replacement = new();
        replacement.SetBinding(OpenPbrSurface3D.BaseMetalnessProperty, new Binding("Metallic"));
        material.Surface = replacement;
        Check(core.Surface.BaseMetalness == 0.95f, "Replacement authoring receives inherited context.");
        notifications = 0;
        previous.BaseWeight = 0.4f;
        Check(notifications == 0, "Replacement unsubscribes the previous authoring component.");
        material.ClearValue(OpenPbrMaterial3D.SurfaceProperty);
        material.Surface.SpecularRoughness = 0.45f;
        Check(core.Surface.SpecularRoughness == 0.45f && !ReferenceEquals(replacement, material.Surface),
            "ClearValue installs an independent subscribed authoring component.");
        OpenPbrSurface3D imported = material.Surface;
        notifications = 0;
        imported.LoadSurface(new OpenPbrSurface { BaseMetalness = 0.75f, SubsurfaceWeight = 1, EmissionLuminance = 400 });
        Check(notifications == 1 && core.Surface.BaseMetalness == 0.75f && core.Surface.SubsurfaceWeight == 1 &&
            core.Surface.EmissionLuminance == 400, "Imported full surface is applied as one notification.");
        material.NitsPerSceneUnit = null;
        Check(core.NitsPerSceneUnit is null && core.Surface.EmissionLuminance == 400,
            "Missing emission scale remains explicit for render validation; authoring is preserved.");
        material.NitsPerSceneUnit = float.NaN;
        Check(material.NitsPerSceneUnit is null && core.NitsPerSceneUnit is null,
            "Bindable validation rejects nonfinite emission scale without changing the material.");
        material.Surface = null!;
        Check(ReferenceEquals(material.Surface, imported), "Bindable validation rejects null authoring.");
        OpenPbrMaterial3D independent = new();
        Check(!ReferenceEquals(independent.Surface, material.Surface), "Default authoring is per wrapper.");
        OpenPbrMaterial3D sharing = new() { Surface = imported };
        imported.BaseMetalness = 0.55f;
        Check(core.Surface.BaseMetalness == 0.55f && sharing.OpenPbrMaterial.Surface.BaseMetalness == 0.55f,
            "Shared authoring updates all explicit wrappers.");
        MaterialXOpenPbrDocument file = new(
            [new MaterialXOpenPbrSurface("full_surface", material.Surface.ToSurface())],
            [new MaterialXSurfaceMaterial("material", "full_surface")]);
        using MemoryStream xml = new();
        MaterialXOpenPbrSerializer.Export(xml, file);
        xml.Position = 0;
        OpenPbrSurface fromXml = MaterialXOpenPbrSerializer.Import(xml).Surfaces[0].Surface;
        material.Surface.LoadSurface(fromXml);
        Check(core.Surface.SubsurfaceWeight == 1 && core.Surface.EmissionLuminance == 400 && core.Surface.BaseMetalness == 0.55f,
            "Actual .mtlx round trip feeds the native XAML material with full authoring values.");
        Slider slider = new() { Minimum = 0, Maximum = 1, Value = fromXml.SpecularRoughness };
        material.Surface.SetBinding(OpenPbrSurface3D.SpecularRoughnessProperty, new Binding(nameof(Slider.Value), source: slider));
        slider.Value = 0.82;
        Check(core.Surface.SpecularRoughness == 0.82f && sharing.OpenPbrMaterial.Surface.SpecularRoughness == 0.82f,
            "After actual XML import, restored slider binding updates both real materials.");
        OpenPbrSurface3D shared = new();
        WeakReference discarded = CreateDiscardedMaterial(shared);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Check(!discarded.IsAlive, "Shared authoring does not retain discarded material wrappers.");
        shared.BaseMetalness = 0.2f;
        GC.KeepAlive(shared);
        ResourceDictionary resources = new();
        resources.LoadFromXaml("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                xmlns:pbr="clr-namespace:Mu3D.Rendering.OpenPbr;assembly=Mu3D.Rendering.OpenPbr">
                <pbr:OpenPbrRenderPass x:Key="OpenPbrPass" Mode="Reference" />
                <pbr:OpenPbrRenderPass x:Key="RasterPass" Mode="Raster" RasterEnvironmentSamples="32" />
                <pbr:OpenPbrRenderPass x:Key="HybridPass" Mode="Hybrid" HybridMaxBounces="8" />
                <pbr:OpenPbrRenderPass x:Key="FastPass" Mode="Fast" />
            </ResourceDictionary>
            """);
        using OpenPbrRenderPass declaredPass = (OpenPbrRenderPass)resources["OpenPbrPass"];
        Check(declaredPass.Mode == OpenPbrRenderMode.Reference,
            "A ResourceDictionary constructs and configures the renderer directly from XAML.");
        Check(declaredPass.Pipeline.Passes.Count == 1 && ReferenceEquals(declaredPass, declaredPass.Pipeline.Passes[0]),
            "The XAML-declared pass exposes the pipeline consumed by the scene view binding.");
        using OpenPbrRenderPass rasterPass = (OpenPbrRenderPass)resources["RasterPass"];
        using OpenPbrRenderPass hybridPass = (OpenPbrRenderPass)resources["HybridPass"];
        Check(rasterPass.Mode == OpenPbrRenderMode.Raster && rasterPass.RasterEnvironmentSamples == 32,
            "XAML selects explicit raster lighting and quadrature budget.");
        Check(hybridPass.Mode == OpenPbrRenderMode.Hybrid && hybridPass.HybridMaxBounces == 8,
            "XAML selects hybrid transport and its independent event budget.");
        using OpenPbrRenderPass fastPass = (OpenPbrRenderPass)resources["FastPass"];
        Check(fastPass.Mode == OpenPbrRenderMode.Fast && fastPass.FastApproximations.Count == 0,
            "XAML selects the approximate Fast mode and starts with an empty approximation report.");
        OpenPbrSurface3D graphSurface = new();
        graphSurface.LoadFromXaml("""
            <local:OpenPbrSurface3D xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                xmlns:local="clr-namespace:Mu3D.Maui.Controls;assembly=Mu3D.Maui.Authoring.Tests"
                Graph="{Binding MaterialGraph}" />
            """);
        var expression = new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode>
            { [OpenPbrInput.SpecularRoughness] = OpenPbrNode.Float(.4f) });
        graphSurface.BindingContext = new { MaterialGraph = expression };
        OpenPbrMaterial3D graphMaterial = new() { Surface = graphSurface };
        Check(ReferenceEquals(graphMaterial.OpenPbrMaterial.Surface.Graph, expression), "XAML binding reaches the renderer's graph without conversion.");
        int graphChanges = 0; graphMaterial.Changed += (_, _) => graphChanges++;
        graphSurface.LoadSurface(new() { Graph = expression, CoatWeight = .5f });
        Check(graphChanges == 1 && ReferenceEquals(graphSurface.ToSurface().Graph, expression), "Loading graph and constants publishes one snapshot.");
        graphSurface.Graph = null;
        Check(graphChanges == 2 && graphMaterial.OpenPbrMaterial.Surface.Graph is null, "Removing a graph restores constant authoring.");
        return checks;

        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks++;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateDiscardedMaterial(OpenPbrSurface3D surface) =>
        new(new OpenPbrMaterial3D { Surface = surface });
}
