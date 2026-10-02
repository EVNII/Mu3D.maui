using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.Samples;
using Mu3D.SceneGraph;

internal static class OpenPbrEditorChecks
{
    internal static void Run()
    {
        OpenPbrSurface3D surface = new() { CoatWeight = .4f, CoatRoughness = .2f };
        OpenPbrNode roughness = OpenPbrNode.Float(.21f);
        OpenPbrNode color = OpenPbrNode.Color(new(.2f, .3f, .4f, 1, StandardColorSpaces.AcesCg));
        surface.Graph = new(new Dictionary<OpenPbrInput, OpenPbrNode>
        {
            [OpenPbrInput.SpecularRoughness] = roughness,
            [OpenPbrInput.BaseColor] = color,
        });
        int commits = 0;
        OpenPbrParameterEditor editor = new(surface, value => { surface.LoadSurface(value); commits++; });
        var outer = (VerticalStackLayout)editor.Content;
        var panel = (VerticalStackLayout)outer.Children[1];
        var groups = (Picker)panel.Children[1];
        var fields = (VerticalStackLayout)panel.Children[2];
        var apply = (IButtonController)panel.Children[3];
        HashSet<string> names = [];
        for (int i = 0; i < groups.Items.Count; i++)
        {
            groups.SelectedIndex = i;
            foreach (Label label in fields.Children.OfType<Label>().Where(l => l.FontAttributes == FontAttributes.Bold))
                names.Add(label.Text);
        }
        foreach (OpenPbrInput input in Enum.GetValues<OpenPbrInput>())
            Check(names.Contains(OpenPbrInputInfo.Get(input).Name), $"Missing editor input: {input}");
        Check(names.Count == 43, "All 41 inputs plus name and scale must be editable.");

        groups.SelectedIndex = groups.Items.IndexOf("Specular");
        Check(!EntryFor("specular_roughness").IsEnabled, "Connected constants must be visibly inactive.");
        OpenPbrGraph originalGraph = surface.Graph;
        apply.SendClicked();
        Check(ReferenceEquals(surface.Graph, originalGraph), "No-op edits must retain graph identity.");
        var connectionRow = fields.Children.OfType<HorizontalStackLayout>().Single();
        ((Switch)connectionRow.Children[0]).IsToggled = false;
        EntryFor("specular_roughness").Text = "1";
        apply.SendClicked();
        Check(surface.SpecularRoughness == 1 && surface.CoatRoughness == .2f, "Base and coat roughness must remain independent.");
        Check(surface.Graph!.Bindings.Count == 1 && ReferenceEquals(surface.Graph.Bindings[OpenPbrInput.BaseColor], color), "Disconnect only the edited input.");

        groups.SelectedIndex = groups.Items.IndexOf("Coat");
        EntryFor("coat_weight").Text = "0";
        EntryFor("coat_roughness").Text = "2";
        int beforeFailure = commits;
        apply.SendClicked();
        Check(commits == beforeFailure && surface.CoatWeight == .4f, "Invalid group must not partially mutate the live surface.");
        EntryFor("coat_roughness").Text = "0.75";
        apply.SendClicked();
        Check(surface.CoatWeight == 0 && surface.CoatRoughness == .75f, "Valid group applies using invariant decimal points.");

        surface.LoadSurface(new OpenPbrSurface { BaseColor = new(.15f, .25f, .35f, 1, StandardColorSpaces.LinearDisplayP3), GeometryNormal = null });
        groups.SelectedIndex = groups.Items.IndexOf("Base");
        editor.Refresh();
        Check(EntryFor("base_color").Text!.EndsWith(";lin_displayp3", StringComparison.Ordinal), "Import must show original color-space identity.");
        apply.SendClicked();
        Check(surface.BaseColor.ColorSpace == StandardColorSpaces.LinearDisplayP3, "Untouched colors must retain their identity.");
        groups.SelectedIndex = groups.Items.IndexOf("Geometry");
        EntryFor("geometry_normal").Text = "0,1,0";
        apply.SendClicked();
        Check(surface.GeometryNormal == System.Numerics.Vector3.UnitY, "Explicit vector edit failed.");
        EntryFor("geometry_normal").Text = "";
        apply.SendClicked();
        Check(surface.GeometryNormal is null, "Empty geometry direction must restore mesh inheritance.");
        Console.WriteLine("OpenPBR editor: complete input coverage, connection ownership, atomic validation, culture, import and vector inheritance passed.");

        Entry EntryFor(string name)
        {
            int index = fields.Children.ToList().FindIndex(v => v is Label label && label.Text == name);
            return fields.Children.Skip(index + 1).OfType<Entry>().First();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
