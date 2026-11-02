using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// The audio Bible: "Hören" plays the recording of the chapter on screen (World English Bible, public domain, from eBible.org),
/// continues with the next chapter, and brings the text along when the reader still shows the chapter that was playing.
/// </summary>
public partial class MainWindow
{
    static readonly Geometry PlayShape = Geometry.Parse("M6 4l14 8-14 8z");
    static readonly Geometry PauseShape = Geometry.Parse("M6 4h4v16H6z M14 4h4v16h-4z");

    readonly AudioPlayer _audio = new();
    DispatcherTimer? _audioTimer;
    CancellationTokenSource? _audioCts;
    string _audioBook = "";
    int _audioChapter;
    bool _audioSetting;

    string AudioCache => Path.Combine(_root, "cache");

    static readonly double[] AudioSpeeds = [0.75, 1, 1.25, 1.5, 1.75, 2];

    void ShowAudioSpeed() => AudioSpeedBtn.Content = _audio.Speed.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + "×";

    void InitAudio()
    {
        _audioTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _audioTimer.Tick += (_, _) => OnAudioTick();
        AudioBtn.Click += async (_, _) =>
        {
            if (AudioBar.IsVisible) StopAudio();
            else await StartAudioAsync(_curBook, _curChapter);
        };
        AudioPlayBtn.Click += async (_, _) =>
        {
            switch (_audio.State)
            {
                case AudioPlayer.PlayState.Playing: _audio.Pause(); break;
                case AudioPlayer.PlayState.Paused: _audio.Resume(); break;
                default: if (_audioBook != "") await StartAudioAsync(_audioBook, _audioChapter); break;
            }
            ShowAudioState();
        };
        AudioNextBtn.Click += async (_, _) => { if (AudioBible.Next(_audioBook, _audioChapter) is { } n) await PlayOtherAsync(n); };
        AudioPrevBtn.Click += async (_, _) => { if (AudioBible.Previous(_audioBook, _audioChapter) is { } p) await PlayOtherAsync(p); };
        AudioCloseBtn.Click += (_, _) => StopAudio();
        _audio.SetSpeed(_settings.AudioSpeed);
        ShowAudioSpeed();
        AudioSpeedBtn.Click += (_, _) =>
        {
            var next = AudioSpeeds.FirstOrDefault(v => v > _audio.Speed + 0.01);
            _settings.AudioSpeed = next == 0 ? AudioSpeeds[0] : next;
            _audio.SetSpeed(_settings.AudioSpeed); SaveSettings(); ShowAudioSpeed();
        };
        AudioSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && !_audioSetting && _audio.CanSeek && AudioSlider.IsPointerOver)
                _audio.Seek(TimeSpan.FromSeconds(AudioSlider.Value));
        };
        Closed += (_, _) => { _audioCts?.Cancel(); _audio.Dispose(); };
    }

    /// <summary>Plays the recording of one chapter; the reader follows when it still shows the chapter that was playing.</summary>
    async Task StartAudioAsync(string book, int chapter)
    {
        if (!AudioBible.Has(book)) { StatusText.Text = L("Für dieses Buch gibt es keine Aufnahme."); AudioBtn.IsChecked = false; return; }
        if (!AudioPlayer.HasPlayer) { StatusText.Text = L("Kein Audio-Player gefunden. Installiere mpv, ffplay oder mpg123."); AudioBtn.IsChecked = false; return; }
        _audioCts?.Cancel();
        var cts = _audioCts = new CancellationTokenSource();
        _audio.Stop();
        _audioBook = book; _audioChapter = chapter;
        AudioBar.IsVisible = true; AudioBtn.IsChecked = true;
        AudioTitle.Text = L("{0} {1} · lade …", BookName(book), chapter);
        ShowAudioState();
        if (TranslationBox.SelectedItem is TranslationInfo t && t.Language != "eng")
            StatusText.Text = L("Die Aufnahme ist die englische {0}; der Text daneben bleibt in deiner Übersetzung.", AudioBible.Name);
        try
        {
            var path = await AudioBible.GetChapterFileAsync(_http, AudioCache, book, chapter, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (!_audio.Play(path)) { AudioTitle.Text = L("Abspielen nicht möglich: {0}", _audio.Error ?? ""); StatusText.Text = AudioTitle.Text; ShowAudioState(); return; }
            AudioTitle.Text = L("{0} {1} · {2}, gelesen von {3}", BookName(book), chapter, AudioBible.Name, AudioBible.Narrator);
            _audioTimer!.Start();
            ShowAudioState();
            if (AudioBible.Next(book, chapter) is { } next) _ = PrefetchAudioAsync(next, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            AudioTitle.Text = L("Aufnahme konnte nicht geladen werden: {0}", ex.Message);
            StatusText.Text = AudioTitle.Text;
            ShowAudioState();
        }
    }

    /// <summary>Loads the next chapter in the background so the change is without a pause.</summary>
    async Task PrefetchAudioAsync((string Book, int Chapter) next, CancellationToken ct)
    {
        try { await AudioBible.GetChapterFileAsync(_http, AudioCache, next.Book, next.Chapter, ct); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException) { /* played later; the error shows then */ }
    }

    /// <summary>Another chapter (buttons, or the end of the one that played): the text moves along if it was showing the old one.</summary>
    async Task PlayOtherAsync((string Book, int Chapter) to)
    {
        var follow = ReaderHost.IsVisible && _curBook == _audioBook && _curChapter == _audioChapter && _books.Any(b => b.Id == to.Book);
        if (follow) await GoToAsync(to.Book, to.Chapter);
        await StartAudioAsync(to.Book, to.Chapter);
    }

    void OnAudioTick()
    {
        _audio.Poll();
        if (_audio.State == AudioPlayer.PlayState.Ended)
        {
            _audioTimer!.Stop();
            if (_settings.AutoMarkRead && _audioBook != "") SetRead(_audioBook, [_audioChapter], true);
            if (AudioBible.Next(_audioBook, _audioChapter) is { } next) _ = PlayOtherAsync(next);
            else { StatusText.Text = L("Ende der Hörbibel."); StopAudio(); }
            return;
        }
        if (_audio.State == AudioPlayer.PlayState.Failed) { _audioTimer!.Stop(); StatusText.Text = L("Abspielen nicht möglich: {0}", _audio.Error ?? ""); }
        ShowAudioState();
    }

    void ShowAudioState()
    {
        AudioPlayIcon.Data = _audio.State == AudioPlayer.PlayState.Playing ? PauseShape : PlayShape;
        AudioPrevBtn.IsEnabled = _audioBook != "" && AudioBible.Previous(_audioBook, _audioChapter) is not null;
        AudioNextBtn.IsEnabled = _audioBook != "" && AudioBible.Next(_audioBook, _audioChapter) is not null;
        AudioSlider.IsEnabled = _audio.CanSeek && _audio.Length > TimeSpan.Zero;
        _audioSetting = true;
        try
        {
            AudioSlider.Maximum = Math.Max(1, _audio.Length.TotalSeconds);
            AudioSlider.Value = Math.Min(AudioSlider.Maximum, _audio.Position.TotalSeconds);
        }
        finally { _audioSetting = false; }
        static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        AudioTime.Text = _audio.Length > TimeSpan.Zero ? $"{Clock(_audio.Position)} / {Clock(_audio.Length)}" : _audio.State is AudioPlayer.PlayState.Playing or AudioPlayer.PlayState.Paused ? Clock(_audio.Position) : "";
    }

    void StopAudio()
    {
        _audioCts?.Cancel();
        _audioTimer?.Stop();
        _audio.Stop();
        AudioBar.IsVisible = false;
        AudioBtn.IsChecked = false;
        _audioBook = "";
    }
}
