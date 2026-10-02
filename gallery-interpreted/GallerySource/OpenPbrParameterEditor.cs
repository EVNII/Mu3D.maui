using Mu3D.GalleryApp.Examples;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;

namespace Mu3D.Samples;

// Native host controls over the same explicit authoring descriptors used by Razor.
internal sealed class OpenPbrParameterEditor : ContentView
{
    private readonly OpenPbrSurface3D surface;
    private readonly Action<OpenPbrSurface> apply;
    private readonly VerticalStackLayout fields = new() { Spacing = 8 };
    private readonly Label message = new() { FontSize = 12 };
    private readonly List<OpenPbrParameterEdit> edits = [];
    private readonly Picker groups = new() { Title = "Material parameter group" };

    internal OpenPbrParameterEditor(OpenPbrSurface3D surface, Action<OpenPbrSurface> apply)
    {
        this.surface = surface;
        this.apply = apply;
        foreach (string group in OpenPbrParameterFields.Groups)
            groups.Items.Add(group);
        groups.SelectedIndexChanged += (_, _) => Refresh();
        Button commit = new() { Text = "Apply this group" };
        commit.Clicked += (_, _) => Commit();
        Button reload = new() { Text = "Discard group edits" };
        reload.Clicked += (_, _) => Refresh();
        VerticalStackLayout panel = new() { Spacing = 8, IsVisible = false };
        panel.Add(new Label { FontSize = 12, Text = "41 OpenPBR inputs. Apply before changing groups; unapplied edits are discarded. Coat and specular roughness are independent. Switch off a connection to edit its constant." });
        panel.Add(groups);
        panel.Add(fields);
        panel.Add(commit);
        panel.Add(reload);
        panel.Add(message);
        Button toggle = new() { Text = "Show material parameters · 41 inputs" };
        toggle.Clicked += (_, _) =>
        {
            panel.IsVisible = !panel.IsVisible;
            toggle.Text = panel.IsVisible ? "Hide material parameters" : "Show material parameters · 41 inputs";
        };
        Content = new VerticalStackLayout { Spacing = 6, Children = { toggle, panel } };
        groups.SelectedIndex = 1;
    }

    internal void Refresh()
    {
        fields.Clear();
        edits.Clear();
        message.Text = "";
        string group = groups.SelectedItem as string ?? "Specular";
        foreach (var edit in OpenPbrParameterFields.EditGroup(surface.ToSurface(), group)) AddField(edit);
    }

    private void AddField(OpenPbrParameterEdit edit)
    {
        fields.Add(new Label { Text = edit.Field.Name, FontAttributes = FontAttributes.Bold, FontSize = 13 });
        Entry entry = new() { Text = edit.Text, Placeholder = "Empty = inherit mesh direction" };
        SemanticProperties.SetDescription(entry, edit.Field.Name);
        Switch boolean = new() { IsToggled = edit.Boolean };
        SemanticProperties.SetDescription(boolean, edit.Field.Name);
        bool isBoolean = edit.Field.ValueType == typeof(bool);
        View control = isBoolean ? boolean : entry;
        if (edit.Connection is { } node)
        {
            Switch connection = new() { IsToggled = edit.UseConnection };
            SemanticProperties.SetDescription(connection, $"Use texture or graph for {edit.Field.Name}");
            HorizontalStackLayout row = new() { Spacing = 8 };
            row.Add(connection);
            row.Add(new Label { Text = "Use texture / graph", VerticalOptions = LayoutOptions.Center });
            fields.Add(row);
            if (node.Type == OpenPbrNodeType.Float)
                fields.Add(new Label { FontSize = 12, Text = FormattableString.Invariant($"Connected range: {node.Minimum.X:G4}–{node.Maximum.X:G4}. The constant below is inactive.") });
            control.IsEnabled = !edit.UseConnection;
            connection.Toggled += (_, e) => { edit.UseConnection = e.Value; control.IsEnabled = !e.Value; };
        }
        entry.TextChanged += (_, e) => edit.Text = e.NewTextValue ?? "";
        boolean.Toggled += (_, e) => edit.Boolean = e.Value;
        fields.Add(control);
        fields.Add(new Label { FontSize = 12, Text = edit.Field.Hint });
        edits.Add(edit);
    }

    private void Commit()
    {
        try
        {
            apply(OpenPbrParameterFields.Apply(surface.ToSurface(), edits));
            Refresh();
            message.Text = "Applied. Other groups retain their current values.";
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or NotSupportedException)
        {
            message.Text = error.Message;
        }
    }
}
