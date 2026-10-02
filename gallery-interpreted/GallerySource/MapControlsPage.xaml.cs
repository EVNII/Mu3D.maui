using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates declarative map and CAD-style camera navigation.</summary>
public partial class MapControlsPage : ContentPage
{
    private static readonly ViewportKey[] KeyboardChoices =
        Enum.GetValues<ViewportKey>();

    /// <summary>Initializes the map-controls example.</summary>
    public MapControlsPage()
    {
        InitializeComponent();
        foreach (Picker picker in new[]
        {
            RotateLeftPicker,
            RotateRightPicker,
            RotateUpPicker,
            RotateDownPicker,
            DollyInPicker,
            DollyOutPicker,
        })
        {
            picker.ItemsSource = KeyboardChoices;
        }
        RotateLeftPicker.SelectedItem = MapKeyboardInput.RotateLeftKey;
        RotateRightPicker.SelectedItem = MapKeyboardInput.RotateRightKey;
        RotateUpPicker.SelectedItem = MapKeyboardInput.RotateUpKey;
        RotateDownPicker.SelectedItem = MapKeyboardInput.RotateDownKey;
        DollyInPicker.SelectedItem = MapKeyboardInput.DollyInKey;
        DollyOutPicker.SelectedItem = MapKeyboardInput.DollyOutKey;
        RefreshKeyboardAccelerators();
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        AttachContinuousKeyboardInput();
        StatusLabel.Text = MapNavigation.Behavior.IsMouseButtonInputAvailable
            ? "Hold multiple configured keys for immediate composite motion; left drag pans and right drag rotates."
            : "Hold multiple configured keys for immediate composite motion; one-finger drag pans the XZ plane.";
        SceneView.InvalidateScene();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        DetachContinuousKeyboardInput();
        base.OnDisappearing();
    }

    private void OnKeyboardBindingChanged(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is not Picker { SelectedItem: ViewportKey key } picker)
        {
            return;
        }

        (MenuFlyoutItem? menuItem, string label) = picker switch
        {
            _ when ReferenceEquals(picker, RotateLeftPicker) =>
                (RotateLeftMenuItem, "Rotate left"),
            _ when ReferenceEquals(picker, RotateRightPicker) =>
                (RotateRightMenuItem, "Rotate right"),
            _ when ReferenceEquals(picker, RotateUpPicker) =>
                (RotateUpMenuItem, "Rotate up"),
            _ when ReferenceEquals(picker, RotateDownPicker) =>
                (RotateDownMenuItem, "Rotate down"),
            _ when ReferenceEquals(picker, DollyInPicker) =>
                (DollyInMenuItem, "Dolly in"),
            _ when ReferenceEquals(picker, DollyOutPicker) =>
                (DollyOutMenuItem, "Dolly out"),
            _ => (null, string.Empty),
        };
        if (menuItem is not null)
        {
            SetKeyboardAccelerator(menuItem, label, key);
            AttachContinuousKeyboardInput();
        }
    }

    private void RefreshKeyboardAccelerators()
    {
        SetKeyboardAccelerator(
            RotateLeftMenuItem,
            "Rotate left",
            MapKeyboardInput.RotateLeftKey);
        SetKeyboardAccelerator(
            RotateRightMenuItem,
            "Rotate right",
            MapKeyboardInput.RotateRightKey);
        SetKeyboardAccelerator(
            RotateUpMenuItem,
            "Rotate up",
            MapKeyboardInput.RotateUpKey);
        SetKeyboardAccelerator(
            RotateDownMenuItem,
            "Rotate down",
            MapKeyboardInput.RotateDownKey);
        SetKeyboardAccelerator(DollyInMenuItem, "Dolly in", MapKeyboardInput.DollyInKey);
        SetKeyboardAccelerator(DollyOutMenuItem, "Dolly out", MapKeyboardInput.DollyOutKey);
    }

    private static void SetKeyboardAccelerator(
        MenuFlyoutItem menuItem,
        string label,
        ViewportKey key)
    {
        menuItem.KeyboardAccelerators.Clear();
#if WINDOWS || IOS || MACCATALYST
        menuItem.Text = key == ViewportKey.None
            ? $"{label} (unbound)"
            : $"{label} ({GetKeyboardAcceleratorKey(key)})";
#else
        menuItem.Text = label;
        if (key == ViewportKey.None)
        {
            return;
        }

        menuItem.KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = GetKeyboardAcceleratorKey(key),
        });
#endif
    }

    private static string GetKeyboardAcceleratorKey(ViewportKey key) => key switch
    {
        ViewportKey.LeftArrow => "Left",
        ViewportKey.RightArrow => "Right",
        ViewportKey.UpArrow => "Up",
        ViewportKey.DownArrow => "Down",
        ViewportKey.PageUp => "PageUp",
        ViewportKey.PageDown => "PageDown",
        ViewportKey.Add => "Add",
        ViewportKey.Subtract => "Subtract",
        >= ViewportKey.A and <= ViewportKey.Z => key.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Map feature {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }

    partial void AttachContinuousKeyboardInput();

    partial void DetachContinuousKeyboardInput();
}
