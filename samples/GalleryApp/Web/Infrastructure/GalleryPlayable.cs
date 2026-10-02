using Mu3D.Toolkit.Animation;

namespace Mu3D.GalleryApp.Web.Infrastructure;

// Same application-owned six-second demo transport; the host supplies VSync time, never a timer.
internal sealed class GalleryPlayable : IPlayable
{
    public event EventHandler? PlaybackChanged;
    public PlaybackState State { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan Duration => TimeSpan.FromSeconds(6);
    public bool CanSeek => true;
    private bool looping;
    public bool IsLooping { get => looping; set { looping = value; Changed(); } }
    public void Play() { if (Position >= Duration) Position = TimeSpan.Zero; State = PlaybackState.Playing; Changed(); }
    public void Pause() { if (State == PlaybackState.Playing) { State = PlaybackState.Paused; Changed(); } }
    public void Stop() { Position = TimeSpan.Zero; State = PlaybackState.Stopped; Changed(); }
    public void Seek(TimeSpan position) { Position = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, 6)); Changed(); }
    internal void Advance(double delta)
    {
        if (State != PlaybackState.Playing) return;
        double next = Position.TotalSeconds + Math.Max(0, delta);
        if (next >= 6 && !looping) { next = 6; State = PlaybackState.Paused; }
        Position = TimeSpan.FromSeconds(looping ? next % 6 : next); Changed();
    }
    private void Changed() => PlaybackChanged?.Invoke(this, EventArgs.Empty);
}
