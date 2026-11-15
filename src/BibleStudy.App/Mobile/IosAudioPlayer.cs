#if IOS
using AVFoundation;
using Foundation;

namespace BibleStudy.App;

/// <summary>The audio Bible on an iPhone: the phone's own player (AVAudioPlayer), with the same members as the desktop player.</summary>
sealed class AudioPlayer : IDisposable
{
    public enum PlayState { Idle, Playing, Paused, Ended, Failed }

    AVAudioPlayer? _player;

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
        if (_player is null) return;
        _player.EnableRate = true;
        _player.Rate = (float)Math.Min(Speed, 2);   // (the system player goes up to 2×)
    }

    public bool Play(string path)
    {
        Stop();
        try
        {
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSession.CategoryPlayback, out _);
            session.SetActive(true, out _);
            var p = AVAudioPlayer.FromUrl(NSUrl.FromFilename(path), out var error);
            if (p is null) { Error = error?.LocalizedDescription ?? "cannot play"; State = PlayState.Failed; return false; }
            p.EnableRate = true;
            p.FinishedPlaying += (_, _) => State = PlayState.Ended;
            p.DecoderError += (_, e) => { Error = e.Error?.LocalizedDescription; State = PlayState.Failed; };
            p.PrepareToPlay();
            Length = TimeSpan.FromSeconds(p.Duration);
            _player = p;
            ApplySpeed();
            p.Play();
            State = PlayState.Playing;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
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
        _player.Play(); State = PlayState.Playing; ApplySpeed();
    }

    public void Seek(TimeSpan to)
    {
        if (_player is null) return;
        _player.CurrentTime = Math.Clamp(to.TotalSeconds, 0, Length.TotalSeconds);
        Position = to;
    }

    public void Poll()
    {
        if (_player is null) return;
        if (State is PlayState.Playing or PlayState.Paused) Position = TimeSpan.FromSeconds(_player.CurrentTime);
    }

    public void Stop()
    {
        if (_player is { } p)
        {
            p.Stop();
            p.Dispose();
            _player = null;
        }
        Position = TimeSpan.Zero;
        if (State is PlayState.Playing or PlayState.Paused) State = PlayState.Idle;
    }

    public void Dispose() => Stop();
}
#endif
