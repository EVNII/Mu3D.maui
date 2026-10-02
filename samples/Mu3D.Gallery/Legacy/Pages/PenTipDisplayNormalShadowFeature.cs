using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Registers the portable application shadow after the native Beauty scene pass.</summary>
internal sealed class PenTipDisplayNormalShadowFeature(
    IReadOnlyList<Mesh> casters,
    PenTipDisplayNormalShadowState state) : ISceneViewFeature
{
    public IDisposable Attach(SceneViewFeatureContext context) => context.RegisterRenderPass(
        renderer => renderer.WorkingColorSpace is StandardRgbColorSpaceReference workingSpace
            ? new PenTipDisplayNormalShadowPass(renderer, casters, state, workingSpace)
            : throw new InvalidOperationException("The Apple Pencil display-normal shadow requires a standard linear RGB working space."),
        SceneViewRenderPassPlacement.AfterScene, order: 900);
}
