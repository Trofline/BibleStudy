#if ANDROID
using Android.Media;

namespace BibleStudy.App;

/// <summary>The audio Bible on Android: the phone's own player (MediaPlayer), with the same members as the desktop player.</summary>
sealed class AudioPlayer : IDisposable
{
    public enum PlayState { Idle, Playing, Paused, Ended, Failed }

    MediaPlayer? _player;

    public PlayState State { get; private set; } = PlayState.Idle;
    public string? Error { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan Length { get; private set; }
    public bool CanSeek => true;
    public double Speed { get; private set; } = 1;
    public static bool HasPlayer => true;

    public void SetSpeed(double speed)
    {
        Speed = Math.Clamp(speed, 0.5, 3);
        ApplySpeed();
    }

    void ApplySpeed()
    {
        if (_player is null || State != PlayState.Playing) return;
        try { _player.PlaybackParams = new PlaybackParams().SetSpeed((float)Speed); } catch (Java.Lang.Exception) { }
    }

    public bool Play(string path)
    {
        Stop();
        try
        {
            var p = new MediaPlayer();
            p.SetDataSource(path);
            p.Prepare();
            p.Completion += (_, _) => State = PlayState.Ended;
            p.Error += (_, e) => { Error = e.What.ToString(); State = PlayState.Failed; };
            Length = TimeSpan.FromMilliseconds(p.Duration);
            p.Start();
            _player = p;
            State = PlayState.Playing;
            ApplySpeed();
            return true;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException)
        {
            Error = ex.Message; State = PlayState.Failed; return false;
        }
    }

    public void Pause()
    {
        if (_player is null || State != PlayState.Playing) return;
        _player.Pause(); State = PlayState.Paused;
    }

    public void Resume()
    {
        if (_player is null || State != PlayState.Paused) return;
        _player.Start(); State = PlayState.Playing; ApplySpeed();
    }

    public void Seek(TimeSpan to)
    {
        if (_player is null) return;
        _player.SeekTo((int)Math.Clamp(to.TotalMilliseconds, 0, Length.TotalMilliseconds));
        Position = to;
    }

    public void Poll()
    {
        if (_player is null) return;
        try { if (State is PlayState.Playing or PlayState.Paused) Position = TimeSpan.FromMilliseconds(_player.CurrentPosition); }
        catch (Java.Lang.IllegalStateException) { }
    }

    public void Stop()
    {
        if (_player is { } p)
        {
            try { p.Stop(); } catch (Java.Lang.IllegalStateException) { }
            p.Release();
            _player = null;
        }
        Position = TimeSpan.Zero;
        if (State is PlayState.Playing or PlayState.Paused) State = PlayState.Idle;
    }

    public void Dispose() => Stop();
}
#endif
