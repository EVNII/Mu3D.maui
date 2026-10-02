using System.Numerics;
using Mu3D.Color;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Gizmos;
using Mu3D.Toolkit.Helpers;
using Mu3D.Toolkit.Rendering;
using static HelperRenderChecks;
using Checks = HelperRenderChecks;

internal static class HelperVisibilityChecks
{
    internal static void Validate(ICollection<string> failures)
    {
        ValidateGizmo(failures);
        ValidateOutline(failures);
    }

    private static void ValidateGizmo(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("gizmo visibility", 640, 480);
        SceneNode parent = new("parent") { VisibilityMask = SceneVisibilityMask.FromLayer(1) };
        SceneNode target = new("selected target");
        parent.AddChild(target);
        fixture.Scene.Add(parent);
        using TransformGizmo gizmo = new(target);
        using TransformGizmoRenderPass pass = new(gizmo);

        void Draw(bool expected, string description)
        {
            int before = fixture.Device.SubmittedPipelineLabels.Count;
            pass.Execute(fixture.Context);
            Checks.Expect(fixture.Device.SubmittedPipelineLabels.Count == before + (expected ? 1 : 0),
                description, failures);
            Checks.Expect(ReferenceEquals(gizmo.Target, target),
                description + " preserves the application-selected target", failures);
        }

        Draw(true, "gizmo draws a matching target below a differently layered parent");
        parent.IsVisible = false;
        Draw(false, "gizmo suppresses a target below a hidden ancestor");
        parent.IsVisible = true;
        target.IsVisible = false;
        Draw(false, "gizmo suppresses an individually hidden target");
        target.IsVisible = true;
        target.VisibilityMask = SceneVisibilityMask.FromLayer(1);
        Draw(false, "gizmo suppresses a target outside the camera mask");
        fixture.Camera.VisibilityMask = SceneVisibilityMask.FromLayer(1);
        parent.VisibilityMask = SceneVisibilityMask.Default;
        Draw(true, "gizmo resumes when the camera includes the target independent of parent layers");

        fixture.Scene.Root.IsVisible = false;
        fixture.Scene.Root.VisibilityMask = SceneVisibilityMask.None;
        Draw(true, "gizmo ignores permanent-root visibility and layer flags like scene traversal");
        parent.RemoveChild(target);
        Draw(false, "gizmo suppresses a detached retained target");
        Scene otherScene = new("other scene");
        otherScene.Add(target);
        Draw(false, "gizmo suppresses a target belonging to another scene");
        parent.AddChild(target);
        Draw(true, "gizmo resumes after the retained target rejoins its own scene");
        fixture.Camera.VisibilityMask = SceneVisibilityMask.None;
        Draw(false, "gizmo suppresses every target when the camera mask is empty");

        int beforeRoot = fixture.Device.SubmittedPipelineLabels.Count;
        gizmo.Target = fixture.Scene.Root;
        fixture.Camera.VisibilityMask = SceneVisibilityMask.All;
        pass.Execute(fixture.Context);
        Checks.Expect(fixture.Device.SubmittedPipelineLabels.Count == beforeRoot &&
            ReferenceEquals(gizmo.Target, fixture.Scene.Root),
            "permanent scene root is not a gizmo target and remains borrowed", failures);
    }

    private static void ValidateOutline(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("outline visibility", 640, 480);
        UnlitMaterial material = new(new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb));
        SceneNode parent = new("ordinary ancestor") { VisibilityMask = SceneVisibilityMask.FromLayer(1) };
        SceneNode group = new("selected group") { VisibilityMask = SceneVisibilityMask.FromLayer(1) };
        Mesh matching = MeshWithTriangles(material, 1, "matching mesh");
        SceneNode maskedBranch = new("masked branch") { VisibilityMask = SceneVisibilityMask.FromLayer(1) };
        Mesh independentChild = MeshWithTriangles(material, 1, "independently layered child");
        Mesh excluded = MeshWithTriangles(material, 3, "excluded large mesh");
        excluded.VisibilityMask = SceneVisibilityMask.FromLayer(1);
        Mesh hidden = MeshWithTriangles(material, 1, "hidden mesh");
        hidden.IsVisible = false;
        maskedBranch.AddChild(independentChild);
        group.AddChild(matching);
        group.AddChild(maskedBranch);
        group.AddChild(excluded);
        group.AddChild(hidden);
        parent.AddChild(group);
        fixture.Scene.Add(parent);

        // Only two matching triangles fit. The masked mesh has three triangles by itself,
        // so charging it before layer filtering would throw instead of producing this mask.
        OutlineHelper helper = new() { Target = group, TriangleLimit = 2 };
        OutlineHelperRenderStyle style = new(OutlineHelperRenderStyle.Default.Color,
            depthMode: OutlineHelperDepthMode.Overlay);
        using OutlineHelperRenderPass pass = new(helper, style, StandardColorSpaces.LinearSrgb);

        void Draw(int triangles, string description)
        {
            int before = fixture.Device.SubmittedPipelineLabels.Count;
            pass.Execute(fixture.Context);
            Checks.Expect(fixture.Device.SubmittedPipelineLabels.Count == before + (triangles > 0 ? 2 : 0),
                description + " submits only an eligible mask/composite pair", failures);
            if (triangles > 0)
            {
                Checks.Expect(fixture.Device.Buffers[^1].LastWriteLength == triangles * 3 * 16,
                    description + " uploads exactly the eligible source triangles", failures);
            }
        }

        Draw(2, "outline reaches matching descendants through masked groups and excludes masked budget");
        Checks.Expect(ReferenceEquals(helper.Target, group),
            "outline camera filtering preserves the selected group", failures);
        parent.IsVisible = false;
        Draw(0, "outline suppresses target subtrees below hidden ancestors");
        helper.IncludeInvisible = true;
        helper.TriangleLimit = 3;
        Draw(3, "outline explicit invisible inclusion bypasses ancestor/child hiding but retains masks");
        Checks.Expect(!parent.IsVisible && !hidden.IsVisible && ReferenceEquals(helper.Target, group),
            "outline invisible inclusion never changes scene visibility or selected target", failures);
        helper.IncludeInvisible = false;
        parent.IsVisible = true;

        fixture.Scene.Root.IsVisible = false;
        fixture.Scene.Root.VisibilityMask = SceneVisibilityMask.None;
        Draw(2, "outline ignores permanent-root flags for an ordinary selected group");
        group.IsVisible = false;
        Draw(0, "outline suppresses an individually hidden selected group by default");
        helper.IncludeInvisible = true;
        Draw(3, "outline invisible inclusion restores a hidden selected group");
        group.IsVisible = true;
        helper.IncludeInvisible = false;

        parent.RemoveChild(group);
        Draw(0, "outline suppresses a detached subtree");
        helper.IncludeInvisible = true;
        Draw(0, "outline invisible inclusion does not bypass detached membership");
        Scene otherScene = new("another outline scene");
        otherScene.Add(group);
        Draw(0, "outline rejects a retained subtree belonging to another scene");
        parent.AddChild(group);
        helper.IncludeInvisible = false;
        Draw(2, "outline resumes after selected subtree reattachment");
        Checks.Expect(ReferenceEquals(helper.Target, group),
            "outline membership transitions retain the application-selected group", failures);

        helper.Target = excluded;
        helper.IncludeInvisible = true;
        Draw(0, "outline selected masked mesh remains excluded with invisible inclusion");
        fixture.Camera.VisibilityMask = SceneVisibilityMask.FromLayer(1);
        Draw(3, "outline matching camera layer restores the same selected mesh");
        Checks.Expect(ReferenceEquals(helper.Target, excluded),
            "outline camera changes do not rewrite the selected mesh", failures);

        fixture.Camera.VisibilityMask = SceneVisibilityMask.Default;
        helper.IncludeInvisible = false;
        helper.Target = matching;
        Draw(1, "outline mesh membership does not require its ancestor layers to match");
        helper.Target = fixture.Scene.Root;
        Draw(2, "permanent scene root anchors an outline subtree despite root flags");
        helper.IncludeDescendants = false;
        Draw(0, "permanent-root outline without descendants has no source triangles");
        helper.IncludeDescendants = true;
        fixture.Camera.VisibilityMask = SceneVisibilityMask.None;
        helper.TriangleLimit = 1;
        Draw(0, "empty camera mask excludes all meshes before the outline triangle budget");
    }

    private static Mesh MeshWithTriangles(UnlitMaterial material, int count, string name)
    {
        uint[] indices = new uint[count * 3];
        for (int index = 0; index < indices.Length; index++) indices[index] = (uint)(index % 3);
        return new Mesh(new MeshGeometry(
            [new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(0, .5f, 0)],
            indices), material, name);
    }
}
