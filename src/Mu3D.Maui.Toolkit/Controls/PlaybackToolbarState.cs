using Mu3D.Toolkit.Animation;

namespace Mu3D.Maui.Toolkit.Controls;

internal readonly record struct PlaybackToolbarSnapshot(
    double Progress,
    string TimeText,
    bool CanPlayPause,
    bool CanSeek,
    bool IsPlaying,
    bool IsBuffering,
    bool IsLooping);

internal static class PlaybackToolbarState
{
    internal static PlaybackToolbarSnapshot Read(IPlayable? playable)
    {
        if (playable is null)
        {
            return new PlaybackToolbarSnapshot(
                0d,
                "--:-- / --:--",
                CanPlayPause: false,
                CanSeek: false,
                IsPlaying: false,
                IsBuffering: false,
                IsLooping: false);
        }

        TimeSpan reportedDuration = playable.Duration;
        TimeSpan reportedPosition = playable.Position;
        TimeSpan duration = reportedDuration < TimeSpan.Zero
            ? TimeSpan.Zero
            : reportedDuration;
        TimeSpan position = reportedPosition < TimeSpan.Zero
            ? TimeSpan.Zero
            : reportedPosition;
        if (duration > TimeSpan.Zero && position > duration)
        {
            position = duration;
        }

        bool durationKnown = duration > TimeSpan.Zero;
        double progress = durationKnown
            ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0d, 1d)
            : 0d;
        PlaybackState state = playable.State;
        return new PlaybackToolbarSnapshot(
            progress,
            durationKnown
                ? $"{Format(position)} / {Format(duration)}"
                : $"{Format(position)} / --:--",
            CanPlayPause: state != PlaybackState.Buffering,
            CanSeek: playable.CanSeek && durationKnown,
            IsPlaying: state == PlaybackState.Playing,
            IsBuffering: state == PlaybackState.Buffering,
            IsLooping: playable.IsLooping);
    }

    internal static TimeSpan PositionFromProgress(TimeSpan duration, double progress)
    {
        if (duration <= TimeSpan.Zero || !double.IsFinite(progress))
        {
            return TimeSpan.Zero;
        }

        double clamped = Math.Clamp(progress, 0d, 1d);
        if (clamped <= 0d)
        {
            return TimeSpan.Zero;
        }
        if (clamped >= 1d)
        {
            return duration;
        }

        double ticks = duration.Ticks * clamped;
        return TimeSpan.FromTicks((long)Math.Round(ticks, MidpointRounding.AwayFromZero));
    }

    private static string Format(TimeSpan value)
    {
        value = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return value.TotalHours >= 1d
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }
}
