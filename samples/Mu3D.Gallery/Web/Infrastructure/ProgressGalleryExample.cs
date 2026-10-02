using System.Numerics;
using Mu3D.Toolkit.Animation;
using Mu3D.Toolkit.Viewports;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Web.Infrastructure;

// Endpoints/targets come directly from the original native ProgressTool declarations.
internal sealed class ProgressGalleryExample : IViewportFrameRequester
{
    internal NativeGalleryScene Definition { get; }
    internal ViewportProgressController Controller { get; }
    private readonly Action requestFrame;

    internal ProgressGalleryExample(string nativePage, Action requestFrame)
    {
        this.requestFrame = requestFrame;
        Definition = NativeGalleryScene.Read(nativePage);
        var mappings = Definition.Source.Descendants()
            .Where(element => element.Name.LocalName == "NodeTransformProgress")
            .Select(element =>
            {
                string reference = (string?)element.Attribute("Target") ?? throw new InvalidDataException("Progress target missing.");
                string key = reference.Replace("{x:Reference ", "", StringComparison.Ordinal).TrimEnd('}');
                SceneNode node = Definition.NamedNodes[key];
                string property = (string?)element.Attribute("Property") ?? "";
                float from = NativeGalleryScene.Number(element, "From", 0), to = NativeGalleryScene.Number(element, "To", 0);
                if (property is not ("X" or "Y" or "RotationY")) throw new NotSupportedException($"Gallery progress property {property}.");
                return new DelegateViewportProgressMapping(progress =>
                {
                    float value = from + (to - from) * (float)progress;
                    if (property == "RotationY") node.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, value * MathF.PI / 180);
                    else
                    {
                        Vector3 position = node.Transform.Position;
                        node.Transform.Position = property == "X" ? position with { X = value } : position with { Y = value };
                    }
                });
            });
        Controller = new(this, mappings);
        Controller.Reapply();
    }
    public void RequestFrame() => requestFrame();
}
