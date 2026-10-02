using System.Numerics;

namespace Mu3D.Toolkit.Controls;

/// <summary>
/// Receives UI-independent Rotate, Pan and Dolly actions from a viewport input adapter.
/// </summary>
/// <remarks>
/// The action names describe user intent rather than a particular camera model. An orbit
/// controller rotates around its target, translates that target and changes target distance. A
/// first-person controller interprets the same actions as look, strafe/lift and forward/backward
/// movement. Implementations borrow their camera, acquire no raw input and own no frame clock.
/// </remarks>
public interface IViewportNavigationController
{
    /// <summary>Occurs after an accepted action changes controller state.</summary>
    event EventHandler? Changed;

    /// <summary>Gets or sets whether normalized navigation actions are accepted.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Gets whether damped motion remains to be consumed.</summary>
    bool HasPendingMotion { get; }

    /// <summary>Applies a signed horizontal/vertical rotation action.</summary>
    /// <param name="delta">Controller-native horizontal and vertical rotation units.</param>
    /// <returns><see langword="true"/> when the action was accepted.</returns>
    bool Rotate(Vector2 delta);

    /// <summary>Applies a signed horizontal/vertical translation action.</summary>
    /// <param name="delta">Controller-native horizontal and vertical translation units.</param>
    /// <returns><see langword="true"/> when the action was accepted.</returns>
    bool Pan(Vector2 delta);

    /// <summary>Applies a signed inward/forward scalar action.</summary>
    /// <param name="delta">A controller-native scalar; positive moves inward or forward.</param>
    /// <returns><see langword="true"/> when the action was accepted.</returns>
    bool Dolly(float delta);

    /// <summary>Consumes pending damped motion for one host-supplied frame interval.</summary>
    /// <param name="elapsedSeconds">A finite non-negative elapsed duration.</param>
    /// <returns><see langword="true"/> when controller state changed.</returns>
    bool Update(float elapsedSeconds);
}
