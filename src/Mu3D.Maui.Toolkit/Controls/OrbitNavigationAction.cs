namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Identifies one discrete orbit or map navigation command.</summary>
/// <remarks>
/// Applications can supply a value as the parameter of
/// <c>OrbitTool.NavigationCommand</c> from MAUI buttons, menus, gestures or keyboard
/// accelerators. The action contains no platform key or pointer type.
/// </remarks>
public enum OrbitNavigationAction
{
    /// <summary>Rotates the camera left around its target.</summary>
    RotateLeft,

    /// <summary>Rotates the camera right around its target.</summary>
    RotateRight,

    /// <summary>Rotates the camera upward around its target.</summary>
    RotateUp,

    /// <summary>Rotates the camera downward around its target.</summary>
    RotateDown,

    /// <summary>Pans the camera and target toward viewport left.</summary>
    PanLeft,

    /// <summary>Pans the camera and target toward viewport right.</summary>
    PanRight,

    /// <summary>Pans the camera and target toward viewport up.</summary>
    PanUp,

    /// <summary>Pans the camera and target toward viewport down.</summary>
    PanDown,

    /// <summary>Moves the camera closer to its target.</summary>
    DollyIn,

    /// <summary>Moves the camera farther from its target.</summary>
    DollyOut,
}
