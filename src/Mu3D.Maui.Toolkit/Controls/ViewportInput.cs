namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Groups bindable viewport input mappings by physical device.</summary>
/// <remarks>
/// This object describes device routes only. Rotate, Pan and Dolly are normalized intents; the
/// attached Orbit, Map or Fly tool decides what those intents mean for its camera model.
/// </remarks>
public sealed class ViewportInput : BindableObject
{
    /// <summary>Initializes the default viewport input mappings.</summary>
    public ViewportInput()
    {
        Mouse = new MouseInput();
        Trackpad = new TrackpadInput();
        Touchscreen = new TouchscreenInput();
        Keyboard = new KeyboardInput();
    }

    /// <summary>Identifies the <see cref="Mouse"/> bindable property.</summary>
    public static readonly BindableProperty MouseProperty = CreateDeviceProperty<MouseInput>(
        nameof(Mouse));

    /// <summary>Identifies the <see cref="Trackpad"/> bindable property.</summary>
    public static readonly BindableProperty TrackpadProperty = CreateDeviceProperty<TrackpadInput>(
        nameof(Trackpad));

    /// <summary>Identifies the <see cref="Touchscreen"/> bindable property.</summary>
    public static readonly BindableProperty TouchscreenProperty =
        CreateDeviceProperty<TouchscreenInput>(nameof(Touchscreen));

    /// <summary>Identifies the <see cref="Keyboard"/> bindable property.</summary>
    public static readonly BindableProperty KeyboardProperty = CreateDeviceProperty<KeyboardInput>(
        nameof(Keyboard));

    /// <summary>Gets or sets mouse and mouse-compatible pointer-button routes.</summary>
    public MouseInput Mouse
    {
        get => (MouseInput)GetValue(MouseProperty);
        set => SetValue(MouseProperty, value);
    }

    /// <summary>Gets or sets native trackpad gesture routes.</summary>
    public TrackpadInput Trackpad
    {
        get => (TrackpadInput)GetValue(TrackpadProperty);
        set => SetValue(TrackpadProperty, value);
    }

    /// <summary>Gets or sets direct-touchscreen gesture routes.</summary>
    public TouchscreenInput Touchscreen
    {
        get => (TouchscreenInput)GetValue(TouchscreenProperty);
        set => SetValue(TouchscreenProperty, value);
    }

    /// <summary>Gets or sets focused hardware-key routes.</summary>
    public KeyboardInput Keyboard
    {
        get => (KeyboardInput)GetValue(KeyboardProperty);
        set => SetValue(KeyboardProperty, value);
    }

    private static BindableProperty CreateDeviceProperty<TDevice>(string name)
        where TDevice : ViewportDeviceInput => BindableProperty.Create(
            name,
            typeof(TDevice),
            typeof(ViewportInput),
            defaultValue: null,
            validateValue: static (_, value) => value is TDevice,
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((ViewportInput)bindable).OnDeviceReplaced(
                    (ViewportDeviceInput?)oldValue,
                    (ViewportDeviceInput)newValue));

    private void OnDeviceReplaced(
        ViewportDeviceInput? oldDevice,
        ViewportDeviceInput newDevice)
    {
        if (oldDevice is not null)
        {
            oldDevice.PropertyChanged -= OnDevicePropertyChanged;
        }
        newDevice.PropertyChanged += OnDevicePropertyChanged;
    }

    private void OnDevicePropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        OnPropertyChanged(nameof(ViewportInput));
    }
}

/// <summary>Base class for one bindable physical-device input configuration.</summary>
public abstract class ViewportDeviceInput : BindableObject
{
    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(ViewportDeviceInput),
        true);

    /// <summary>Gets or sets whether this device configuration accepts input.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }
}

/// <summary>Configures mouse and mouse-compatible pointer-button input.</summary>
/// <remarks>
/// Platforms can expose a trackpad press as a mouse-compatible pointer button. Continuous
/// two-finger translation and pinch remain separately configurable through <see cref="TrackpadInput"/>.
/// </remarks>
public sealed class MouseInput : ViewportDeviceInput
{
    /// <summary>Identifies the <see cref="LeftButtonDragAction"/> bindable property.</summary>
    public static readonly BindableProperty LeftButtonDragActionProperty = CreateDragProperty(
        nameof(LeftButtonDragAction),
        ViewportDragAction.Rotate);

    /// <summary>Identifies the <see cref="MiddleButtonDragAction"/> bindable property.</summary>
    public static readonly BindableProperty MiddleButtonDragActionProperty = CreateDragProperty(
        nameof(MiddleButtonDragAction),
        ViewportDragAction.None);

    /// <summary>Identifies the <see cref="RightButtonDragAction"/> bindable property.</summary>
    public static readonly BindableProperty RightButtonDragActionProperty = CreateDragProperty(
        nameof(RightButtonDragAction),
        ViewportDragAction.Pan);

    /// <summary>Identifies the <see cref="WheelAction"/> bindable property.</summary>
    public static readonly BindableProperty WheelActionProperty = CreateScalarProperty(
        nameof(WheelAction),
        ViewportScalarAction.Dolly);

    /// <summary>Gets or sets the left-button drag route.</summary>
    public ViewportDragAction LeftButtonDragAction
    {
        get => (ViewportDragAction)GetValue(LeftButtonDragActionProperty);
        set => SetValue(LeftButtonDragActionProperty, value);
    }

    /// <summary>Gets or sets the middle-button drag route.</summary>
    public ViewportDragAction MiddleButtonDragAction
    {
        get => (ViewportDragAction)GetValue(MiddleButtonDragActionProperty);
        set => SetValue(MiddleButtonDragActionProperty, value);
    }

    /// <summary>Gets or sets the right-button drag route.</summary>
    public ViewportDragAction RightButtonDragAction
    {
        get => (ViewportDragAction)GetValue(RightButtonDragActionProperty);
        set => SetValue(RightButtonDragActionProperty, value);
    }

    /// <summary>Gets or sets the pointer-wheel route.</summary>
    public ViewportScalarAction WheelAction
    {
        get => (ViewportScalarAction)GetValue(WheelActionProperty);
        set => SetValue(WheelActionProperty, value);
    }

    private static BindableProperty CreateDragProperty(
        string name,
        ViewportDragAction defaultValue) => BindableProperty.Create(
            name,
            typeof(ViewportDragAction),
            typeof(MouseInput),
            defaultValue,
            validateValue: static (_, value) =>
                value is ViewportDragAction action && Enum.IsDefined(action));

    private static BindableProperty CreateScalarProperty(
        string name,
        ViewportScalarAction defaultValue) => BindableProperty.Create(
            name,
            typeof(ViewportScalarAction),
            typeof(MouseInput),
            defaultValue,
            validateValue: static (_, value) =>
                value is ViewportScalarAction action && Enum.IsDefined(action));
}

/// <summary>Configures native trackpad gestures independently of pointer-button input.</summary>
public sealed class TrackpadInput : ViewportDeviceInput
{
    /// <summary>Identifies the <see cref="TwoFingerDragAction"/> bindable property.</summary>
    public static readonly BindableProperty TwoFingerDragActionProperty = BindableProperty.Create(
        nameof(TwoFingerDragAction),
        typeof(ViewportDragAction),
        typeof(TrackpadInput),
        ViewportDragAction.Pan,
        validateValue: static (_, value) =>
            value is ViewportDragAction action && Enum.IsDefined(action));

    /// <summary>Identifies the <see cref="PinchAction"/> bindable property.</summary>
    public static readonly BindableProperty PinchActionProperty = BindableProperty.Create(
        nameof(PinchAction),
        typeof(ViewportScalarAction),
        typeof(TrackpadInput),
        ViewportScalarAction.Dolly,
        validateValue: static (_, value) =>
            value is ViewportScalarAction action && Enum.IsDefined(action));

    /// <summary>Gets or sets the continuous two-finger translation route.</summary>
    public ViewportDragAction TwoFingerDragAction
    {
        get => (ViewportDragAction)GetValue(TwoFingerDragActionProperty);
        set => SetValue(TwoFingerDragActionProperty, value);
    }

    /// <summary>Gets or sets the native pinch route.</summary>
    public ViewportScalarAction PinchAction
    {
        get => (ViewportScalarAction)GetValue(PinchActionProperty);
        set => SetValue(PinchActionProperty, value);
    }
}

/// <summary>Configures direct-touchscreen gestures.</summary>
public sealed class TouchscreenInput : ViewportDeviceInput
{
    /// <summary>Identifies the <see cref="OneFingerDragAction"/> bindable property.</summary>
    public static readonly BindableProperty OneFingerDragActionProperty = CreateDragProperty(
        nameof(OneFingerDragAction),
        ViewportDragAction.Rotate);

    /// <summary>Identifies the <see cref="TwoFingerDragAction"/> bindable property.</summary>
    public static readonly BindableProperty TwoFingerDragActionProperty = CreateDragProperty(
        nameof(TwoFingerDragAction),
        ViewportDragAction.Pan);

    /// <summary>Identifies the <see cref="PinchAction"/> bindable property.</summary>
    public static readonly BindableProperty PinchActionProperty = BindableProperty.Create(
        nameof(PinchAction),
        typeof(ViewportScalarAction),
        typeof(TouchscreenInput),
        ViewportScalarAction.Dolly,
        validateValue: static (_, value) =>
            value is ViewportScalarAction action && Enum.IsDefined(action));

    /// <summary>Gets or sets the direct one-finger drag route.</summary>
    public ViewportDragAction OneFingerDragAction
    {
        get => (ViewportDragAction)GetValue(OneFingerDragActionProperty);
        set => SetValue(OneFingerDragActionProperty, value);
    }

    /// <summary>Gets or sets the direct two-finger centroid-drag route.</summary>
    public ViewportDragAction TwoFingerDragAction
    {
        get => (ViewportDragAction)GetValue(TwoFingerDragActionProperty);
        set => SetValue(TwoFingerDragActionProperty, value);
    }

    /// <summary>Gets or sets the direct pinch route.</summary>
    public ViewportScalarAction PinchAction
    {
        get => (ViewportScalarAction)GetValue(PinchActionProperty);
        set => SetValue(PinchActionProperty, value);
    }

    private static BindableProperty CreateDragProperty(
        string name,
        ViewportDragAction defaultValue) => BindableProperty.Create(
            name,
            typeof(ViewportDragAction),
            typeof(TouchscreenInput),
            defaultValue,
            validateValue: static (_, value) =>
                value is ViewportDragAction action && Enum.IsDefined(action));
}

/// <summary>Configures focused hardware-key routes.</summary>
public sealed class KeyboardInput : ViewportDeviceInput
{
    /// <summary>Identifies the <see cref="RotateLeftKey"/> bindable property.</summary>
    public static readonly BindableProperty RotateLeftKeyProperty = CreateKeyProperty(
        nameof(RotateLeftKey), ViewportKey.LeftArrow);
    /// <summary>Identifies the <see cref="RotateRightKey"/> bindable property.</summary>
    public static readonly BindableProperty RotateRightKeyProperty = CreateKeyProperty(
        nameof(RotateRightKey), ViewportKey.RightArrow);
    /// <summary>Identifies the <see cref="RotateUpKey"/> bindable property.</summary>
    public static readonly BindableProperty RotateUpKeyProperty = CreateKeyProperty(
        nameof(RotateUpKey), ViewportKey.UpArrow);
    /// <summary>Identifies the <see cref="RotateDownKey"/> bindable property.</summary>
    public static readonly BindableProperty RotateDownKeyProperty = CreateKeyProperty(
        nameof(RotateDownKey), ViewportKey.DownArrow);

    /// <summary>Identifies the <see cref="PanLeftKey"/> bindable property.</summary>
    public static readonly BindableProperty PanLeftKeyProperty = CreateKeyProperty(
        nameof(PanLeftKey), ViewportKey.None);
    /// <summary>Identifies the <see cref="PanRightKey"/> bindable property.</summary>
    public static readonly BindableProperty PanRightKeyProperty = CreateKeyProperty(
        nameof(PanRightKey), ViewportKey.None);
    /// <summary>Identifies the <see cref="PanUpKey"/> bindable property.</summary>
    public static readonly BindableProperty PanUpKeyProperty = CreateKeyProperty(
        nameof(PanUpKey), ViewportKey.None);
    /// <summary>Identifies the <see cref="PanDownKey"/> bindable property.</summary>
    public static readonly BindableProperty PanDownKeyProperty = CreateKeyProperty(
        nameof(PanDownKey), ViewportKey.None);

    /// <summary>Identifies the <see cref="DollyInKey"/> bindable property.</summary>
    public static readonly BindableProperty DollyInKeyProperty = CreateKeyProperty(
        nameof(DollyInKey), ViewportKey.PageUp);
    /// <summary>Identifies the <see cref="DollyOutKey"/> bindable property.</summary>
    public static readonly BindableProperty DollyOutKeyProperty = CreateKeyProperty(
        nameof(DollyOutKey), ViewportKey.PageDown);
    /// <summary>Identifies the <see cref="AlternateDollyInKey"/> bindable property.</summary>
    public static readonly BindableProperty AlternateDollyInKeyProperty = CreateKeyProperty(
        nameof(AlternateDollyInKey), ViewportKey.Add);
    /// <summary>Identifies the <see cref="AlternateDollyOutKey"/> bindable property.</summary>
    public static readonly BindableProperty AlternateDollyOutKeyProperty = CreateKeyProperty(
        nameof(AlternateDollyOutKey), ViewportKey.Subtract);

    /// <summary>Gets or sets the rotate-left key.</summary>
    public ViewportKey RotateLeftKey { get => GetKey(RotateLeftKeyProperty); set => SetValue(RotateLeftKeyProperty, value); }
    /// <summary>Gets or sets the rotate-right key.</summary>
    public ViewportKey RotateRightKey { get => GetKey(RotateRightKeyProperty); set => SetValue(RotateRightKeyProperty, value); }
    /// <summary>Gets or sets the rotate-up key.</summary>
    public ViewportKey RotateUpKey { get => GetKey(RotateUpKeyProperty); set => SetValue(RotateUpKeyProperty, value); }
    /// <summary>Gets or sets the rotate-down key.</summary>
    public ViewportKey RotateDownKey { get => GetKey(RotateDownKeyProperty); set => SetValue(RotateDownKeyProperty, value); }
    /// <summary>Gets or sets the pan-left key.</summary>
    public ViewportKey PanLeftKey { get => GetKey(PanLeftKeyProperty); set => SetValue(PanLeftKeyProperty, value); }
    /// <summary>Gets or sets the pan-right key.</summary>
    public ViewportKey PanRightKey { get => GetKey(PanRightKeyProperty); set => SetValue(PanRightKeyProperty, value); }
    /// <summary>Gets or sets the pan-up key.</summary>
    public ViewportKey PanUpKey { get => GetKey(PanUpKeyProperty); set => SetValue(PanUpKeyProperty, value); }
    /// <summary>Gets or sets the pan-down key.</summary>
    public ViewportKey PanDownKey { get => GetKey(PanDownKeyProperty); set => SetValue(PanDownKeyProperty, value); }
    /// <summary>Gets or sets the primary dolly-in key.</summary>
    public ViewportKey DollyInKey { get => GetKey(DollyInKeyProperty); set => SetValue(DollyInKeyProperty, value); }
    /// <summary>Gets or sets the primary dolly-out key.</summary>
    public ViewportKey DollyOutKey { get => GetKey(DollyOutKeyProperty); set => SetValue(DollyOutKeyProperty, value); }
    /// <summary>Gets or sets the alternate dolly-in key.</summary>
    public ViewportKey AlternateDollyInKey { get => GetKey(AlternateDollyInKeyProperty); set => SetValue(AlternateDollyInKeyProperty, value); }
    /// <summary>Gets or sets the alternate dolly-out key.</summary>
    public ViewportKey AlternateDollyOutKey { get => GetKey(AlternateDollyOutKeyProperty); set => SetValue(AlternateDollyOutKeyProperty, value); }

    private ViewportKey GetKey(BindableProperty property) => (ViewportKey)GetValue(property);

    private static BindableProperty CreateKeyProperty(string name, ViewportKey defaultValue) =>
        BindableProperty.Create(
            name,
            typeof(ViewportKey),
            typeof(KeyboardInput),
            defaultValue,
            validateValue: static (_, value) =>
                value is ViewportKey key && Enum.IsDefined(key));
}
