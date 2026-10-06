namespace Mu3D.Toolkit.Diagnostics;

/// <summary>Defines the information shown by a frame-statistics indicator.</summary>
public enum FrameStatisticsDisplayMode
{
    /// <summary>Shows FPS, frame time, a short statistics summary and the optional history graph.</summary>
    Normal = 0,

    /// <summary>Shows only the current FPS in a compact indicator.</summary>
    Compact = 1,

    /// <summary>Shows FPS, frame time, timing breakdowns, resource counts and the optional history graph.</summary>
    Detail = 2,
}
