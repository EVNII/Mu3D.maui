namespace Mu3D.Toolkit.Animation;

/// <summary>Describes the current transport state of an <see cref="IPlayable"/>.</summary>
public enum PlaybackState
{
    /// <summary>Playback is stopped and may be restarted from its current or initial position.</summary>
    Stopped,

    /// <summary>Playback is actively advancing.</summary>
    Playing,

    /// <summary>Playback is paused at its current position.</summary>
    Paused,

    /// <summary>The source is temporarily waiting for data or preparation.</summary>
    Buffering,
}

/// <summary>
/// Defines a UI-independent playback source that a transport control can observe and operate.
/// </summary>
/// <remarks>
/// Implementations own their clock, scheduling, animation evaluation and resource lifecycle. A
/// viewport toolbar only calls this contract and never assumes whether the source is a glTF clip,
/// camera path, MAUI animation bridge, video-like timeline or application-defined sequence.
/// Implementations must raise <see cref="PlaybackChanged"/> whenever a value exposed by this
/// interface changes. The event may be raised from any thread; UI adapters are responsible for
/// dispatching presentation updates.
/// </remarks>
public interface IPlayable
{
    /// <summary>Occurs when playback state, timing, capabilities or looping changes.</summary>
    event EventHandler? PlaybackChanged;

    /// <summary>Gets the current transport state.</summary>
    PlaybackState State { get; }

    /// <summary>Gets the current non-negative playback position.</summary>
    TimeSpan Position { get; }

    /// <summary>
    /// Gets the total duration, or <see cref="TimeSpan.Zero"/> when duration is not yet known.
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>Gets whether the current source accepts <see cref="Seek"/> requests.</summary>
    bool CanSeek { get; }

    /// <summary>Gets or sets whether playback repeats after reaching its duration.</summary>
    bool IsLooping { get; set; }

    /// <summary>Starts or resumes playback according to the source's scheduling policy.</summary>
    void Play();

    /// <summary>Pauses playback without changing its current position.</summary>
    void Pause();

    /// <summary>Stops playback according to the source's policy.</summary>
    /// <remarks>An implementation may reset its position or preserve it, but must document which.</remarks>
    void Stop();

    /// <summary>Requests a new playback position.</summary>
    /// <param name="position">The requested non-negative source-relative position.</param>
    void Seek(TimeSpan position);
}
