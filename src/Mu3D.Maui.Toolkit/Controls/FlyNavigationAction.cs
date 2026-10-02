namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Identifies one discrete first-person navigation command.</summary>
public enum FlyNavigationAction
{
    /// <summary>Moves along the current forward direction.</summary>
    MoveForward,
    /// <summary>Moves opposite the current forward direction.</summary>
    MoveBackward,
    /// <summary>Strafes toward camera left.</summary>
    MoveLeft,
    /// <summary>Strafes toward camera right.</summary>
    MoveRight,
    /// <summary>Moves along world up.</summary>
    MoveUp,
    /// <summary>Moves opposite world up.</summary>
    MoveDown,
    /// <summary>Turns left around world up.</summary>
    LookLeft,
    /// <summary>Turns right around world up.</summary>
    LookRight,
    /// <summary>Looks upward.</summary>
    LookUp,
    /// <summary>Looks downward.</summary>
    LookDown,
}
