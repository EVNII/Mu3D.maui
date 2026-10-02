namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Defines optional Toolkit selection metadata as MAUI attached properties.</summary>
/// <remarks>
/// These properties do not modify the base Mu3D scene-node contract. They are read only by an
/// explicitly attached selection tool. Downstream MAUI packages can use the same attached-property
/// pattern for their own strongly typed node metadata without a shared untyped property bag.
/// </remarks>
public static class SceneSelection
{
    /// <summary>Identifies the attached <c>IsSelectable</c> property.</summary>
    public static readonly BindableProperty IsSelectableProperty = BindableProperty.CreateAttached(
        "IsSelectable",
        typeof(bool),
        typeof(SceneSelection),
        true);

    /// <summary>Identifies the attached <c>Mask</c> property.</summary>
    public static readonly BindableProperty MaskProperty = BindableProperty.CreateAttached(
        "Mask",
        typeof(uint),
        typeof(SceneSelection),
        1u);

    /// <summary>Gets whether the default Toolkit selection adapter may propose an object.</summary>
    /// <param name="target">The bindable object carrying optional selection metadata.</param>
    public static bool GetIsSelectable(BindableObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (bool)target.GetValue(IsSelectableProperty);
    }

    /// <summary>Sets whether the default Toolkit selection adapter may propose an object.</summary>
    /// <param name="target">The bindable object receiving optional selection metadata.</param>
    /// <param name="value">False to exclude the object from default proposals.</param>
    public static void SetIsSelectable(BindableObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsSelectableProperty, value);
    }

    /// <summary>Gets the thirty-two-bit selection-category mask attached to an object.</summary>
    /// <param name="target">The bindable object carrying optional selection metadata.</param>
    public static uint GetMask(BindableObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (uint)target.GetValue(MaskProperty);
    }

    /// <summary>Sets the thirty-two-bit selection-category mask attached to an object.</summary>
    /// <param name="target">The bindable object receiving optional selection metadata.</param>
    /// <param name="value">The complete category bit field.</param>
    public static void SetMask(BindableObject target, uint value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(MaskProperty, value);
    }
}
