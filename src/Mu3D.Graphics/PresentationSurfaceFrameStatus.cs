namespace Mu3D.Graphics;

/// <summary>Reports the result of acquiring and presenting one surface frame.</summary>
public enum PresentationSurfaceFrameStatus
{
    /// <summary>The frame was presented using an optimal surface texture.</summary>
    PresentedOptimal,

    /// <summary>The frame was presented, but the acquired surface texture was suboptimal.</summary>
    PresentedSuboptimal,

    /// <summary>No surface texture became available before the backend acquisition timeout.</summary>
    Timeout,

    /// <summary>The surface configuration is outdated and should be configured again.</summary>
    Outdated,

    /// <summary>The platform surface was lost and should be configured again or recreated.</summary>
    Lost,

    /// <summary>The backend reported an unspecified surface acquisition error.</summary>
    Error,
}
